using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Endpoints auditados para crear revisiones adicionales de Gemini.
    /// Los endpoints históricos permanecen disponibles. Esta versión toma un
    /// bloqueo lógico por diagnóstico dentro de la transacción para impedir que
    /// dos dispositivos creen revisiones simultáneas por encima del límite.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/diagnostico-ia-revisiones/v2")]
    public sealed class DiagnosticoIARevisionV2Controller : ControllerBase
    {
        private readonly DiagnosticoIADbContext db;
        private readonly PermisoApiService permisos;
        private readonly DiagnosticoIAProcesamientoQueue queue;
        private readonly DiagnosticoIAProcesamientoEstadoStore estadoStore;

        public DiagnosticoIARevisionV2Controller(
            DiagnosticoIADbContext db,
            PermisoApiService permisos,
            DiagnosticoIAProcesamientoQueue queue,
            DiagnosticoIAProcesamientoEstadoStore estadoStore)
        {
            this.db = db;
            this.permisos = permisos;
            this.queue = queue;
            this.estadoStore = estadoStore;
        }

        [HttpPost("{id:int}/tecnico")]
        public async Task<IActionResult> SolicitarComoTecnico(
            int id,
            [FromBody] DiagnosticoIATecnicoRevisionRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazSolicitud,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (!permiso.Permitido)
                return StatusCode(permiso.CodigoEstado, Error(permiso.Mensaje));

            string motivo = Normalizar(request.Motivo, 2000);
            if (motivo.Length < 8)
            {
                return BadRequest(Error(
                    "Explique con al menos 8 caracteres qué debe revisar nuevamente Gemini."));
            }

            await using IDbContextTransaction transaccion =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

            if (!await TomarBloqueoAsync(id, transaccion, cancellationToken))
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                return Conflict(Error(
                    "Otra operación está actualizando este diagnóstico. Intente nuevamente en unos segundos."));
            }

            bool transaccionConfirmada = false;

            try
            {
                DiagnosticoIA? diagnostico = await CargarAsync(
                    id,
                    cancellationToken);

                if (diagnostico == null)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return NotFound(Error("La solicitud no existe."));
                }

                if (diagnostico.UsuarioSolicitanteId != usuarioId!.Value)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Forbid();
                }

                if (diagnostico.Estado !=
                    DiagnosticoIAFlujo.Estados.PendienteDecisionTecnico)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "Solo puede solicitar otra evaluación mientras la solicitud esté pendiente de su decisión."));
                }

                IActionResult? limite = await ValidarLimiteAsync(
                    diagnostico,
                    cancellationToken);

                if (limite != null)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return limite;
                }

                int completadas = diagnostico.RevisionesIA.Count(item =>
                    item.Estado == "COMPLETADA");

                var revision = new DiagnosticoIARevision
                {
                    DiagnosticoIAId = diagnostico.DiagnosticoIAId,
                    UsuarioClasificadorId = usuarioId.Value,
                    RetroalimentacionClasificador = motivo,
                    DiagnosticoPropuestoClasificador =
                        Normalizar(request.DiagnosticoPropuesto, 300),
                    FechaSolicitudRevisionUtc = DateTime.UtcNow,
                    Estado = "ANALIZANDO"
                };

                string anterior = diagnostico.Estado;
                diagnostico.Estado = DiagnosticoIAFlujo.Estados.AnalizandoIA;
                diagnostico.RevisionesIA.Add(revision);

                AgregarHistorial(
                    diagnostico,
                    usuarioId.Value,
                    anterior,
                    diagnostico.Estado,
                    "TECNICO_SOLICITA_NUEVA_EVALUACION_V2",
                    $"El técnico solicitó la evaluación adicional {completadas + 1} a Gemini. Motivo: {motivo}");

                await db.SaveChangesAsync(cancellationToken);
                await transaccion.CommitAsync(cancellationToken);
                transaccionConfirmada = true;

                DiagnosticoIAProcesamientoEstado estado = estadoStore.Actualizar(
                    diagnostico.DiagnosticoIAId,
                    diagnostico.Estado,
                    "EN_COLA_REVISION",
                    "La nueva evaluación fue guardada. Esperando turno para consultar Gemini...",
                    0,
                    diagnostico.Imagenes.Count);

                await queue.EncolarAsync(
                    new DiagnosticoIAProcesamientoTrabajo(
                        diagnostico.DiagnosticoIAId,
                        usuarioId.Value,
                        false,
                        DiagnosticoIAProcesamientoOperaciones.Revision,
                        revision.DiagnosticoIARevisionId),
                    CancellationToken.None);

                return Accepted(new
                {
                    success = true,
                    message = "La nueva evaluación fue enviada a la cola del servidor.",
                    data = estado
                });
            }
            catch (OperationCanceledException)
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        [HttpPost("{id:int}/analizador")]
        public async Task<IActionResult> SolicitarComoAnalizador(
            int id,
            [FromBody] DiagnosticoIASegundaRevisionRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAnalizador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (!permiso.Permitido)
                return StatusCode(permiso.CodigoEstado, Error(permiso.Mensaje));

            string retroalimentacion = Normalizar(
                request.RetroalimentacionAnalizador,
                2000);

            if (retroalimentacion.Length < 8)
            {
                return BadRequest(Error(
                    "Describa con más detalle qué debe revisar Gemini."));
            }

            await using IDbContextTransaction transaccion =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

            if (!await TomarBloqueoAsync(id, transaccion, cancellationToken))
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                return Conflict(Error(
                    "Otra operación está actualizando este diagnóstico. Intente nuevamente en unos segundos."));
            }

            bool transaccionConfirmada = false;

            try
            {
                DiagnosticoIA? diagnostico = await CargarAsync(
                    id,
                    cancellationToken);

                if (diagnostico == null)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return NotFound(Error("La solicitud no existe."));
                }

                if (diagnostico.Estado is not
                    (DiagnosticoIAFlujo.Estados.PendienteAnalizador or
                     DiagnosticoIAFlujo.Estados.EnAnalisisHumano or
                     DiagnosticoIAFlujo.Estados.DevueltoCorreccion))
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return Conflict(Error(
                        "La revisión adicional solo está disponible durante el análisis humano."));
                }

                IActionResult? limite = await ValidarLimiteAsync(
                    diagnostico,
                    cancellationToken);

                if (limite != null)
                {
                    await transaccion.RollbackAsync(CancellationToken.None);
                    return limite;
                }

                int completadas = diagnostico.RevisionesIA.Count(item =>
                    item.Estado == "COMPLETADA");

                var revision = new DiagnosticoIARevision
                {
                    DiagnosticoIAId = diagnostico.DiagnosticoIAId,
                    UsuarioClasificadorId = usuarioId!.Value,
                    RetroalimentacionClasificador = retroalimentacion,
                    DiagnosticoPropuestoClasificador =
                        Normalizar(request.DiagnosticoPropuestoAnalizador, 300),
                    FechaSolicitudRevisionUtc = DateTime.UtcNow,
                    Estado = "ANALIZANDO"
                };

                diagnostico.RevisionesIA.Add(revision);

                AgregarHistorial(
                    diagnostico,
                    usuarioId.Value,
                    diagnostico.Estado,
                    diagnostico.Estado,
                    "REVISION_IA_ENCOLADA_V2",
                    $"Se solicitó la revisión adicional {completadas + 1} a Gemini.");

                await db.SaveChangesAsync(cancellationToken);
                await transaccion.CommitAsync(cancellationToken);
                transaccionConfirmada = true;

                DiagnosticoIAProcesamientoEstado estado = estadoStore.Actualizar(
                    diagnostico.DiagnosticoIAId,
                    diagnostico.Estado,
                    "EN_COLA_REVISION",
                    "La revisión fue guardada. Esperando turno para consultar Gemini...",
                    0,
                    0);

                await queue.EncolarAsync(
                    new DiagnosticoIAProcesamientoTrabajo(
                        diagnostico.DiagnosticoIAId,
                        usuarioId.Value,
                        false,
                        DiagnosticoIAProcesamientoOperaciones.Revision,
                        revision.DiagnosticoIARevisionId),
                    CancellationToken.None);

                return Accepted(new
                {
                    success = true,
                    message = "La revisión adicional fue enviada a la cola del servidor.",
                    data = estado
                });
            }
            catch (OperationCanceledException)
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch
            {
                if (!transaccionConfirmada)
                    await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private async Task<IActionResult?> ValidarLimiteAsync(
            DiagnosticoIA diagnostico,
            CancellationToken cancellationToken)
        {
            if (diagnostico.RevisionesIA.Any(item =>
                    item.Estado == "ANALIZANDO"))
            {
                return Conflict(Error(
                    "Ya existe una evaluación adicional en proceso para este diagnóstico."));
            }

            int completadas = diagnostico.RevisionesIA.Count(item =>
                item.Estado == "COMPLETADA");

            DiagnosticoIAConfiguracion? configuracion =
                await db.Configuraciones
                    .AsNoTracking()
                    .OrderBy(item => item.DiagnosticoIAConfiguracionId)
                    .FirstOrDefaultAsync(cancellationToken);

            int maximo = Math.Clamp(
                configuracion?.MaximoRevisionesGemini ?? 2,
                1,
                20);

            bool ilimitadas = configuracion?.RevisionesIlimitadas ?? false;

            if (!ilimitadas && completadas >= maximo)
            {
                return Conflict(Error(
                    $"Este diagnóstico ya alcanzó el máximo de {maximo} revisiones adicionales configuradas."));
            }

            return null;
        }

        private async Task<DiagnosticoIA?> CargarAsync(
            int id,
            CancellationToken cancellationToken) =>
            await db.Diagnosticos
                .Include(item => item.Imagenes)
                .Include(item => item.RevisionesIA)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(
                    item => item.DiagnosticoIAId == id && item.Activo,
                    cancellationToken);

        private async Task<bool> TomarBloqueoAsync(
            int diagnosticoId,
            IDbContextTransaction transaccion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            await using DbCommand comando = conexion.CreateCommand();
            comando.Transaction = transaccion.GetDbTransaction();
            comando.CommandText = """
DECLARE @resultado INT;
EXEC @resultado = sys.sp_getapplock
    @Resource = @recurso,
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 5000;
SELECT @resultado;
""";

            DbParameter parametro = comando.CreateParameter();
            parametro.ParameterName = "@recurso";
            parametro.Value = $"CONATRADEC_DIAGNOSTICO_IA_REVISION_{diagnosticoId}";
            comando.Parameters.Add(parametro);

            object? resultado = await comando.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(resultado ?? -999) >= 0;
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int usuarioId)
                ? usuarioId
                : null;
        }

        private static void AgregarHistorial(
            DiagnosticoIA diagnostico,
            int usuarioId,
            string estadoAnterior,
            string estadoNuevo,
            string accion,
            string detalle)
        {
            diagnostico.Historial.Add(
                new DiagnosticoIAHistorial
                {
                    UsuarioId = usuarioId,
                    EstadoAnterior = Normalizar(estadoAnterior, 40),
                    EstadoNuevo = Normalizar(estadoNuevo, 40),
                    Accion = Normalizar(accion, 80),
                    Detalle = Normalizar(detalle, 2000),
                    FechaUtc = DateTime.UtcNow
                });
        }

        private static string Normalizar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo
                ? texto
                : texto[..maximo];
        }

        private static object Error(string mensaje) => new
        {
            success = false,
            message = mensaje
        };
    }
}
