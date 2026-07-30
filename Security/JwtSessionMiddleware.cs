using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Middleware;
using System.IdentityModel.Tokens.Jwt;

namespace CONATRADEC_API.Security
{
    /// <summary>
    /// Exige JWT en rutas privadas y reemplaza la identidad enviada por el
    /// cliente con los valores firmados del token.
    /// </summary>
    public sealed class JwtSessionMiddleware
    {
        public const string HeaderActividadUsuario =
            "X-Actividad-Usuario";

        public const string ItemAuthenticationError =
            "__CONATRADEC_JWT_ERROR";

        private readonly RequestDelegate next;

        public JwtSessionMiddleware(
            RequestDelegate next)
        {
            this.next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            SesionActivaService sesionActivaService)
        {
            if (!EsRutaProtegida(context.Request.Path) ||
                EsRutaPublica(context.Request))
            {
                await next(context);
                return;
            }

            if (context.User.Identity?.IsAuthenticated != true)
            {
                string codigo =
                    context.Items[
                        ItemAuthenticationError] as string ??
                    "AUTH_TOKEN_REQUIRED";

                await ResponderSesionRechazadaAsync(
                    context,
                    ObtenerMensajeAutenticacion(codigo),
                    codigo);

                return;
            }

            if (!TryGetClaimEntero(
                    context,
                    "uid",
                    out int usuarioId) ||
                !TryGetClaimEntero(
                    context,
                    "sv",
                    out int versionSesion))
            {
                await ResponderSesionRechazadaAsync(
                    context,
                    "El token no contiene una identidad de sesión válida.",
                    "AUTH_TOKEN_INVALID");

                return;
            }

            string? sesionId =
                context.User.FindFirst(
                    JwtRegisteredClaimNames.Jti)?.Value;

            if (string.IsNullOrWhiteSpace(sesionId))
            {
                await ResponderSesionRechazadaAsync(
                    context,
                    "El token no contiene un identificador de sesión.",
                    "AUTH_TOKEN_INVALID");

                return;
            }

            bool registrarActividad =
                EsActividadReal(
                    context.Request.Headers[
                        HeaderActividadUsuario]
                        .ToString());

            EstadoSesionToken estado =
                sesionActivaService
                    .ValidarYRegistrarActividad(
                        sesionId,
                        usuarioId,
                        versionSesion,
                        registrarActividad);

            if (estado != EstadoSesionToken.Valida)
            {
                (string mensaje, string codigo) =
                    ObtenerError(estado);

                await ResponderSesionRechazadaAsync(
                    context,
                    mensaje,
                    codigo);

                return;
            }

            /*
             * Los valores enviados por el cliente se eliminan y se sustituyen
             * con la identidad validada criptográficamente.
             */
            context.Request.Headers[
                VersionSesionMiddleware.HeaderUsuarioId] =
                usuarioId.ToString();

            context.Request.Headers[
                VersionSesionMiddleware.HeaderVersionSesion] =
                versionSesion.ToString();

            ReemplazarCabecera(
                context,
                "X-Usuario-Nombre",
                context.User.FindFirst("name")?.Value);

            ReemplazarCabecera(
                context,
                "X-Rol-Nombre",
                context.User.FindFirst("role")?.Value);

            context.Request.Headers.Remove(
                HeaderActividadUsuario);

            await next(context);

            /*
             * Si VersionSesionMiddleware detectó posteriormente un cambio de rol,
             * permisos o estado, se elimina también el jti de memoria.
             */
            if (context.Response.Headers.TryGetValue(
                    VersionSesionMiddleware.HeaderSesionInvalidada,
                    out var values) &&
                values.Any(value =>
                    string.Equals(
                        value,
                        "true",
                        StringComparison.OrdinalIgnoreCase)))
            {
                sesionActivaService.Revocar(
                    sesionId);
            }
        }

        private static bool EsRutaProtegida(
            PathString path)
        {
            string value =
                path.Value ??
                string.Empty;

            bool esApi =
                value.Equals(
                    "/api",
                    StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith(
                    "/api/",
                    StringComparison.OrdinalIgnoreCase);

            bool esConectividad =
                value.Equals(
                    "/conectividad/dispositivos",
                    StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith(
                    "/conectividad/dispositivos/",
                    StringComparison.OrdinalIgnoreCase);

            return esApi || esConectividad;
        }

        private static bool EsRutaPublica(
            HttpRequest request)
        {
            string path =
                request.Path.Value ??
                string.Empty;

            if (HttpMethods.IsOptions(request.Method))
                return true;

            if (path.StartsWith(
                    "/api/auth",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.StartsWith(
                    "/api/actualizaciones/descargar/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return EsRutaExacta(
                       path,
                       "/api/actualizaciones/descargas/portal") ||
                   EsRutaExacta(
                       path,
                       "/api/actualizaciones/descargas/validar") ||
                   EsRutaExacta(
                       path,
                       "/api/actualizaciones/descargas/validar-formulario");
        }

        private static bool EsRutaExacta(
            string actual,
            string esperada) =>
            string.Equals(
                actual.TrimEnd('/'),
                esperada,
                StringComparison.OrdinalIgnoreCase);

        private static bool EsActividadReal(
            string value) =>
            string.Equals(
                value,
                "true",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                value,
                "1",
                StringComparison.OrdinalIgnoreCase);

        private static bool TryGetClaimEntero(
            HttpContext context,
            string claim,
            out int value)
        {
            string? text =
                context.User.FindFirst(claim)?.Value;

            return int.TryParse(
                       text,
                       out value) &&
                   value > 0;
        }

        private static void ReemplazarCabecera(
            HttpContext context,
            string nombre,
            string? valor)
        {
            context.Request.Headers.Remove(
                nombre);

            if (string.IsNullOrWhiteSpace(valor))
                return;

            context.Request.Headers[nombre] =
                Uri.EscapeDataString(
                    valor.Trim());
        }

        private static string ObtenerMensajeAutenticacion(
            string codigo) =>
            codigo switch
            {
                "SESSION_TOKEN_EXPIRED" =>
                    "El token de sesión expiró. Inicie sesión nuevamente.",

                "AUTH_TOKEN_INVALID" =>
                    "El token de sesión no es válido. Inicie sesión nuevamente.",

                _ =>
                    "La solicitud requiere un token de sesión válido."
            };

        private static (string Mensaje, string Codigo)
            ObtenerError(
                EstadoSesionToken estado) =>
            estado switch
            {
                EstadoSesionToken.Inactiva =>
                    (
                        "La sesión se cerró por inactividad. Inicie sesión nuevamente.",
                        "SESSION_INACTIVITY_TIMEOUT"
                    ),

                EstadoSesionToken.Expirada =>
                    (
                        "El token de sesión expiró. Inicie sesión nuevamente.",
                        "SESSION_TOKEN_EXPIRED"
                    ),

                _ =>
                    (
                        "La sesión ya no se encuentra activa. Inicie sesión nuevamente.",
                        "SESSION_NOT_ACTIVE"
                    )
            };

        private static async Task
            ResponderSesionRechazadaAsync(
                HttpContext context,
                string message,
                string code)
        {
            context.Response.StatusCode =
                StatusCodes.Status401Unauthorized;

            context.Response.ContentType =
                "application/json; charset=utf-8";

            context.Response.Headers[
                VersionSesionMiddleware.HeaderSesionInvalidada] =
                "true";

            context.Response.Headers["Cache-Control"] =
                "no-store";

            var response =
                ApiErrorResponseFactory.Create(
                    context,
                    StatusCodes.Status401Unauthorized,
                    message: message,
                    code: code);

            await context.Response.WriteAsJsonAsync(
                response);
        }
    }
}
