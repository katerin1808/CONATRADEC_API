using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Canal v2 para la aplicación instalada.
    ///
    /// A diferencia del endpoint administrativo, esta comprobación forma parte
    /// de la seguridad y mantenimiento del cliente instalado, por lo que está
    /// disponible para cualquier sesión JWT válida. El permiso temporal de
    /// descarga se entrega separado de la URL y debe enviarse posteriormente en
    /// X-Permiso-Descarga.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/actualizaciones/aplicacion/v2")]
    public sealed class ActualizacionesAplicacionV2Controller :
        ControllerBase
    {
        private const string EstadoPublicada = "PUBLICADA";

        private static readonly TimeSpan VigenciaPermiso =
            TimeSpan.FromHours(2);

        private readonly ActualizacionesDbContext actualizacionesDb;
        private readonly IWebHostEnvironment environment;

        public ActualizacionesAplicacionV2Controller(
            ActualizacionesDbContext actualizacionesDb,
            IWebHostEnvironment environment)
        {
            this.actualizacionesDb = actualizacionesDb;
            this.environment = environment;
        }

        [HttpGet("comprobar")]
        public async Task<IActionResult> Comprobar(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromQuery] string plataforma,
            [FromQuery] long versionCodigo,
            [FromQuery] string canal = "PRODUCCION",
            CancellationToken cancellationToken = default)
        {
            if (!usuarioSesionId.HasValue ||
                usuarioSesionId.Value <= 0)
            {
                return Unauthorized(new
                {
                    success = false,
                    message =
                        "No se encontró una sesión autenticada válida."
                });
            }

            string plataformaNormalizada =
                NormalizarPlataforma(plataforma);

            string canalNormalizado =
                NormalizarCanal(canal);

            if (string.IsNullOrWhiteSpace(plataformaNormalizada) ||
                string.IsNullOrWhiteSpace(canalNormalizado))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La plataforma o el canal no son válidos."
                });
            }

            List<ActualizacionAplicacion> disponibles =
                await actualizacionesDb.ActualizacionesAplicacion
                    .AsNoTracking()
                    .Where(x =>
                        x.Activo &&
                        x.Estado == EstadoPublicada &&
                        x.Plataforma == plataformaNormalizada &&
                        x.Canal == canalNormalizado &&
                        x.VersionCodigo > versionCodigo)
                    .OrderByDescending(x => x.VersionCodigo)
                    .ToListAsync(cancellationToken);

            ActualizacionAplicacion? actualizacion =
                disponibles.FirstOrDefault();

            if (actualizacion == null)
            {
                return Ok(new
                {
                    success = true,
                    message = "La aplicación está actualizada.",
                    actualizacionDisponible = false,
                    data = (object?)null
                });
            }

            if (!System.IO.File.Exists(
                    actualizacion.RutaArchivo))
            {
                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        success = false,
                        message =
                            "La versión está publicada, pero su instalador no está disponible en el servidor."
                    });
            }

            /*
             * Una versión obligatoria intermedia no puede quedar anulada por
             * una publicación posterior marcada como opcional.
             */
            bool obligatoria = disponibles.Any(x =>
                x.Obligatoria ||
                (x.VersionMinimaCodigo.HasValue &&
                 versionCodigo < x.VersionMinimaCodigo.Value));

            string operacionId =
                ActualizacionDescargaTokenService.NuevaOperacionId();

            string permisoDescarga =
                ActualizacionDescargaTokenService.Crear(
                    environment,
                    actualizacion.ActualizacionAplicacionId,
                    null,
                    operacionId,
                    VigenciaPermiso);

            var data = new ActualizacionDisponibleV2Dto
            {
                ActualizacionAplicacionId =
                    actualizacion.ActualizacionAplicacionId,
                Plataforma = actualizacion.Plataforma,
                Canal = actualizacion.Canal,
                VersionNombre = actualizacion.VersionNombre,
                VersionCodigo = actualizacion.VersionCodigo,
                NotasVersion = actualizacion.NotasVersion,
                Obligatoria = obligatoria,
                VersionMinimaCodigo =
                    actualizacion.VersionMinimaCodigo,
                NombreArchivo = actualizacion.NombreArchivo,
                TipoContenido = actualizacion.TipoContenido,
                TamanoBytes = actualizacion.TamanoBytes,
                HashSha256 = actualizacion.HashSha256,
                UrlDescarga = ConstruirUrlDescarga(
                    actualizacion.ActualizacionAplicacionId),
                PermisoDescarga = permisoDescarga,
                FechaPublicacionUtc =
                    actualizacion.FechaPublicacionUtc
            };

            await RegistrarAuditoriaAsync(
                actualizacion,
                usuarioSesionId.Value,
                operacionId,
                cancellationToken);

            Response.Headers["Cache-Control"] = "no-store";

            return Ok(new
            {
                success = true,
                message = obligatoria
                    ? "Existe una actualización obligatoria."
                    : "Existe una nueva actualización disponible.",
                actualizacionDisponible = true,
                data
            });
        }

        private async Task RegistrarAuditoriaAsync(
            ActualizacionAplicacion actualizacion,
            int usuarioId,
            string operacionId,
            CancellationToken cancellationToken)
        {
            string forwardedFor =
                Request.Headers["X-Forwarded-For"]
                    .ToString();

            string ip = ObtenerIpCliente(
                HttpContext.Connection.RemoteIpAddress,
                forwardedFor);

            string agente =
                Request.Headers["User-Agent"]
                    .ToString();

            string dispositivo =
                DecodificarEncabezado(
                    Request.Headers["X-Dispositivo"]
                        .ToString());

            string plataformaCliente =
                DecodificarEncabezado(
                    Request.Headers["X-Plataforma"]
                        .ToString());

            actualizacionesDb.AuditoriaDescargas.Add(
                new ActualizacionDescargaAuditoria
                {
                    ActualizacionLlaveDescargaId = null,
                    ActualizacionAplicacionId =
                        actualizacion.ActualizacionAplicacionId,
                    OperacionId = operacionId,
                    Resultado = "AUTORIZADA_APLICACION",
                    Detalle =
                        "La sesión autenticada recibió un permiso temporal de descarga mediante el canal v2.",
                    Plataforma = actualizacion.Plataforma,
                    Canal = actualizacion.Canal,
                    VersionNombre = actualizacion.VersionNombre,
                    VersionCodigo = actualizacion.VersionCodigo,
                    NombreArchivo = actualizacion.NombreArchivo,
                    IpCliente = Limitar(ip, 80),
                    EncabezadoForwardedFor =
                        Limitar(forwardedFor, 500),
                    AgenteUsuario = Limitar(agente, 1000),
                    Navegador = "Aplicación CONATRADEC",
                    SistemaOperativo =
                        Limitar(plataformaCliente, 100),
                    TipoDispositivo =
                        "Aplicación instalada",
                    IdentificadorDispositivoWeb =
                        Limitar(dispositivo, 100),
                    Destinatario =
                        $"Usuario {usuarioId}",
                    UsuarioGeneradorId = usuarioId,
                    FechaUtc = DateTime.UtcNow
                });

            await actualizacionesDb.SaveChangesAsync(
                cancellationToken);
        }

        private string ConstruirUrlDescarga(
            int actualizacionId)
        {
            string baseUrl = ObtenerBaseUrlPublica();

            return baseUrl.TrimEnd('/') +
                   $"/api/actualizaciones/descargar/{actualizacionId}";
        }

        private string ObtenerBaseUrlPublica()
        {
            string scheme = Request.Scheme;
            HostString host = Request.Host;

            if (EsProxyPrivado(
                    HttpContext.Connection.RemoteIpAddress))
            {
                string forwardedProto =
                    Request.Headers["X-Forwarded-Proto"]
                        .ToString()
                        .Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .FirstOrDefault() ??
                    string.Empty;

                if (forwardedProto is "http" or "https")
                    scheme = forwardedProto;

                string forwardedHost =
                    Request.Headers["X-Forwarded-Host"]
                        .ToString()
                        .Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .FirstOrDefault() ??
                    string.Empty;

                if (Uri.TryCreate(
                        $"{scheme}://{forwardedHost}",
                        UriKind.Absolute,
                        out Uri? publicUri))
                {
                    host =
                        new HostString(
                            publicUri.Authority);
                }
            }

            return $"{scheme}://{host}{Request.PathBase}";
        }

        private static string ObtenerIpCliente(
            IPAddress? remota,
            string forwardedFor)
        {
            string ip =
                remota?.ToString() ??
                "desconocida";

            if (!EsProxyPrivado(remota) ||
                string.IsNullOrWhiteSpace(forwardedFor))
            {
                return ip;
            }

            string candidata = forwardedFor
                .Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .FirstOrDefault() ??
                string.Empty;

            return IPAddress.TryParse(candidata, out _)
                ? candidata
                : ip;
        }

        private static string DecodificarEncabezado(
            string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return string.Empty;

            try
            {
                return Uri.UnescapeDataString(valor);
            }
            catch (UriFormatException)
            {
                return valor;
            }
        }

        private static string NormalizarPlataforma(
            string? valor)
        {
            string normalizado =
                (valor ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant();

            return normalizado switch
            {
                "ANDROID" => "ANDROID",
                "WINDOWS" => "WINDOWS",
                "WINUI" => "WINDOWS",
                _ => string.Empty
            };
        }

        private static string NormalizarCanal(
            string? valor)
        {
            string normalizado =
                (valor ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant();

            return normalizado switch
            {
                "PRODUCCION" => "PRODUCCION",
                "PRODUCCIÓN" => "PRODUCCION",
                "PRUEBAS" => "PRUEBAS",
                _ => string.Empty
            };
        }

        private static bool EsProxyPrivado(
            IPAddress? ip)
        {
            if (ip == null ||
                IPAddress.IsLoopback(ip))
            {
                return true;
            }

            if (ip.AddressFamily ==
                System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                return ip.IsIPv6LinkLocal ||
                       ip.IsIPv6SiteLocal;
            }

            byte[] bytes =
                ip.GetAddressBytes();

            return bytes[0] == 10 ||
                   (bytes[0] == 172 &&
                    bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 &&
                    bytes[1] == 168) ||
                   (bytes[0] == 169 &&
                    bytes[1] == 254);
        }

        private static string Limitar(
            string? valor,
            int maximo)
        {
            string texto =
                valor?.Trim() ??
                string.Empty;

            return texto.Length <= maximo
                ? texto
                : texto[..maximo];
        }
    }
}
