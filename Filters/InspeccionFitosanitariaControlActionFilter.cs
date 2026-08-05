using CONATRADEC_API.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections;
using System.Reflection;

namespace CONATRADEC_API.Filters
{
    /// <summary>
    /// Aplica la regla de cierre definitivo en el servidor. Una vez cerrada,
    /// ninguna operación POST, PUT, PATCH o DELETE puede alterar la inspección,
    /// aunque un cliente antiguo todavía muestre botones.
    /// </summary>
    public sealed class InspeccionFitosanitariaControlActionFilter :
        IAsyncActionFilter
    {
        private const string RutaBase = "/api/inspecciones-fitosanitarias";
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;

        public InspeccionFitosanitariaControlActionFilter(
            InspeccionFitosanitariaControlDatabaseInitializer control)
        {
            this.control = control;
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

            int? id = ObtenerId(context);
            bool escritura = HttpMethods.IsPost(context.HttpContext.Request.Method) ||
                             HttpMethods.IsPut(context.HttpContext.Request.Method) ||
                             HttpMethods.IsPatch(context.HttpContext.Request.Method) ||
                             HttpMethods.IsDelete(context.HttpContext.Request.Method);

            if (escritura && id is > 0)
            {
                InspeccionFitosanitariaControlRegistro? registro =
                    await control.ObtenerAsync(id.Value, cancellationToken);

                if (registro?.CerradaTecnico == true)
                {
                    context.Result = new ConflictObjectResult(new
                    {
                        success = false,
                        message = "La inspección está cerrada definitivamente y es de solo lectura. No se permite realizar ninguna modificación."
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
                    message = "El nombre de la inspección no puede superar 150 caracteres."
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

            await EnriquecerDetalleAsync(ejecutado.Result, cancellationToken);
        }

        private async Task EnriquecerDetalleAsync(
            IActionResult? resultado,
            CancellationToken cancellationToken)
        {
            if (resultado is not ObjectResult objectResult ||
                objectResult.Value == null)
                return;

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

            bool procesamiento = !registro.CerradaTecnico &&
                await control.TieneProcesamientoActivoAsync(id, cancellationToken);
            bool puedeGestionar = ObtenerBooleano(
                detalle,
                "PuedeGestionarSolicitud");

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
                detalle["PuedeCerrarInspeccion"] =
                    puedeGestionar && !procesamiento;
                detalle["MotivoNoPuedeCerrar"] = procesamiento
                    ? "Espere a que terminen las fotografías que están pendientes o en análisis IA antes de cerrar."
                    : puedeGestionar
                        ? "El cierre es definitivo. Después solo podrá consultar la inspección."
                        : "Solo el técnico que creó la inspección puede cerrarla.";
            }

            sobre["data"] = detalle;
            objectResult.Value = sobre;
        }

        private static async Task<string?> LeerNombreCreacionAsync(
            ActionExecutingContext context)
        {
            if (!EsCreacion(context) ||
                !context.HttpContext.Request.HasFormContentType)
                return null;

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
                   string.Equals(ruta, RutaBase, StringComparison.OrdinalIgnoreCase);
        }

        private static int? ObtenerId(ActionExecutingContext context)
        {
            if (context.ActionArguments.TryGetValue("id", out object? valor) &&
                int.TryParse(valor?.ToString(), out int id) && id > 0)
                return id;

            if (context.RouteData.Values.TryGetValue("id", out valor) &&
                int.TryParse(valor?.ToString(), out id) && id > 0)
                return id;

            return null;
        }

        private static bool TryObtenerIdRespuesta(
            IActionResult? resultado,
            out int id)
        {
            id = 0;
            if (resultado is not ObjectResult objectResult ||
                objectResult.Value == null)
                return false;

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
                return new(existente, StringComparer.OrdinalIgnoreCase);

            if (objeto is IDictionary diccionario)
            {
                var resultado = new Dictionary<string, object?>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry item in diccionario)
                    resultado[item.Key?.ToString() ?? string.Empty] = item.Value;
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
                if (string.Equals(clave, nombre, StringComparison.OrdinalIgnoreCase))
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
            bool.TryParse(contenido?.ToString(), out bool valor) && valor;
    }
}
