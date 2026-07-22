using CONATRADEC_API.Auditing;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CONATRADEC_API.Middleware
{
    /// <summary>
    /// Auditoría transversal de controladores:
    /// - cubre cualquier ControllerActionDescriptor, aunque no use /api;
    /// - valida la identidad contra la base de datos;
    /// - captura errores controlados y excepciones;
    /// - registra metadatos seguros de multipart/form-data;
    /// - no almacena archivos ni respuestas binarias completas.
    /// </summary>
    public sealed class BitacoraMiddleware
    {
        private const int MaximoCuerpoSolicitud =
            256 * 1024;

        private const int MaximoRespuestaCapturada =
            64 * 1024;

        private const int MaximoParametros =
            16000;

        private readonly RequestDelegate siguiente;
        private readonly ILogger<BitacoraMiddleware> logger;
        private readonly IServiceScopeFactory scopeFactory;

        public BitacoraMiddleware(
            RequestDelegate siguiente,
            ILogger<BitacoraMiddleware> logger,
            IServiceScopeFactory scopeFactory)
        {
            this.siguiente = siguiente;
            this.logger = logger;
            this.scopeFactory = scopeFactory;
        }

        public async Task InvokeAsync(
            HttpContext httpContext,
            AuditRequestContext auditRequestContext)
        {
            if (!DebeAuditar(httpContext))
            {
                await siguiente(httpContext);
                return;
            }

            Guid bitacoraId = Guid.NewGuid();
            auditRequestContext.Iniciar(bitacoraId);

            Stopwatch cronometro =
                Stopwatch.StartNew();

            Exception? excepcion = null;

            SolicitudAuditada solicitud =
                SolicitudAuditada.Vacia;

            IdentidadBitacora identidad =
                IdentidadBitacora.NoIdentificada;

            Stream cuerpoRespuestaOriginal =
                httpContext.Response.Body;

            var capturaRespuesta =
                new LimitedResponseCaptureStream(
                    cuerpoRespuestaOriginal,
                    MaximoRespuestaCapturada);

            httpContext.Response.Body =
                capturaRespuesta;

            httpContext.Response.Headers[
                "X-Correlation-Id"] =
                httpContext.TraceIdentifier;

            try
            {
                solicitud =
                    await PrepararSolicitudAsync(
                        httpContext);

                identidad =
                    await ResolverIdentidadInicialAsync(
                        httpContext,
                        solicitud);

                await siguiente(httpContext);

                if (EsLogin(httpContext.Request.Path))
                {
                    identidad =
                        await CompletarIdentidadLoginAsync(
                            httpContext,
                            solicitud,
                            identidad);
                }
            }
            catch (Exception ex)
            {
                excepcion = ex;

                logger.LogError(
                    ex,
                    "Error no controlado en {Metodo} {Ruta}. " +
                    "CorrelationId: {CorrelationId}",
                    httpContext.Request.Method,
                    httpContext.Request.Path,
                    httpContext.TraceIdentifier);

                throw;
            }
            finally
            {
                cronometro.Stop();

                httpContext.Response.Body =
                    cuerpoRespuestaOriginal;

                try
                {
                    await GuardarBitacoraAsync(
                        httpContext,
                        auditRequestContext,
                        solicitud,
                        identidad,
                        capturaRespuesta,
                        cronometro.ElapsedMilliseconds,
                        excepcion);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "No fue posible guardar la bitácora de " +
                        "{Metodo} {Ruta}. CorrelationId: {CorrelationId}",
                        httpContext.Request.Method,
                        httpContext.Request.Path,
                        httpContext.TraceIdentifier);
                }
                finally
                {
                    capturaRespuesta.Dispose();
                    auditRequestContext.Limpiar();
                }
            }
        }

        private static bool DebeAuditar(
            HttpContext context)
        {
            ControllerActionDescriptor? descriptor =
                context.GetEndpoint()?
                    .Metadata
                    .GetMetadata<ControllerActionDescriptor>();

            if (descriptor != null)
                return true;

            // También registra intentos a rutas /api inexistentes,
            // que no poseen ControllerActionDescriptor.
            return context.Request.Path.StartsWithSegments(
                "/api");
        }

        private static async Task<SolicitudAuditada>
            PrepararSolicitudAsync(
                HttpContext context)
        {
            HttpRequest request = context.Request;

            var contenido =
                new Dictionary<string, object?>();

            ControllerActionDescriptor? descriptor =
                context.GetEndpoint()?
                    .Metadata
                    .GetMetadata<ControllerActionDescriptor>();

            if (descriptor != null)
            {
                contenido["controlador"] =
                    descriptor.ControllerName;

                contenido["accionMvc"] =
                    descriptor.ActionName;
            }

            if (request.RouteValues.Count > 0)
            {
                contenido["ruta"] =
                    request.RouteValues.ToDictionary(
                        x => x.Key,
                        x => x.Value?.ToString());
            }

            if (request.Query.Count > 0)
            {
                contenido["consulta"] =
                    request.Query.ToDictionary(
                        x => x.Key,
                        x => AuditSanitizer.EsSensible(
                                x.Key)
                            ? "***PROTEGIDO***"
                            : AuditSanitizer.Truncar(
                                x.Value.ToString(),
                                2000));
            }

            string cuerpoJsonSanitizado =
                string.Empty;

            if (request.HasFormContentType)
            {
                await AgregarFormularioAsync(
                    request,
                    contenido);
            }
            else if (EsContenidoJson(
                         request.ContentType))
            {
                cuerpoJsonSanitizado =
                    await LeerJsonAsync(request);

                if (!string.IsNullOrWhiteSpace(
                        cuerpoJsonSanitizado))
                {
                    try
                    {
                        contenido["cuerpo"] =
                            JsonSerializer
                                .Deserialize<object>(
                                    cuerpoJsonSanitizado);
                    }
                    catch
                    {
                        contenido["cuerpo"] =
                            cuerpoJsonSanitizado;
                    }
                }
            }
            else if (request.ContentLength is > 0)
            {
                contenido["cuerpo"] =
                    $"[CONTENIDO NO TEXTUAL: " +
                    $"{request.ContentLength.Value:N0} bytes, " +
                    $"{request.ContentType ?? "sin tipo"}]";
            }

            string parametros =
                AuditSanitizer.Truncar(
                    JsonSerializer.Serialize(contenido),
                    MaximoParametros);

            return new SolicitudAuditada
            {
                Parametros = parametros,
                CuerpoJsonSanitizado =
                    cuerpoJsonSanitizado
            };
        }

        private static async Task AgregarFormularioAsync(
            HttpRequest request,
            IDictionary<string, object?> contenido)
        {
            try
            {
                IFormCollection formulario =
                    await request.ReadFormAsync(
                        request.HttpContext.RequestAborted);

                if (formulario.Count > 0)
                {
                    contenido["formulario"] =
                        formulario.ToDictionary(
                            x => x.Key,
                            x => AuditSanitizer.EsSensible(
                                    x.Key)
                                ? "***PROTEGIDO***"
                                : AuditSanitizer.Truncar(
                                    x.Value.ToString(),
                                    2000));
                }

                if (formulario.Files.Count > 0)
                {
                    contenido["archivos"] =
                        formulario.Files
                            .Take(50)
                            .Select(archivo => new
                            {
                                campo = archivo.Name,
                                nombre =
                                    Path.GetFileName(
                                        archivo.FileName),
                                tipoContenido =
                                    archivo.ContentType ??
                                    string.Empty,
                                tamanoBytes =
                                    archivo.Length
                            })
                            .ToList();

                    if (formulario.Files.Count > 50)
                    {
                        contenido[
                            "archivosAdicionalesOmitidos"] =
                            formulario.Files.Count - 50;
                    }
                }
            }
            catch (OperationCanceledException)
                when (request.HttpContext
                    .RequestAborted
                    .IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                contenido["formulario"] =
                    $"[NO FUE POSIBLE LEER METADATOS: " +
                    $"{ex.GetType().Name}]";
            }
        }

        private static async Task<string> LeerJsonAsync(
            HttpRequest request)
        {
            if (request.ContentLength is <= 0)
                return string.Empty;

            if (request.ContentLength >
                MaximoCuerpoSolicitud)
            {
                return JsonSerializer.Serialize(new
                {
                    contenidoOmitido = true,
                    motivo =
                        "El cuerpo supera el máximo permitido para auditoría.",
                    tamanoBytes =
                        request.ContentLength
                });
            }

            request.EnableBuffering();

            using var lector =
                new StreamReader(
                    request.Body,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);

            string cuerpo =
                await lector.ReadToEndAsync();

            request.Body.Position = 0;

            return AuditSanitizer.SanitizarJson(
                cuerpo,
                MaximoParametros);
        }

        private async Task GuardarBitacoraAsync(
            HttpContext httpContext,
            AuditRequestContext auditRequestContext,
            SolicitudAuditada solicitud,
            IdentidadBitacora identidad,
            LimitedResponseCaptureStream capturaRespuesta,
            long duracionMs,
            Exception? excepcion)
        {
            HttpRequest request =
                httpContext.Request;

            int codigoEstado =
                excepcion == null
                    ? httpContext.Response.StatusCode
                    : StatusCodes
                        .Status500InternalServerError;

            ControllerActionDescriptor? descriptor =
                httpContext.GetEndpoint()?
                    .Metadata
                    .GetMetadata<ControllerActionDescriptor>();

            string modulo =
                ObtenerModulo(
                    httpContext,
                    descriptor);

            string accion =
                ObtenerAccion(
                    httpContext,
                    descriptor);

            string error =
                ConstruirErrorSeguro(
                    httpContext.Response,
                    capturaRespuesta,
                    codigoEstado,
                    excepcion);

            string nombreAccion =
                descriptor?.ActionName ??
                string.Empty;

            string descripcion =
                string.IsNullOrWhiteSpace(nombreAccion)
                    ? $"{accion} en {modulo}"
                    : $"{accion} en {modulo}." +
                      $"{nombreAccion}";

            var bitacora = new Bitacora
            {
                bitacoraId =
                    auditRequestContext.BitacoraId,
                fechaHoraUtc = DateTime.UtcNow,
                usuarioId = identidad.UsuarioId,
                usuarioNombre =
                    AuditSanitizer.Truncar(
                        identidad.UsuarioNombre,
                        150),
                rolNombre =
                    AuditSanitizer.Truncar(
                        identidad.RolNombre,
                        100),
                modulo =
                    AuditSanitizer.Truncar(
                        modulo,
                        120),
                accion = accion,
                metodoHttp = request.Method,
                endpoint =
                    AuditSanitizer.Truncar(
                        ConstruirEndpoint(request),
                        500),
                paginaOrigen =
                    AuditSanitizer.Truncar(
                        ObtenerEncabezado(
                            request,
                            "X-Pagina-Origen"),
                        500),
                descripcion =
                    AuditSanitizer.Truncar(
                        descripcion,
                        1000),
                parametros =
                    solicitud.Parametros,
                direccionIp =
                    AuditSanitizer.Truncar(
                        httpContext.Connection
                            .RemoteIpAddress?
                            .ToString(),
                        100),
                dispositivo =
                    AuditSanitizer.Truncar(
                        ObtenerEncabezado(
                            request,
                            "X-Dispositivo"),
                        200),
                plataforma =
                    AuditSanitizer.Truncar(
                        ObtenerEncabezado(
                            request,
                            "X-Plataforma"),
                        100),
                versionApp =
                    AuditSanitizer.Truncar(
                        ObtenerEncabezado(
                            request,
                            "X-Version-App"),
                        50),
                correlationId =
                    AuditSanitizer.Truncar(
                        httpContext.TraceIdentifier,
                        100),
                codigoEstado = codigoEstado,
                exitoso =
                    excepcion == null &&
                    codigoEstado < 400,
                duracionMs = duracionMs,
                error = error
            };

            foreach (AuditEntityChange cambio
                     in auditRequestContext.Cambios)
            {
                bitacora.detalles.Add(
                    new BitacoraDetalle
                    {
                        bitacoraId =
                            bitacora.bitacoraId,
                        fechaHoraUtc =
                            cambio.FechaHoraUtc,
                        entidad =
                            AuditSanitizer.Truncar(
                                cambio.Entidad,
                                150),
                        entidadId =
                            AuditSanitizer.Truncar(
                                cambio.EntidadId,
                                300),
                        operacion =
                            AuditSanitizer.Truncar(
                                cambio.Operacion,
                                30),
                        valoresAnteriores =
                            cambio.ValoresAnteriores,
                        valoresNuevos =
                            cambio.ValoresNuevos,
                        propiedadesModificadas =
                            cambio
                                .PropiedadesModificadas
                    });
            }

            // Contexto nuevo e independiente del empleado por el endpoint.
            // Así, una excepción o un estado inválido del contexto principal
            // no impide guardar la auditoría.
            await using AsyncServiceScope scope =
                scopeFactory.CreateAsyncScope();

            BitacoraDbContext bitacoraDb =
                scope.ServiceProvider
                    .GetRequiredService<
                        BitacoraDbContext>();

            bitacoraDb.Bitacoras.Add(bitacora);

            await bitacoraDb.SaveChangesAsync(
                CancellationToken.None);
        }

        private static async Task<IdentidadBitacora>
            ResolverIdentidadInicialAsync(
                HttpContext context,
                SolicitudAuditada solicitud)
        {
            if (EsLogin(context.Request.Path))
            {
                string usuarioIngresado =
                    ObtenerUsuarioLogin(
                        solicitud.CuerpoJsonSanitizado);

                return new IdentidadBitacora
                {
                    UsuarioId = null,
                    UsuarioNombre =
                        string.IsNullOrWhiteSpace(
                            usuarioIngresado)
                            ? "Usuario no identificado"
                            : usuarioIngresado,
                    RolNombre = string.Empty
                };
            }

            string encabezadoUsuarioId =
                ObtenerEncabezado(
                    context.Request,
                    "X-Usuario-Id");

            if (!int.TryParse(
                    encabezadoUsuarioId,
                    out int usuarioId) ||
                usuarioId <= 0)
            {
                return IdentidadBitacora.NoIdentificada;
            }

            try
            {
                DBContext db =
                    context.RequestServices
                        .GetRequiredService<DBContext>();

                var usuario =
                    await db.Usuarios
                        .AsNoTracking()
                        .Where(x =>
                            x.UsuarioId == usuarioId &&
                            x.activo)
                        .Select(x => new
                        {
                            x.UsuarioId,
                            x.nombreUsuario,
                            x.nombreCompletoUsuario,
                            RolNombre =
                                x.Rol != null
                                    ? x.Rol.nombreRol
                                    : string.Empty
                        })
                        .FirstOrDefaultAsync(
                            context.RequestAborted);

                if (usuario == null)
                {
                    return new IdentidadBitacora
                    {
                        UsuarioId = null,
                        UsuarioNombre =
                            $"Usuario no validado " +
                            $"(ID {usuarioId})",
                        RolNombre = string.Empty
                    };
                }

                return new IdentidadBitacora
                {
                    UsuarioId = usuario.UsuarioId,
                    UsuarioNombre =
                        !string.IsNullOrWhiteSpace(
                            usuario.nombreCompletoUsuario)
                            ? usuario
                                .nombreCompletoUsuario
                            : usuario.nombreUsuario,
                    RolNombre =
                        usuario.RolNombre ??
                        string.Empty
                };
            }
            catch
            {
                // Un problema secundario al resolver la identidad no debe
                // impedir que el endpoint se ejecute.
                return new IdentidadBitacora
                {
                    UsuarioId = null,
                    UsuarioNombre =
                        $"Usuario no validado " +
                        $"(ID {usuarioId})",
                    RolNombre = string.Empty
                };
            }
        }

        private static async Task<IdentidadBitacora>
            CompletarIdentidadLoginAsync(
                HttpContext context,
                SolicitudAuditada solicitud,
                IdentidadBitacora identidadInicial)
        {
            int codigoEstado =
                context.Response.StatusCode;

            bool loginExitoso =
                codigoEstado >= 200 &&
                codigoEstado < 300;

            if (!loginExitoso)
                return identidadInicial;

            string usuarioIngresado =
                ObtenerUsuarioLogin(
                    solicitud.CuerpoJsonSanitizado);

            if (string.IsNullOrWhiteSpace(
                    usuarioIngresado))
            {
                return identidadInicial;
            }

            try
            {
                DBContext db =
                    context.RequestServices
                        .GetRequiredService<DBContext>();

                string valorBusqueda =
                    usuarioIngresado.Trim();

                var usuario =
                    await db.Usuarios
                        .AsNoTracking()
                        .Where(x =>
                            x.activo &&
                            (x.nombreUsuario ==
                                valorBusqueda ||
                             x.correoUsuario ==
                                valorBusqueda))
                        .Select(x => new
                        {
                            x.UsuarioId,
                            x.nombreUsuario,
                            x.nombreCompletoUsuario,
                            RolNombre =
                                x.Rol != null
                                    ? x.Rol.nombreRol
                                    : string.Empty
                        })
                        .FirstOrDefaultAsync(
                            CancellationToken.None);

                if (usuario == null)
                    return identidadInicial;

                return new IdentidadBitacora
                {
                    UsuarioId = usuario.UsuarioId,
                    UsuarioNombre =
                        !string.IsNullOrWhiteSpace(
                            usuario.nombreCompletoUsuario)
                            ? usuario
                                .nombreCompletoUsuario
                            : usuario.nombreUsuario,
                    RolNombre =
                        usuario.RolNombre ??
                        string.Empty
                };
            }
            catch
            {
                return identidadInicial;
            }
        }

        private static string ConstruirErrorSeguro(
            HttpResponse response,
            LimitedResponseCaptureStream captura,
            int codigoEstado,
            Exception? excepcion)
        {
            if (excepcion != null)
            {
                var mensajes = new List<string>();
                Exception? actual = excepcion;
                int nivel = 0;

                while (actual != null && nivel < 5)
                {
                    mensajes.Add(
                        $"{actual.GetType().Name}: " +
                        $"{actual.Message}");

                    actual = actual.InnerException;
                    nivel++;
                }

                return AuditSanitizer.SanitizarTexto(
                    string.Join(
                        " | ",
                        mensajes),
                    MaximoParametros);
            }

            if (codigoEstado < 400)
                return string.Empty;

            if (!EsContenidoTexto(
                    response.ContentType))
            {
                return $"HTTP {codigoEstado} " +
                       "sin detalle textual.";
            }

            byte[] bytes =
                captura.ObtenerBytes();

            if (bytes.Length == 0)
            {
                return $"HTTP {codigoEstado} " +
                       "sin detalle de respuesta.";
            }

            string contenido =
                Encoding.UTF8.GetString(bytes);

            if (EsContenidoJson(
                    response.ContentType))
            {
                return AuditSanitizer.SanitizarJson(
                    contenido,
                    MaximoParametros);
            }

            return AuditSanitizer.SanitizarTexto(
                contenido,
                MaximoParametros);
        }

        private static string ObtenerModulo(
            HttpContext context,
            ControllerActionDescriptor? descriptor)
        {
            if (!string.IsNullOrWhiteSpace(
                    descriptor?.ControllerName))
            {
                return descriptor.ControllerName;
            }

            string[] segmentos =
                context.Request.Path.Value?
                    .Split(
                        '/',
                        StringSplitOptions
                            .RemoveEmptyEntries) ??
                Array.Empty<string>();

            return segmentos.Length > 0
                ? segmentos[0]
                : "Sistema";
        }

        private static string ObtenerAccion(
            HttpContext context,
            ControllerActionDescriptor? descriptor)
        {
            HttpRequest request = context.Request;

            if (EsLogin(request.Path))
                return "INICIAR_SESION";

            string texto =
                $"{request.Path} " +
                $"{descriptor?.ActionName ?? string.Empty}";

            if (ContieneAlguno(
                    texto,
                    "eliminar",
                    "delete",
                    "desactivar",
                    "anular"))
            {
                return "ELIMINAR";
            }

            if (ContieneAlguno(
                    texto,
                    "actualizar",
                    "update",
                    "editar",
                    "edit"))
            {
                return "ACTUALIZAR";
            }

            if (ContieneAlguno(
                    texto,
                    "calcular",
                    "reporte",
                    "pdf",
                    "exportar",
                    "descargar",
                    "generar"))
            {
                return "EJECUTAR";
            }

            return request.Method
                .ToUpperInvariant() switch
            {
                "GET" => "CONSULTAR",
                "HEAD" => "CONSULTAR",
                "POST" => "CREAR",
                "PUT" => "ACTUALIZAR",
                "PATCH" => "ACTUALIZAR",
                "DELETE" => "ELIMINAR",
                _ => "EJECUTAR"
            };
        }

        private static bool ContieneAlguno(
            string texto,
            params string[] valores)
        {
            return valores.Any(valor =>
                texto.Contains(
                    valor,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string ConstruirEndpoint(
            HttpRequest request)
        {
            if (request.Query.Count == 0)
                return request.Path.Value ??
                       string.Empty;

            IEnumerable<string> pares =
                request.Query.Select(x =>
                {
                    string valor =
                        AuditSanitizer.EsSensible(
                            x.Key)
                            ? "***PROTEGIDO***"
                            : AuditSanitizer.Truncar(
                                x.Value.ToString(),
                                500);

                    return
                        $"{Uri.EscapeDataString(x.Key)}=" +
                        Uri.EscapeDataString(valor);
                });

            return
                $"{request.Path}?" +
                string.Join("&", pares);
        }

        private static string ObtenerEncabezado(
            HttpRequest request,
            string nombre)
        {
            if (!request.Headers.TryGetValue(
                    nombre,
                    out StringValues valor))
            {
                return string.Empty;
            }

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

        private static string ObtenerUsuarioLogin(
            string cuerpo)
        {
            if (string.IsNullOrWhiteSpace(cuerpo))
                return string.Empty;

            try
            {
                using JsonDocument documento =
                    JsonDocument.Parse(cuerpo);

                JsonElement raiz =
                    documento.RootElement;

                if (raiz.ValueKind !=
                    JsonValueKind.Object)
                {
                    return string.Empty;
                }

                string[] nombresPermitidos =
                {
                    "UsuarioOEmail",
                    "usuarioOEmail",
                    "usuarioOEmail",
                    "NombreUsuario",
                    "nombreUsuario",
                    "username",
                    "usuario"
                };

                foreach (JsonProperty propiedad
                         in raiz.EnumerateObject())
                {
                    bool coincide =
                        nombresPermitidos.Any(nombre =>
                            string.Equals(
                                nombre,
                                propiedad.Name,
                                StringComparison
                                    .OrdinalIgnoreCase));

                    if (!coincide)
                        continue;

                    return propiedad.Value.ValueKind ==
                           JsonValueKind.String
                        ? propiedad.Value.GetString() ??
                          string.Empty
                        : propiedad.Value.ToString();
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static bool EsLogin(
            PathString ruta)
        {
            return ruta.StartsWithSegments(
                "/api/auth/login");
        }

        private static bool EsContenidoJson(
            string? contentType)
        {
            return !string.IsNullOrWhiteSpace(
                       contentType) &&
                   (contentType.Contains(
                        "application/json",
                        StringComparison.OrdinalIgnoreCase) ||
                    contentType.Contains(
                        "+json",
                        StringComparison.OrdinalIgnoreCase));
        }

        private static bool EsContenidoTexto(
            string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return false;

            return EsContenidoJson(contentType) ||
                   contentType.StartsWith(
                       "text/",
                       StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains(
                       "application/problem",
                       StringComparison.OrdinalIgnoreCase) ||
                   contentType.Contains(
                       "application/xml",
                       StringComparison.OrdinalIgnoreCase);
        }

        private sealed class SolicitudAuditada
        {
            public static SolicitudAuditada Vacia { get; } =
                new();

            public string Parametros { get; init; } =
                "{}";

            public string CuerpoJsonSanitizado { get; init; } =
                string.Empty;
        }

        private sealed class IdentidadBitacora
        {
            public static IdentidadBitacora NoIdentificada =>
                new()
                {
                    UsuarioId = null,
                    UsuarioNombre =
                        "Usuario no identificado",
                    RolNombre = string.Empty
                };

            public int? UsuarioId { get; init; }

            public string UsuarioNombre { get; init; } =
                string.Empty;

            public string RolNombre { get; init; } =
                string.Empty;
        }
    }
}
