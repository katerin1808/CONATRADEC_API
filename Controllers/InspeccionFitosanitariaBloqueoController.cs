using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Reserva temporalmente una inspección para una sola sesión por etapa.
    /// ANALIZADOR y APROBADOR poseen bloqueos independientes para conservar el
    /// trabajo por fotografía sin permitir dos usuarios simultáneos en la
    /// misma etapa.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/revision-fitosanitaria/{id:int}/bloqueo")]
    public sealed class InspeccionFitosanitariaBloqueoController : ControllerBase
    {
        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly DBContext db;
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaBloqueoDatabase bloqueos;
        private readonly InspeccionFitosanitariaAsignacionDatabase asignaciones;

        public InspeccionFitosanitariaBloqueoController(
            DiagnosticoIADbContext diagnosticoDb,
            DBContext db,
            PermisoApiService permisos)
        {
            this.diagnosticoDb = diagnosticoDb;
            this.db = db;
            this.permisos = permisos;
            bloqueos = new InspeccionFitosanitariaBloqueoDatabase(diagnosticoDb);
            asignaciones = new InspeccionFitosanitariaAsignacionDatabase(
                diagnosticoDb);
        }

        [HttpPost("adquirir")]
        public async Task<IActionResult> Adquirir(
            int id,
            [FromQuery] string modo,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            string etapa = NormalizarModo(modo);
            if (string.IsNullOrWhiteSpace(etapa))
            {
                return BadRequest(Error(
                    "El bloqueo exclusivo solo está disponible para analizador o aprobador."));
            }

            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId.Value,
                etapa,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            bool existe = await diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .AnyAsync(item =>
                    item.DiagnosticoIAId == id &&
                    item.Activo,
                    cancellationToken);

            if (!existe)
            {
                return NotFound(Error(
                    "La inspección indicada no existe o ya no está activa."));
            }

            /*
             * La reserva temporal no debe apropiarse permanentemente de la
             * etapa solo por abrir la pantalla. Si ya existe una asignación
             * funcional previa, se respeta; cuando aún no existe, la primera
             * operación real será la que asigne el expediente como hasta ahora.
             */
            InspeccionFitosanitariaAsignacionRegistro asignacion =
                await asignaciones.ObtenerAsync(id, cancellationToken);

            int? asignado = etapa == "ANALIZADOR"
                ? asignacion.UsuarioAnalizadorId
                : asignacion.UsuarioAprobadorId;

            if (asignado.HasValue && asignado.Value != usuarioId.Value)
            {
                string nombreAsignado = await ObtenerNombreUsuarioAsync(
                    asignado,
                    cancellationToken);

                return Conflict(new
                {
                    success = false,
                    message = string.IsNullOrWhiteSpace(nombreAsignado)
                        ? $"La inspección ya está asignada a otro usuario para la etapa de {NombreEtapa(etapa)}."
                        : $"La inspección está asignada a {nombreAsignado} para la etapa de {NombreEtapa(etapa)}.",
                    data = new
                    {
                        adquirido = false,
                        inspeccionId = id,
                        modo = etapa.ToLowerInvariant(),
                        usuarioId = asignado,
                        usuarioNombre = nombreAsignado
                    }
                });
            }

            ResultadoAdquisicionBloqueoInspeccion resultado =
                await bloqueos.AdquirirAsync(
                    id,
                    usuarioId.Value,
                    etapa,
                    cancellationToken);

            if (!resultado.Exitoso || resultado.Bloqueo == null)
            {
                string nombre = await ObtenerNombreUsuarioAsync(
                    resultado.Bloqueo?.UsuarioId,
                    cancellationToken);

                string mensaje = !string.IsNullOrWhiteSpace(nombre)
                    ? resultado.Bloqueo?.UsuarioId == usuarioId.Value
                        ? $"Esta misma cuenta ya tiene abierta la inspección como {NombreEtapa(etapa)} en otra ventana o dispositivo. Cierre esa sesión o espere a que el bloqueo venza automáticamente."
                        : $"La inspección está siendo utilizada por {nombre} como {NombreEtapa(etapa)}."
                    : resultado.Mensaje;

                return Conflict(new
                {
                    success = false,
                    message = mensaje,
                    data = CrearRespuesta(
                        resultado.Bloqueo,
                        nombre,
                        adquirido: false)
                });
            }

            string usuarioNombre = await ObtenerNombreUsuarioAsync(
                usuarioId,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    $"La inspección quedó bloqueada para esta sesión de {NombreEtapa(etapa)}.",
                data = CrearRespuesta(
                    resultado.Bloqueo,
                    usuarioNombre,
                    adquirido: true)
            });
        }

        [HttpPost("renovar")]
        public async Task<IActionResult> Renovar(
            int id,
            [FromQuery] string modo,
            [FromBody] InspeccionBloqueoSesionRequest? request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            string etapa = NormalizarModo(modo);
            if (string.IsNullOrWhiteSpace(etapa) ||
                request == null ||
                !Guid.TryParse(request.Token, out Guid token))
            {
                return BadRequest(Error(
                    "La sesión de bloqueo enviada no es válida."));
            }

            BloqueoInspeccionRegistro? bloqueo = await bloqueos.RenovarAsync(
                id,
                usuarioId.Value,
                etapa,
                token,
                cancellationToken);

            if (bloqueo == null)
            {
                return Conflict(Error(
                    "El bloqueo exclusivo de esta ficha venció o fue liberado. Regrese a la bandeja y abra nuevamente la inspección."));
            }

            string usuarioNombre = await ObtenerNombreUsuarioAsync(
                usuarioId,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Bloqueo renovado correctamente.",
                data = CrearRespuesta(
                    bloqueo,
                    usuarioNombre,
                    adquirido: true)
            });
        }

        [HttpPost("liberar")]
        public async Task<IActionResult> Liberar(
            int id,
            [FromQuery] string modo,
            [FromBody] InspeccionBloqueoSesionRequest? request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            string etapa = NormalizarModo(modo);
            if (string.IsNullOrWhiteSpace(etapa) ||
                request == null ||
                !Guid.TryParse(request.Token, out Guid token))
            {
                return BadRequest(Error(
                    "La sesión de bloqueo enviada no es válida."));
            }

            await bloqueos.LiberarAsync(
                id,
                usuarioId.Value,
                etapa,
                token,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Bloqueo liberado correctamente."
            });
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            int usuarioId,
            string etapa,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            string interfaz = etapa == "ANALIZADOR"
                ? DiagnosticoIAFlujo.InterfazAnalizador
                : DiagnosticoIAFlujo.InterfazAprobador;

            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                permiso,
                cancellationToken);

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                Error(resultado.Mensaje));
        }

        private async Task<string> ObtenerNombreUsuarioAsync(
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (usuarioId is not > 0)
                return string.Empty;

            Usuario? usuario = await db.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.UsuarioId == usuarioId.Value,
                    cancellationToken);

            if (usuario == null)
                return $"usuario #{usuarioId.Value}";

            if (!string.IsNullOrWhiteSpace(usuario.nombreCompletoUsuario))
                return usuario.nombreCompletoUsuario.Trim();

            if (!string.IsNullOrWhiteSpace(usuario.nombreUsuario))
                return usuario.nombreUsuario.Trim();

            return $"usuario #{usuarioId.Value}";
        }

        private static object CrearRespuesta(
            BloqueoInspeccionRegistro? bloqueo,
            string usuarioNombre,
            bool adquirido)
        {
            if (bloqueo == null)
            {
                return new
                {
                    adquirido,
                    inspeccionId = 0,
                    modo = string.Empty,
                    usuarioId = 0,
                    usuarioNombre = usuarioNombre ?? string.Empty,
                    token = string.Empty,
                    fechaAdquisicionUtc = (DateTime?)null,
                    ultimoHeartbeatUtc = (DateTime?)null,
                    expiraUtc = (DateTime?)null,
                    vigenciaSegundos =
                        InspeccionFitosanitariaBloqueoDatabase.VigenciaSegundos
                };
            }

            return new
            {
                adquirido,
                inspeccionId = bloqueo.InspeccionId,
                modo = bloqueo.Etapa.ToLowerInvariant(),
                usuarioId = bloqueo.UsuarioId,
                usuarioNombre = usuarioNombre ?? string.Empty,
                token = adquirido
                    ? bloqueo.TokenSesion.ToString("D")
                    : string.Empty,
                fechaAdquisicionUtc = bloqueo.FechaAdquisicionUtc,
                ultimoHeartbeatUtc = bloqueo.UltimoHeartbeatUtc,
                expiraUtc = bloqueo.ExpiraUtc,
                vigenciaSegundos =
                    InspeccionFitosanitariaBloqueoDatabase.VigenciaSegundos
            };
        }

        private static string NormalizarModo(string? modo)
        {
            string valor = (modo ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            return valor switch
            {
                "analizador" => "ANALIZADOR",
                "aprobador" => "APROBADOR",
                _ => string.Empty
            };
        }

        private static string NombreEtapa(string etapa) =>
            etapa == "ANALIZADOR"
                ? "analizador"
                : "aprobador";

        private int? ObtenerUsuarioId()
        {
            string? valor = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("UsuarioId") ??
                User.FindFirstValue("usuarioId");

            return int.TryParse(valor, out int id) && id > 0
                ? id
                : null;
        }

        private static object Error(string mensaje) =>
            new
            {
                success = false,
                message = mensaje
            };
    }

    public sealed class InspeccionBloqueoSesionRequest
    {
        public string Token { get; set; } = string.Empty;
    }
}
