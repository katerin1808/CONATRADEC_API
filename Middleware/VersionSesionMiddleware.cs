using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Middleware
{
    /// <summary>
    /// Valida la versión de sesión enviada por MAUI y por el portal web.
    ///
    /// Si una sesión antigua envía X-Usuario-Id, pero todavía no posee
    /// X-Version-Sesion, también se invalida. Esto obliga a iniciar sesión
    /// nuevamente después de instalar esta mejora y evita conservar permisos
    /// cargados antes del cambio.
    /// </summary>
    public sealed class VersionSesionMiddleware
    {
        public const string HeaderUsuarioId = "X-Usuario-Id";
        public const string HeaderVersionSesion = "X-Version-Sesion";
        public const string HeaderSesionInvalidada = "X-Sesion-Invalidada";

        private readonly RequestDelegate next;

        public VersionSesionMiddleware(RequestDelegate next)
        {
            this.next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            DBContext db)
        {
            if (DebeOmitir(context.Request.Path))
            {
                await next(context);
                return;
            }

            /*
             * Las solicitudes realmente anónimas no llevan X-Usuario-Id y
             * pueden continuar. Cuando el encabezado sí existe, la versión
             * pasa a ser obligatoria.
             */
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

            /*
             * Cuando el usuario modifica su propio rol, la solicitud comenzó
             * con una sesión válida, pero deja de ser válida durante el PUT.
             * Antes de enviar la respuesta se agrega un encabezado que obliga
             * al cliente actual a cerrar sesión inmediatamente.
             */
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
                        // No se reemplaza una respuesta válida por un fallo
                        // secundario de comprobación.
                    }
                });
            }

            await next(context);
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
