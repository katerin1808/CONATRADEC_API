using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Operaciones del flujo independiente por fotografía. El analizador puede
    /// trabajar con las evidencias que el técnico ya envió, aunque todavía
    /// existan otras fotografías bajo responsabilidad del técnico.
    ///
    /// La revisión humana solo avanza al aprobador cuando la etapa técnica fue
    /// finalizada y todas las fotografías activas no descartadas ya cuentan con
    /// una clasificación humana. El cierre de la revisión se aplica de manera
    /// conjunta, sin perder el expediente individual de cada evidencia.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/inspecciones-fitosanitarias-flujo")]
    public sealed class InspeccionFitosanitariaFlujoIndependienteController :
        ControllerBase
    {
        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaDatabase database;
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;

        public InspeccionFitosanitariaFlujoIndependienteController(
            DiagnosticoIADbContext diagnosticoDb,
            PermisoApiService permisos,
            InspeccionFitosanitariaControlDatabaseInitializer control)
        {
            this.diagnosticoDb = diagnosticoDb;
            this.permisos = permisos;
            this.control = control;
            database = new InspeccionFitosanitariaDatabase(diagnosticoDb);
        }

        /// <summary>
        /// Bandeja liviana sin N+1. Una inspección aparece para el analizador
        /// desde que contiene al menos una fotografía enviada. Los contadores
        /// indican cuántas evidencias recibió y cuántas siguen con el técnico.
        /// </summary>
        [HttpGet("bandeja")]
        public async Task<IActionResult> ObtenerBandeja(
            [FromQuery] string modo = "mis",
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            string modoNormalizado = (modo ?? "mis")
                .Trim()
                .ToLowerInvariant();

            IActionResult? acceso = await ValidarAccesoBandejaAsync(
                usuarioId.Value,
                modoNormalizado,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await database.InicializarAsync(cancellationToken);
            await control.InicializarAsync(cancellationToken);

            IQueryable<DiagnosticoIA> consulta = diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .Where(item => item.Activo);

            if (modoNormalizado is not "analizador" and
                not "aprobador" and
                not "historial")
            {
                consulta = consulta.Where(item =>
                    item.UsuarioSolicitanteId == usuarioId.Value);
            }

            List<DiagnosticoIA> inspecciones = await consulta
                .Include(item => item.Imagenes)
                .OrderByDescending(item => item.FechaSolicitudUtc)
                .ThenByDescending(item => item.DiagnosticoIAId)
                .Take(300)
                .ToListAsync(cancellationToken);

            int[] ids = inspecciones
                .Select(item => item.DiagnosticoIAId)
                .ToArray();

            Dictionary<int, List<FotoMetadatos>> fotosPorInspeccion =
                await database.ObtenerFotosPorDiagnosticosAsync(
                    ids,
                    cancellationToken);

            Dictionary<int, InspeccionFitosanitariaControlRegistro> controles =
                await control.ObtenerPorInspeccionesAsync(
                    ids,
                    cancellationToken);

            var data = new List<InspeccionFitosanitariaFlujoListaDto>();

            foreach (DiagnosticoIA inspeccion in inspecciones)
            {
                List<FotoMetadatos> fotos =
                    (fotosPorInspeccion.GetValueOrDefault(
                        inspeccion.DiagnosticoIAId) ?? [])
                    .Where(item => item.Activo)
                    .ToList();

                InspeccionFitosanitariaControlRegistro controlActual =
                    controles.GetValueOrDefault(
                        inspeccion.DiagnosticoIAId) ??
                    new InspeccionFitosanitariaControlRegistro
                    {
                        InspeccionId = inspeccion.DiagnosticoIAId,
                        UsuarioSolicitanteId =
                            inspeccion.UsuarioSolicitanteId,
                        NombreInspeccion =
                            $"Inspección #{inspeccion.DiagnosticoIAId}",
                        Activo = inspeccion.Activo
                    };

                bool cierreDefinitivo =
                    controlActual.CerradaDefinitiva ||
                    controlActual.CerradaTecnico;

                bool incluir = DebeIncluirse(
                    modoNormalizado,
                    cierreDefinitivo,
                    fotos);

                if (!incluir)
                    continue;

                int pendientesTecnico = fotos.Count(EsPendienteTecnico);
                int recibidasAnalizador = fotos.Count(EsRecibidaPorAnalizador);
                int erroresIA = fotos.Count(item =>
                    NormalizarEstado(item.Estado) ==
                    InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA);

                string estado =
                    InspeccionFitosanitariaFlujo.CalcularEstadoInspeccion(
                        fotos.Select(item => item.Estado),
                        cierreDefinitivo);

                data.Add(new InspeccionFitosanitariaFlujoListaDto
                {
                    InspeccionId = inspeccion.DiagnosticoIAId,
                    NombreInspeccion =
                        string.IsNullOrWhiteSpace(
                            controlActual.NombreInspeccion)
                            ? $"Inspección #{inspeccion.DiagnosticoIAId}"
                            : controlActual.NombreInspeccion.Trim(),
                    CodigoTerreno = inspeccion.CodigoTerreno,
                    FechaRegistroSistemaUtc = inspeccion.FechaSolicitudUtc,
                    Estado = estado,

                    /*
                     * El cliente MAUI conserva este nombre por compatibilidad,
                     * pero en las bandejas representa el fin de la etapa técnica.
                     */
                    CerradaTecnico = controlActual.EtapaTecnicaFinalizada,
                    FechaCierreTecnicoUtc =
                        controlActual.FechaFinEtapaTecnicaUtc,
                    TotalFotografias = fotos.Count,
                    Pendientes = modoNormalizado == "analizador"
                        ? pendientesTecnico
                        : fotos.Count(item =>
                            !InspeccionFitosanitariaFlujo.EsEstadoFinal(
                                item.Estado) &&
                            NormalizarEstado(item.Estado) !=
                                InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA),
                    ConError = erroresIA,

                    /*
                     * Para el analizador Finalizadas se usa como contador de
                     * recibidas. La página presenta el nombre correcto y evita
                     * ampliar el contrato histórico del cliente.
                     */
                    Finalizadas = modoNormalizado == "analizador"
                        ? recibidasAnalizador
                        : fotos.Count(item =>
                            InspeccionFitosanitariaFlujo.EsEstadoFinal(
                                item.Estado)),
                    UrlMiniatura = inspeccion.Imagenes
                        .OrderBy(item => item.Orden)
                        .Select(item => item.UrlImagen)
                        .FirstOrDefault() ?? string.Empty
                });
            }

            return Ok(new
            {
                success = true,
                message = modoNormalizado == "analizador"
                    ? "Inspecciones con fotografías recibidas por el analizador obtenidas correctamente."
                    : "Inspecciones obtenidas correctamente.",
                data
            });
        }

        /// <summary>
        /// Guarda la clasificación humana de una fotografía. Si el usuario
        /// solicita avanzar al aprobador pero aún faltan fotografías del técnico
        /// o clasificaciones humanas, la información se conserva como borrador.
        /// Cuando se completa la última clasificación, todas las clasificaciones
        /// listas avanzan juntas al aprobador.
        /// </summary>
        [HttpPost("{id:int}/analisis-humano-individual")]
        public async Task<IActionResult> GuardarAnalisisHumanoIndividual(
            int id,
            [FromBody] InspeccionFotosAnalisisHumanoRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAnalizador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request.Fotografias.Count != 1)
                return OperacionDebeSerIndividual();

            IActionResult? cierre = await ValidarInspeccionAbiertaAsync(
                id,
                cancellationToken);

            if (cierre != null)
                return cierre;

            InspeccionFotoAnalisisHumanoItemRequest item =
                request.Fotografias[0];

            await database.InicializarAsync(cancellationToken);

            await using var transaccion =
                await diagnosticoDb.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                DiagnosticoIA? inspeccion = await CargarInspeccionAsync(
                    id,
                    cancellationToken);

                if (inspeccion == null)
                    return NoEncontrado();

                DiagnosticoIAImagen? imagen = inspeccion.Imagenes
                    .FirstOrDefault(value =>
                        value.DiagnosticoIAImagenId == item.FotografiaId);

                if (imagen == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "La fotografía no pertenece a la inspección."
                    });
                }

                FotoMetadatos? meta = await database.ObtenerFotoAsync(
                    item.FotografiaId,
                    cancellationToken);

                if (meta == null || meta.Descartada || !meta.Activo)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "La fotografía no se encuentra disponible."
                    });
                }

                /*
                 * Una selección múltiple puede finalizar toda la revisión al
                 * procesar su última fotografía pendiente. Si el ciclo visual
                 * todavía intenta procesar otro elemento de la selección, se
                 * responde como operación idempotente en lugar de generar un
                 * error artificial.
                 */
                if (NormalizarEstado(meta.Estado) ==
                    InspeccionFitosanitariaFlujo.FotoEstados
                        .PendienteAprobacion)
                {
                    return Ok(new
                    {
                        success = true,
                        message =
                            "La fotografía ya fue enviada al aprobador al finalizar la revisión humana.",
                        data = CrearResultadoOperacion(
                            item.FotografiaId,
                            InspeccionFitosanitariaFlujo.FotoEstados
                                .PendienteAprobacion,
                            "La fotografía ya se encuentra pendiente de aprobación.")
                    });
                }

                if (NormalizarEstado(meta.Estado) is not
                    (InspeccionFitosanitariaFlujo.FotoEstados.PendienteAnalizador or
                     InspeccionFitosanitariaFlujo.FotoEstados.EnAnalisisHumano or
                     InspeccionFitosanitariaFlujo.FotoEstados.DevueltaAnalizador))
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "La fotografía no está disponible para análisis humano."
                    });
                }

                if (string.IsNullOrWhiteSpace(item.Diagnostico))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "El diagnóstico humano es obligatorio."
                    });
                }

                EstadoRevisionAnalizador estadoRevision =
                    await ObtenerEstadoRevisionAnalizadorAsync(
                        id,
                        item.FotografiaId,
                        cancellationToken);

                /*
                 * Primero se registra el análisis actual como borrador. Si esta
                 * era la última clasificación requerida y el técnico terminó su
                 * etapa, el cierre posterior convierte las últimas versiones de
                 * todas las fotografías en ENVIADO dentro de la misma transacción.
                 */
                await database.GuardarAnalisisHumanoAsync(
                    item.FotografiaId,
                    usuarioId!.Value,
                    item.CalidadEvaluacion,
                    item.EstadoGeneral,
                    item.CategoriaPrincipal,
                    SerializarLista(item.CategoriasSecundarias),
                    item.Diagnostico,
                    item.TipoDiagnostico,
                    item.Severidad,
                    item.NivelCerteza,
                    item.Observaciones,
                    enviar: false,
                    cancellationToken);

                await database.CambiarEstadoFotoAsync(
                    item.FotografiaId,
                    usuarioId.Value,
                    InspeccionFitosanitariaFlujo.FotoEstados.EnAnalisisHumano,
                    InspeccionFitosanitariaFlujo.Acciones.AnalisisHumanoGuardado,
                    "El análisis humano individual fue guardado como borrador.",
                    fechaAnalisisHumanoUtc: DateTime.UtcNow,
                    cancellationToken: cancellationToken);

                bool finalizarRevision =
                    request.EnviarAprobacion &&
                    estadoRevision.PuedeFinalizarConFotografiaActual;

                string estadoNuevo;
                string mensaje;

                if (finalizarRevision)
                {
                    await FinalizarRevisionAnalizadorAsync(
                        id,
                        usuarioId.Value,
                        cancellationToken);

                    estadoNuevo =
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .PendienteAprobacion;
                    mensaje =
                        "Todas las fotografías recibidas fueron clasificadas. La revisión humana se finalizó y quedó disponible para el aprobador.";
                }
                else
                {
                    estadoNuevo =
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .EnAnalisisHumano;

                    mensaje = request.EnviarAprobacion
                        ? estadoRevision.MensajeBloqueo
                        : "La clasificación de la fotografía quedó guardada como borrador.";
                }

                await ActualizarEstadoInspeccionAsync(
                    inspeccion,
                    cancellationToken);

                await transaccion.CommitAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = mensaje,
                    data = CrearResultadoOperacion(
                        item.FotografiaId,
                        estadoNuevo,
                        mensaje)
                });
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        /// <summary>
        /// Registra la decisión final de una sola fotografía. La aprobación de
        /// una evidencia no cambia ni completa automáticamente las demás.
        /// </summary>
        [HttpPost("{id:int}/aprobacion-individual")]
        public async Task<IActionResult> RegistrarAprobacionIndividual(
            int id,
            [FromBody] InspeccionFotosAprobacionRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (request.Fotografias.Count != 1)
                return OperacionDebeSerIndividual();

            IActionResult? cierre = await ValidarInspeccionAbiertaAsync(
                id,
                cancellationToken);

            if (cierre != null)
                return cierre;

            InspeccionFotoAprobacionItemRequest item =
                request.Fotografias[0];

            if (!InspeccionFitosanitariaFlujo.DecisionesAprobacion.Todas
                    .Contains(item.Decision ?? string.Empty))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La decisión de aprobación no es válida."
                });
            }

            await database.InicializarAsync(cancellationToken);

            await using var transaccion =
                await diagnosticoDb.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                DiagnosticoIA? inspeccion = await CargarInspeccionAsync(
                    id,
                    cancellationToken);

                if (inspeccion == null)
                    return NoEncontrado();

                DiagnosticoIAImagen? imagen = inspeccion.Imagenes
                    .FirstOrDefault(value =>
                        value.DiagnosticoIAImagenId == item.FotografiaId);

                if (imagen == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "La fotografía no pertenece a la inspección."
                    });
                }

                FotoMetadatos? meta = await database.ObtenerFotoAsync(
                    item.FotografiaId,
                    cancellationToken);

                if (meta == null || NormalizarEstado(meta.Estado) !=
                    InspeccionFitosanitariaFlujo.FotoEstados
                        .PendienteAprobacion)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "La fotografía no está pendiente de aprobación."
                    });
                }

                AnalisisHumanoRegistro? analisis =
                    await database.ObtenerUltimoAnalisisHumanoAsync(
                        item.FotografiaId,
                        cancellationToken);

                if (analisis == null ||
                    !string.Equals(
                        analisis.EstadoRegistro,
                        "ENVIADO",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "No existe un análisis humano enviado para esta fotografía."
                    });
                }

                string decision = item.Decision.Trim().ToUpperInvariant();
                string estadoNuevo = decision switch
                {
                    InspeccionFitosanitariaFlujo.DecisionesAprobacion.Aprobar =>
                        InspeccionFitosanitariaFlujo.FotoEstados.Aprobada,
                    InspeccionFitosanitariaFlujo.DecisionesAprobacion
                        .AprobarConCorreccion =>
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .AprobadaConCorreccion,
                    InspeccionFitosanitariaFlujo.DecisionesAprobacion.Devolver =>
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .DevueltaAnalizador,
                    InspeccionFitosanitariaFlujo.DecisionesAprobacion.Rechazar =>
                        InspeccionFitosanitariaFlujo.FotoEstados.Rechazada,
                    _ => InspeccionFitosanitariaFlujo.FotoEstados.NoConcluyente
                };

                bool decisionPositiva = estadoNuevo is
                    InspeccionFitosanitariaFlujo.FotoEstados.Aprobada or
                    InspeccionFitosanitariaFlujo.FotoEstados
                        .AprobadaConCorreccion;

                await database.RegistrarAprobacionAsync(
                    item.FotografiaId,
                    analisis.AnalisisHumanoId,
                    usuarioId!.Value,
                    decision,
                    ValorOAnterior(
                        item.CalidadEvaluacionFinal,
                        analisis.CalidadEvaluacion),
                    ValorOAnterior(
                        item.EstadoGeneralFinal,
                        analisis.EstadoGeneral),
                    ValorOAnterior(
                        item.CategoriaPrincipalFinal,
                        analisis.CategoriaPrincipal),
                    item.CategoriasSecundariasFinales.Count > 0
                        ? SerializarLista(
                            item.CategoriasSecundariasFinales)
                        : analisis.CategoriasSecundariasJson,
                    ValorOAnterior(
                        item.DiagnosticoFinal,
                        analisis.Diagnostico),
                    ValorOAnterior(
                        item.TipoDiagnosticoFinal,
                        analisis.TipoDiagnostico),
                    ValorOAnterior(
                        item.SeveridadFinal,
                        analisis.Severidad),
                    ValorOAnterior(
                        item.NivelCertezaFinal,
                        analisis.NivelCerteza),
                    item.Observaciones,
                    decisionPositiva && item.AutorizaPublicacionAlbum,
                    analisis.UsuarioAnalizadorId == usuarioId.Value,
                    cancellationToken);

                await database.CambiarEstadoFotoAsync(
                    item.FotografiaId,
                    usuarioId.Value,
                    estadoNuevo,
                    InspeccionFitosanitariaFlujo.Acciones
                        .AprobacionRegistrada,
                    $"El aprobador registró la decisión individual {decision}.",
                    fechaAprobacionUtc: DateTime.UtcNow,
                    cancellationToken: cancellationToken);

                await ActualizarEstadoInspeccionAsync(
                    inspeccion,
                    cancellationToken);

                await transaccion.CommitAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "La decisión de la fotografía fue registrada correctamente.",
                    data = CrearResultadoOperacion(
                        item.FotografiaId,
                        estadoNuevo,
                        "Decisión registrada correctamente.")
                });
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private async Task<EstadoRevisionAnalizador>
            ObtenerEstadoRevisionAnalizadorAsync(
                int inspeccionId,
                int fotografiaActualId,
                CancellationToken cancellationToken)
        {
            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(
                    inspeccionId,
                    cancellationToken);

            List<FotoMetadatos> fotos = (await database.ObtenerFotosAsync(
                    inspeccionId,
                    cancellationToken))
                .Where(item => item.Activo && !item.Descartada)
                .ToList();

            Dictionary<int, AnalisisHumanoRegistro> analisis =
                await database.ObtenerUltimosAnalisisHumanosAsync(
                    inspeccionId,
                    cancellationToken);

            int pendientesTecnico = fotos.Count(EsPendienteTecnico);
            int faltanAnalisisOtros = fotos.Count(item =>
                item.FotografiaId != fotografiaActualId &&
                !analisis.ContainsKey(item.FotografiaId));

            bool etapaFinalizada =
                registro?.EtapaTecnicaFinalizada == true;

            bool puedeFinalizar =
                etapaFinalizada &&
                pendientesTecnico == 0 &&
                faltanAnalisisOtros == 0 &&
                fotos.Count > 0;

            string mensaje;

            if (!etapaFinalizada)
            {
                mensaje = pendientesTecnico > 0
                    ? $"La clasificación se guardó como borrador. El técnico todavía debe enviar o descartar {pendientesTecnico} fotografía(s)."
                    : "La clasificación se guardó como borrador. El técnico todavía debe finalizar su etapa antes de enviar la revisión al aprobador.";
            }
            else if (faltanAnalisisOtros > 0)
            {
                mensaje =
                    $"La clasificación se guardó como borrador. Faltan {faltanAnalisisOtros} fotografía(s) por clasificar antes de finalizar la revisión humana.";
            }
            else
            {
                mensaje =
                    "La revisión está completa y puede enviarse al aprobador.";
            }

            return new EstadoRevisionAnalizador(
                puedeFinalizar,
                mensaje);
        }

        /// <summary>
        /// Convierte las últimas clasificaciones de todas las fotografías
        /// activas no descartadas en versiones enviadas y las mueve juntas a
        /// PENDIENTE_APROBACION. Se ejecuta dentro de la transacción abierta por
        /// GuardarAnalisisHumanoIndividual.
        /// </summary>
        private async Task FinalizarRevisionAnalizadorAsync(
            int inspeccionId,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            await diagnosticoDb.Database.ExecuteSqlInterpolatedAsync(
                $"""
DECLARE @ahora DATETIME2(0) = SYSUTCDATETIME();

WITH ultimos AS
(
    SELECT
        h.DiagnosticoIAImagenAnalisisHumanoId,
        ROW_NUMBER() OVER
        (
            PARTITION BY h.DiagnosticoIAImagenId
            ORDER BY h.Version DESC,
                     h.DiagnosticoIAImagenAnalisisHumanoId DESC
        ) AS rn
    FROM dbo.diagnosticoIAImagenAnalisisHumano h
    INNER JOIN dbo.diagnosticoIAImagen i
        ON i.DiagnosticoIAImagenId = h.DiagnosticoIAImagenId
    WHERE i.DiagnosticoIAId = {inspeccionId}
      AND ISNULL(i.Activo, 1) = 1
      AND ISNULL(i.Descartada, 0) = 0
)
UPDATE h
SET h.EstadoRegistro = N'ENVIADO',
    h.FechaActualizacionUtc = @ahora,
    h.FechaEnvioUtc = COALESCE(h.FechaEnvioUtc, @ahora)
FROM dbo.diagnosticoIAImagenAnalisisHumano h
INNER JOIN ultimos u
    ON u.DiagnosticoIAImagenAnalisisHumanoId =
       h.DiagnosticoIAImagenAnalisisHumanoId
WHERE u.rn = 1;

INSERT INTO dbo.diagnosticoIAImagenHistorialV2
(
    DiagnosticoIAImagenId,
    UsuarioId,
    EstadoAnterior,
    EstadoNuevo,
    Accion,
    Detalle,
    FechaUtc
)
SELECT
    i.DiagnosticoIAImagenId,
    {usuarioId},
    UPPER(ISNULL(i.Estado, N'BORRADOR')),
    N'PENDIENTE_APROBACION',
    N'ANALISIS_HUMANO_ENVIADO',
    N'El analizador finalizó la revisión completa y envió la fotografía al aprobador.',
    @ahora
FROM dbo.diagnosticoIAImagen i
WHERE i.DiagnosticoIAId = {inspeccionId}
  AND ISNULL(i.Activo, 1) = 1
  AND ISNULL(i.Descartada, 0) = 0
  AND UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
  (
      N'PENDIENTE_ANALIZADOR',
      N'EN_ANALISIS_HUMANO',
      N'DEVUELTO_PARA_CORRECCION',
      N'DEVUELTA_AL_ANALIZADOR'
  );

UPDATE dbo.diagnosticoIAImagen
SET Estado = N'PENDIENTE_APROBACION',
    FechaAnalisisHumanoUtc = COALESCE(FechaAnalisisHumanoUtc, @ahora)
WHERE DiagnosticoIAId = {inspeccionId}
  AND ISNULL(Activo, 1) = 1
  AND ISNULL(Descartada, 0) = 0
  AND UPPER(ISNULL(Estado, N'BORRADOR')) IN
  (
      N'PENDIENTE_ANALIZADOR',
      N'EN_ANALISIS_HUMANO',
      N'DEVUELTO_PARA_CORRECCION',
      N'DEVUELTA_AL_ANALIZADOR'
  );
""",
                cancellationToken);
        }

        private async Task<IActionResult?> ValidarAccesoBandejaAsync(
            int usuarioId,
            string modo,
            CancellationToken cancellationToken)
        {
            if (modo == "analizador")
            {
                return await ValidarPermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAnalizador,
                    TipoPermisoApi.Leer,
                    cancellationToken);
            }

            if (modo == "aprobador")
            {
                return await ValidarPermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAprobador,
                    TipoPermisoApi.Leer,
                    cancellationToken);
            }

            if (modo == "historial")
            {
                bool permitido =
                    await TienePermisoAsync(
                        usuarioId,
                        DiagnosticoIAFlujo.InterfazSolicitud,
                        TipoPermisoApi.Leer,
                        cancellationToken) ||
                    await TienePermisoAsync(
                        usuarioId,
                        DiagnosticoIAFlujo.InterfazAnalizador,
                        TipoPermisoApi.Leer,
                        cancellationToken) ||
                    await TienePermisoAsync(
                        usuarioId,
                        DiagnosticoIAFlujo.InterfazAprobador,
                        TipoPermisoApi.Leer,
                        cancellationToken);

                return permitido
                    ? null
                    : Forbid();
            }

            return await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazSolicitud,
                TipoPermisoApi.Leer,
                cancellationToken);
        }

        private static bool DebeIncluirse(
            string modo,
            bool cerradaDefinitiva,
            IReadOnlyCollection<FotoMetadatos> fotos)
        {
            if (modo == "analizador")
            {
                return !cerradaDefinitiva && fotos.Any(item =>
                    NormalizarEstado(item.Estado) is
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .PendienteAnalizador or
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .EnAnalisisHumano or
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .DevueltaAnalizador);
            }

            if (modo == "aprobador")
            {
                return !cerradaDefinitiva && fotos.Any(item =>
                    NormalizarEstado(item.Estado) ==
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .PendienteAprobacion);
            }

            if (modo == "historial")
            {
                return cerradaDefinitiva &&
                       fotos.Count > 0 &&
                       fotos.All(item =>
                           InspeccionFitosanitariaFlujo.EsEstadoFinal(
                               item.Estado));
            }

            return true;
        }

        private static bool EsPendienteTecnico(FotoMetadatos foto) =>
            !foto.Descartada && NormalizarEstado(foto.Estado) is
                InspeccionFitosanitariaFlujo.FotoEstados.Borrador or
                InspeccionFitosanitariaFlujo.FotoEstados.PendienteIA or
                InspeccionFitosanitariaFlujo.FotoEstados.AnalizandoIA or
                InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA or
                InspeccionFitosanitariaFlujo.FotoEstados
                    .PendienteDecisionTecnico;

        private static bool EsRecibidaPorAnalizador(FotoMetadatos foto) =>
            !foto.Descartada && !EsPendienteTecnico(foto);

        private static string NormalizarEstado(string? estado) =>
            (estado ?? string.Empty).Trim().ToUpperInvariant();

        private async Task<DiagnosticoIA?> CargarInspeccionAsync(
            int id,
            CancellationToken cancellationToken) =>
            await diagnosticoDb.Diagnosticos
                .Include(item => item.Imagenes)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(item =>
                    item.DiagnosticoIAId == id && item.Activo,
                    cancellationToken);

        private async Task ActualizarEstadoInspeccionAsync(
            DiagnosticoIA inspeccion,
            CancellationToken cancellationToken)
        {
            List<FotoMetadatos> fotos = await database.ObtenerFotosAsync(
                inspeccion.DiagnosticoIAId,
                cancellationToken);

            InspeccionFitosanitariaControlRegistro? controlActual =
                await control.ObtenerAsync(
                    inspeccion.DiagnosticoIAId,
                    cancellationToken);

            bool cerradaDefinitiva =
                controlActual?.CerradaDefinitiva == true ||
                controlActual?.CerradaTecnico == true;

            string estadoNuevo =
                InspeccionFitosanitariaFlujo.CalcularEstadoInspeccion(
                    fotos.Where(item => item.Activo)
                        .Select(item => item.Estado),
                    cerradaDefinitiva);

            if (!string.Equals(
                    inspeccion.Estado,
                    estadoNuevo,
                    StringComparison.OrdinalIgnoreCase))
            {
                inspeccion.Estado = estadoNuevo;
                await diagnosticoDb.SaveChangesAsync(cancellationToken);
            }
        }

        private async Task<IActionResult?> ValidarInspeccionAbiertaAsync(
            int id,
            CancellationToken cancellationToken)
        {
            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(id, cancellationToken);

            if (registro == null || !registro.Activo)
                return NoEncontrado();

            if (!registro.CerradaTecnico && !registro.CerradaDefinitiva)
                return null;

            return Conflict(new
            {
                success = false,
                message =
                    "La inspección está cerrada definitivamente y solo puede consultarse."
            });
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            int? usuarioId,
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            if (!usuarioId.HasValue)
                return Forbid();

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                tipo,
                cancellationToken);

            if (permiso.Permitido)
                return null;

            return StatusCode(
                permiso.CodigoEstado,
                new
                {
                    success = false,
                    message = permiso.Mensaje
                });
        }

        private async Task<bool> TienePermisoAsync(
            int usuarioId,
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                tipo,
                cancellationToken);

            return permiso.Permitido;
        }

        private int? ObtenerUsuarioId()
        {
            string? valor = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                            User.FindFirstValue("usuarioId") ??
                            User.FindFirstValue("sub");

            return int.TryParse(valor, out int id) && id > 0
                ? id
                : null;
        }

        private IActionResult NoEncontrado() =>
            NotFound(new
            {
                success = false,
                message = "No se encontró la inspección indicada."
            });

        private IActionResult OperacionDebeSerIndividual() =>
            BadRequest(new
            {
                success = false,
                message =
                    "La operación debe contener exactamente una fotografía. Cada evidencia mantiene sus propios datos y decisiones."
            });

        private static InspeccionOperacionMasivaDto CrearResultadoOperacion(
            int fotografiaId,
            string estado,
            string mensaje) =>
            new()
            {
                TotalSolicitadas = 1,
                TotalExitosas = 1,
                TotalConError = 0,
                Resultados =
                [
                    new InspeccionOperacionItemDto
                    {
                        FotografiaId = fotografiaId,
                        Exitoso = true,
                        Estado = estado,
                        Mensaje = mensaje
                    }
                ]
            };

        private static string SerializarLista(IEnumerable<string>? valores) =>
            JsonSerializer.Serialize(
                (valores ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());

        private static string ValorOAnterior(
            string? nuevo,
            string anterior) =>
            string.IsNullOrWhiteSpace(nuevo)
                ? anterior
                : nuevo.Trim();

        private sealed record EstadoRevisionAnalizador(
            bool PuedeFinalizarConFotografiaActual,
            string MensajeBloqueo);
    }

    /// <summary>
    /// Respuesta compatible con las bandejas MAUI, enriquecida con el nombre
    /// de la inspección y los contadores de recepción progresiva.
    /// </summary>
    public sealed class InspeccionFitosanitariaFlujoListaDto
    {
        public int InspeccionId { get; set; }
        public string NombreInspeccion { get; set; } = string.Empty;
        public string CodigoTerreno { get; set; } = string.Empty;
        public DateTime FechaRegistroSistemaUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public bool CerradaTecnico { get; set; }
        public DateTime? FechaCierreTecnicoUtc { get; set; }
        public int TotalFotografias { get; set; }
        public int Pendientes { get; set; }
        public int ConError { get; set; }
        public int Finalizadas { get; set; }
        public string UrlMiniatura { get; set; } = string.Empty;
    }
}
