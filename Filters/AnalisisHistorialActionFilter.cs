using CONATRADEC_API.DTOs;
using CONATRADEC_API.Reportes;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CONATRADEC_API.Filters;

/// <summary>
/// Aplica control de concurrencia y captura de versiones sin duplicar la
/// lógica del GuardarTodoController.
/// </summary>
public sealed class AnalisisHistorialActionFilter : IAsyncActionFilter
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    private readonly AnalisisReporteHistoricoService historial;
    private readonly AnalisisEdicionLockService editLock;
    private readonly AnalisisEdicionDatabaseLockService databaseLock;
    private readonly ILogger<AnalisisHistorialActionFilter> logger;

    public AnalisisHistorialActionFilter(
        AnalisisReporteHistoricoService historial,
        AnalisisEdicionLockService editLock,
        AnalisisEdicionDatabaseLockService databaseLock,
        ILogger<AnalisisHistorialActionFilter> logger)
    {
        this.historial = historial;
        this.editLock = editLock;
        this.databaseLock = databaseLock;
        this.logger = logger;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        HttpRequest request = context.HttpContext.Request;
        string path = (request.Path.Value ?? string.Empty)
            .TrimEnd('/');

        if (request.Method == HttpMethods.Get &&
            path.StartsWith(
                "/api/reportes/analisis/",
                StringComparison.OrdinalIgnoreCase))
        {
            await ProcesarLecturaReporteProtegidaAsync(
                context,
                next,
                path);
            return;
        }

        if (request.Method == HttpMethods.Get &&
            path.Contains(
                "/api/guardar-todo/listardetalle/",
                StringComparison.OrdinalIgnoreCase))
        {
            await ProcesarDetalleAsync(context, next);
            return;
        }

        if (request.Method == HttpMethods.Get &&
            path.StartsWith(
                "/api/auditoria-analisis/",
                StringComparison.OrdinalIgnoreCase) &&
            ObtenerUltimoEntero(path) > 0)
        {
            await ProcesarAuditoriaDetalleAsync(context, next);
            return;
        }

        if (request.Method == HttpMethods.Get &&
            (path.Equals(
                 "/api/analisis-listado/paginado",
                 StringComparison.OrdinalIgnoreCase) ||
             path.Contains(
                 "/api/guardar-todo/listar-usuario",
                 StringComparison.OrdinalIgnoreCase) ||
             path.Contains(
                 "/api/guardar-todo/listar usuario",
                 StringComparison.OrdinalIgnoreCase) ||
             path.Equals(
                 "/api/auditoria-analisis",
                 StringComparison.OrdinalIgnoreCase)))
        {
            await ProcesarListadoAsync(context, next);
            return;
        }

        if (request.Method == HttpMethods.Post &&
            path.Equals(
                "/api/analisis-offline/sincronizar",
                StringComparison.OrdinalIgnoreCase))
        {
            await ProcesarSincronizacionOfflineAsync(context, next);
            return;
        }

        if (request.Method == HttpMethods.Post &&
            path.Equals(
                "/api/guardar-todo",
                StringComparison.OrdinalIgnoreCase))
        {
            await ProcesarCreacionAsync(context, next);
            return;
        }

        if (request.Method == HttpMethods.Put &&
            path.Contains(
                "/api/guardar-todo/editar/",
                StringComparison.OrdinalIgnoreCase))
        {
            await ProcesarEdicionAsync(context, next);
            return;
        }

        if (request.Method == HttpMethods.Delete &&
            path.StartsWith(
                "/api/guardar-todo/",
                StringComparison.OrdinalIgnoreCase))
        {
            await ProcesarEliminacionAsync(context, next);
            return;
        }

        await next();
    }

    private async Task ProcesarLecturaReporteProtegidaAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next,
        string path)
    {
        int id = ObtenerPrimerEnteroDespuesDePrefijo(
            path,
            "/api/reportes/analisis/");

        if (id <= 0)
        {
            await next();
            return;
        }

        await using IAsyncDisposable localReleaser =
            await editLock.AdquirirAsync(
                id,
                context.HttpContext.RequestAborted);

        IAsyncDisposable databaseReleaser;

        try
        {
            databaseReleaser = await databaseLock.AdquirirAsync(
                id,
                context.HttpContext.RequestAborted);
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(
                ex,
                "El reporte del cálculo {CalculoId} esperó una edición que continúa en proceso.",
                id);

            context.Result = new ConflictObjectResult(new
            {
                success = false,
                code = "ANALYSIS_EDIT_IN_PROGRESS",
                message =
                    "El análisis está siendo actualizado. " +
                    "Espere unos segundos antes de abrir o descargar el reporte."
            });
            return;
        }

        await using (databaseReleaser)
        {
            await next();
        }
    }

    private async Task ProcesarDetalleAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        ActionExecutedContext executed = await next();

        if (!EsExitoso(executed))
            return;

        int id = ObtenerEnteroRuta(context, "id");
        if (id <= 0)
            id = ObtenerUltimoEntero(context.HttpContext.Request.Path.Value);

        if (id <= 0)
            return;

        AnalisisControlHistorialDto? control =
            await historial.ObtenerControlAsync(
                id,
                context.HttpContext.RequestAborted);

        if (control == null)
            return;

        AgregarEncabezadosControl(
            context.HttpContext.Response,
            control);

        JsonNode? root = ObtenerJsonResultado(executed.Result);

        if (root is JsonObject objeto &&
            objeto["data"] is JsonObject data)
        {
            data["controlHistorial"] =
                JsonSerializer.SerializeToNode(
                    new
                    {
                        analisisSueloCalculoId =
                            control.AnalisisSueloCalculoId,
                        versionRegistro = control.VersionRegistro,
                        fechaCreacionClienteUtc =
                            control.FechaCreacionClienteUtc,
                        fechaUltimaModificacionUtc =
                            control.FechaUltimaModificacionUtc,
                        origenRegistro = control.OrigenRegistro,
                        etag = control.ETag
                    },
                    JsonOptions);

            executed.Result = CrearResultadoJson(
                executed.Result,
                objeto);
        }
    }

    private async Task ProcesarAuditoriaDetalleAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        ActionExecutedContext executed = await next();

        if (!EsExitoso(executed))
            return;

        JsonNode? root = ObtenerJsonResultado(executed.Result);

        if (root is not JsonObject objeto ||
            objeto["data"] is not JsonObject data ||
            data["resumen"] is not JsonObject resumen)
        {
            return;
        }

        int calculoId =
            LeerEnteroJson(resumen["analisisSueloCalculoId"]);

        if (calculoId <= 0)
            return;

        AnalisisReporte? reporte =
            await historial.ObtenerReporteSinCapturarAsync(
                calculoId,
                cancellationToken:
                    context.HttpContext.RequestAborted);

        AnalisisControlHistorialDto? control =
            await historial.ObtenerControlAsync(
                calculoId,
                context.HttpContext.RequestAborted);

        List<AnalisisVersionHistorialDto> versiones =
            await historial.ListarVersionesAsync(
                calculoId,
                context.HttpContext.RequestAborted);

        if (reporte != null &&
            resumen["terreno"] is JsonObject terreno)
        {
            terreno["propietario"] = reporte.Cliente;
            terreno["descripcionHistorica"] = reporte.Terreno;
        }

        if (control != null)
        {
            resumen["controlHistorial"] =
                JsonSerializer.SerializeToNode(
                    new
                    {
                        control.VersionRegistro,
                        control.FechaCreacionClienteUtc,
                        control.FechaUltimaModificacionUtc,
                        control.OrigenRegistro,
                        control.ETag
                    },
                    JsonOptions);

            AgregarEncabezadosControl(
                context.HttpContext.Response,
                control);
        }

        data["versionesHistorial"] =
            JsonSerializer.SerializeToNode(
                versiones,
                JsonOptions);

        executed.Result = CrearResultadoJson(
            executed.Result,
            objeto);
    }

    private async Task ProcesarListadoAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        ActionExecutedContext executed = await next();

        if (!EsExitoso(executed))
            return;

        JsonNode? root = ObtenerJsonResultado(executed.Result);
        if (root == null)
            return;

        await historial.EnriquecerListadoAsync(
            root,
            context.HttpContext.RequestAborted);

        executed.Result = CrearResultadoJson(
            executed.Result,
            root);
    }

    private async Task ProcesarSincronizacionOfflineAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        AnalisisOfflineSincronizarDto? envelope =
            context.ActionArguments.Values
                .OfType<AnalisisOfflineSincronizarDto>()
                .FirstOrDefault();

        if (envelope == null || envelope.solicitud == null)
        {
            await next();
            return;
        }

        if (await historial.OperacionOfflineCompletadaAsync(
                envelope.operacionLocalId,
                context.HttpContext.RequestAborted))
        {
            await next();
            return;
        }

        bool esEdicion = string.Equals(
            envelope.tipoOperacion,
            "EDITAR",
            StringComparison.OrdinalIgnoreCase);

        if (!esEdicion)
        {
            ActionExecutedContext executed = await next();

            if (!EsExitoso(executed))
                return;

            int? calculoId =
                ObtenerCalculoIdResultado(executed.Result);

            if (!calculoId.HasValue)
            {
                calculoId =
                    await historial.BuscarCalculoPorIdentificadorAsync(
                        envelope.solicitud
                            .datosAnalisis
                            .identificadorAnalisisSuelo,
                        CancellationToken.None);
            }

            if (!calculoId.HasValue)
                return;

            try
            {
                DateTime? fechaCreacion =
                    envelope.solicitud.fechaCreacionClienteUtc ??
                    NormalizarUtc(envelope.fechaCalculoLocalUtc);

                await ProtegerCreacionConfirmadaAsync(
                    calculoId.Value,
                    envelope.solicitud,
                    "CREAR_OFFLINE",
                    "OFFLINE",
                    fechaCreacion,
                    context.HttpContext);
            }
            catch (Exception ex)
            {
                try
                {
                    await historial.InicializarMetadatosCreacionAsync(
                        calculoId.Value,
                        envelope.solicitud.fechaCreacionClienteUtc ??
                            NormalizarUtc(envelope.fechaCalculoLocalUtc),
                        "OFFLINE",
                        CancellationToken.None);
                }
                catch
                {
                    // El proceso de recuperación volverá a intentarlo.
                }

                RegistrarAdvertenciaHistorial(
                    context.HttpContext.Response,
                    ex,
                    "sincronizar creación offline");
            }

            return;
        }

        int id = envelope.analisisSueloCalculoId ?? 0;

        if (id <= 0)
        {
            await next();
            return;
        }

        await using IAsyncDisposable localReleaser =
            await editLock.AdquirirAsync(
                id,
                context.HttpContext.RequestAborted);

        IAsyncDisposable databaseReleaser;

        try
        {
            databaseReleaser = await databaseLock.AdquirirAsync(
                id,
                context.HttpContext.RequestAborted);
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(
                ex,
                "La sincronización offline del cálculo {CalculoId} encontró otra edición en proceso.",
                id);

            context.Result = new ConflictObjectResult(new
            {
                success = false,
                code = "ANALYSIS_EDIT_IN_PROGRESS",
                message =
                    "Otro proceso está actualizando este análisis. " +
                    "La operación offline quedó pendiente para revisión."
            });
            return;
        }

        await using (databaseReleaser)
        {
            /*
             * La reserva idempotente pudo completarse mientras se esperaba el
             * bloqueo del análisis. En ese caso el controlador debe devolver
             * su respuesta anterior sin incrementar otra versión.
             */
            if (await historial.OperacionOfflineCompletadaAsync(
                    envelope.operacionLocalId,
                    context.HttpContext.RequestAborted))
            {
                await next();
                return;
            }

            AnalisisControlHistorialDto? control =
                await historial.ObtenerControlAsync(
                    id,
                    context.HttpContext.RequestAborted);

            if (control == null)
            {
                context.Result = new NotFoundObjectResult(new
                {
                    success = false,
                    message =
                        "No se encontró el análisis que se debe sincronizar."
                });
                return;
            }

            int? versionEsperada =
                envelope.solicitud.versionRegistro;

            if (!versionEsperada.HasValue || versionEsperada.Value <= 0)
            {
                versionEsperada =
                    AnalisisReporteHistoricoService
                        .ObtenerVersionDesdeETag(
                            envelope.solicitud.etagBase,
                            id);
            }

            if (!versionEsperada.HasValue)
            {
                context.Result = new ObjectResult(new
                {
                    success = false,
                    code = "ANALYSIS_VERSION_REQUIRED",
                    message =
                        "La edición offline no contiene la versión original del análisis. " +
                        "Debe abrirse nuevamente en línea antes de sincronizarse.",
                    versionActual = control.VersionRegistro,
                    etagActual = control.ETag
                })
                {
                    StatusCode =
                        StatusCodes.Status428PreconditionRequired
                };
                return;
            }

            if (versionEsperada.Value != control.VersionRegistro)
            {
                context.Result = new ConflictObjectResult(new
                {
                    success = false,
                    code = "ANALYSIS_OFFLINE_CONFLICT",
                    message =
                        "El análisis cambió en el servidor después de la edición offline. " +
                        "La operación no fue aplicada para evitar sobrescribir información más reciente.",
                    versionEnviada = versionEsperada.Value,
                    versionActual = control.VersionRegistro,
                    etagActual = control.ETag,
                    fechaUltimaModificacionUtc =
                        control.FechaUltimaModificacionUtc
                });
                return;
            }

            try
            {
                await historial.CapturarSiFaltaAsync(
                    id,
                    control.VersionRegistro,
                    "ANTES_EDITAR_OFFLINE",
                    ObtenerUsuarioId(context.HttpContext),
                    control.OrigenRegistro,
                    control.FechaCreacionClienteUtc,
                    solicitud: null,
                    context.HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "No fue posible proteger la versión previa del cálculo offline {CalculoId}.",
                    id);

                context.Result = new ObjectResult(new
                {
                    success = false,
                    code = "ANALYSIS_HISTORY_UNAVAILABLE",
                    message =
                        "La sincronización fue cancelada porque no fue posible proteger la versión actual."
                })
                {
                    StatusCode =
                        StatusCodes.Status503ServiceUnavailable
                };
                return;
            }

            ActionExecutedContext executed = await next();

            if (!EsExitoso(executed))
                return;

            try
            {
                int nuevaVersion =
                    await historial.IncrementarVersionAsync(
                        id,
                        "OFFLINE",
                        CancellationToken.None);

                await EjecutarConReintentoAsync(() =>
                    historial.CapturarSiFaltaAsync(
                        id,
                        nuevaVersion,
                        "EDITAR_OFFLINE",
                        ObtenerUsuarioId(context.HttpContext),
                        "OFFLINE",
                        envelope.solicitud.fechaCreacionClienteUtc ??
                            control.FechaCreacionClienteUtc,
                        envelope.solicitud,
                        CancellationToken.None));

                AnalisisControlHistorialDto? actualizado =
                    await historial.ObtenerControlAsync(
                        id,
                        CancellationToken.None);

                if (actualizado != null)
                {
                    AgregarEncabezadosControl(
                        context.HttpContext.Response,
                        actualizado);
                }
            }
            catch (Exception ex)
            {
                RegistrarAdvertenciaHistorial(
                    context.HttpContext.Response,
                    ex,
                    "sincronizar edición offline");
            }
        }
    }

    private async Task ProcesarCreacionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        GuardarTodoDto? dto = ObtenerDto(context);
        ActionExecutedContext executed = await next();

        if (!EsExitoso(executed) || dto == null)
            return;

        int? calculoId =
            ObtenerCalculoIdResultado(executed.Result);

        if (!calculoId.HasValue)
        {
            calculoId =
                await historial.BuscarCalculoPorIdentificadorAsync(
                    dto.datosAnalisis.identificadorAnalisisSuelo,
                    CancellationToken.None);
        }

        if (!calculoId.HasValue)
            return;

        try
        {
            string origen = ObtenerOrigen(dto, context.HttpContext.Request);

            await ProtegerCreacionConfirmadaAsync(
                calculoId.Value,
                dto,
                "CREAR",
                origen,
                dto.fechaCreacionClienteUtc,
                context.HttpContext);
        }
        catch (Exception ex)
        {
            try
            {
                await historial.InicializarMetadatosCreacionAsync(
                    calculoId.Value,
                    dto.fechaCreacionClienteUtc,
                    ObtenerOrigen(dto, context.HttpContext.Request),
                    CancellationToken.None);
            }
            catch
            {
                // El proceso de recuperación volverá a intentarlo.
            }

            RegistrarAdvertenciaHistorial(
                context.HttpContext.Response,
                ex,
                "crear");
        }
    }

    private async Task ProtegerCreacionConfirmadaAsync(
        int analisisSueloCalculoId,
        GuardarTodoDto dto,
        string tipoEvento,
        string origen,
        DateTime? fechaCreacionClienteUtc,
        HttpContext httpContext)
    {
        /*
         * La creación ya fue confirmada por el controlador. Se utiliza un
         * token independiente de RequestAborted para que cerrar la app o perder
         * conexión no deje el análisis sin versión ni snapshot.
         */
        await using IAsyncDisposable localReleaser =
            await editLock.AdquirirAsync(
                analisisSueloCalculoId,
                CancellationToken.None);

        await using IAsyncDisposable databaseReleaser =
            await databaseLock.AdquirirAsync(
                analisisSueloCalculoId,
                CancellationToken.None);

        await historial.InicializarMetadatosCreacionAsync(
            analisisSueloCalculoId,
            fechaCreacionClienteUtc,
            origen,
            CancellationToken.None);

        AnalisisControlHistorialDto? control =
            await historial.ObtenerControlAsync(
                analisisSueloCalculoId,
                CancellationToken.None);

        if (control == null)
        {
            throw new InvalidOperationException(
                "No fue posible recuperar el control del análisis creado.");
        }

        await EjecutarConReintentoAsync(() =>
            historial.CapturarSiFaltaAsync(
                analisisSueloCalculoId,
                control.VersionRegistro,
                tipoEvento,
                ObtenerUsuarioId(httpContext),
                origen,
                fechaCreacionClienteUtc,
                dto,
                CancellationToken.None));

        AgregarEncabezadosControl(
            httpContext.Response,
            control);
    }

    private async Task ProcesarEdicionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        int id = ObtenerEnteroRuta(context, "id");
        if (id <= 0)
            id = ObtenerUltimoEntero(context.HttpContext.Request.Path.Value);

        if (id <= 0)
        {
            context.Result = new BadRequestObjectResult(new
            {
                success = false,
                message = "El identificador del cálculo no es válido."
            });
            return;
        }

        await using IAsyncDisposable releaser =
            await editLock.AdquirirAsync(
                id,
                context.HttpContext.RequestAborted);

        IAsyncDisposable databaseReleaser;

        try
        {
            databaseReleaser =
                await databaseLock.AdquirirAsync(
                    id,
                    context.HttpContext.RequestAborted);
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(
                ex,
                "El cálculo {CalculoId} ya está siendo editado.",
                id);

            context.Result = new ConflictObjectResult(new
            {
                success = false,
                code = "ANALYSIS_EDIT_IN_PROGRESS",
                message =
                    "Otro proceso está actualizando este análisis. " +
                    "Espere unos segundos, vuelva a abrirlo e intente nuevamente."
            });
            return;
        }

        await using (databaseReleaser)
        {
            await ProcesarEdicionProtegidaAsync(
                context,
                next,
                id);
        }
    }

    private async Task ProcesarEdicionProtegidaAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next,
        int id)
    {
        GuardarTodoDto? dto = ObtenerDto(context);

        AnalisisControlHistorialDto? control =
            await historial.ObtenerControlAsync(
                id,
                context.HttpContext.RequestAborted);

        if (control == null)
        {
            context.Result = new NotFoundObjectResult(new
            {
                success = false,
                message = "No se encontró el análisis que se debe editar."
            });
            return;
        }

        int? versionEsperada = dto?.versionRegistro;

        if (!versionEsperada.HasValue || versionEsperada.Value <= 0)
        {
            versionEsperada =
                AnalisisReporteHistoricoService.ObtenerVersionDesdeETag(
                    dto?.etagBase,
                    id);
        }

        if (!versionEsperada.HasValue || versionEsperada.Value <= 0)
        {
            versionEsperada =
                AnalisisReporteHistoricoService.ObtenerVersionDesdeETag(
                    context.HttpContext.Request.Headers["If-Match"].ToString(),
                    id);
        }

        if (!versionEsperada.HasValue)
        {
            context.Result = new ObjectResult(new
            {
                success = false,
                code = "ANALYSIS_VERSION_REQUIRED",
                message =
                    "El análisis debe volver a cargarse antes de editarlo. " +
                    "No se recibió la versión que fue visualizada por el usuario.",
                versionActual = control.VersionRegistro,
                etagActual = control.ETag
            })
            {
                StatusCode = StatusCodes.Status428PreconditionRequired
            };
            return;
        }

        if (versionEsperada.Value != control.VersionRegistro)
        {
            context.Result = new ConflictObjectResult(new
            {
                success = false,
                code = "ANALYSIS_EDIT_CONFLICT",
                message =
                    "Este análisis fue modificado por otra operación después de que usted lo cargó. " +
                    "Vuelva a abrirlo para revisar los cambios antes de guardar.",
                versionEnviada = versionEsperada.Value,
                versionActual = control.VersionRegistro,
                etagActual = control.ETag,
                fechaUltimaModificacionUtc =
                    control.FechaUltimaModificacionUtc
            });
            return;
        }

        try
        {
            await historial.CapturarSiFaltaAsync(
                id,
                control.VersionRegistro,
                "ANTES_EDITAR",
                ObtenerUsuarioId(context.HttpContext),
                control.OrigenRegistro,
                control.FechaCreacionClienteUtc,
                solicitud: null,
                context.HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "No fue posible asegurar la versión previa del cálculo {CalculoId}.",
                id);

            context.Result = new ObjectResult(new
            {
                success = false,
                code = "ANALYSIS_HISTORY_UNAVAILABLE",
                message =
                    "No fue posible proteger la versión actual del análisis. " +
                    "La edición fue cancelada para evitar perder el historial."
            })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            return;
        }

        ActionExecutedContext executed = await next();

        if (!EsExitoso(executed))
            return;

        try
        {
            string origen = ObtenerOrigen(dto, context.HttpContext.Request);

            int nuevaVersion =
                await historial.IncrementarVersionAsync(
                    id,
                    origen,
                    CancellationToken.None);

            await EjecutarConReintentoAsync(() =>
                historial.CapturarSiFaltaAsync(
                    id,
                    nuevaVersion,
                    "EDITAR",
                    ObtenerUsuarioId(context.HttpContext),
                    origen,
                    dto?.fechaCreacionClienteUtc ??
                        control.FechaCreacionClienteUtc,
                    dto,
                    CancellationToken.None));

            AnalisisControlHistorialDto? actualizado =
                await historial.ObtenerControlAsync(
                    id,
                    CancellationToken.None);

            if (actualizado != null)
            {
                AgregarEncabezadosControl(
                    context.HttpContext.Response,
                    actualizado);
            }
        }
        catch (Exception ex)
        {
            RegistrarAdvertenciaHistorial(
                context.HttpContext.Response,
                ex,
                "editar");
        }
    }

    private async Task ProcesarEliminacionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        int analisisSueloId =
            ObtenerEnteroRuta(context, "analisisSueloId");

        if (analisisSueloId <= 0)
        {
            analisisSueloId =
                ObtenerUltimoEntero(
                    context.HttpContext.Request.Path.Value);
        }

        List<int> calculos =
            await historial.ObtenerCalculosPorAnalisisAsync(
                analisisSueloId,
                context.HttpContext.RequestAborted);

        var releasers = new List<IAsyncDisposable>();

        try
        {
            /*
             * Esta parte ocurre antes de ejecutar DELETE. Una falla cancela
             * la operación porque todavía es posible proteger los datos.
             */
            foreach (int id in calculos.OrderBy(x => x))
            {
                releasers.Add(
                    await editLock.AdquirirAsync(
                        id,
                        context.HttpContext.RequestAborted));

                releasers.Add(
                    await databaseLock.AdquirirAsync(
                        id,
                        context.HttpContext.RequestAborted));

                AnalisisControlHistorialDto? control =
                    await historial.ObtenerControlAsync(
                        id,
                        context.HttpContext.RequestAborted);

                if (control == null)
                    continue;

                await historial.CapturarSiFaltaAsync(
                    id,
                    control.VersionRegistro,
                    "ANTES_ELIMINAR",
                    ObtenerUsuarioId(context.HttpContext),
                    control.OrigenRegistro,
                    control.FechaCreacionClienteUtc,
                    solicitud: null,
                    context.HttpContext.RequestAborted);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "No fue posible proteger el historial antes de eliminar el análisis {AnalisisId}.",
                analisisSueloId);

            context.Result = new ObjectResult(new
            {
                success = false,
                code = "ANALYSIS_HISTORY_UNAVAILABLE",
                message =
                    "La eliminación fue cancelada porque no fue posible proteger el historial del análisis."
            })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };

            await LiberarAsync(releasers);
            return;
        }

        try
        {
            ActionExecutedContext executed = await next();

            if (!EsExitoso(executed))
                return;

            /*
             * DELETE ya terminó. Si falla la marca final, la versión previa
             * continúa protegida y se registra una advertencia recuperable.
             */
            try
            {
                foreach (int id in calculos)
                {
                    AnalisisControlHistorialDto? control =
                        await historial.ObtenerControlAsync(
                            id,
                            CancellationToken.None);

                    if (control == null)
                        continue;

                    int nuevaVersion =
                        await historial.IncrementarVersionAsync(
                            id,
                            control.OrigenRegistro,
                            CancellationToken.None);

                    await EjecutarConReintentoAsync(() =>
                        historial.DuplicarUltimaVersionAsync(
                            id,
                            nuevaVersion,
                            "ELIMINAR",
                            ObtenerUsuarioId(context.HttpContext),
                            control.OrigenRegistro,
                            control.FechaCreacionClienteUtc,
                            CancellationToken.None));
                }
            }
            catch (Exception ex)
            {
                RegistrarAdvertenciaHistorial(
                    context.HttpContext.Response,
                    ex,
                    "eliminar");
            }
        }
        finally
        {
            await LiberarAsync(releasers);
        }
    }

    private static async Task EjecutarConReintentoAsync(
        Func<Task> action)
    {
        Exception? ultimaExcepcion = null;

        for (int intento = 1; intento <= 3; intento++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex)
            {
                ultimaExcepcion = ex;

                if (intento < 3)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(150 * intento));
                }
            }
        }

        throw ultimaExcepcion ??
            new InvalidOperationException(
                "No fue posible completar la operación histórica.");
    }

    private static async Task LiberarAsync(
        List<IAsyncDisposable> releasers)
    {
        for (int index = releasers.Count - 1; index >= 0; index--)
            await releasers[index].DisposeAsync();
    }

    private void RegistrarAdvertenciaHistorial(
        HttpResponse response,
        Exception ex,
        string operacion)
    {
        logger.LogError(
            ex,
            "La operación de {Operacion} terminó, pero no fue posible completar el snapshot histórico.",
            operacion);

        response.Headers["X-CONATRADEC-Historial-Advertencia"] =
            "SNAPSHOT_PENDING";
    }

    private static GuardarTodoDto? ObtenerDto(
        ActionExecutingContext context) =>
        context.ActionArguments.Values
            .OfType<GuardarTodoDto>()
            .FirstOrDefault();

    private static string ObtenerOrigen(
        GuardarTodoDto? dto,
        HttpRequest request)
    {
        string origen = dto?.origenRegistro ?? string.Empty;

        if (string.IsNullOrWhiteSpace(origen))
        {
            origen = request.Headers["X-Modo-Sesion"].ToString();

            try
            {
                origen = Uri.UnescapeDataString(origen);
            }
            catch
            {
                // Conserva el texto recibido.
            }
        }

        return origen.Contains(
                "offline",
                StringComparison.OrdinalIgnoreCase) ||
            origen.Contains(
                "sin conexión",
                StringComparison.OrdinalIgnoreCase)
            ? "OFFLINE"
            : "ONLINE";
    }

    private static DateTime? NormalizarUtc(DateTime fecha)
    {
        if (fecha == default)
            return null;

        return fecha.Kind switch
        {
            DateTimeKind.Utc => fecha,
            DateTimeKind.Local => fecha.ToUniversalTime(),
            _ => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
        };
    }

    private static int? ObtenerUsuarioId(HttpContext context)
    {
        string? valor =
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.User.FindFirstValue("usuarioId") ??
            context.User.FindFirstValue("sub") ??
            context.Request.Headers["X-Usuario-Id"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(valor))
            return null;

        try
        {
            valor = Uri.UnescapeDataString(valor);
        }
        catch
        {
            // Conserva el valor recibido.
        }

        return int.TryParse(valor, out int id) && id > 0
            ? id
            : null;
    }

    private static int ObtenerEnteroRuta(
        ActionExecutingContext context,
        string nombre)
    {
        if (context.RouteData.Values.TryGetValue(nombre, out object? valor) &&
            int.TryParse(valor?.ToString(), out int id))
        {
            return id;
        }

        return 0;
    }

    private static int ObtenerPrimerEnteroDespuesDePrefijo(
        string? path,
        string prefijo)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        string resto = path[prefijo.Length..].Trim('/');
        string primerSegmento = resto
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        return int.TryParse(primerSegmento, out int id) ? id : 0;
    }

    private static int ObtenerUltimoEntero(string? path)
    {
        string ultimo = (path ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? string.Empty;

        return int.TryParse(ultimo, out int id) ? id : 0;
    }

    private static bool EsExitoso(ActionExecutedContext context)
    {
        if (context.Exception != null && !context.ExceptionHandled)
            return false;

        int statusCode = context.Result switch
        {
            ObjectResult objectResult =>
                objectResult.StatusCode ?? StatusCodes.Status200OK,
            StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
            _ => StatusCodes.Status200OK
        };

        return statusCode is >= 200 and < 400;
    }

    private static void AgregarEncabezadosControl(
        HttpResponse response,
        AnalisisControlHistorialDto control)
    {
        response.Headers["ETag"] = control.ETag;
        response.Headers["X-Version-Registro"] =
            control.VersionRegistro.ToString();

        if (control.FechaUltimaModificacionUtc.HasValue)
        {
            response.Headers["Last-Modified"] =
                control.FechaUltimaModificacionUtc.Value.ToString("R");
        }
    }

    private static int LeerEnteroJson(JsonNode? node)
    {
        if (node == null)
            return 0;

        try
        {
            return node.GetValue<int>();
        }
        catch
        {
            return int.TryParse(node.ToString(), out int value)
                ? value
                : 0;
        }
    }


    private static int? ObtenerCalculoIdResultado(IActionResult? result)
    {
        JsonNode? root = ObtenerJsonResultado(result);

        if (root is not JsonObject objeto)
            return null;

        JsonObject? data = objeto["data"] as JsonObject;
        int id = LeerEnteroJson(
            data?["analisisSueloCalculoId"] ??
            objeto["analisisSueloCalculoId"]);

        return id > 0 ? id : null;
    }

    private static JsonNode? ObtenerJsonResultado(IActionResult? result)
    {
        object? value = result switch
        {
            ObjectResult objectResult => objectResult.Value,
            JsonResult jsonResult => jsonResult.Value,
            _ => null
        };

        return value == null
            ? null
            : JsonSerializer.SerializeToNode(value, JsonOptions);
    }

    private static IActionResult CrearResultadoJson(
        IActionResult? original,
        JsonNode value)
    {
        int? statusCode = original switch
        {
            ObjectResult objectResult => objectResult.StatusCode,
            JsonResult jsonResult => jsonResult.StatusCode,
            _ => null
        };

        return new JsonResult(value)
        {
            StatusCode = statusCode,
            SerializerSettings = JsonOptions
        };
    }
}
