using CONATRADEC_API.Auditing;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CONATRADEC_API.Middleware
{
    public sealed class BitacoraMiddleware
    {
        private const int MaximoCuerpoSolicitud = 64 * 1024;
        private readonly RequestDelegate siguiente;
        private readonly ILogger<BitacoraMiddleware> logger;

        public BitacoraMiddleware(
            RequestDelegate siguiente,
            ILogger<BitacoraMiddleware> logger)
        {
            this.siguiente = siguiente;
            this.logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext httpContext,
            AuditRequestContext auditRequestContext)
        {
            if (!DebeAuditar(httpContext.Request.Path))
            {
                await siguiente(httpContext);
                return;
            }

            Guid bitacoraId = Guid.NewGuid();
            auditRequestContext.Iniciar(bitacoraId);

            string cuerpoSolicitud = await LeerCuerpoSolicitudAsync(
                httpContext.Request);

            Stopwatch cronometro = Stopwatch.StartNew();
            Exception? excepcion = null;

            httpContext.Response.Headers["X-Correlation-Id"] =
                httpContext.TraceIdentifier;

            try
            {
                await siguiente(httpContext);
            }
            catch (Exception ex)
            {
                excepcion = ex;
                throw;
            }
            finally
            {
                cronometro.Stop();

                try
                {
                    await GuardarBitacoraAsync(
                        httpContext,
                        auditRequestContext,
                        cuerpoSolicitud,
                        cronometro.ElapsedMilliseconds,
                        excepcion);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "No fue posible guardar la bitácora de la solicitud {Metodo} {Ruta}.",
                        httpContext.Request.Method,
                        httpContext.Request.Path);
                }
                finally
                {
                    auditRequestContext.Limpiar();
                }
            }
        }

        private static bool DebeAuditar(PathString ruta)
        {
            // También se audita la consulta de la propia bitácora.
            // No existe recursión porque el guardado se realiza directamente
            // con BitacoraDbContext, sin generar una nueva solicitud HTTP.
            return ruta.StartsWithSegments("/api");
        }

        private static async Task<string> LeerCuerpoSolicitudAsync(
            HttpRequest request)
        {
            if (request.ContentLength is null or <= 0 ||
                request.ContentLength > MaximoCuerpoSolicitud ||
                request.ContentType == null ||
                !request.ContentType.Contains(
                    "json",
                    StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            request.EnableBuffering();

            using var lector = new StreamReader(
                request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);

            string cuerpo = await lector.ReadToEndAsync();
            request.Body.Position = 0;

            return AuditSanitizer.SanitizarJson(cuerpo);
        }

        private static async Task GuardarBitacoraAsync(
            HttpContext httpContext,
            AuditRequestContext auditRequestContext,
            string cuerpoSolicitud,
            long duracionMs,
            Exception? excepcion)
        {
            BitacoraDbContext db = httpContext.RequestServices
                .GetRequiredService<BitacoraDbContext>();

            HttpRequest request = httpContext.Request;
            int codigoEstado = excepcion == null
                ? httpContext.Response.StatusCode
                : StatusCodes.Status500InternalServerError;

            string modulo = ObtenerModulo(httpContext);
            string accion = ObtenerAccion(request.Method, request.Path);
            string usuarioNombre = ObtenerEncabezado(
                request,
                "X-Usuario-Nombre");

            if (string.IsNullOrWhiteSpace(usuarioNombre) &&
                request.Path.StartsWithSegments("/api/auth/login"))
            {
                usuarioNombre = ObtenerUsuarioLogin(cuerpoSolicitud);
            }

            int? usuarioId = int.TryParse(
                ObtenerEncabezado(request, "X-Usuario-Id"),
                out int idUsuario)
                    ? idUsuario
                    : null;

            var bitacora = new Bitacora
            {
                bitacoraId = auditRequestContext.BitacoraId,
                fechaHoraUtc = DateTime.UtcNow,
                usuarioId = usuarioId,
                usuarioNombre = AuditSanitizer.Truncar(
                    usuarioNombre,
                    150),
                rolNombre = AuditSanitizer.Truncar(
                    ObtenerEncabezado(request, "X-Rol-Nombre"),
                    100),
                modulo = AuditSanitizer.Truncar(modulo, 120),
                accion = accion,
                metodoHttp = request.Method,
                endpoint = AuditSanitizer.Truncar(
                    ConstruirEndpoint(request),
                    500),
                paginaOrigen = AuditSanitizer.Truncar(
                    ObtenerEncabezado(request, "X-Pagina-Origen"),
                    500),
                descripcion = AuditSanitizer.Truncar(
                    $"{accion} en {modulo}",
                    1000),
                parametros = ConstruirParametros(
                    request,
                    cuerpoSolicitud),
                direccionIp = AuditSanitizer.Truncar(
                    httpContext.Connection.RemoteIpAddress?.ToString(),
                    100),
                dispositivo = AuditSanitizer.Truncar(
                    ObtenerEncabezado(request, "X-Dispositivo"),
                    200),
                plataforma = AuditSanitizer.Truncar(
                    ObtenerEncabezado(request, "X-Plataforma"),
                    100),
                versionApp = AuditSanitizer.Truncar(
                    ObtenerEncabezado(request, "X-Version-App"),
                    50),
                correlationId = AuditSanitizer.Truncar(
                    httpContext.TraceIdentifier,
                    100),
                codigoEstado = codigoEstado,
                exitoso = excepcion == null && codigoEstado < 400,
                duracionMs = duracionMs,
                error = AuditSanitizer.Truncar(
                    excepcion?.ToString(),
                    16000)
            };

            foreach (AuditEntityChange cambio in auditRequestContext.Cambios)
            {
                bitacora.detalles.Add(new BitacoraDetalle
                {
                    bitacoraId = bitacora.bitacoraId,
                    fechaHoraUtc = cambio.FechaHoraUtc,
                    entidad = AuditSanitizer.Truncar(cambio.Entidad, 150),
                    entidadId = AuditSanitizer.Truncar(cambio.EntidadId, 300),
                    operacion = AuditSanitizer.Truncar(cambio.Operacion, 30),
                    valoresAnteriores = cambio.ValoresAnteriores,
                    valoresNuevos = cambio.ValoresNuevos,
                    propiedadesModificadas = cambio.PropiedadesModificadas
                });
            }

            db.Bitacoras.Add(bitacora);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        private static string ObtenerModulo(HttpContext context)
        {
            ControllerActionDescriptor? descriptor = context
                .GetEndpoint()?
                .Metadata
                .GetMetadata<ControllerActionDescriptor>();

            if (!string.IsNullOrWhiteSpace(descriptor?.ControllerName))
                return descriptor.ControllerName;

            string[] segmentos = context.Request.Path.Value?
                .Split('/', StringSplitOptions.RemoveEmptyEntries) ??
                Array.Empty<string>();

            return segmentos.Length > 1
                ? segmentos[1]
                : "Sistema";
        }

        private static string ObtenerAccion(string metodo, PathString ruta)
        {
            string rutaTexto = ruta.Value ?? string.Empty;

            if (ruta.StartsWithSegments("/api/auth/login"))
                return "INICIAR_SESION";

            if (rutaTexto.Contains("eliminar", StringComparison.OrdinalIgnoreCase) ||
                rutaTexto.Contains("desactivar", StringComparison.OrdinalIgnoreCase) ||
                rutaTexto.Contains("anular", StringComparison.OrdinalIgnoreCase))
            {
                return "ELIMINAR";
            }

            if (rutaTexto.Contains("actualizar", StringComparison.OrdinalIgnoreCase) ||
                rutaTexto.Contains("editar", StringComparison.OrdinalIgnoreCase))
            {
                return "ACTUALIZAR";
            }

            if (rutaTexto.Contains("calcular", StringComparison.OrdinalIgnoreCase) ||
                rutaTexto.Contains("reporte", StringComparison.OrdinalIgnoreCase) ||
                rutaTexto.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
                rutaTexto.Contains("exportar", StringComparison.OrdinalIgnoreCase))
            {
                return "EJECUTAR";
            }

            return metodo.ToUpperInvariant() switch
            {
                "GET" => "CONSULTAR",
                "POST" => "CREAR",
                "PUT" => "ACTUALIZAR",
                "PATCH" => "ACTUALIZAR",
                "DELETE" => "ELIMINAR",
                _ => "EJECUTAR"
            };
        }

        private static string ConstruirParametros(
            HttpRequest request,
            string cuerpoSolicitud)
        {
            var contenido = new Dictionary<string, object?>();

            if (request.RouteValues.Count > 0)
                contenido["ruta"] = request.RouteValues;

            if (request.Query.Count > 0)
            {
                contenido["consulta"] = request.Query.ToDictionary(
                    x => x.Key,
                    x => AuditSanitizer.EsSensible(x.Key)
                        ? "***PROTEGIDO***"
                        : AuditSanitizer.Truncar(x.Value.ToString(), 2000));
            }

            if (!string.IsNullOrWhiteSpace(cuerpoSolicitud))
            {
                try
                {
                    contenido["cuerpo"] = JsonSerializer.Deserialize<object>(
                        cuerpoSolicitud);
                }
                catch
                {
                    contenido["cuerpo"] = cuerpoSolicitud;
                }
            }

            return AuditSanitizer.Truncar(
                JsonSerializer.Serialize(contenido),
                16000);
        }


        private static string ConstruirEndpoint(HttpRequest request)
        {
            if (request.Query.Count == 0)
                return request.Path.Value ?? string.Empty;

            IEnumerable<string> pares = request.Query.Select(x =>
            {
                string valor = AuditSanitizer.EsSensible(x.Key)
                    ? "***PROTEGIDO***"
                    : AuditSanitizer.Truncar(x.Value.ToString(), 500);

                return $"{Uri.EscapeDataString(x.Key)}=" +
                       Uri.EscapeDataString(valor);
            });

            return $"{request.Path}?{string.Join("&", pares)}";
        }

        private static string ObtenerEncabezado(
            HttpRequest request,
            string nombre)
        {
            if (!request.Headers.TryGetValue(nombre, out var valor))
                return string.Empty;

            string texto = valor.ToString();

            try
            {
                return Uri.UnescapeDataString(texto);
            }
            catch
            {
                return texto;
            }
        }

        private static string ObtenerUsuarioLogin(string cuerpo)
        {
            if (string.IsNullOrWhiteSpace(cuerpo))
                return string.Empty;

            try
            {
                using JsonDocument documento = JsonDocument.Parse(cuerpo);
                JsonElement raiz = documento.RootElement;

                string[] nombres =
                {
                    "usuarioOEmail", "nombreUsuario", "NombreUsuario",
                    "username", "usuario"
                };

                foreach (string nombre in nombres)
                {
                    if (raiz.TryGetProperty(nombre, out JsonElement valor))
                        return valor.GetString() ?? string.Empty;
                }
            }
            catch
            {
            }

            return string.Empty;
        }
    }
}
