using CONATRADEC_API.Auditing;
using CONATRADEC_API.Endpoints;
using CONATRADEC_API.Filters;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Middleware;
using CONATRADEC_API.Models;
using CONATRADEC_API.Security;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpSize = SixLabors.ImageSharp.Size;

var builder = WebApplication.CreateBuilder(args);

const long tamanoMaximoActualizacion = 1024L * 1024L * 1024L;
const string politicaCorsPropietarios = "ConatradecPropietarios";

string[] origenesCorsConfigurados =
    builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? [];

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = tamanoMaximoActualizacion;
});

builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = tamanoMaximoActualizacion;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = tamanoMaximoActualizacion;
});

/*
 * Permite que el portal de propietarios ejecutado localmente desde Expo Web
 * consuma la API durante las pruebas. En producción se pueden agregar
 * orígenes explícitos mediante la sección Cors:AllowedOrigins.
 *
 * No se habilita AllowAnyOrigin ni AllowCredentials.
 */
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        politicaCorsPropietarios,
        policy =>
        {
            policy
                .SetIsOriginAllowed(origin =>
                    EsOrigenCorsPermitido(
                        origin,
                        origenesCorsConfigurados))
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});

builder.Services.AddScoped<
    AnalisisEdicionPropietarioActionFilter>();

builder.Services.AddScoped<AnalisisHistorialActionFilter>();
builder.Services.AddScoped<InspeccionFitosanitariaControlActionFilter>();
builder.Services.AddScoped<InspeccionFitosanitariaControlDatabaseInitializer>();
builder.Services.AddScoped<InspeccionFitosanitariaAdministracionDatabaseInitializer>();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiErrorResponseFilter>();

    /*
     * Primero se valida que la edición pertenezca al usuario propietario.
     * Después se aplican concurrencia, versiones e historial.
     */
    options.Filters.AddService<
        AnalisisEdicionPropietarioActionFilter>();

    options.Filters.AddService<AnalisisHistorialActionFilter>();
    options.Filters.AddService<InspeccionFitosanitariaControlActionFilter>();
});

builder.Services.AddConatradecJwt(
    builder.Configuration);

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        IDictionary<string, string[]> errors =
            ApiErrorResponseFactory.FromModelState(context.ModelState);

        var response = ApiErrorResponseFactory.Create(
            context.HttpContext,
            StatusCodes.Status400BadRequest,
            message:
                "Revise los campos indicados e intente nuevamente.",
            errors: errors,
            code: "VALIDATION_ERROR");

        return new BadRequestObjectResult(response);
    };
});

builder.Services.AddScoped<AnalisisSueloCalculoService>();
builder.Services.AddScoped<AnalisisReporteDatosService>();
builder.Services.AddScoped<AnalisisReporteHistoricoService>();
builder.Services.AddSingleton<AnalisisEdicionLockService>();
builder.Services.AddScoped<AnalisisEdicionDatabaseLockService>();
builder.Services.AddHostedService<AnalisisHistorialBackfillHostedService>();

builder.Services.AddConatradecImageStorage(
    builder.Configuration);

builder.Services.AddScoped<ImageService>();
builder.Services.AddScoped<GeminiDiagnosticoService>();
builder.Services.AddScoped<DiagnosticoIADatabaseInitializer>();

// Procesamiento prolongado de Diagnóstico IA fuera de la solicitud HTTP.
builder.Services.AddSingleton<DiagnosticoIAProcesamientoQueue>();
builder.Services.AddSingleton<DiagnosticoIAProcesamientoEstadoStore>();
builder.Services.AddScoped<DiagnosticoIAProcesamientoService>();
builder.Services.AddHostedService<DiagnosticoIAProcesamientoHostedService>();

builder.Services.AddScoped<AnalisisSueloDatabaseInitializer>();
builder.Services.AddScoped<AnalisisHistorialDatabaseInitializer>();
builder.Services.AddScoped<PortalWebDatabaseInitializer>();
builder.Services.AddScoped<ParametrizacionAccesoDatabaseInitializer>();
builder.Services.AddScoped<ControlAnalisisDatabaseInitializer>();
builder.Services.AddScoped<PermisoApiService>();
builder.Services.AddScoped<NoticiasDatabaseInitializer>();
builder.Services.AddScoped<BusquedaTextoCompletoNoticiasService>();

// Jerarquía administrativa del Álbum Botánico.
builder.Services.AddScoped<AlbumJerarquiaDatabaseInitializer>();

builder.Services.AddScoped<DispositivoConexionService>();
builder.Services.AddScoped<DispositivosConexionDatabaseInitializer>();
builder.Services.AddScoped<UmbralesAlertasService>();
builder.Services.AddScoped<CapasSueloMapaService>();

builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 256;
});

builder.Services.Configure<ClimaMapaOptions>(
    builder.Configuration.GetSection(ClimaMapaOptions.Seccion));

builder.Services.AddHttpClient<ClimaMapaService>(
    (serviceProvider, client) =>
    {
        ClimaMapaOptions options = serviceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<ClimaMapaOptions>>()
            .Value;

        string baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? "https://api.open-meteo.com/"
            : options.BaseUrl.Trim();

        client.BaseAddress = new Uri(
            baseUrl.EndsWith('/') ? baseUrl : $"{baseUrl}/");

        client.Timeout = TimeSpan.FromSeconds(
            Math.Clamp(options.SegundosTimeout, 5, 60));

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "CONATRADEC-CentroGeoespacial/1.0");
    });

builder.Services.AddScoped<AlertasAgricolasDatabaseInitializer>();
builder.Services.AddScoped<ActualizacionesDatabaseInitializer>();

builder.Services.AddScoped<AuditRequestContext>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddScoped<AuditTransactionInterceptor>();

builder.Services.AddScoped<SessionVersionSaveChangesInterceptor>();
builder.Services.AddScoped<SessionSecurityDatabaseInitializer>();

string connectionString =
    builder.Configuration
        .GetConnectionString("DefaultConnection") ??
    throw new InvalidOperationException(
        "No se encontró la cadena de conexión DefaultConnection.");

builder.Services.AddDbContext<DBContext>(
    (serviceProvider, options) =>
    {
        options
            .UseSqlServer(connectionString)
            .AddInterceptors(
                serviceProvider
                    .GetRequiredService<AuditSaveChangesInterceptor>(),
                serviceProvider
                    .GetRequiredService<AuditTransactionInterceptor>(),
                serviceProvider
                    .GetRequiredService<SessionVersionSaveChangesInterceptor>());

        if (builder.Environment.IsDevelopment())
        {
            options.LogTo(
                Console.WriteLine,
                LogLevel.Information);
        }
    });

builder.Services.AddDbContext<NoticiasDbContext>(
    (serviceProvider, options) =>
    {
        options
            .UseSqlServer(connectionString)
            .AddInterceptors(
                serviceProvider
                    .GetRequiredService<AuditSaveChangesInterceptor>(),
                serviceProvider
                    .GetRequiredService<AuditTransactionInterceptor>());
    });

builder.Services.AddDbContext<BitacoraDbContext>(
    options =>
    {
        options.UseSqlServer(connectionString);
    });

builder.Services.AddDbContext<DispositivosConexionDbContext>(
    options =>
    {
        options.UseSqlServer(connectionString);
    });

builder.Services.AddDbContext<AlertasAgricolasDbContext>(
    options =>
    {
        options.UseSqlServer(connectionString);
    });

builder.Services.AddDbContext<ActualizacionesDbContext>(
    options =>
    {
        options.UseSqlServer(connectionString);
    });

builder.Services.AddDbContext<DiagnosticoIADbContext>(
    options =>
    {
        options.UseSqlServer(connectionString);
    });

// Contexto aislado utilizado por AlbumJerarquiaController.
builder.Services.AddDbContext<AlbumJerarquiaDbContext>(
    options =>
    {
        options.UseSqlServer(connectionString);
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Sirve las mismas URLs públicas desde una carpeta física persistente
// configurada fuera del directorio de publicación.
app.UseConatradecImageStorage();

app.UseRouting();

/*
 * CORS debe ejecutarse después de UseRouting y antes de autenticación.
 * La política solo autoriza orígenes locales o configurados explícitamente.
 */
app.UseCors(politicaCorsPropietarios);

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseAuthentication();
app.UseMiddleware<JwtSessionMiddleware>();
app.UseMiddleware<VersionSesionMiddleware>();

app.UseStatusCodePages(
    async statusCodeContext =>
    {
        HttpResponse response = statusCodeContext
            .HttpContext
            .Response;

        if (response.HasStarted ||
            response.ContentLength is > 0 ||
            !string.IsNullOrWhiteSpace(response.ContentType))
        {
            return;
        }

        response.ContentType =
            "application/json; charset=utf-8";

        var errorResponse = ApiErrorResponseFactory.Create(
            statusCodeContext.HttpContext,
            response.StatusCode);

        await response.WriteAsJsonAsync(errorResponse);
    });

app.UseMiddleware<BitacoraMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapDispositivosConexionEndpoints();

app.MapGet(
    "/imagenes/miniatura",
    async Task<IResult> (
        HttpContext context,
        ImageService imageService,
        string ruta,
        int ancho = 720,
        int alto = 480,
        int calidad = 68,
        CancellationToken cancellationToken = default) =>
    {
        try
        {
            MiniaturaImagenResult? miniatura =
                await imageService.ObtenerOCrearMiniaturaAsync(
                    ruta,
                    ancho,
                    alto,
                    calidad,
                    cancellationToken);

            if (miniatura == null)
                return Results.NotFound();

            string etag = $"\"{miniatura.ETag}\"";

            context.Response.Headers["ETag"] = etag;
            context.Response.Headers["Cache-Control"] =
                "public,max-age=2592000,immutable";
            context.Response.Headers["Last-Modified"] =
                miniatura.UltimaModificacion.ToString("R");
            context.Response.Headers["X-Content-Type-Options"] =
                "nosniff";

            string ifNoneMatch = context
                .Request
                .Headers["If-None-Match"]
                .ToString();

            bool noModificada = ifNoneMatch
                .Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Any(value => string.Equals(
                    value,
                    etag,
                    StringComparison.Ordinal));

            if (noModificada)
            {
                return Results.StatusCode(
                    StatusCodes.Status304NotModified);
            }

            return Results.File(
                miniatura.RutaFisica,
                "image/webp");
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(499);
        }
    });


/*
 * Copia JPEG para la preparación offline de Windows.
 *
 * Esta ruta es deliberadamente distinta al controlador experimental anterior
 * para evitar conflictos si ese archivo aún existe en alguna publicación.
 * Android y la navegación normal continúan usando /imagenes/miniatura (WebP).
 */
app.MapGet(
    "/imagenes/offline-windows/jpeg-directo",
    async Task<IResult> (
        HttpContext context,
        ImageStoragePathService storage,
        ILoggerFactory loggerFactory,
        string ruta,
        int ancho = 720,
        int alto = 720,
        int calidad = 78,
        CancellationToken cancellationToken = default) =>
    {
        ILogger logger =
            loggerFactory.CreateLogger("ImagenOfflineWindows");

        try
        {
            if (string.IsNullOrWhiteSpace(ruta))
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = "La ruta de la imagen es obligatoria."
                });
            }

            if (ancho < 120 || ancho > 1600 ||
                alto < 120 || alto > 1600)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message =
                        "Las dimensiones solicitadas no son válidas."
                });
            }

            if (calidad < 50 || calidad > 90)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message =
                        "La calidad solicitada no es válida."
                });
            }

            string rutaNormalizada;

            try
            {
                rutaNormalizada =
                    Uri.UnescapeDataString(ruta.Trim());
            }
            catch
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = "La ruta de la imagen no es válida."
                });
            }

            if (Uri.TryCreate(
                    rutaNormalizada,
                    UriKind.Absolute,
                    out Uri? uriImagen))
            {
                rutaNormalizada = uriImagen.AbsolutePath;
            }

            rutaNormalizada = rutaNormalizada
                .Replace('\\', '/')
                .Trim();

            if (!rutaNormalizada.StartsWith('/'))
                rutaNormalizada = "/" + rutaNormalizada;

            /*
             * El Álbum puede contener:
             *
             * - fotografías administradas directamente en album-botanico;
             * - portadas de categorias-album;
             * - evidencia original de Inspección/Diagnóstico IA publicada
             *   oficialmente en el Álbum.
             *
             * El flujo fitosanitario conserva la ruta original de la evidencia,
             * por lo que no debe exigirse que el archivo haya sido copiado a
             * album-botanico. Se aceptan ambas carpetas históricas utilizadas
             * por el backend para Diagnóstico IA.
             */
            bool perteneceAlbum =
                rutaNormalizada.StartsWith(
                    "/resources/uploads/album-botanico/",
                    StringComparison.OrdinalIgnoreCase) ||
                rutaNormalizada.StartsWith(
                    "/resources/uploads/categorias-album/",
                    StringComparison.OrdinalIgnoreCase) ||
                rutaNormalizada.StartsWith(
                    "/resources/uploads/diagnosticos-ia/",
                    StringComparison.OrdinalIgnoreCase) ||
                rutaNormalizada.StartsWith(
                    "/resources/uploads/diagnostico-ia/",
                    StringComparison.OrdinalIgnoreCase);

            if (!perteneceAlbum ||
                rutaNormalizada.Contains(
                    "..",
                    StringComparison.Ordinal))
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message =
                        "La imagen solicitada no pertenece al Álbum Botánico.",
                    ruta = rutaNormalizada
                });
            }

            if (!storage.TryResolverRutaPublica(
                    rutaNormalizada,
                    out string rutaFisica))
            {
                return Results.NotFound(new
                {
                    success = false,
                    message =
                        "La ruta pública no pudo resolverse en el almacenamiento de imágenes.",
                    ruta = rutaNormalizada
                });
            }

            if (!File.Exists(rutaFisica))
            {
                /*
                 * Compatibilidad con fotografías creadas antes de mover el
                 * almacenamiento fuera del directorio de publicación.
                 *
                 * Si todavía existe una copia en resources/uploads, se
                 * recupera automáticamente hacia la carpeta persistente.
                 */
                string prefijoPublico = "/resources/uploads/";
                string relativaLegacy = rutaNormalizada.StartsWith(
                        prefijoPublico,
                        StringComparison.OrdinalIgnoreCase)
                    ? rutaNormalizada[prefijoPublico.Length..]
                    : string.Empty;

                string legacyRoot = Path.GetFullPath(
                    storage.LegacyRootPath);

                string legacyCandidate = string.IsNullOrWhiteSpace(
                        relativaLegacy)
                    ? string.Empty
                    : Path.GetFullPath(
                        Path.Combine(
                            legacyRoot,
                            relativaLegacy.Replace(
                                '/',
                                Path.DirectorySeparatorChar)));

                string legacyPrefix =
                    legacyRoot.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;

                bool legacySeguro =
                    !string.IsNullOrWhiteSpace(legacyCandidate) &&
                    (
                        legacyCandidate.StartsWith(
                            legacyPrefix,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            legacyCandidate,
                            legacyRoot,
                            StringComparison.OrdinalIgnoreCase)
                    );

                if (legacySeguro &&
                    File.Exists(legacyCandidate))
                {
                    string? carpetaDestino =
                        Path.GetDirectoryName(rutaFisica);

                    if (!string.IsNullOrWhiteSpace(carpetaDestino))
                        Directory.CreateDirectory(carpetaDestino);

                    File.Copy(
                        legacyCandidate,
                        rutaFisica,
                        overwrite: false);
                }
            }

            if (!File.Exists(rutaFisica))
            {
                return Results.NotFound(new
                {
                    success = false,
                    code = "ALBUM_IMAGE_FILE_MISSING",
                    message =
                        "La fotografía existe en los datos del álbum, pero el archivo físico no fue encontrado en el almacenamiento del servidor.",
                    ruta = rutaNormalizada
                });
            }

            await using FileStream input = new(
                rutaFisica,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);

            using ImageSharpImage imagen =
                await ImageSharpImage.LoadAsync(
                    input,
                    cancellationToken);

            imagen.Mutate(x => x.AutoOrient());

            if (imagen.Width > ancho ||
                imagen.Height > alto)
            {
                imagen.Mutate(x => x.Resize(
                    new ResizeOptions
                    {
                        Size = new ImageSharpSize(ancho, alto),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3,
                        Compand = true
                    }));
            }

            await using var output = new MemoryStream();

            await imagen.SaveAsync(
                output,
                new JpegEncoder
                {
                    Quality = calidad
                },
                cancellationToken);

            context.Response.Headers["Cache-Control"] =
                "public,max-age=604800";

            context.Response.Headers["X-Content-Type-Options"] =
                "nosniff";

            return Results.File(
                output.ToArray(),
                "image/jpeg");
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(499);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "No fue posible generar la copia JPEG offline de Windows para {Ruta}.",
                ruta);

            return Results.Problem(
                statusCode:
                    StatusCodes.Status500InternalServerError,
                title:
                    "No fue posible preparar la fotografía para Windows.",
                detail:
                    "El servidor no pudo convertir la fotografía solicitada a JPEG.");
        }
    });

await using (
    AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    SessionSecurityDatabaseInitializer sessionInitializer =
        scope.ServiceProvider
            .GetRequiredService<SessionSecurityDatabaseInitializer>();

    await sessionInitializer.InicializarAsync();

    PortalWebDatabaseInitializer portalInitializer =
        scope.ServiceProvider
            .GetRequiredService<PortalWebDatabaseInitializer>();

    await portalInitializer.InicializarAsync();

    ParametrizacionAccesoDatabaseInitializer accesoInitializer =
        scope.ServiceProvider
            .GetRequiredService<ParametrizacionAccesoDatabaseInitializer>();

    await accesoInitializer.InicializarAsync();

    ControlAnalisisDatabaseInitializer controlAnalisisInitializer =
        scope.ServiceProvider
            .GetRequiredService<ControlAnalisisDatabaseInitializer>();

    await controlAnalisisInitializer.InicializarAsync();

    DiagnosticoIADatabaseInitializer diagnosticoIAInitializer =
        scope.ServiceProvider
            .GetRequiredService<DiagnosticoIADatabaseInitializer>();

    await diagnosticoIAInitializer.InicializarAsync();

    InspeccionFitosanitariaControlDatabaseInitializer inspeccionControlInitializer =
        scope.ServiceProvider
            .GetRequiredService<InspeccionFitosanitariaControlDatabaseInitializer>();

    await inspeccionControlInitializer.InicializarAsync();

    InspeccionFitosanitariaAdministracionDatabaseInitializer inspeccionAdminInitializer =
        scope.ServiceProvider
            .GetRequiredService<InspeccionFitosanitariaAdministracionDatabaseInitializer>();

    await inspeccionAdminInitializer.InicializarAsync();

    // Crea o repara la jerarquía del Álbum Botánico antes de atender solicitudes.
    AlbumJerarquiaDatabaseInitializer albumJerarquiaInitializer =
        scope.ServiceProvider
            .GetRequiredService<AlbumJerarquiaDatabaseInitializer>();

    await albumJerarquiaInitializer.InicializarAsync();

    DispositivosConexionDatabaseInitializer dispositivosInitializer =
        scope.ServiceProvider
            .GetRequiredService<DispositivosConexionDatabaseInitializer>();

    await dispositivosInitializer.InicializarAsync();

    AnalisisSueloDatabaseInitializer analisisInitializer =
        scope.ServiceProvider
            .GetRequiredService<AnalisisSueloDatabaseInitializer>();

    await analisisInitializer.InicializarAsync();

    AnalisisHistorialDatabaseInitializer historialInitializer =
        scope.ServiceProvider
            .GetRequiredService<AnalisisHistorialDatabaseInitializer>();

    await historialInitializer.InicializarAsync();

    NoticiasDatabaseInitializer noticiasInitializer =
        scope.ServiceProvider
            .GetRequiredService<NoticiasDatabaseInitializer>();

    await noticiasInitializer.InicializarAsync();

    AlertasAgricolasDatabaseInitializer alertasInitializer =
        scope.ServiceProvider
            .GetRequiredService<AlertasAgricolasDatabaseInitializer>();

    await alertasInitializer.InicializarAsync();

    ActualizacionesDatabaseInitializer actualizacionesInitializer =
        scope.ServiceProvider
            .GetRequiredService<ActualizacionesDatabaseInitializer>();

    await actualizacionesInitializer.InicializarAsync();
}

app.Run();

static bool EsOrigenCorsPermitido(
    string? origen,
    IReadOnlyCollection<string> origenesConfigurados)
{
    if (string.IsNullOrWhiteSpace(origen))
        return false;

    string origenNormalizado =
        origen.Trim().TrimEnd('/');

    if (origenesConfigurados.Any(configurado =>
        string.Equals(
            configurado?.Trim().TrimEnd('/'),
            origenNormalizado,
            StringComparison.OrdinalIgnoreCase)))
    {
        return true;
    }

    if (!Uri.TryCreate(
        origenNormalizado,
        UriKind.Absolute,
        out Uri? uri))
    {
        return false;
    }

    bool esquemaValido =
        uri.Scheme.Equals(
            Uri.UriSchemeHttp,
            StringComparison.OrdinalIgnoreCase) ||
        uri.Scheme.Equals(
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase);

    return esquemaValido && uri.IsLoopback;
}
