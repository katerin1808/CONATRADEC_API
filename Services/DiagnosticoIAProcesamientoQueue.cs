using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CONATRADEC_API.Services
{
    public static class DiagnosticoIAProcesamientoOperaciones
    {
        public const string Analisis = "ANALISIS";
        public const string Revision = "REVISION";
    }

    public sealed record DiagnosticoIAProcesamientoTrabajo(
        int DiagnosticoIAId,
        int UsuarioId,
        bool EsReintento,
        string TipoOperacion = DiagnosticoIAProcesamientoOperaciones.Analisis,
        int? DiagnosticoIARevisionId = null);

    public sealed class DiagnosticoIAProcesamientoEstado
    {
        public int DiagnosticoIAId { get; init; }
        public string Estado { get; init; } = string.Empty;
        public string Etapa { get; init; } = string.Empty;
        public string Mensaje { get; init; } = string.Empty;
        public int FotografiasProcesadas { get; init; }
        public int TotalFotografias { get; init; }
        public int Porcentaje { get; init; }
        public bool Finalizado { get; init; }
        public bool TieneError { get; init; }
        public DateTime FechaActualizacionUtc { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Cola única del servidor. Evita mantener una solicitud HTTP abierta
    /// mientras Gemini analiza uno o varios bloques de fotografías.
    /// </summary>
    public sealed class DiagnosticoIAProcesamientoQueue
    {
        private readonly Channel<DiagnosticoIAProcesamientoTrabajo> channel =
            Channel.CreateUnbounded<DiagnosticoIAProcesamientoTrabajo>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

        private readonly ConcurrentDictionary<int, byte> encolados = new();

        public bool IntentarEncolar(
            DiagnosticoIAProcesamientoTrabajo trabajo)
        {
            if (!encolados.TryAdd(trabajo.DiagnosticoIAId, 0))
                return false;

            if (channel.Writer.TryWrite(trabajo))
                return true;

            encolados.TryRemove(trabajo.DiagnosticoIAId, out _);
            return false;
        }

        public async ValueTask EncolarAsync(
            DiagnosticoIAProcesamientoTrabajo trabajo,
            CancellationToken cancellationToken = default)
        {
            if (!encolados.TryAdd(trabajo.DiagnosticoIAId, 0))
                return;

            try
            {
                await channel.Writer.WriteAsync(
                    trabajo,
                    cancellationToken);
            }
            catch
            {
                encolados.TryRemove(trabajo.DiagnosticoIAId, out _);
                throw;
            }
        }

        public ValueTask<DiagnosticoIAProcesamientoTrabajo> DesencolarAsync(
            CancellationToken cancellationToken) =>
            channel.Reader.ReadAsync(cancellationToken);

        public void Completar(int diagnosticoIAId) =>
            encolados.TryRemove(diagnosticoIAId, out _);
    }

    /// <summary>
    /// Mantiene el avance inmediato para que MAUI pueda consultarlo cada pocos
    /// segundos. El estado definitivo siempre se conserva en la base de datos.
    /// </summary>
    public sealed class DiagnosticoIAProcesamientoEstadoStore
    {
        private readonly ConcurrentDictionary<int, DiagnosticoIAProcesamientoEstado>
            estados = new();

        public DiagnosticoIAProcesamientoEstado Actualizar(
            int diagnosticoIAId,
            string estado,
            string etapa,
            string mensaje,
            int procesadas,
            int total,
            bool finalizado = false,
            bool tieneError = false)
        {
            total = Math.Max(0, total);
            procesadas = Math.Clamp(procesadas, 0, Math.Max(total, procesadas));

            int porcentaje = total <= 0
                ? (finalizado && !tieneError ? 100 : 0)
                : Math.Clamp(
                    (int)Math.Round(procesadas * 100d / total),
                    0,
                    100);

            var valor = new DiagnosticoIAProcesamientoEstado
            {
                DiagnosticoIAId = diagnosticoIAId,
                Estado = estado,
                Etapa = etapa,
                Mensaje = mensaje,
                FotografiasProcesadas = procesadas,
                TotalFotografias = total,
                Porcentaje = porcentaje,
                Finalizado = finalizado,
                TieneError = tieneError,
                FechaActualizacionUtc = DateTime.UtcNow
            };

            estados[diagnosticoIAId] = valor;
            return valor;
        }

        public bool IntentarObtener(
            int diagnosticoIAId,
            out DiagnosticoIAProcesamientoEstado estado) =>
            estados.TryGetValue(diagnosticoIAId, out estado!);
    }

    public sealed class DiagnosticoIAProcesamientoHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory scopeFactory;
        private readonly DiagnosticoIAProcesamientoQueue queue;
        private readonly ILogger<DiagnosticoIAProcesamientoHostedService> logger;

        public DiagnosticoIAProcesamientoHostedService(
            IServiceScopeFactory scopeFactory,
            DiagnosticoIAProcesamientoQueue queue,
            ILogger<DiagnosticoIAProcesamientoHostedService> logger)
        {
            this.scopeFactory = scopeFactory;
            this.queue = queue;
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            await RecuperarPendientesAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                DiagnosticoIAProcesamientoTrabajo trabajo;

                try
                {
                    trabajo = await queue.DesencolarAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await using AsyncServiceScope scope =
                        scopeFactory.CreateAsyncScope();

                    DiagnosticoIAProcesamientoService service =
                        scope.ServiceProvider.GetRequiredService<
                            DiagnosticoIAProcesamientoService>();

                    await service.ProcesarAsync(
                        trabajo,
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    // El registro queda ANALIZANDO_IA y se recupera al reiniciar.
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Fallo no controlado en el trabajo IA {DiagnosticoIAId}.",
                        trabajo.DiagnosticoIAId);
                }
                finally
                {
                    queue.Completar(trabajo.DiagnosticoIAId);
                }
            }
        }

        private async Task RecuperarPendientesAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                await using AsyncServiceScope scope =
                    scopeFactory.CreateAsyncScope();

                DiagnosticoIADbContext db =
                    scope.ServiceProvider.GetRequiredService<
                        DiagnosticoIADbContext>();

                /*
                 * Los diagnósticos generados antes de incorporar la decisión
                 * explícita del técnico podían quedar directamente en
                 * PENDIENTE_ANALIZADOR. Se corrigen una sola vez siempre que
                 * todavía no tengan análisis humano ni un historial que
                 * confirme el envío voluntario al analizador.
                 */
                List<DiagnosticoIA> pendientesDecisionLegados =
                    await db.Diagnosticos
                        .Include(item => item.Historial)
                        .Include(item => item.AnalisisHumanos)
                        .Where(item =>
                            item.Activo &&
                            item.Estado ==
                                DiagnosticoIAFlujo.Estados.PendienteAnalizador &&
                            !item.AnalisisHumanos.Any() &&
                            !item.Historial.Any(historial =>
                                historial.Accion ==
                                    "TECNICO_ENVIA_ANALIZADOR"))
                        .ToListAsync(cancellationToken);

                foreach (DiagnosticoIA diagnostico
                         in pendientesDecisionLegados)
                {
                    string anterior = diagnostico.Estado;
                    diagnostico.Estado =
                        DiagnosticoIAFlujo.Estados.PendienteDecisionTecnico;

                    diagnostico.Historial.Add(
                        new DiagnosticoIAHistorial
                        {
                            UsuarioId =
                                diagnostico.UsuarioSolicitanteId,
                            EstadoAnterior = anterior,
                            EstadoNuevo = diagnostico.Estado,
                            Accion =
                                "MIGRACION_DECISION_TECNICO",
                            Detalle =
                                "El caso volvió al técnico porque fue creado antes de exigir una decisión explícita previa al análisis humano.",
                            FechaUtc = DateTime.UtcNow
                        });
                }

                if (pendientesDecisionLegados.Count > 0)
                    await db.SaveChangesAsync(cancellationToken);

                List<DiagnosticoIAProcesamientoTrabajo> pendientes =
                    await db.Diagnosticos
                        .AsNoTracking()
                        .Where(item =>
                            item.Activo &&
                            item.Estado ==
                                DiagnosticoIAFlujo.Estados.AnalizandoIA &&
                            !item.RevisionesIA.Any(revision =>
                                revision.Estado == "ANALIZANDO"))
                        .OrderBy(item => item.FechaSolicitudUtc)
                        .Select(item =>
                            new DiagnosticoIAProcesamientoTrabajo(
                                item.DiagnosticoIAId,
                                item.UsuarioSolicitanteId,
                                false,
                                DiagnosticoIAProcesamientoOperaciones.Analisis,
                                null))
                        .ToListAsync(cancellationToken);

                foreach (DiagnosticoIAProcesamientoTrabajo trabajo in pendientes)
                    queue.IntentarEncolar(trabajo);

                List<DiagnosticoIAProcesamientoTrabajo> revisionesPendientes =
                    await db.RevisionesIA
                        .AsNoTracking()
                        .Where(item =>
                            item.Estado == "ANALIZANDO" &&
                            item.Diagnostico.Activo)
                        .OrderBy(item => item.FechaSolicitudRevisionUtc)
                        .Select(item =>
                            new DiagnosticoIAProcesamientoTrabajo(
                                item.DiagnosticoIAId,
                                item.UsuarioClasificadorId,
                                false,
                                DiagnosticoIAProcesamientoOperaciones.Revision,
                                item.DiagnosticoIARevisionId))
                        .ToListAsync(cancellationToken);

                foreach (DiagnosticoIAProcesamientoTrabajo trabajo
                         in revisionesPendientes)
                {
                    queue.IntentarEncolar(trabajo);
                }

                int totalRecuperados =
                    pendientes.Count + revisionesPendientes.Count;

                if (totalRecuperados > 0)
                {
                    logger.LogInformation(
                        "Se recuperaron {Cantidad} trabajos de Diagnóstico IA pendientes.",
                        totalRecuperados);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "No fue posible recuperar los diagnósticos IA pendientes.");
            }
        }
    }
}
