using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Services
{
    /// <summary>
    /// Registra y actualiza el estado reportado por la app MAUI.
    /// El reloj del servidor determina el último latido. La fecha de ubicación
    /// procede del dispositivo y únicamente se acepta si no reemplaza un dato
    /// más reciente guardado previamente.
    /// </summary>
    public sealed class DispositivoConexionService
    {
        public const int MinutosToleranciaPredeterminados = 2;

        private readonly DispositivosConexionDbContext dispositivosDb;
        private readonly DBContext db;

        public DispositivoConexionService(
            DispositivosConexionDbContext dispositivosDb,
            DBContext db)
        {
            this.dispositivosDb = dispositivosDb;
            this.db = db;
        }

        public async Task<ReportarDispositivoConexionResponse> ReportarAsync(
            ReportarDispositivoConexionRequest request,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            string instalacionId = NormalizarGuid(
                request.InstalacionId,
                "El identificador de instalación no es válido.");

            string sesionId = NormalizarGuid(
                request.SesionId,
                "El identificador de sesión no es válido.");

            ValidarUbicacion(request);

            if (request.UsuarioId <= 0)
            {
                throw new ArgumentException(
                    "Debe indicar el usuario que inició sesión.");
            }

            var usuario = await db.Usuarios
                .AsNoTracking()
                .Where(x =>
                    x.UsuarioId == request.UsuarioId &&
                    x.activo)
                .Select(x => new
                {
                    x.UsuarioId,
                    x.nombreUsuario,
                    x.nombreCompletoUsuario,
                    x.correoUsuario,
                    RolNombre = x.Rol != null
                        ? x.Rol.nombreRol
                        : string.Empty
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (usuario == null)
            {
                throw new UnauthorizedAccessException(
                    "El usuario reportado no existe o está inactivo.");
            }

            DateTime ahoraUtc = DateTime.UtcNow;

            DispositivoConexion? dispositivo =
                await dispositivosDb.DispositivosConexion
                    .FirstOrDefaultAsync(
                        x => x.InstalacionId == instalacionId,
                        cancellationToken);

            bool esNuevo = dispositivo == null;
            bool nuevaSesion = esNuevo ||
                !string.Equals(
                    dispositivo!.SesionId,
                    sesionId,
                    StringComparison.Ordinal);

            if (dispositivo == null)
            {
                dispositivo = new DispositivoConexion
                {
                    InstalacionId = instalacionId,
                    FechaRegistroUtc = ahoraUtc,
                    FechaInicioSesionUtc = ahoraUtc,
                    CantidadSesiones = 1,
                    Activo = true
                };

                dispositivosDb.DispositivosConexion.Add(dispositivo);
            }
            else if (nuevaSesion)
            {
                dispositivo.FechaInicioSesionUtc = ahoraUtc;
                dispositivo.CantidadSesiones =
                    Math.Max(0, dispositivo.CantidadSesiones) + 1;
            }

            dispositivo.SesionId = sesionId;
            dispositivo.UsuarioId = usuario.UsuarioId;
            dispositivo.UsuarioNombre = PreferirTexto(
                usuario.nombreCompletoUsuario,
                usuario.nombreUsuario,
                150);
            dispositivo.CorreoUsuario = Limitar(usuario.correoUsuario, 150);
            dispositivo.RolNombre = Limitar(usuario.RolNombre, 100);
            dispositivo.Plataforma = Limitar(request.Plataforma, 30);
            dispositivo.TipoDispositivo = Limitar(request.TipoDispositivo, 30);
            dispositivo.Fabricante = Limitar(request.Fabricante, 100);
            dispositivo.Modelo = Limitar(request.Modelo, 150);
            dispositivo.NombreDispositivo = Limitar(
                request.NombreDispositivo,
                150);
            dispositivo.SistemaOperativo = Limitar(
                request.SistemaOperativo,
                100);
            dispositivo.VersionSistema = Limitar(
                request.VersionSistema,
                50);
            dispositivo.VersionApp = Limitar(request.VersionApp, 50);
            dispositivo.BuildApp = Limitar(request.BuildApp, 50);
            dispositivo.Idioma = Limitar(request.Idioma, 20);
            dispositivo.TipoConexion = Limitar(request.TipoConexion, 100);
            dispositivo.PaginaActual = Limitar(request.PaginaActual, 500);
            dispositivo.DireccionIp = Limitar(
                ObtenerDireccionIp(httpContext),
                100);
            dispositivo.UserAgent = Limitar(
                httpContext.Request.Headers["User-Agent"].ToString(),
                500);

            bool ubicacionActualizada = ActualizarUbicacion(
                dispositivo,
                request,
                ahoraUtc);

            dispositivo.UltimoLatidoUtc = ahoraUtc;
            dispositivo.FechaDesconexionUtc = null;
            dispositivo.ConectadoReportado = true;
            dispositivo.Activo = true;

            await dispositivosDb.SaveChangesAsync(cancellationToken);

            return new ReportarDispositivoConexionResponse
            {
                Success = true,
                Message = esNuevo
                    ? "El dispositivo fue registrado correctamente."
                    : "La conexión del dispositivo fue actualizada.",
                DispositivoConexionId = dispositivo.DispositivoConexionId,
                UltimoLatidoUtc = ahoraUtc,
                ConsideradoConectadoHastaUtc = ahoraUtc.AddMinutes(
                    MinutosToleranciaPredeterminados),
                UbicacionActualizada = ubicacionActualizada
            };
        }

        public async Task<bool> DesconectarAsync(
            DesconectarDispositivoConexionRequest request,
            CancellationToken cancellationToken)
        {
            string instalacionId = NormalizarGuid(
                request.InstalacionId,
                "El identificador de instalación no es válido.");

            string sesionId = NormalizarGuid(
                request.SesionId,
                "El identificador de sesión no es válido.");

            DispositivoConexion? dispositivo =
                await dispositivosDb.DispositivosConexion
                    .FirstOrDefaultAsync(
                        x => x.InstalacionId == instalacionId,
                        cancellationToken);

            if (dispositivo == null)
                return false;

            // Una sesión anterior no debe desconectar una sesión nueva que ya
            // haya iniciado en la misma instalación.
            if (!string.Equals(
                    dispositivo.SesionId,
                    sesionId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            DateTime ahoraUtc = DateTime.UtcNow;
            dispositivo.ConectadoReportado = false;
            dispositivo.FechaDesconexionUtc = ahoraUtc;
            dispositivo.UltimoLatidoUtc = ahoraUtc;

            await dispositivosDb.SaveChangesAsync(cancellationToken);
            return true;
        }

        private static bool ActualizarUbicacion(
            DispositivoConexion dispositivo,
            ReportarDispositivoConexionRequest request,
            DateTime ahoraUtc)
        {
            string estadoPermiso = Limitar(
                request.EstadoPermisoUbicacion,
                30);

            if (!string.IsNullOrWhiteSpace(estadoPermiso))
            {
                dispositivo.EstadoPermisoUbicacion = estadoPermiso;
            }

            string origen = Limitar(request.OrigenUbicacion, 30);
            if (!string.IsNullOrWhiteSpace(origen))
                dispositivo.OrigenUbicacion = origen;

            if (!request.Latitud.HasValue ||
                !request.Longitud.HasValue)
            {
                return false;
            }

            DateTime fechaUbicacionUtc = request.FechaUbicacionUtc.HasValue
                ? NormalizarFechaUtc(request.FechaUbicacionUtc.Value)
                : ahoraUtc;

            // Evita aceptar fechas futuras causadas por un reloj incorrecto.
            if (fechaUbicacionUtc > ahoraUtc.AddMinutes(5))
                fechaUbicacionUtc = ahoraUtc;

            if (dispositivo.FechaUbicacionUtc.HasValue &&
                fechaUbicacionUtc < dispositivo.FechaUbicacionUtc.Value)
            {
                return false;
            }

            dispositivo.Latitud = ConvertirCoordenada(
                request.Latitud.Value,
                6);
            dispositivo.Longitud = ConvertirCoordenada(
                request.Longitud.Value,
                6);
            dispositivo.PrecisionMetros = request.PrecisionMetros.HasValue
                ? ConvertirCoordenada(
                    Math.Max(0, request.PrecisionMetros.Value),
                    2)
                : null;
            dispositivo.FechaUbicacionUtc = fechaUbicacionUtc;
            dispositivo.UbicacionSimulada = request.UbicacionSimulada;

            return true;
        }

        private static void ValidarUbicacion(
            ReportarDispositivoConexionRequest request)
        {
            bool tieneLatitud = request.Latitud.HasValue;
            bool tieneLongitud = request.Longitud.HasValue;

            if (tieneLatitud != tieneLongitud)
            {
                throw new ArgumentException(
                    "Latitud y longitud deben enviarse juntas.");
            }

            if (!tieneLatitud)
                return;

            if (request.Latitud!.Value is < -90 or > 90)
            {
                throw new ArgumentException(
                    "La latitud reportada no es válida.");
            }

            if (request.Longitud!.Value is < -180 or > 180)
            {
                throw new ArgumentException(
                    "La longitud reportada no es válida.");
            }
        }

        private static DateTime NormalizarFechaUtc(DateTime fecha)
        {
            return fecha.Kind switch
            {
                DateTimeKind.Utc => fecha,
                DateTimeKind.Local => fecha.ToUniversalTime(),
                _ => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
            };
        }

        private static decimal ConvertirCoordenada(
            double valor,
            int decimales)
        {
            return decimal.Round(
                (decimal)valor,
                decimales,
                MidpointRounding.AwayFromZero);
        }

        private static string NormalizarGuid(
            string? valor,
            string mensajeError)
        {
            if (!Guid.TryParse(valor, out Guid guid))
                throw new ArgumentException(mensajeError);

            return guid.ToString("N");
        }

        private static string PreferirTexto(
            string? principal,
            string? alternativo,
            int maximo)
        {
            string valor = !string.IsNullOrWhiteSpace(principal)
                ? principal
                : alternativo ?? string.Empty;

            return Limitar(valor, maximo);
        }

        private static string Limitar(string? valor, int maximo)
        {
            string texto = valor?.Trim() ?? string.Empty;
            return texto.Length <= maximo
                ? texto
                : texto[..maximo];
        }

        private static string ObtenerDireccionIp(HttpContext context)
        {
            string forwardedFor = context.Request.Headers[
                "X-Forwarded-For"].FirstOrDefault() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                return forwardedFor
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?
                    .Trim() ?? string.Empty;
            }

            return context.Connection.RemoteIpAddress?.ToString() ??
                string.Empty;
        }
    }
}
