using CONATRADEC_API.DTOs;
using CONATRADEC_API.Services;
using System.Security.Claims;

namespace CONATRADEC_API.Endpoints
{
    /// <summary>
    /// Endpoints livianos consumidos por la app MAUI.
    /// Se publican fuera de /api para que los latidos repetitivos no llenen
    /// la bitácora transversal, pero continúan protegidos por JWT.
    /// </summary>
    public static class DispositivosConexionEndpoints
    {
        public static IEndpointRouteBuilder MapDispositivosConexionEndpoints(
            this IEndpointRouteBuilder endpoints)
        {
            RouteGroupBuilder group = endpoints
                .MapGroup("/conectividad/dispositivos")
                .WithTags("Conectividad de dispositivos")
                .RequireAuthorization();

            group.MapPost(
                "/reportar",
                ReportarAsync)
                .WithName("ReportarConexionDispositivo")
                .Produces<ReportarDispositivoConexionResponse>(
                    StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status401Unauthorized);

            group.MapPost(
                "/desconectar",
                DesconectarAsync)
                .WithName("DesconectarDispositivo")
                .Produces(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status401Unauthorized);

            return endpoints;
        }

        private static async Task<IResult> ReportarAsync(
            ReportarDispositivoConexionRequest request,
            HttpContext httpContext,
            DispositivoConexionService service,
            CancellationToken cancellationToken)
        {
            try
            {
                int usuarioId =
                    ObtenerUsuarioIdAutenticado(
                        httpContext);

                // Nunca se confía en el UsuarioId enviado en el JSON.
                request.UsuarioId = usuarioId;

                ReportarDispositivoConexionResponse response =
                    await service.ReportarAsync(
                        request,
                        httpContext,
                        cancellationToken);

                return Results.Ok(response);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new
                    {
                        success = false,
                        message = ex.Message
                    },
                    statusCode:
                        StatusCodes.Status401Unauthorized);
            }
        }

        private static async Task<IResult> DesconectarAsync(
            DesconectarDispositivoConexionRequest request,
            HttpContext httpContext,
            DispositivoConexionService service,
            CancellationToken cancellationToken)
        {
            try
            {
                int usuarioId =
                    ObtenerUsuarioIdAutenticado(
                        httpContext);

                bool actualizado =
                    await service.DesconectarAsync(
                        request,
                        usuarioId,
                        cancellationToken);

                return Results.Ok(new
                {
                    success = true,
                    actualizado,
                    message = actualizado
                        ? "El dispositivo fue marcado como desconectado."
                        : "No había una sesión activa coincidente."
                });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new
                    {
                        success = false,
                        message = ex.Message
                    },
                    statusCode:
                        StatusCodes.Status401Unauthorized);
            }
        }

        private static int ObtenerUsuarioIdAutenticado(
            HttpContext httpContext)
        {
            string? value =
                httpContext.User
                    .FindFirstValue("uid");

            if (!int.TryParse(
                    value,
                    out int usuarioId) ||
                usuarioId <= 0)
            {
                throw new UnauthorizedAccessException(
                    "El token no contiene un usuario válido.");
            }

            return usuarioId;
        }
    }
}
