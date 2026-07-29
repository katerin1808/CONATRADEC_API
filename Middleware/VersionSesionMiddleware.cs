using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Middleware
{
    /// <summary>
    /// Valida la versión de sesión y protege la descarga física de instaladores.
    /// La ruta /api/actualizaciones/descargar/{id} solamente continúa cuando
    /// recibe un permiso temporal válido emitido después de consumir una llave.
    /// </summary>
    public sealed class VersionSesionMiddleware
    {
        public const string HeaderUsuarioId = "X-Usuario-Id";
        public const string HeaderVersionSesion = "X-Version-Sesion";
        public const string HeaderSesionInvalidada = "X-Sesion-Invalidada";

        private readonly RequestDelegate next;
        private readonly ILogger<VersionSesionMiddleware> logger;

        public VersionSesionMiddleware(
            RequestDelegate next,
            ILogger<VersionSesionMiddleware> logger)
        {
            this.next = next;
            this.logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            DBContext db,
            ActualizacionesDbContext actualizacionesDb,
            IWebHostEnvironment environment)
        {
            if (EsRutaArchivoActualizacion(
                    context.Request.Path,
                    out int actualizacionId))
            {
                await ProcesarDescargaProtegidaAsync(
                    context,
                    actualizacionesDb,
                    environment,
                    actualizacionId);
                return;
            }

            if (DebeOmitir(context.Request.Path))
            {
                await next(context);
                return;
            }

            if (!TryGetUsuarioId(context, out int usuarioId))
            {
                await next(context);
                return;
            }

            if (!TryGetVersionSesion(context, out int versionSesion))
            {
                await ResponderSesionInvalidadaAsync(context);
                return;
            }

            EstadoSesion? estadoInicial = await ObtenerEstadoAsync(
                db,
                usuarioId,
                context.RequestAborted);

            if (!EsValida(estadoInicial, versionSesion))
            {
                await ResponderSesionInvalidadaAsync(context);
                return;
            }

            if (DebeValidarAlFinal(context.Request))
            {
                context.Response.OnStarting(async () =>
                {
                    try
                    {
                        EstadoSesion? estadoFinal = await ObtenerEstadoAsync(
                            db,
                            usuarioId,
                            context.RequestAborted);

                        if (!EsValida(estadoFinal, versionSesion))
                        {
                            context.Response.Headers[
                                HeaderSesionInvalidada] = "true";
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch
                    {
                        // Una comprobación secundaria no reemplaza la respuesta.
                    }
                });
            }

            await next(context);
        }

        private async Task ProcesarDescargaProtegidaAsync(
            HttpContext context,
            ActualizacionesDbContext actualizacionesDb,
            IWebHostEnvironment environment,
            int actualizacionId)
        {
            string permiso = context.Request.Query["permiso"]
                .ToString();

            if (string.IsNullOrWhiteSpace(permiso))
            {
                context.Request.Cookies.TryGetValue(
                    ActualizacionDescargaTokenService.ObtenerNombreCookie(
                        actualizacionId),
                    out permiso);
            }

            bool valido = ActualizacionDescargaTokenService.TryValidar(
                environment,
                permiso,
                actualizacionId,
                out PermisoDescargaPayload payload);

            if (!valido)
            {
                await ResponderDescargaNoAutorizadaAsync(context);
                return;
            }

            context.Items[
                ActualizacionDescargaTokenService.ItemOperacionId] =
                payload.OperacionId;

            context.Items[
                ActualizacionDescargaTokenService.ItemLlaveId] =
                payload.ActualizacionLlaveDescargaId;

            await RegistrarInicioDescargaAsync(
                actualizacionesDb,
                payload,
                context.RequestAborted);

            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Robots-Tag"] =
                "noindex, nofollow, noarchive";

            await next(context);
        }

        private async Task RegistrarInicioDescargaAsync(
            ActualizacionesDbContext actualizacionesDb,
            PermisoDescargaPayload payload,
            CancellationToken cancellationToken)
        {
            try
            {
                bool yaRegistrada = await actualizacionesDb
                    .AuditoriaDescargas
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.OperacionId == payload.OperacionId &&
                            x.Resultado == "DESCARGA_INICIADA",
                        cancellationToken);

                if (yaRegistrada)
                    return;

                ActualizacionDescargaAuditoria? autorizacion =
                    await actualizacionesDb.AuditoriaDescargas
                        .AsNoTracking()
                        .Where(x =>
                            x.OperacionId == payload.OperacionId &&
                            (x.Resultado == "AUTORIZADA" ||
                             x.Resultado == "AUTORIZADA_APLICACION"))
                        .OrderByDescending(x => x.FechaUtc)
                        .FirstOrDefaultAsync(cancellationToken);

                if (autorizacion == null)
                    return;

                actualizacionesDb.AuditoriaDescargas.Add(
                    new ActualizacionDescargaAuditoria
                    {
                        ActualizacionLlaveDescargaId =
                            autorizacion.ActualizacionLlaveDescargaId,
                        ActualizacionAplicacionId =
                            autorizacion.ActualizacionAplicacionId,
                        OperacionId = autorizacion.OperacionId,
                        Resultado = "DESCARGA_INICIADA",
                        Detalle =
                            "El navegador solicitó el archivo autorizado.",
                        Plataforma = autorizacion.Plataforma,
                        Canal = autorizacion.Canal,
                        VersionNombre = autorizacion.VersionNombre,
                        VersionCodigo = autorizacion.VersionCodigo,
                        NombreArchivo = autorizacion.NombreArchivo,
                        IpCliente = autorizacion.IpCliente,
                        EncabezadoForwardedFor =
                            autorizacion.EncabezadoForwardedFor,
                        AgenteUsuario = autorizacion.AgenteUsuario,
                        Navegador = autorizacion.Navegador,
                        SistemaOperativo =
                            autorizacion.SistemaOperativo,
                        TipoDispositivo = autorizacion.TipoDispositivo,
                        IdentificadorDispositivoWeb =
                            autorizacion.IdentificadorDispositivoWeb,
                        Destinatario = autorizacion.Destinatario,
                        UsuarioGeneradorId =
                            autorizacion.UsuarioGeneradorId,
                        FechaUtc = DateTime.UtcNow
                    });

                await actualizacionesDb.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "No fue posible registrar el inicio de la descarga {OperacionId}.",
                    payload.OperacionId);
            }
        }

        private static bool EsRutaArchivoActualizacion(
            PathString path,
            out int actualizacionId)
        {
            actualizacionId = 0;

            const string prefijo =
                "/api/actualizaciones/descargar/";

            string valor = path.Value ?? string.Empty;

            if (!valor.StartsWith(
                    prefijo,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string segmento = valor[prefijo.Length..]
                .Trim('/');

            return int.TryParse(segmento, out actualizacionId) &&
                   actualizacionId > 0;
        }

        private static bool TryGetUsuarioId(
            HttpContext context,
            out int usuarioId)
        {
            usuarioId = 0;

            string texto = context.Request.Headers[HeaderUsuarioId]
                .ToString();

            return int.TryParse(texto, out usuarioId) &&
                   usuarioId > 0;
        }

        private static bool TryGetVersionSesion(
            HttpContext context,
            out int versionSesion)
        {
            versionSesion = 0;

            string texto = context.Request.Headers[HeaderVersionSesion]
                .ToString();

            return int.TryParse(texto, out versionSesion) &&
                   versionSesion > 0;
        }

        private static async Task<EstadoSesion?> ObtenerEstadoAsync(
            DBContext db,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            return await db.Usuarios
                .AsNoTracking()
                .Where(item => item.UsuarioId == usuarioId)
                .Select(item => new EstadoSesion(
                    item.activo,
                    item.versionSesion))
                .FirstOrDefaultAsync(cancellationToken);
        }

        private static bool EsValida(
            EstadoSesion? estado,
            int versionRecibida) =>
            estado is not null &&
            estado.Activo &&
            estado.VersionSesion == versionRecibida;

        private static bool DebeValidarAlFinal(HttpRequest request) =>
            HttpMethods.IsPut(request.Method) &&
            request.Path.StartsWithSegments(
                "/api/usuarios/actualizar");

        private static bool DebeOmitir(PathString path) =>
            path.StartsWithSegments("/api/auth") ||
            path.StartsWithSegments("/swagger") ||
            path.StartsWithSegments("/resources") ||
            path.StartsWithSegments("/imagenes");

        private static async Task ResponderDescargaNoAutorizadaAsync(
            HttpContext context)
        {
            context.Response.StatusCode =
                StatusCodes.Status401Unauthorized;

            context.Response.ContentType =
                "application/json; charset=utf-8";

            context.Response.Headers["Cache-Control"] = "no-store";

            var response = ApiErrorResponseFactory.Create(
                context,
                StatusCodes.Status401Unauthorized,
                message:
                    "La descarga requiere una llave válida o un permiso temporal vigente.",
                code: "DOWNLOAD_PERMISSION_REQUIRED");

            await context.Response.WriteAsJsonAsync(response);
        }

        private static async Task ResponderSesionInvalidadaAsync(
            HttpContext context)
        {
            context.Response.StatusCode =
                StatusCodes.Status401Unauthorized;

            context.Response.ContentType =
                "application/json; charset=utf-8";

            context.Response.Headers[HeaderSesionInvalidada] = "true";

            var response = ApiErrorResponseFactory.Create(
                context,
                StatusCodes.Status401Unauthorized,
                message:
                    "Su rol o sus permisos cambiaron. Inicie sesión nuevamente para aplicar la nueva configuración.",
                code: "SESSION_INVALIDATED");

            await context.Response.WriteAsJsonAsync(response);
        }

        private sealed record EstadoSesion(
            bool Activo,
            int VersionSesion);
    }
}
