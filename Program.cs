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
