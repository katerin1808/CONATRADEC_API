using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Security.Claims;

namespace CONATRADEC_API.Filters
{
    /// <summary>
    /// Reglas transversales del flujo fitosanitario:
    /// - el técnico puede preparar fotografías mientras su etapa siga abierta;
    /// - las fotografías enviadas ya no pueden ser descartadas por el técnico;
    /// - el analizador puede revisar las evidencias ya enviadas aunque la etapa
    ///   técnica todavía continúe abierta;
    /// - el aprobador interviene después de finalizar la revisión humana;
    /// - el cierre definitivo convierte todo el expediente en solo lectura;
    /// - las decisiones individuales conservan el historial de cada fotografía;
    /// - el límite de reevaluaciones IA se aplica por fotografía y se valida
    ///   también en backend antes de iniciar una nueva llamada al proveedor.
    /// </summary>
    public sealed class InspeccionFitosanitariaControlActionFilter :
        IAsyncActionFilter
    {
        private const string RutaBase = "/api/inspecciones-fitosanitarias";
        private const string RutaAlbumJerarquia = "/api/album-jerarquia";
        private const string RutaClasificacionIA =
            "/api/diagnostico-ia-clasificacion";
        private const string RutaPublicacionAlbum =
            "/api/publicaciones-album-fitosanitarias";

        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;
        private readonly InspeccionFitosanitariaDevolucionDatabase devoluciones;
        private readonly InspeccionFitosanitariaAsignacionDatabase asignaciones;
        private readonly PermisoApiService permisos;

        public InspeccionFitosanitariaControlActionFilter(
            DiagnosticoIADbContext db,
            InspeccionFitosanitariaControlDatabaseInitializer control,
            PermisoApiService permisos)
        {
            diagnosticoDb = db;
            this.control = control;
            this.permisos = permisos;
            devoluciones = new InspeccionFitosanitariaDevolucionDatabase(db);
            asignaciones = new InspeccionFitosanitariaAsignacionDatabase(db);
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

            if (EsRutaSolicitudRevisionIA(ruta))
            {
                IActionResult? errorLimite =
                    await ValidarLimiteReevaluacionIAAsync(
                        context,
                        cancellationToken);

                if (errorLimite != null)
                {
                    context.Result = errorLimite;
                    return;
                }
            }

            int? id = ObtenerId(context);
            bool escritura = EsEscritura(context.HttpContext.Request.Method);

            if (escritura && id is > 0)
            {
                InspeccionFitosanitariaControlRegistro? registro =
                    await control.ObtenerAsync(id.Value, cancellationToken);

                if (registro?.CerradaDefinitiva == true &&
                    !EsRutaPublicacionAlbum(ruta))
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
                        operacionAprobador)
                    {
                        context.Result = new ConflictObjectResult(new
                        {
                            success = false,
                            message =
                                "La revisión humana todavía no ha sido finalizada. El aprobador no puede intervenir."
                        });
                        return;
                    }

                    if (!registro.EtapaTecnicaFinalizada &&
                        operacionAnalizador)
                    {
                        InspeccionFitosanitariaEstadoEtapaTecnica
                            estadoRecepcion =
                                await control.ObtenerEstadoEtapaTecnicaAsync(
                                    id.Value,
                                    cancellationToken);

                        if (estadoRecepcion.TotalEnviadasRevision == 0)
                        {
                            context.Result = new ConflictObjectResult(new
                            {
                                success = false,
                                message =
                                    "El técnico todavía no ha enviado fotografías para revisión humana."
                            });
                            return;
                        }
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

            if (EsRespuestaExitosa(ejecutado.Result) &&
                EsRutaDescarte(ruta) &&
                id is > 0)
            {
                int? usuarioId = ObtenerUsuarioId(context.HttpContext.User);
                if (usuarioId.HasValue)
                {
                    await devoluciones.ResolverDevolucionesPorDescarteAsync(
                        id.Value,
                        ObtenerFotografiaIds(context),
                        usuarioId.Value,
                        cancellationToken);
                }
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

        private async Task<IActionResult?> ValidarLimiteReevaluacionIAAsync(
            ActionExecutingContext context,
            CancellationToken cancellationToken)
        {
            int[] fotografiaIds = ObtenerFotografiaIds(context);
            if (fotografiaIds.Length != 1)
                return null;

            DiagnosticoIAConfiguracion? configuracion =
                await diagnosticoDb.Configuraciones
                    .AsNoTracking()
                    .OrderBy(item => item.DiagnosticoIAConfiguracionId)
                    .FirstOrDefaultAsync(cancellationToken);

            bool ilimitadas =
                configuracion?.RevisionesIlimitadas ?? false;

            if (ilimitadas)
                return null;

            int maximo = Math.Clamp(
                configuracion?.MaximoRevisionesGemini ?? 2,
                1,
                20);

            Dictionary<int, int> completadas =
                await ObtenerReevaluacionesCompletadasAsync(
                    fotografiaIds,
                    cancellationToken);

            int utilizadas = completadas.GetValueOrDefault(fotografiaIds[0]);
            if (utilizadas < maximo)
                return null;

            return new BadRequestObjectResult(new
            {
                success = false,
                message =
                    $"Esta fotografía ya alcanzó el máximo de {maximo} reevaluaciones adicionales de Gemini. El análisis inicial no cuenta dentro de este límite.",
                data = new
                {
                    fotografiaId = fotografiaIds[0],
                    revisionesCompletadas = utilizadas,
                    maximoRevisiones = maximo,
                    revisionesIlimitadas = false,
                    revisionesRestantes = 0
                }
            });
        }

        private async Task<IActionResult?> ValidarDescarteTecnicoAsync(
            ActionExecutingContext context,
            int inspeccionId,
            CancellationToken cancellationToken)
        {
            int? usuarioId = ObtenerUsuarioId(context.HttpContext.User);
            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(inspeccionId, cancellationToken);

            if (!usuarioId.HasValue ||
                registro == null ||
                registro.UsuarioSolicitanteId != usuarioId.Value)
            {
                return new ObjectResult(new
                {
                    success = false,
                    message =
                        "Solo el técnico que creó la inspección puede descartar una evidencia."
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

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
                "PENDIENTE_DECISION_TECNICO",
                "DEVUELTA_AL_TECNICO"
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

            bool requiereEtapaFinalizada = modo == "aprobador";

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
                    registro.CerradaDefinitiva;

                if (requiereEtapaFinalizada &&
                    (!registro.EtapaTecnicaFinalizada || cierreDefinitivo))
                {
                    continue;
                }

                atributos["EtapaTecnicaFinalizada"] =
                    registro.EtapaTecnicaFinalizada;
                atributos["FechaFinEtapaTecnicaUtc"] =
                    registro.FechaFinEtapaTecnicaUtc;
                atributos["CerradaDefinitiva"] = cierreDefinitivo;
                atributos["FechaCierreDefinitivoUtc"] =
                    registro.FechaCierreDefinitivoUtc;

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

            InspeccionFitosanitariaAsignacionRegistro asignacion =
                await asignaciones.ObtenerAsync(id, cancellationToken);

            bool puedeGestionarOriginal = ObtenerBooleano(
                detalle,
                "PuedeGestionarSolicitud");

            int? usuarioId = ObtenerUsuarioId(context.HttpContext.User);

            detalle["NombreInspeccion"] = registro.NombreInspeccion;
            detalle["EtapaTecnicaFinalizada"] =
                registro.EtapaTecnicaFinalizada;
            detalle["FechaFinEtapaTecnicaUtc"] =
                registro.FechaFinEtapaTecnicaUtc;
            detalle["UsuarioFinEtapaTecnicaId"] =
                registro.UsuarioFinEtapaTecnicaId;
            detalle["CerradaDefinitiva"] = registro.CerradaDefinitiva;
            detalle["FechaCierreDefinitivoUtc"] =
                registro.FechaCierreDefinitivoUtc;
            detalle["UsuarioCierreDefinitivoId"] =
                registro.UsuarioCierreDefinitivoId;
            detalle["EsSoloLectura"] =
                registro.CerradaDefinitiva;
            detalle["UsuarioAnalizadorAsignadoId"] =
                asignacion.UsuarioAnalizadorId;
            detalle["UsuarioAprobadorAsignadoId"] =
                asignacion.UsuarioAprobadorId;
            detalle["VersionAsignacion"] =
                asignacion.VersionConcurrencia;

            await EnriquecerLimiteReevaluacionesAsync(
                detalle,
                cancellationToken);

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

            if (registro.CerradaDefinitiva)
            {
                bool puedePublicarCopia = await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAlbum,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

                detalle["PuedeGestionarSolicitud"] = false;
                detalle["PuedeCerrarInspeccion"] = false;
                detalle["PuedeAnalizar"] = false;
                detalle["PuedeAprobar"] = false;
                detalle["PuedePublicarAlbum"] = puedePublicarCopia;
                detalle["MotivoNoPuedeCerrar"] =
                    "La inspección fue cerrada definitivamente. El expediente es de solo lectura, pero las fotografías autorizadas todavía pueden copiarse al Álbum Botánico.";
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

                /*
                 * Basta con que exista una fotografía enviada para habilitar al
                 * analizador. La fotografía individual y su estado siguen siendo
                 * la autoridad final para permitir la selección.
                 */
                detalle["PuedeAnalizar"] =
                    puedeAnalizar &&
                    estadoTecnico.TotalEnviadasRevision > 0 &&
                    asignacion.UsuarioAprobadorId != usuarioId &&
                    (!asignacion.UsuarioAnalizadorId.HasValue ||
                     asignacion.UsuarioAnalizadorId == usuarioId);
                detalle["PuedeAprobar"] =
                    puedeAprobar &&
                    registro.EtapaTecnicaFinalizada &&
                    asignacion.UsuarioAnalizadorId != usuarioId &&
                    (!asignacion.UsuarioAprobadorId.HasValue ||
                     asignacion.UsuarioAprobadorId == usuarioId);
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

        private async Task EnriquecerLimiteReevaluacionesAsync(
            Dictionary<string, object?> detalle,
            CancellationToken cancellationToken)
        {
            if (!TryObtenerValor(detalle, "Fotografias", out object? valor) ||
                valor is not IEnumerable enumerable ||
                valor is string)
            {
                return;
            }

            List<object> fotografias = enumerable.Cast<object>().ToList();
            int[] ids = fotografias
                .Select(item =>
                {
                    Dictionary<string, object?> atributos = Atributos(item);
                    return TryObtenerEntero(
                        atributos,
                        "FotografiaId",
                        out int fotografiaId)
                            ? fotografiaId
                            : 0;
                })
                .Where(item => item > 0)
                .Distinct()
                .ToArray();

            if (ids.Length == 0)
                return;

            DiagnosticoIAConfiguracion? configuracion =
                await diagnosticoDb.Configuraciones
                    .AsNoTracking()
                    .OrderBy(item => item.DiagnosticoIAConfiguracionId)
                    .FirstOrDefaultAsync(cancellationToken);

            int maximo = Math.Clamp(
                configuracion?.MaximoRevisionesGemini ?? 2,
                1,
                20);
            bool ilimitadas =
                configuracion?.RevisionesIlimitadas ?? false;

            Dictionary<int, int> completadas =
                await ObtenerReevaluacionesCompletadasAsync(
                    ids,
                    cancellationToken);

            var enriquecidas = new List<Dictionary<string, object?>>();

            foreach (object fotografia in fotografias)
            {
                Dictionary<string, object?> atributos = Atributos(fotografia);
                if (!TryObtenerEntero(
                        atributos,
                        "FotografiaId",
                        out int fotografiaId))
                {
                    enriquecidas.Add(atributos);
                    continue;
                }

                int utilizadas = completadas.GetValueOrDefault(fotografiaId);
                int restantes = ilimitadas
                    ? int.MaxValue
                    : Math.Max(0, maximo - utilizadas);

                atributos["RevisionesIACompletadas"] = utilizadas;
                atributos["MaximoRevisionesIA"] = maximo;
                atributos["RevisionesIAIlimitadas"] = ilimitadas;
                atributos["RevisionesIARestantes"] = restantes;
                atributos["PuedeSolicitarRevisionIA"] =
                    ilimitadas || utilizadas < maximo;

                enriquecidas.Add(atributos);
            }

            detalle["Fotografias"] = enriquecidas;
        }

        private async Task<Dictionary<int, int>>
            ObtenerReevaluacionesCompletadasAsync(
                IReadOnlyCollection<int> fotografiaIds,
                CancellationToken cancellationToken)
        {
            int[] ids = fotografiaIds
                .Where(item => item > 0)
                .Distinct()
                .ToArray();

            var resultado = ids.ToDictionary(item => item, _ => 0);
            if (ids.Length == 0)
                return resultado;

            DbConnection conexion = diagnosticoDb.Database.GetDbConnection();
            bool cerrarConexion = conexion.State != ConnectionState.Open;

            if (cerrarConexion)
                await conexion.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand comando = conexion.CreateCommand();
                string parametros = string.Join(
                    ", ",
                    ids.Select((_, indice) => $"@foto{indice}"));

                comando.CommandText = $"""
SELECT
    DiagnosticoIAImagenId,
    COUNT(1) AS Total
FROM dbo.diagnosticoIAImagenRevisionIA
WHERE DiagnosticoIAImagenId IN ({parametros})
  AND TipoRevision = N'REVISION_SOLICITADA'
  AND Estado = N'COMPLETADA'
GROUP BY DiagnosticoIAImagenId;
""";

                for (int indice = 0; indice < ids.Length; indice++)
                {
                    DbParameter parametro = comando.CreateParameter();
                    parametro.ParameterName = $"@foto{indice}";
                    parametro.Value = ids[indice];
                    parametro.DbType = DbType.Int32;
                    comando.Parameters.Add(parametro);
                }

                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    int fotografiaId = reader.GetInt32(0);
                    int total = reader.GetInt32(1);
                    resultado[fotografiaId] = total;
                }
            }
            finally
            {
                if (cerrarConexion)
                    await conexion.CloseAsync();
            }

            return resultado;
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
            CoincideRutaBase(ruta, RutaBase) ||
            CoincideRutaBase(ruta, RutaClasificacionIA) ||
            CoincideRutaBase(ruta, RutaPublicacionAlbum) ||
            EsRutaAlbumVinculadaInspeccion(ruta);

        /// <summary>
        /// El Álbum Botánico administrativo utiliza las rutas /inicio,
        /// /galeria-paginada, /subcategorias y /registros. Esas operaciones no
        /// pertenecen al flujo de una inspección y no deben inicializar ni
        /// validar las tablas fitosanitarias.
        ///
        /// Solo se controla la rama /diagnosticos, utilizada para consultar o
        /// resolver la clasificación jerárquica de fotografías que sí forman
        /// parte de una inspección.
        /// </summary>
        private static bool EsRutaAlbumVinculadaInspeccion(string ruta) =>
            CoincideRutaBase(
                ruta,
                RutaAlbumJerarquia + "/diagnosticos");

        /// <summary>
        /// Exige una frontera real después del prefijo. Así la ruta
        /// /api/inspecciones-fitosanitarias-flujo no se confunde con la ruta
        /// base /api/inspecciones-fitosanitarias.
        /// </summary>
        private static bool CoincideRutaBase(string ruta, string rutaBase)
        {
            string normalizada = ruta.TrimEnd('/');

            return string.Equals(
                       normalizada,
                       rutaBase,
                       StringComparison.OrdinalIgnoreCase) ||
                   normalizada.StartsWith(
                       rutaBase + "/",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool EsRutaPublicacionAlbum(string ruta) =>
            ruta.TrimEnd('/').EndsWith(
                "/publicar-album",
                StringComparison.OrdinalIgnoreCase) ||
            ruta.Contains(
                "/api/publicaciones-album-fitosanitarias/",
                StringComparison.OrdinalIgnoreCase);

        private static bool EsRutaSolicitudRevisionIA(string ruta) =>
            ruta.TrimEnd('/').EndsWith(
                "/solicitar-revision-ia",
                StringComparison.OrdinalIgnoreCase);

        private static bool EsOperacionTecnico(string ruta)
        {
            string normalizada = ruta.TrimEnd('/').ToLowerInvariant();

            if (normalizada.EndsWith("/cerrar-definitivo") ||
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
            string[] nombres = ["id", "diagnosticoId", "inspeccionId"];

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
