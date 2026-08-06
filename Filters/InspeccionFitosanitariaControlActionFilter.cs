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
    /// - el técnico puede preparar fotografías mientras su etapa siga abierta;
    /// - las fotografías enviadas ya no pueden ser descartadas por el técnico;
    /// - el analizador y el aprobador trabajan únicamente después del cierre
    ///   de la etapa técnica;
    /// - el cierre definitivo convierte todo el expediente en solo lectura;
    /// - las decisiones individuales se ejecutan sobre una fotografía por
    ///   petición.
    /// </summary>
    public sealed class InspeccionFitosanitariaControlActionFilter :
        IAsyncActionFilter
    {
        private const string RutaBase = "/api/inspecciones-fitosanitarias";
        private const string RutaAlbumJerarquia = "/api/album-jerarquia";

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
            if (!EsRutaControlada(ruta))
            {
                await next();
                return;
            }

            CancellationToken cancellationToken =
                context.HttpContext.RequestAborted;

            await control.InicializarAsync(cancellationToken);

            /*
             * El controlador histórico todavía expone /cerrar-tecnico. Esa
             * ruta mezclaba el cierre técnico con el cierre global anterior.
             * Se conserva únicamente para compatibilidad de compilación, pero
             * se bloquea para impedir que un cliente antiguo ejecute el flujo
             * equivocado. El frontend actualizado usa
             * /finalizar-etapa-tecnica.
             */
            if (EsRutaCierreTecnicoAnterior(ruta))
            {
                context.Result = new ConflictObjectResult(new
                {
                    success = false,
                    message =
                        "La ruta cerrar-tecnico fue reemplazada por finalizar-etapa-tecnica. Actualice el cliente antes de continuar."
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

                if (registro?.CerradaDefinitiva == true ||
                    registro?.CerradaTecnico == true)
                {
                    context.Result = new ConflictObjectResult(new
                    {
                        success = false,
                        message =
                            "La inspección está cerrada definitivamente y es de solo lectura. No se permite realizar ninguna modificación."
                    });
                    return;
                }

                if (registro != null)
                {
                    if (registro.EtapaTecnicaFinalizada &&
                        EsOperacionTecnico(ruta))
                    {
                        context.Result = new ConflictObjectResult(new
                        {
                            success = false,
                            message =
                                "La etapa técnica ya fue finalizada. El técnico no puede agregar, reevaluar, descartar ni modificar fotografías enviadas a revisión."
                        });
                        return;
                    }

                    bool operacionAnalizador =
                        EsOperacionAnalizador(context, ruta);
                    bool operacionAprobador =
                        EsOperacionAprobador(context, ruta);

                    if (!registro.EtapaTecnicaFinalizada &&
                        (operacionAnalizador || operacionAprobador))
                    {
                        context.Result = new ConflictObjectResult(new
                        {
                            success = false,
                            message = operacionAnalizador
                                ? "El analizador no puede intervenir hasta que el técnico finalice y envíe la inspección."
                                : "La inspección todavía no ha finalizado su etapa técnica."
                        });
                        return;
                    }

                    if (EsRutaDescarte(ruta))
                    {
                        IActionResult? errorDescarte =
                            await ValidarDescarteTecnicoAsync(
                                context,
                                id.Value,
                                cancellationToken);

                        if (errorDescarte != null)
                        {
                            context.Result = errorDescarte;
                            return;
                        }
                    }
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

            await FiltrarBandejaPorEtapaAsync(
                context,
                ejecutado.Result,
                cancellationToken);

            await EnriquecerDetalleAsync(
                context,
                ejecutado.Result,
                cancellationToken);
        }

        private async Task<IActionResult?> ValidarDescarteTecnicoAsync(
            ActionExecutingContext context,
            int inspeccionId,
            CancellationToken cancellationToken)
        {
            int[] ids = ObtenerFotografiaIds(context);
            if (ids.Length == 0)
                return null;

            Dictionary<int, string> estados =
                await control.ObtenerEstadosFotografiasAsync(
                    inspeccionId,
                    ids,
                    cancellationToken);

            string[] permitidos =
            [
                "BORRADOR",
                "PENDIENTE_IA",
                "ERROR_IA",
                "PENDIENTE_DECISION_TECNICO"
            ];

            bool existeBloqueada = ids.Any(id =>
                !estados.TryGetValue(id, out string? estado) ||
                !permitidos.Contains(
                    estado,
                    StringComparer.OrdinalIgnoreCase));

            if (!existeBloqueada)
                return null;

            return new ConflictObjectResult(new
            {
                success = false,
                message =
                    "No puede descartar una fotografía después de enviarla a revisión. La evidencia y su historial quedan bloqueados para el técnico desde el estado PENDIENTE_ANALIZADOR."
            });
        }

        private async Task FiltrarBandejaPorEtapaAsync(
            ActionExecutingContext context,
            IActionResult? resultado,
            CancellationToken cancellationToken)
        {
            string ruta = context.HttpContext.Request.Path.Value ?? string.Empty;
            if (!ruta.TrimEnd('/').EndsWith(
                    "/bandeja",
                    StringComparison.OrdinalIgnoreCase) ||
                resultado is not ObjectResult objectResult ||
                objectResult.Value == null)
            {
                return;
            }

            string modo = context.HttpContext.Request.Query["modo"]
                .ToString()
                .Trim()
                .ToLowerInvariant();

            Dictionary<string, object?> sobre = Atributos(objectResult.Value);
            if (!TryObtenerValor(sobre, "data", out object? data) ||
                data is not IEnumerable enumerable ||
                data is string)
            {
                return;
            }

            List<object> elementos = enumerable
                .Cast<object>()
                .ToList();

            int[] ids = elementos
                .Select(item =>
                {
                    Dictionary<string, object?> atributos = Atributos(item);
                    return TryObtenerEntero(
                        atributos,
                        "InspeccionId",
                        out int inspeccionId)
                            ? inspeccionId
                            : 0;
                })
                .Where(item => item > 0)
                .Distinct()
                .ToArray();

            Dictionary<int, InspeccionFitosanitariaControlRegistro> registros =
                await control.ObtenerPorInspeccionesAsync(
                    ids,
                    cancellationToken);

            bool requiereEtapaFinalizada =
                modo is "analizador" or "aprobador";

            var enriquecidos = new List<Dictionary<string, object?>>();

            foreach (object item in elementos)
            {
                Dictionary<string, object?> atributos = Atributos(item);
                if (!TryObtenerEntero(
                        atributos,
                        "InspeccionId",
                        out int inspeccionId) ||
                    !registros.TryGetValue(
                        inspeccionId,
                        out InspeccionFitosanitariaControlRegistro? registro))
                {
                    continue;
                }

                bool cierreDefinitivo =
                    registro.CerradaDefinitiva || registro.CerradaTecnico;

                if (requiereEtapaFinalizada &&
                    (!registro.EtapaTecnicaFinalizada || cierreDefinitivo))
                {
                    continue;
                }

                atributos["CerradaTecnico"] =
                    registro.EtapaTecnicaFinalizada;
                atributos["FechaCierreTecnicoUtc"] =
                    registro.FechaFinEtapaTecnicaUtc;
                atributos["CerradaDefinitiva"] = cierreDefinitivo;
                atributos["FechaCierreDefinitivoUtc"] =
                    registro.FechaCierreDefinitivoUtc ??
                    registro.FechaCierreTecnicoUtc;

                if (registro.EtapaTecnicaFinalizada &&
                    TryObtenerValor(
                        atributos,
                        "Estado",
                        out object? estadoActual) &&
                    string.Equals(
                        estadoActual?.ToString(),
                        "PENDIENTE_ANALIZADOR",
                        StringComparison.OrdinalIgnoreCase))
                {
                    atributos["Estado"] = "PENDIENTE_REVISION";
                }

                enriquecidos.Add(atributos);
            }

            sobre["data"] = enriquecidos;
            objectResult.Value = sobre;
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
            if (!TryObtenerValor(sobre, "data", out object? data) || data == null ||
                (data is IEnumerable && data is not string))
            {
                return;
            }

            Dictionary<string, object?> detalle = Atributos(data);
            if (!TryObtenerEntero(detalle, "InspeccionId", out int id))
                return;

            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(id, cancellationToken);

            if (registro == null)
                return;

            InspeccionFitosanitariaEstadoEtapaTecnica estadoTecnico =
                await control.ObtenerEstadoEtapaTecnicaAsync(
                    id,
                    cancellationToken);

            bool puedeGestionarOriginal = ObtenerBooleano(
                detalle,
                "PuedeGestionarSolicitud");

            int? usuarioId = ObtenerUsuarioId(context.HttpContext.User);

            detalle["NombreInspeccion"] = registro.NombreInspeccion;
            /*
             * Compatibilidad con el cliente MAUI actual: CerradaTecnico se
             * expone como cierre de la etapa técnica. El valor histórico de
             * cierre global permanece separado dentro del control interno.
             */
            detalle["CerradaTecnico"] = registro.EtapaTecnicaFinalizada;
            detalle["FechaCierreTecnicoUtc"] =
                registro.FechaFinEtapaTecnicaUtc;
            detalle["UsuarioCierreTecnicoId"] =
                registro.UsuarioFinEtapaTecnicaId;
            detalle["CerradaDefinitiva"] = registro.CerradaDefinitiva;
            detalle["FechaCierreDefinitivoUtc"] =
                registro.FechaCierreDefinitivoUtc;
            detalle["UsuarioCierreDefinitivoId"] =
                registro.UsuarioCierreDefinitivoId;
            detalle["EsSoloLectura"] =
                registro.CerradaDefinitiva || registro.CerradaTecnico;

            if (registro.EtapaTecnicaFinalizada &&
                TryObtenerValor(
                    detalle,
                    "Estado",
                    out object? estadoActual) &&
                string.Equals(
                    estadoActual?.ToString(),
                    "PENDIENTE_ANALIZADOR",
                    StringComparison.OrdinalIgnoreCase))
            {
                detalle["Estado"] = "PENDIENTE_REVISION";
            }

            if (registro.CerradaDefinitiva || registro.CerradaTecnico)
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

                bool puedeGestionar =
                    puedeGestionarOriginal &&
                    !registro.EtapaTecnicaFinalizada;

                detalle["PuedeGestionarSolicitud"] = puedeGestionar;
                detalle["PuedeAnalizar"] =
                    puedeAnalizar && registro.EtapaTecnicaFinalizada;
                detalle["PuedeAprobar"] =
                    puedeAprobar && registro.EtapaTecnicaFinalizada;
                detalle["PuedePublicarAlbum"] =
                    puedePublicar && registro.EtapaTecnicaFinalizada;
                detalle["PuedeCerrarInspeccion"] =
                    puedeGestionar && estadoTecnico.ListaParaCerrar;
                detalle["MotivoNoPuedeCerrar"] =
                    CrearMotivoCierreTecnico(
                        puedeGestionarOriginal,
                        registro,
                        estadoTecnico);
            }

            sobre["data"] = detalle;
            objectResult.Value = sobre;
        }

        private static string CrearMotivoCierreTecnico(
            bool puedeGestionar,
            InspeccionFitosanitariaControlRegistro registro,
            InspeccionFitosanitariaEstadoEtapaTecnica estado)
        {
            if (!puedeGestionar)
            {
                return "Solo el técnico que creó la inspección puede finalizar y enviarla al analizador.";
            }

            if (registro.EtapaTecnicaFinalizada)
            {
                return "La etapa técnica ya fue finalizada y la inspección se encuentra disponible para el analizador.";
            }

            if (estado.TotalActivas == 0)
            {
                return "La inspección debe conservar al menos una fotografía activa.";
            }

            if (estado.TotalEnviadasRevision == 0)
            {
                return "Envíe al menos una fotografía a revisión antes de finalizar la etapa técnica.";
            }

            if (estado.TotalProcesando > 0)
            {
                return $"Espere: {estado.TotalProcesando} fotografía(s) continúan procesándose.";
            }

            if (estado.TotalNoPreparadas > 0)
            {
                return $"Todavía existen {estado.TotalNoPreparadas} fotografía(s) que deben enviarse a revisión o descartarse.";
            }

            return "Todas las fotografías activas fueron enviadas a revisión o descartadas. Ya puede finalizar la etapa técnica.";
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

        private static bool EsRutaControlada(string ruta) =>
            ruta.StartsWith(RutaBase, StringComparison.OrdinalIgnoreCase) ||
            ruta.StartsWith(
                RutaAlbumJerarquia,
                StringComparison.OrdinalIgnoreCase);

        private static bool EsRutaCierreTecnicoAnterior(string ruta) =>
            ruta.TrimEnd('/').EndsWith(
                "/cerrar-tecnico",
                StringComparison.OrdinalIgnoreCase);

        private static bool EsOperacionTecnico(string ruta)
        {
            string normalizada = ruta.TrimEnd('/').ToLowerInvariant();

            if (normalizada.EndsWith("/cerrar-definitivo") ||
                normalizada.EndsWith("/cerrar-tecnico") ||
                normalizada.EndsWith("/finalizar-etapa-tecnica"))
            {
                return false;
            }

            return normalizada.EndsWith("/fotografias") ||
                   normalizada.EndsWith("/procesar-fotografias") ||
                   normalizada.EndsWith("/solicitar-revision-ia") ||
                   normalizada.EndsWith("/enviar-analizador") ||
                   normalizada.EndsWith("/descartar-fotografias");
        }

        private static bool EsOperacionAnalizador(
            ActionExecutingContext context,
            string ruta)
        {
            string normalizada = ruta.TrimEnd('/').ToLowerInvariant();

            if (normalizada.EndsWith("/analisis-humano") ||
                normalizada.EndsWith("/analisis-humano-individual"))
            {
                return true;
            }

            return normalizada.EndsWith("/resolver") &&
                   ObtenerEtapa(context) == "ANALIZADOR";
        }

        private static bool EsOperacionAprobador(
            ActionExecutingContext context,
            string ruta)
        {
            string normalizada = ruta.TrimEnd('/').ToLowerInvariant();

            if (normalizada.EndsWith("/aprobaciones") ||
                normalizada.EndsWith("/aprobacion-individual"))
            {
                return true;
            }

            return normalizada.EndsWith("/resolver") &&
                   ObtenerEtapa(context) == "APROBADOR";
        }

        private static string ObtenerEtapa(ActionExecutingContext context)
        {
            foreach (object? argumento in context.ActionArguments.Values)
            {
                PropertyInfo? propiedad = argumento?.GetType().GetProperty(
                    "Etapa",
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.IgnoreCase);

                string? valor = propiedad?.GetValue(argumento)?.ToString();
                if (!string.IsNullOrWhiteSpace(valor))
                    return valor.Trim().ToUpperInvariant();
            }

            return string.Empty;
        }

        private static bool EsRutaDescarte(string ruta) =>
            ruta.TrimEnd('/').EndsWith(
                "/descartar-fotografias",
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
            int[] ids = ObtenerFotografiaIds(context);
            return ids.Length == 0 ? null : ids.Length;
        }

        private static int[] ObtenerFotografiaIds(
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

                var ids = new List<int>();
                foreach (object? valor in valores)
                {
                    if (valor == null)
                        continue;

                    if (int.TryParse(valor.ToString(), out int idDirecto) &&
                        idDirecto > 0)
                    {
                        ids.Add(idDirecto);
                        continue;
                    }

                    PropertyInfo? propiedadId = valor.GetType().GetProperty(
                        "FotografiaId",
                        BindingFlags.Public |
                        BindingFlags.Instance |
                        BindingFlags.IgnoreCase);

                    if (int.TryParse(
                            propiedadId?.GetValue(valor)?.ToString(),
                            out int idObjeto) &&
                        idObjeto > 0)
                    {
                        ids.Add(idObjeto);
                    }
                }

                return ids.Distinct().ToArray();
            }

            return [];
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

        private static bool EsEscritura(string metodo) =>
            HttpMethods.IsPost(metodo) ||
            HttpMethods.IsPut(metodo) ||
            HttpMethods.IsPatch(metodo) ||
            HttpMethods.IsDelete(metodo);

        private static int? ObtenerId(ActionExecutingContext context)
        {
            string[] nombres = ["id", "diagnosticoId"];

            foreach (string nombre in nombres)
            {
                if (context.ActionArguments.TryGetValue(
                        nombre,
                        out object? valor) &&
                    int.TryParse(valor?.ToString(), out int id) &&
                    id > 0)
                {
                    return id;
                }

                if (context.RouteData.Values.TryGetValue(
                        nombre,
                        out valor) &&
                    int.TryParse(valor?.ToString(), out id) &&
                    id > 0)
                {
                    return id;
                }
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
