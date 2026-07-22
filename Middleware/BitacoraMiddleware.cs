using CONATRADEC_API.Auditing;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CONATRADEC_API.Middleware
{
    /// <summary>
    /// Registra en la bitácora todas las solicitudes dirigidas a la API.
    /// Para el inicio de sesión obtiene la identidad directamente desde la
    /// base de datos después de que la autenticación finaliza correctamente.
    /// </summary>
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
            // El guardado directo con BitacoraDbContext no genera otra
            // solicitud HTTP, por lo que no se produce recursión.
            return ruta.StartsWithSegments("/api");
        }

        private static async Task<string> LeerCuerpoSolicitudAsync(
            HttpRequest request)
        {
            if (request.ContentLength.HasValue &&
                (request.ContentLength.Value <= 0 ||
                 request.ContentLength.Value > MaximoCuerpoSolicitud))
            {
                return string.Empty;
            }

            if (request.ContentType == null ||
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

            if (cuerpo.Length > MaximoCuerpoSolicitud)
                cuerpo = cuerpo[..MaximoCuerpoSolicitud];

            return AuditSanitizer.SanitizarJson(cuerpo);
        }

        private static async Task GuardarBitacoraAsync(
            HttpContext httpContext,
            AuditRequestContext auditRequestContext,
            string cuerpoSolicitud,
            long duracionMs,
            Exception? excepcion)
        {
            BitacoraDbContext bitacoraDb = httpContext.RequestServices
                .GetRequiredService<BitacoraDbContext>();

            HttpRequest request = httpContext.Request;

            int codigoEstado = excepcion == null
                ? httpContext.Response.StatusCode
                : StatusCodes.Status500InternalServerError;

            string modulo = ObtenerModulo(httpContext);
            string accion = ObtenerAccion(
                request.Method,
                request.Path);

            IdentidadBitacora identidad = await ResolverIdentidadAsync(
                httpContext,
                cuerpoSolicitud,
                codigoEstado,
                excepcion);

            var bitacora = new Bitacora
            {
                bitacoraId = auditRequestContext.BitacoraId,
                fechaHoraUtc = DateTime.UtcNow,
                usuarioId = identidad.UsuarioId,
                usuarioNombre = AuditSanitizer.Truncar(
                    identidad.UsuarioNombre,
                    150),
                rolNombre = AuditSanitizer.Truncar(
                    identidad.RolNombre,
                    100),
                modulo = AuditSanitizer.Truncar(
                    modulo,
                    120),
                accion = accion,
                metodoHttp = request.Method,
                endpoint = AuditSanitizer.Truncar(
                    ConstruirEndpoint(request),
                    500),
                paginaOrigen = AuditSanitizer.Truncar(
                    ObtenerEncabezado(
                        request,
                        "X-Pagina-Origen"),
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
                    ObtenerEncabezado(
                        request,
                        "X-Dispositivo"),
                    200),
                plataforma = AuditSanitizer.Truncar(
                    ObtenerEncabezado(
                        request,
                        "X-Plataforma"),
                    100),
                versionApp = AuditSanitizer.Truncar(
                    ObtenerEncabezado(
                        request,
                        "X-Version-App"),
                    50),
                correlationId = AuditSanitizer.Truncar(
                    httpContext.TraceIdentifier,
                    100),
                codigoEstado = codigoEstado,
                exitoso = excepcion == null &&
                           codigoEstado < 400,
                duracionMs = duracionMs,
                error = AuditSanitizer.Truncar(
                    excepcion?.ToString(),
                    16000)
            };

            foreach (AuditEntityChange cambio
                     in auditRequestContext.Cambios)
            {
                bitacora.detalles.Add(new BitacoraDetalle
                {
                    bitacoraId = bitacora.bitacoraId,
                    fechaHoraUtc = cambio.FechaHoraUtc,
                    entidad = AuditSanitizer.Truncar(
                        cambio.Entidad,
                        150),
                    entidadId = AuditSanitizer.Truncar(
                        cambio.EntidadId,
                        300),
                    operacion = AuditSanitizer.Truncar(
                        cambio.Operacion,
                        30),
                    valoresAnteriores =
                        cambio.ValoresAnteriores,
                    valoresNuevos =
                        cambio.ValoresNuevos,
                    propiedadesModificadas =
                        cambio.PropiedadesModificadas
                });
            }

            bitacoraDb.Bitacoras.Add(bitacora);
            await bitacoraDb.SaveChangesAsync(
                CancellationToken.None);
        }

        /// <summary>
        /// Para solicitudes normales utiliza los encabezados enviados por
        /// la aplicación. Para el login ignora esos encabezados, porque
        /// podrían pertenecer a una sesión anterior, y resuelve el usuario
        /// autenticado directamente desde la base de datos.
        /// </summary>
        private static async Task<IdentidadBitacora>
            ResolverIdentidadAsync(
                HttpContext httpContext,
                string cuerpoSolicitud,
                int codigoEstado,
                Exception? excepcion)
        {
            HttpRequest request = httpContext.Request;
            bool esLogin = request.Path.StartsWithSegments(
                "/api/auth/login");

            if (!esLogin)
            {
                int? usuarioId = int.TryParse(
                    ObtenerEncabezado(
                        request,
                        "X-Usuario-Id"),
                    out int idUsuario)
                        ? idUsuario
                        : null;

                return new IdentidadBitacora
                {
                    UsuarioId = usuarioId,
                    UsuarioNombre = ObtenerEncabezado(
                        request,
                        "X-Usuario-Nombre"),
                    RolNombre = ObtenerEncabezado(
                        request,
                        "X-Rol-Nombre")
                };
            }

            // En el login no se utilizan los encabezados de identidad,
            // porque la sesión nueva todavía no ha sido guardada por MAUI.
            string usuarioIngresado =
                ObtenerUsuarioLogin(cuerpoSolicitud);

            var identidad = new IdentidadBitacora
            {
                UsuarioId = null,
                UsuarioNombre = usuarioIngresado,
                RolNombre = string.Empty
            };

            bool loginExitoso =
                excepcion == null &&
                codigoEstado >= 200 &&
                codigoEstado < 300;

            if (!loginExitoso ||
                string.IsNullOrWhiteSpace(usuarioIngresado))
            {
                return identidad;
            }

            try
            {
                DBContext db = httpContext.RequestServices
                    .GetRequiredService<DBContext>();

                string valorBusqueda = usuarioIngresado.Trim();

                var usuario = await db.Usuarios
                    .AsNoTracking()
                    .Where(x =>
                        x.activo &&
                        (x.nombreUsuario == valorBusqueda ||
                         x.correoUsuario == valorBusqueda))
                    .Select(x => new
                    {
                        x.UsuarioId,
                        x.nombreUsuario,
                        x.nombreCompletoUsuario,
                        RolNombre = x.Rol.nombreRol
                    })
                    .FirstOrDefaultAsync(
                        CancellationToken.None);

                if (usuario != null)
                {
                    identidad.UsuarioId = usuario.UsuarioId;
                    identidad.UsuarioNombre =
                        !string.IsNullOrWhiteSpace(
                            usuario.nombreCompletoUsuario)
                            ? usuario.nombreCompletoUsuario
                            : usuario.nombreUsuario;

                    identidad.RolNombre =
                        usuario.RolNombre ?? string.Empty;
                }
            }
            catch
            {
                // Si no fuera posible resolver la identidad, se conserva
                // al menos el usuario escrito en el formulario. Un error
                // secundario de auditoría no debe afectar el login.
            }

            return identidad;
        }

        private static string ObtenerModulo(
            HttpContext context)
        {
            ControllerActionDescriptor? descriptor = context
                .GetEndpoint()?
                .Metadata
                .GetMetadata<ControllerActionDescriptor>();

            if (!string.IsNullOrWhiteSpace(
                    descriptor?.ControllerName))
            {
                return descriptor.ControllerName;
            }

            string[] segmentos = context.Request.Path.Value?
                .Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries) ??
                Array.Empty<string>();

            return segmentos.Length > 1
                ? segmentos[1]
                : "Sistema";
        }

        private static string ObtenerAccion(
            string metodo,
            PathString ruta)
        {
            string rutaTexto = ruta.Value ?? string.Empty;

            if (ruta.StartsWithSegments(
                    "/api/auth/login"))
            {
                return "INICIAR_SESION";
            }

            if (rutaTexto.Contains(
                    "eliminar",
                    StringComparison.OrdinalIgnoreCase) ||
                rutaTexto.Contains(
                    "desactivar",
                    StringComparison.OrdinalIgnoreCase) ||
                rutaTexto.Contains(
                    "anular",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "ELIMINAR";
            }

            if (rutaTexto.Contains(
                    "actualizar",
                    StringComparison.OrdinalIgnoreCase) ||
                rutaTexto.Contains(
                    "editar",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "ACTUALIZAR";
            }

            if (rutaTexto.Contains(
                    "calcular",
                    StringComparison.OrdinalIgnoreCase) ||
                rutaTexto.Contains(
                    "reporte",
                    StringComparison.OrdinalIgnoreCase) ||
                rutaTexto.Contains(
                    "pdf",
                    StringComparison.OrdinalIgnoreCase) ||
                rutaTexto.Contains(
                    "exportar",
                    StringComparison.OrdinalIgnoreCase))
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
            var contenido =
                new Dictionary<string, object?>();

            if (request.RouteValues.Count > 0)
                contenido["ruta"] = request.RouteValues;

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

            if (!string.IsNullOrWhiteSpace(
                    cuerpoSolicitud))
            {
                try
                {
                    contenido["cuerpo"] =
                        JsonSerializer.Deserialize<object>(
                            cuerpoSolicitud);
                }
                catch
                {
                    contenido["cuerpo"] =
                        cuerpoSolicitud;
                }
            }

            return AuditSanitizer.Truncar(
                JsonSerializer.Serialize(contenido),
                16000);
        }

        private static string ConstruirEndpoint(
            HttpRequest request)
        {
            if (request.Query.Count == 0)
                return request.Path.Value ?? string.Empty;

            IEnumerable<string> pares =
                request.Query.Select(x =>
                {
                    string valor =
                        AuditSanitizer.EsSensible(x.Key)
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
                    out var valor))
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

        /// <summary>
        /// Obtiene el usuario escrito en el JSON del login sin depender
        /// de mayúsculas o minúsculas. La contraseña ya llega protegida
        /// por AuditSanitizer.
        /// </summary>
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
                                StringComparison.OrdinalIgnoreCase));

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
                // El registro de auditoría no debe interrumpir la API.
            }

            return string.Empty;
        }

        private sealed class IdentidadBitacora
        {
            public int? UsuarioId { get; set; }
            public string UsuarioNombre { get; set; } =
                string.Empty;
            public string RolNombre { get; set; } =
                string.Empty;
        }
    }
}