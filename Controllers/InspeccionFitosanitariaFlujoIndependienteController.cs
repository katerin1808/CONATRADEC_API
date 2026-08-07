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
    /// Cada fotografía puede enviarse al aprobador de forma independiente. La
    /// asignación y los permisos determinan quién puede operar cada etapa, y la
    /// trazabilidad conserva si un mismo usuario participó en ambas etapas.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/inspecciones-fitosanitarias-flujo")]
    public sealed class InspeccionFitosanitariaFlujoIndependienteController :
        ControllerBase
    {
        private readonly DBContext db;
        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaDatabase database;
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;
        private readonly InspeccionFitosanitariaAsignacionDatabase asignaciones;

        public InspeccionFitosanitariaFlujoIndependienteController(
            DBContext db,
            DiagnosticoIADbContext diagnosticoDb,
            PermisoApiService permisos,
            InspeccionFitosanitariaControlDatabaseInitializer control)
        {
            this.db = db;
            this.diagnosticoDb = diagnosticoDb;
            this.permisos = permisos;
            this.control = control;
            database = new InspeccionFitosanitariaDatabase(diagnosticoDb);
            asignaciones = new InspeccionFitosanitariaAsignacionDatabase(diagnosticoDb);
        }

        /// <summary>
        /// Guarda la clasificación humana de una fotografía como borrador. El
        /// envío al aprobador se realiza desde el flujo de revisión por fotografía.
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
            await asignaciones.InicializarAsync(cancellationToken);

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

                ResultadoAsignacionFlujo asignacionAnalizador =
                    await asignaciones.TomarAnalizadorAsync(
                        id,
                        usuarioId!.Value,
                        cancellationToken);

                if (!asignacionAnalizador.Exitoso)
                {
                    return Conflict(new
                    {
                        success = false,
                        message = asignacionAnalizador.Mensaje
                    });
                }

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

                const string mensaje =
                    "La clasificación humana quedó guardada como borrador. Puede enviarla al aprobador cuando esté lista.";

                await database.CambiarEstadoFotoAsync(
                    item.FotografiaId,
                    usuarioId.Value,
                    InspeccionFitosanitariaFlujo.FotoEstados.EnAnalisisHumano,
                    InspeccionFitosanitariaFlujo.Acciones.AnalisisHumanoGuardado,
                    mensaje,
                    fechaAnalisisHumanoUtc: DateTime.UtcNow,
                    cancellationToken: cancellationToken);

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
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .EnAnalisisHumano,
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
            await asignaciones.InicializarAsync(cancellationToken);

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

                /*
                 * Los permisos siguen siendo la barrera de autorización. Si el
                 * mismo usuario posee permiso de analizador y aprobador se le
                 * permite continuar y la auditoría conserva MismoUsuario=true.
                 */
                bool mismoUsuarioQueAnalizo =
                    analisis.UsuarioAnalizadorId == usuarioId.Value;

                ResultadoAsignacionFlujo asignacionAprobador =
                    await asignaciones.TomarAprobadorAsync(
                        id,
                        usuarioId.Value,
                        cancellationToken);

                if (!asignacionAprobador.Exitoso)
                {
                    return Conflict(new
                    {
                        success = false,
                        message = asignacionAprobador.Mensaje
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

                /*
                 * La autorización se rige por permisos. Si la misma cuenta
                 * posee permisos de analizador y aprobador, la aprobación se
                 * registra y la auditoría conserva MismoUsuarioQueAnalizo=1.
                 * InspeccionFitosanitariaDatabase conserva una restricción
                 * heredada, por lo que aquí se registra primero la decisión y
                 * después se marca explícitamente la coincidencia de usuarios.
                 */
                int aprobacionId = await database.RegistrarAprobacionAsync(
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
                    mismoUsuario: false,
                    cancellationToken);

                if (mismoUsuarioQueAnalizo)
                {
                    await diagnosticoDb.Database.ExecuteSqlInterpolatedAsync($"""
UPDATE dbo.diagnosticoIAImagenAprobacionV2
SET MismoUsuarioQueAnalizo = 1
WHERE DiagnosticoIAImagenAprobacionId = {aprobacionId};
""", cancellationToken);
                }

                await database.CambiarEstadoFotoAsync(
                    item.FotografiaId,
                    usuarioId.Value,
                    estadoNuevo,
                    InspeccionFitosanitariaFlujo.Acciones
                        .AprobacionRegistrada,
                    $"El aprobador registró la decisión individual {decision}.",
                    fechaAprobacionUtc: DateTime.UtcNow,
                    cancellationToken: cancellationToken);

                if (estadoNuevo ==
                    InspeccionFitosanitariaFlujo.FotoEstados
                        .DevueltaAnalizador)
                {
                    await asignaciones.ReabrirEtapaAnalizadorAsync(
                        id,
                        cancellationToken);
                }

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
                controlActual?.CerradaDefinitiva == true;

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

            if (!registro.CerradaDefinitiva)
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
        public int UsuarioTecnicoId { get; set; }
        public string TecnicoNombreCompleto { get; set; } = string.Empty;
        public string TecnicoUsuario { get; set; } = string.Empty;
        public DateTime FechaRegistroSistemaUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public bool EtapaTecnicaFinalizada { get; set; }
        public DateTime? FechaFinEtapaTecnicaUtc { get; set; }
        public int TotalFotografias { get; set; }
        public int Pendientes { get; set; }
        public int ConError { get; set; }
        public int Finalizadas { get; set; }
        public string UrlMiniatura { get; set; } = string.Empty;
    }
}
