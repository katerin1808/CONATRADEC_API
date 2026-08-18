using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CONATRADEC_API.Filters
{
    /// <summary>
    /// Autoriza las operaciones del módulo Análisis de suelo y conserva la
    /// protección histórica de propiedad para la edición.
    ///
    /// Reglas:
    /// - GET requiere Leer;
    /// - POST requiere Agregar;
    /// - PUT requiere Actualizar y ser propietario del análisis;
    /// - DELETE requiere Eliminar;
    /// - leer detalle ajeno requiere además AnalisisSueloTodosPage/Leer;
    /// - la sincronización offline aplica el permiso de Crear/Editar según su
    ///   tipo de operación y mantiene la regla de propietario para edición.
    /// </summary>
    public sealed class AnalisisEdicionPropietarioActionFilter :
        IAsyncActionFilter
    {
        private const string InterfazAnalisis = "MainPage";
        private const string InterfazVerTodos = "AnalisisSueloTodosPage";

        private const string RutaBase =
            "/api/guardar-todo";

        private const string RutaEditar =
            "/api/guardar-todo/editar/";

        private const string RutaDetalle =
            "/api/guardar-todo/listardetalle/";

        private const string RutaSincronizarOffline =
            "/api/analisis-offline/sincronizar";

        private readonly DBContext db;
        private readonly PermisoApiService permisoApiService;

        public AnalisisEdicionPropietarioActionFilter(
            DBContext db,
            PermisoApiService permisoApiService)
        {
            this.db = db;
            this.permisoApiService = permisoApiService;
        }

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            HttpRequest request = context.HttpContext.Request;
            string path =
                (request.Path.Value ?? string.Empty)
                    .TrimEnd('/');

            if (!EsRutaAnalisisProtegida(request, path))
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
                        "No se recibió una sesión válida para operar análisis de suelo."
                });
                return;
            }

            TipoPermisoApi permisoRequerido =
                ObtenerPermisoRequerido(
                    context,
                    request,
                    path);

            ResultadoPermisoApi permiso =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    InterfazAnalisis,
                    permisoRequerido,
                    context.HttpContext.RequestAborted);

            if (!permiso.Permitido)
            {
                context.Result = new ObjectResult(new
                {
                    success = false,
                    code = "ANALYSIS_PERMISSION_DENIED",
                    message = permiso.Mensaje
                })
                {
                    StatusCode = permiso.CodigoEstado
                };
                return;
            }

            if (EsCreacion(context, request, path, out GuardarTodoDto? creacion) &&
                !ValidarUsuarioSolicitudCreacion(
                    context,
                    usuarioSesionId,
                    creacion))
            {
                return;
            }

            if (request.Method == HttpMethods.Get &&
                !await ValidarAlcanceLecturaLegadaAsync(
                    context,
                    usuarioSesionId,
                    path))
            {
                return;
            }

            if (request.Method == HttpMethods.Get &&
                path.StartsWith(
                    RutaDetalle,
                    StringComparison.OrdinalIgnoreCase))
            {
                int detalleId =
                    ObtenerEnteroArgumento(context, "id");

                if (detalleId <= 0)
                    detalleId = ObtenerUltimoEntero(path);

                if (!await PuedeLeerDetalleAsync(
                        usuarioSesionId,
                        detalleId,
                        context.HttpContext.RequestAborted))
                {
                    context.Result = new ObjectResult(new
                    {
                        success = false,
                        code = "ANALYSIS_READ_NOT_ALLOWED",
                        message =
                            "No tiene permiso para consultar este análisis porque pertenece a otro usuario."
                    })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                    return;
                }
            }

            if (IntentarObtenerEdicion(
                    context,
                    out int analisisSueloCalculoId,
                    out GuardarTodoDto? solicitud))
            {
                if (!await ValidarPropietarioEdicionAsync(
                        context,
                        usuarioSesionId,
                        analisisSueloCalculoId,
                        solicitud))
                {
                    return;
                }
            }

            await next();
        }

        private static bool EsRutaAnalisisProtegida(
            HttpRequest request,
            string path)
        {
            if (path.Equals(
                    RutaSincronizarOffline,
                    StringComparison.OrdinalIgnoreCase))
            {
                return request.Method == HttpMethods.Post;
            }

            if (!path.StartsWith(
                    RutaBase,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return request.Method == HttpMethods.Get ||
                   request.Method == HttpMethods.Post ||
                   request.Method == HttpMethods.Put ||
                   request.Method == HttpMethods.Delete;
        }

        private static TipoPermisoApi ObtenerPermisoRequerido(
            ActionExecutingContext context,
            HttpRequest request,
            string path)
        {
            if (path.Equals(
                    RutaSincronizarOffline,
                    StringComparison.OrdinalIgnoreCase))
            {
                AnalisisOfflineSincronizarDto? envelope =
                    context.ActionArguments.Values
                        .OfType<AnalisisOfflineSincronizarDto>()
                        .FirstOrDefault();

                return string.Equals(
                        envelope?.tipoOperacion,
                        "EDITAR",
                        StringComparison.OrdinalIgnoreCase)
                    ? TipoPermisoApi.Actualizar
                    : TipoPermisoApi.Agregar;
            }

            if (request.Method == HttpMethods.Post)
                return TipoPermisoApi.Agregar;

            if (request.Method == HttpMethods.Put)
                return TipoPermisoApi.Actualizar;

            if (request.Method == HttpMethods.Delete)
                return TipoPermisoApi.Eliminar;

            return TipoPermisoApi.Leer;
        }

        /// <summary>
        /// Las rutas históricas de listado se conservan por compatibilidad,
        /// pero nunca deben permitir que un usuario con alcance propio obtenga
        /// todos los análisis. El endpoint paginado moderno mantiene su propia
        /// autorización en su controlador.
        /// </summary>
        private async Task<bool> ValidarAlcanceLecturaLegadaAsync(
            ActionExecutingContext context,
            int usuarioSesionId,
            string path)
        {
            bool listadoCompleto =
                path.Equals(
                    RutaBase,
                    StringComparison.OrdinalIgnoreCase);

            bool listadoPorUsuario =
                path.Equals(
                    RutaBase + "/listar-usuario",
                    StringComparison.OrdinalIgnoreCase) ||
                path.Equals(
                    RutaBase + "/listar usuario",
                    StringComparison.OrdinalIgnoreCase) ||
                path.Equals(
                    RutaBase + "/listar%20usuario",
                    StringComparison.OrdinalIgnoreCase);

            if (!listadoCompleto && !listadoPorUsuario)
                return true;

            if (listadoPorUsuario &&
                int.TryParse(
                    context.HttpContext.Request.Query["usuarioId"].ToString(),
                    out int usuarioSolicitado) &&
                usuarioSolicitado == usuarioSesionId)
            {
                return true;
            }

            ResultadoPermisoApi permisoGlobal =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    InterfazVerTodos,
                    TipoPermisoApi.Leer,
                    context.HttpContext.RequestAborted);

            if (permisoGlobal.Permitido)
                return true;

            context.Result = new ObjectResult(new
            {
                success = false,
                code = "ANALYSIS_GLOBAL_READ_REQUIRED",
                message =
                    "No tiene permiso para consultar análisis de otros usuarios."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };

            return false;
        }

        private async Task<bool> PuedeLeerDetalleAsync(
            int usuarioSesionId,
            int analisisSueloCalculoId,
            CancellationToken cancellationToken)
        {
            if (analisisSueloCalculoId <= 0)
                return false;

            int? propietarioId =
                await db.AnalisisSueloCalculos
                    .AsNoTracking()
                    .Where(item =>
                        item.analisisSueloCalculoId ==
                            analisisSueloCalculoId &&
                        item.activo)
                    .Select(item => item.usuarioId)
                    .FirstOrDefaultAsync(cancellationToken);

            if (propietarioId == usuarioSesionId)
                return true;

            ResultadoPermisoApi permisoGlobal =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    InterfazVerTodos,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            return permisoGlobal.Permitido;
        }

        private async Task<bool> ValidarPropietarioEdicionAsync(
            ActionExecutingContext context,
            int usuarioSesionId,
            int analisisSueloCalculoId,
            GuardarTodoDto? solicitud)
        {
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
                    return false;
                }

                context.Result = new ObjectResult(new
                {
                    success = false,
                    code = "ANALYSIS_OWNER_REQUIRED",
                    message =
                        "El análisis no tiene un usuario propietario asignado y no puede editarse hasta corregir ese dato."
                })
                {
                    StatusCode = StatusCodes.Status409Conflict
                };
                return false;
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
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return false;
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
                    StatusCode = StatusCodes.Status403Forbidden
                };
                return false;
            }

            return true;
        }

        private static bool EsCreacion(
            ActionExecutingContext context,
            HttpRequest request,
            string path,
            out GuardarTodoDto? solicitud)
        {
            solicitud = null;

            if (request.Method == HttpMethods.Post &&
                path.Equals(
                    RutaBase,
                    StringComparison.OrdinalIgnoreCase))
            {
                solicitud =
                    context.ActionArguments.Values
                        .OfType<GuardarTodoDto>()
                        .FirstOrDefault();

                return solicitud != null;
            }

            if (request.Method != HttpMethods.Post ||
                !path.Equals(
                    RutaSincronizarOffline,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            AnalisisOfflineSincronizarDto? envelope =
                context.ActionArguments.Values
                    .OfType<AnalisisOfflineSincronizarDto>()
                    .FirstOrDefault();

            if (envelope == null ||
                string.Equals(
                    envelope.tipoOperacion,
                    "EDITAR",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            solicitud = envelope.solicitud;
            return solicitud != null;
        }

        private static bool ValidarUsuarioSolicitudCreacion(
            ActionExecutingContext context,
            int usuarioSesionId,
            GuardarTodoDto? solicitud)
        {
            int usuarioSolicitud =
                solicitud?.datosAnalisis?.usuarioId ?? 0;

            if (usuarioSolicitud == usuarioSesionId)
                return true;

            context.Result = new ObjectResult(new
            {
                success = false,
                code = "ANALYSIS_CREATE_USER_MISMATCH",
                message =
                    "El usuario propietario enviado en el análisis no coincide con el usuario autenticado."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };

            return false;
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
                    analisisSueloCalculoId = ObtenerUltimoEntero(path);

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

        private static int ObtenerUltimoEntero(string? path)
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
