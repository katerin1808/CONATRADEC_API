using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CONATRADEC_API.Filters
{
    /// <summary>
    /// Protege la edición de los análisis de suelo según su propietario.
    ///
    /// Reglas:
    /// - cualquier usuario con permiso de actualización puede intentar editar;
    /// - el usuario autenticado debe ser el propietario original del análisis;
    /// - la regla se aplica a la edición online y a la sincronización offline;
    /// - el rol Administrador no concede permiso para modificar análisis ajenos.
    ///
    /// Este filtro no modifica los cálculos ni el contenido de la solicitud.
    /// Solamente autoriza o rechaza la operación antes de ejecutar el controlador.
    /// </summary>
    public sealed class AnalisisEdicionPropietarioActionFilter :
        IAsyncActionFilter
    {
        private const string RutaEditar =
            "/api/guardar-todo/editar/";

        private const string RutaSincronizarOffline =
            "/api/analisis-offline/sincronizar";

        private readonly DBContext db;

        public AnalisisEdicionPropietarioActionFilter(
            DBContext db)
        {
            this.db = db;
        }

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            if (!IntentarObtenerEdicion(
                    context,
                    out int analisisSueloCalculoId,
                    out GuardarTodoDto? solicitud))
            {
                await next();
                return;
            }

            int usuarioSesionId =
                ObtenerUsuarioSesionId(
                    context.HttpContext.User);

            if (usuarioSesionId <= 0)
            {
                context.Result = new UnauthorizedObjectResult(new
                {
                    success = false,
                    code = "AUTH_USER_REQUIRED",
                    message =
                        "No se recibió una sesión válida para editar el análisis."
                });
                return;
            }

            int? propietarioId =
                await db.AnalisisSueloCalculos
                    .AsNoTracking()
                    .Where(item =>
                        item.analisisSueloCalculoId ==
                            analisisSueloCalculoId &&
                        item.activo)
                    .Select(item => item.usuarioId)
                    .FirstOrDefaultAsync(
                        context.HttpContext.RequestAborted);

            if (!propietarioId.HasValue ||
                propietarioId.Value <= 0)
            {
                bool existeAnalisis =
                    await db.AnalisisSueloCalculos
                        .AsNoTracking()
                        .AnyAsync(
                            item =>
                                item.analisisSueloCalculoId ==
                                    analisisSueloCalculoId &&
                                item.activo,
                            context.HttpContext.RequestAborted);

                if (!existeAnalisis)
                {
                    context.Result = new NotFoundObjectResult(new
                    {
                        success = false,
                        code = "ANALYSIS_NOT_FOUND",
                        message =
                            "No se encontró el análisis que se desea editar."
                    });
                    return;
                }

                context.Result = new ObjectResult(new
                {
                    success = false,
                    code = "ANALYSIS_OWNER_REQUIRED",
                    message =
                        "El análisis no tiene un usuario propietario asignado y no puede editarse hasta corregir ese dato."
                })
                {
                    StatusCode =
                        StatusCodes.Status409Conflict
                };
                return;
            }

            if (propietarioId.Value != usuarioSesionId)
            {
                context.Result = new ObjectResult(new
                {
                    success = false,
                    code = "ANALYSIS_EDIT_NOT_OWNER",
                    message =
                        "No puede editar este análisis porque pertenece a otro usuario. Solamente su propietario puede modificarlo."
                })
                {
                    StatusCode =
                        StatusCodes.Status403Forbidden
                };
                return;
            }

            int usuarioSolicitud =
                solicitud?.datosAnalisis?.usuarioId ??
                0;

            if (usuarioSolicitud > 0 &&
                usuarioSolicitud != usuarioSesionId)
            {
                context.Result = new ObjectResult(new
                {
                    success = false,
                    code = "ANALYSIS_EDIT_USER_MISMATCH",
                    message =
                        "El propietario enviado en el análisis no coincide con el usuario autenticado."
                })
                {
                    StatusCode =
                        StatusCodes.Status403Forbidden
                };
                return;
            }

            await next();
        }

        private static bool IntentarObtenerEdicion(
            ActionExecutingContext context,
            out int analisisSueloCalculoId,
            out GuardarTodoDto? solicitud)
        {
            analisisSueloCalculoId = 0;
            solicitud = null;

            HttpRequest request =
                context.HttpContext.Request;

            string path =
                (request.Path.Value ?? string.Empty)
                    .TrimEnd('/');

            bool esEdicionOnline =
                request.Method == HttpMethods.Put &&
                path.StartsWith(
                    RutaEditar,
                    StringComparison.OrdinalIgnoreCase);

            if (esEdicionOnline)
            {
                analisisSueloCalculoId =
                    ObtenerEnteroArgumento(
                        context,
                        "id");

                if (analisisSueloCalculoId <= 0)
                {
                    analisisSueloCalculoId =
                        ObtenerUltimoEntero(path);
                }

                solicitud =
                    context.ActionArguments.Values
                        .OfType<GuardarTodoDto>()
                        .FirstOrDefault();

                return analisisSueloCalculoId > 0;
            }

            bool esSincronizacionOffline =
                request.Method == HttpMethods.Post &&
                path.Equals(
                    RutaSincronizarOffline,
                    StringComparison.OrdinalIgnoreCase);

            if (!esSincronizacionOffline)
                return false;

            AnalisisOfflineSincronizarDto? envelope =
                context.ActionArguments.Values
                    .OfType<AnalisisOfflineSincronizarDto>()
                    .FirstOrDefault();

            if (envelope == null ||
                !string.Equals(
                    envelope.tipoOperacion,
                    "EDITAR",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            analisisSueloCalculoId =
                envelope.analisisSueloCalculoId ??
                0;

            solicitud = envelope.solicitud;

            return analisisSueloCalculoId > 0;
        }

        private static int ObtenerUsuarioSesionId(
            ClaimsPrincipal principal)
        {
            string? value =
                principal.FindFirst("uid")?.Value ??
                principal.FindFirst(
                    JwtRegisteredClaimNames.Sub)?.Value ??
                principal.FindFirst(
                    ClaimTypes.NameIdentifier)?.Value;

            return int.TryParse(
                    value,
                    out int usuarioId)
                ? usuarioId
                : 0;
        }

        private static int ObtenerEnteroArgumento(
            ActionExecutingContext context,
            string nombre)
        {
            if (!context.ActionArguments.TryGetValue(
                    nombre,
                    out object? value))
            {
                return 0;
            }

            if (value is int entero)
                return entero;

            return int.TryParse(
                    value?.ToString(),
                    out int resultado)
                ? resultado
                : 0;
        }

        private static int ObtenerUltimoEntero(
            string? path)
        {
            string ultimo =
                (path ?? string.Empty)
                    .Split(
                        '/',
                        StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault() ??
                string.Empty;

            return int.TryParse(
                    ultimo,
                    out int resultado)
                ? resultado
                : 0;
        }
    }
}
