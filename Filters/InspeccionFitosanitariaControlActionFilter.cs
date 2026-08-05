using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections;
using System.Reflection;
using System.Security.Claims;

namespace CONATRADEC_API.Filters
{
    /// <summary>
    /// Reglas transversales del flujo fitosanitario:
    /// - una inspección cerrada es completamente de solo lectura;
    /// - las operaciones que contienen decisiones o textos se ejecutan sobre
    ///   una sola fotografía por petición;
    /// - el cierre global solo se habilita cuando todas las fotografías activas
    ///   terminaron su expediente individual.
    /// </summary>
    public sealed class InspeccionFitosanitariaControlActionFilter :
        IAsyncActionFilter
    {
        private const string RutaBase = "/api/inspecciones-fitosanitarias";

        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;
        private readonly PermisoApiService permisos;

        public InspeccionFitosanitariaControlActionFilter(
            InspeccionFitosanitariaControlDatabaseInitializer control,
            PermisoApiService permisos)
        {
            this.control = control;
            this.permisos = permisos;
        }

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            string ruta = context.HttpContext.Request.Path.Value ?? string.Empty;
            if (!ruta.StartsWith(RutaBase, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            CancellationToken cancellationToken =
                context.HttpContext.RequestAborted;

            await control.InicializarAsync(cancellationToken);

            /*
             * La ruta antigua cerraba la etapa técnica para habilitar al
             * analizador. El flujo nuevo mantiene abierta la inspección durante
             * todas las etapas y la cierra únicamente al final.
             */
            if (EsRutaCierreTecnicoAnterior(ruta))
            {
                context.Result = new ConflictObjectResult(new
                {
                    success = false,
                    message =
                        "La operación de cierre técnico fue reemplazada. Cada fotografía debe completar primero su proceso independiente y luego debe utilizarse el cierre definitivo de la inspección."
                });
                return;
            }

            if (RequiereFotografiaIndividual(ruta))
            {
                int? cantidad = ObtenerCantidadFotografias(context);
                if (cantidad.HasValue && cantidad.Value != 1)
                {
                    context.Result = new BadRequestObjectResult(new
                    {
                        success = false,
                        message =
                            "Esta operación debe enviarse para una sola fotografía. Cada evidencia conserva sus propios motivos, decisiones, análisis e historial."
                    });
                    return;
                }
            }

            int? id = ObtenerId(context);
            bool escritura = EsEscritura(context.HttpContext.Request.Method);

            if (escritura && id is > 0)
            {
                InspeccionFitosanitariaControlRegistro? registro =
                    await control.ObtenerAsync(id.Value, cancellationToken);

                if (registro?.CerradaTecnico == true)
                {
                    context.Result = new ConflictObjectResult(new
                    {
                        success = false,
                        message =
                            "La inspección está cerrada definitivamente y es de solo lectura. No se permite realizar ninguna modificación."
                    });
                    return;
                }
            }

            string? nombreCreacion = await LeerNombreCreacionAsync(context);
            if (nombreCreacion is { Length: > 150 })
            {
                context.Result = new BadRequestObjectResult(new
                {
                    success = false,
                    message =
                        "El nombre de la inspección no puede superar 150 caracteres."
                });
                return;
            }

            ActionExecutedContext ejecutado = await next();
            if (ejecutado.Exception != null || ejecutado.Canceled)
                return;

            if (EsRespuestaExitosa(ejecutado.Result) &&
                EsCreacion(context) &&
                TryObtenerIdRespuesta(ejecutado.Result, out int creadoId))
            {
                await control.ActualizarNombreAsync(
                    creadoId,
                    nombreCreacion,
                    cancellationToken);
            }

            await EnriquecerDetalleAsync(
                context,
                ejecutado.Result,
                cancellationToken);
        }

        private async Task EnriquecerDetalleAsync(
            ActionExecutingContext context,
            IActionResult? resultado,
            CancellationToken cancellationToken)
        {
            if (resultado is not ObjectResult objectResult ||
                objectResult.Value == null)
            {
                return;
            }

            Dictionary<string, object?> sobre = Atributos(objectResult.Value);
            if (!TryObtenerValor(sobre, "data", out object? data) || data == null)
                return;

            Dictionary<string, object?> detalle = Atributos(data);
            if (!TryObtenerEntero(detalle, "InspeccionId", out int id))
                return;

            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(id, cancellationToken);

            if (registro == null)
                return;

            InspeccionFitosanitariaEstadoCierre estadoCierre =
                await control.ObtenerEstadoCierreAsync(
                    id,
                    cancellationToken);

            bool puedeGestionar = ObtenerBooleano(
                detalle,
                "PuedeGestionarSolicitud");

            int? usuarioId = ObtenerUsuarioId(context.HttpContext.User);

            detalle["NombreInspeccion"] = registro.NombreInspeccion;
            detalle["CerradaTecnico"] = registro.CerradaTecnico;
            detalle["FechaCierreTecnicoUtc"] = registro.FechaCierreTecnicoUtc;
            detalle["UsuarioCierreTecnicoId"] = registro.UsuarioCierreTecnicoId;
            detalle["EsSoloLectura"] = registro.CerradaTecnico;

            if (registro.CerradaTecnico)
            {
                detalle["PuedeGestionarSolicitud"] = false;
                detalle["PuedeCerrarInspeccion"] = false;
                detalle["PuedeAnalizar"] = false;
                detalle["PuedeAprobar"] = false;
                detalle["PuedePublicarAlbum"] = false;
                detalle["MotivoNoPuedeCerrar"] =
                    "La inspección fue cerrada definitivamente y solo puede consultarse.";
            }
            else
            {
                bool puedeAnalizar = await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAnalizador,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

                bool puedeAprobar = await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAprobador,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

                bool puedePublicar = await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAlbum,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

                detalle["PuedeGestionarSolicitud"] = puedeGestionar;
                detalle["PuedeAnalizar"] = puedeAnalizar;
                detalle["PuedeAprobar"] = puedeAprobar;
                detalle["PuedePublicarAlbum"] = puedePublicar;
                detalle["PuedeCerrarInspeccion"] =
                    puedeGestionar && estadoCierre.TodasFinalizadas;
                detalle["MotivoNoPuedeCerrar"] =
                    CrearMotivoCierre(
                        puedeGestionar,
                        estadoCierre);
            }

            sobre["data"] = detalle;
            objectResult.Value = sobre;
        }

        private static string CrearMotivoCierre(
            bool puedeGestionar,
            InspeccionFitosanitariaEstadoCierre estado)
        {
            if (!puedeGestionar)
            {
                return "Solo el técnico que creó la inspección puede cerrarla definitivamente.";
            }

            if (estado.TotalActivas == 0)
            {
                return "La inspección debe conservar al menos una fotografía activa antes de cerrarse.";
            }

            if (estado.TodasFinalizadas)
            {
                return "Todas las fotografías finalizaron. El cierre es definitivo y después el expediente quedará únicamente para consulta.";
            }

            if (estado.TotalProcesando > 0)
            {
                return $"Espere: {estado.TotalProcesando} fotografía(s) continúan procesándose y {estado.TotalPendientes} todavía no finalizan.";
            }

            return $"Todavía existen {estado.TotalPendientes} fotografía(s) con un proceso individual pendiente.";
        }

        private async Task<bool> TienePermisoAsync(
            int? usuarioId,
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            if (!usuarioId.HasValue)
                return false;

            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                tipo,
                cancellationToken);

            return resultado.Permitido;
        }

        private static async Task<string?> LeerNombreCreacionAsync(
            ActionExecutingContext context)
        {
            if (!EsCreacion(context) ||
                !context.HttpContext.Request.HasFormContentType)
            {
                return null;
            }

            IFormCollection form = await context.HttpContext.Request
                .ReadFormAsync(context.HttpContext.RequestAborted);

            string valor = form["NombreInspeccion"].ToString().Trim();
            return valor.Length == 0 ? null : valor;
        }

        private static bool EsCreacion(ActionExecutingContext context)
        {
            HttpRequest request = context.HttpContext.Request;
            string ruta = request.Path.Value?.TrimEnd('/') ?? string.Empty;

            return HttpMethods.IsPost(request.Method) &&
                   string.Equals(
                       ruta,
                       RutaBase,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool EsRutaCierreTecnicoAnterior(string ruta) =>
            ruta.TrimEnd('/').EndsWith(
                "/cerrar-tecnico",
                StringComparison.OrdinalIgnoreCase);

        private static bool RequiereFotografiaIndividual(string ruta)
        {
            string normalizada = ruta.TrimEnd('/').ToLowerInvariant();

            return normalizada.EndsWith("/solicitar-revision-ia") ||
                   normalizada.EndsWith("/descartar-fotografias") ||
                   normalizada.EndsWith("/analisis-humano") ||
                   normalizada.EndsWith("/aprobaciones") ||
                   normalizada.EndsWith("/analisis-humano-individual") ||
                   normalizada.EndsWith("/aprobacion-individual");
        }

        private static int? ObtenerCantidadFotografias(
            ActionExecutingContext context)
        {
            foreach (object? argumento in context.ActionArguments.Values)
            {
                if (argumento == null)
                    continue;

                Type tipo = argumento.GetType();
                PropertyInfo? propiedad = tipo.GetProperty(
                    "FotografiaIds",
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.IgnoreCase) ??
                    tipo.GetProperty(
                        "Fotografias",
                        BindingFlags.Public |
                        BindingFlags.Instance |
                        BindingFlags.IgnoreCase);

                if (propiedad?.GetValue(argumento) is not IEnumerable valores ||
                    valores is string)
                {
                    continue;
                }

                int cantidad = 0;
                foreach (object? _ in valores)
                    cantidad++;

                return cantidad;
            }

            return null;
        }

        private static bool EsEscritura(string metodo) =>
            HttpMethods.IsPost(metodo) ||
            HttpMethods.IsPut(metodo) ||
            HttpMethods.IsPatch(metodo) ||
            HttpMethods.IsDelete(metodo);

        private static int? ObtenerId(ActionExecutingContext context)
        {
            if (context.ActionArguments.TryGetValue("id", out object? valor) &&
                int.TryParse(valor?.ToString(), out int id) && id > 0)
            {
                return id;
            }

            if (context.RouteData.Values.TryGetValue("id", out valor) &&
                int.TryParse(valor?.ToString(), out id) && id > 0)
            {
                return id;
            }

            return null;
        }

        private static int? ObtenerUsuarioId(ClaimsPrincipal usuario)
        {
            string? valor =
                usuario.FindFirstValue(ClaimTypes.NameIdentifier) ??
                usuario.FindFirstValue("usuarioId") ??
                usuario.FindFirstValue("sub");

            return int.TryParse(valor, out int id) && id > 0
                ? id
                : null;
        }

        private static bool TryObtenerIdRespuesta(
            IActionResult? resultado,
            out int id)
        {
            id = 0;
            if (resultado is not ObjectResult objectResult ||
                objectResult.Value == null)
            {
                return false;
            }

            Dictionary<string, object?> sobre = Atributos(objectResult.Value);
            return TryObtenerValor(sobre, "data", out object? data) &&
                   data != null &&
                   TryObtenerEntero(Atributos(data), "InspeccionId", out id);
        }

        private static bool EsRespuestaExitosa(IActionResult? resultado) =>
            resultado is ObjectResult objectResult &&
            (objectResult.StatusCode == null ||
             objectResult.StatusCode is >= 200 and < 400);

        private static Dictionary<string, object?> Atributos(object objeto)
        {
            if (objeto is Dictionary<string, object?> existente)
            {
                return new(
                    existente,
                    StringComparer.OrdinalIgnoreCase);
            }

            if (objeto is IDictionary diccionario)
            {
                var resultado = new Dictionary<string, object?>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (DictionaryEntry item in diccionario)
                {
                    resultado[item.Key?.ToString() ?? string.Empty] =
                        item.Value;
                }

                return resultado;
            }

            return objeto.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(item => item.CanRead)
                .ToDictionary(
                    item => item.Name,
                    item => item.GetValue(objeto),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryObtenerValor(
            IReadOnlyDictionary<string, object?> datos,
            string nombre,
            out object? valor)
        {
            foreach ((string clave, object? contenido) in datos)
            {
                if (string.Equals(
                        clave,
                        nombre,
                        StringComparison.OrdinalIgnoreCase))
                {
                    valor = contenido;
                    return true;
                }
            }

            valor = null;
            return false;
        }

        private static bool TryObtenerEntero(
            IReadOnlyDictionary<string, object?> datos,
            string nombre,
            out int valor)
        {
            valor = 0;
            return TryObtenerValor(datos, nombre, out object? contenido) &&
                   int.TryParse(contenido?.ToString(), out valor) &&
                   valor > 0;
        }

        private static bool ObtenerBooleano(
            IReadOnlyDictionary<string, object?> datos,
            string nombre) =>
            TryObtenerValor(datos, nombre, out object? contenido) &&
            bool.TryParse(contenido?.ToString(), out bool valor) &&
            valor;
    }
}
