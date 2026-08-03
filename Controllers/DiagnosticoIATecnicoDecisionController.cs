using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/diagnostico-ia")]
    public sealed class DiagnosticoIATecnicoDecisionController : ControllerBase
    {
        private readonly DiagnosticoIADbContext db;
        private readonly PermisoApiService permisos;
        private readonly DiagnosticoIAProcesamientoQueue queue;
        private readonly DiagnosticoIAProcesamientoEstadoStore estadoStore;

        public DiagnosticoIATecnicoDecisionController(
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

        [HttpPost("{id:int}/decision-tecnico/enviar-analizador")]
        public async Task<IActionResult> EnviarAlAnalizador(
            int id,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            DiagnosticoIA? diagnostico = await CargarAsync(
                id,
                cancellationToken);

            IActionResult? acceso = await ValidarPropietarioAsync(
                diagnostico,
                usuarioId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (diagnostico!.Estado !=
                DiagnosticoIAFlujo.Estados.PendienteDecisionTecnico)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "La solicitud no está pendiente de la decisión del técnico."
                });
            }

            if (diagnostico.Imagenes.Count == 0 ||
                diagnostico.Imagenes.Any(item => item.ResultadoIA == null))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede enviar al analizador porque todavía existen fotografías sin resultado individual válido."
                });
            }

            if (diagnostico.Imagenes.Any(item =>
                    item.ResultadoIA != null &&
                    item.ResultadoIA.RequiereDecisionClasificacion &&
                    DiagnosticoIAFlujo.ClasificacionAlbum.EstaPendiente(
                        item.ResultadoIA.EstadoClasificacionAlbum)))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Antes de enviar al analizador debe resolver las clasificaciones del Álbum Botánico que Gemini dejó pendientes."
                });
            }

            if (diagnostico.RevisionesIA.Any(item =>
                    item.Estado == "ANALIZANDO"))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Gemini todavía está realizando una evaluación adicional."
                });
            }

            string anterior = diagnostico.Estado;
            diagnostico.Estado =
                DiagnosticoIAFlujo.Estados.PendienteAnalizador;

            AgregarHistorial(
                diagnostico,
                usuarioId!.Value,
                anterior,
                diagnostico.Estado,
                "TECNICO_ENVIA_ANALIZADOR",
                "El técnico revisó el resultado preliminar y decidió enviarlo a la bandeja del analizador humano.");

            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "La solicitud fue enviada al analizador humano.",
                data = new
                {
                    diagnosticoIAId = diagnostico.DiagnosticoIAId,
                    estado = diagnostico.Estado
                }
            });
        }

        [HttpPost("{id:int}/decision-tecnico/solicitar-nueva-evaluacion")]
        public async Task<IActionResult> SolicitarNuevaEvaluacion(
            int id,
            [FromBody] DiagnosticoIATecnicoRevisionRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            DiagnosticoIA? diagnostico = await CargarAsync(
                id,
                cancellationToken);

            IActionResult? acceso = await ValidarPropietarioAsync(
                diagnostico,
                usuarioId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (diagnostico!.Estado !=
                DiagnosticoIAFlujo.Estados.PendienteDecisionTecnico)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Solo puede solicitar otra evaluación mientras la solicitud esté pendiente de su decisión."
                });
            }

            string motivo = Normalizar(request.Motivo, 2000);

            if (motivo.Length < 8)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Explique con al menos 8 caracteres qué debe revisar nuevamente Gemini."
                });
            }

            if (diagnostico.RevisionesIA.Any(item =>
                    item.Estado == "ANALIZANDO"))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe una evaluación adicional en proceso."
                });
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

            bool ilimitadas =
                configuracion?.RevisionesIlimitadas ?? false;

            if (!ilimitadas && completadas >= maximo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        $"La solicitud ya alcanzó el máximo de {maximo} evaluaciones adicionales configuradas."
                });
            }

            var revision = new DiagnosticoIARevision
            {
                DiagnosticoIAId = diagnostico.DiagnosticoIAId,
                UsuarioClasificadorId = usuarioId!.Value,
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
                "TECNICO_SOLICITA_NUEVA_EVALUACION",
                $"El técnico solicitó la evaluación adicional {completadas + 1} a Gemini. Motivo: {motivo}");

            await db.SaveChangesAsync(cancellationToken);

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
                message =
                    "La nueva evaluación fue enviada a la cola del servidor.",
                data = estado
            });
        }

        [HttpPost("{id:int}/decision-tecnico/no-continuar")]
        public async Task<IActionResult> NoContinuar(
            int id,
            [FromBody] DiagnosticoIATecnicoCancelarRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            DiagnosticoIA? diagnostico = await CargarAsync(
                id,
                cancellationToken);

            IActionResult? acceso = await ValidarPropietarioAsync(
                diagnostico,
                usuarioId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (diagnostico!.Estado !=
                DiagnosticoIAFlujo.Estados.PendienteDecisionTecnico)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Solo puede detener una solicitud pendiente de su decisión."
                });
            }

            string motivo = Normalizar(request.Motivo, 1000);

            if (motivo.Length < 8)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Explique con al menos 8 caracteres por qué no desea continuar."
                });
            }

            string anterior = diagnostico.Estado;
            diagnostico.Estado =
                DiagnosticoIAFlujo.Estados.CanceladoPorTecnico;

            AgregarHistorial(
                diagnostico,
                usuarioId!.Value,
                anterior,
                diagnostico.Estado,
                "TECNICO_NO_CONTINUA",
                motivo);

            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "La solicitud fue cerrada por decisión del técnico. Las fotografías y los resultados se conservaron.",
                data = new
                {
                    diagnosticoIAId = diagnostico.DiagnosticoIAId,
                    estado = diagnostico.Estado
                }
            });
        }

        private async Task<DiagnosticoIA?> CargarAsync(
            int id,
            CancellationToken cancellationToken) =>
            await db.Diagnosticos
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.ResultadoIA)
                .Include(item => item.RevisionesIA)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(
                    item => item.DiagnosticoIAId == id && item.Activo,
                    cancellationToken);

        private async Task<IActionResult?> ValidarPropietarioAsync(
            DiagnosticoIA? diagnostico,
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (diagnostico == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La solicitud no existe."
                });
            }

            if (!usuarioId.HasValue ||
                diagnostico.UsuarioSolicitanteId != usuarioId.Value)
            {
                return Forbid();
            }

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazSolicitud,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    new
                    {
                        success = false,
                        message = permiso.Mensaje
                    });
            }

            return null;
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
    }

    public sealed class DiagnosticoIATecnicoRevisionRequest
    {
        public string Motivo { get; set; } = string.Empty;
        public string? DiagnosticoPropuesto { get; set; }
    }

    public sealed class DiagnosticoIATecnicoCancelarRequest
    {
        public string Motivo { get; set; } = string.Empty;
    }
}
