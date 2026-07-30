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
using Microsoft.Extensions.FileProviders;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

const long tamanoMaximoActualizacion = 1024L * 1024L * 1024L;

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

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiErrorResponseFilter>();
});

// JWT, expiración absoluta y control de inactividad de las sesiones.
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
builder.Services.AddScoped<ImageService>();

builder.Services.AddScoped<AnalisisSueloDatabaseInitializer>();
builder.Services.AddScoped<PortalWebDatabaseInitializer>();
builder.Services.AddScoped<ControlAnalisisDatabaseInitializer>();
builder.Services.AddScoped<PermisoApiService>();
builder.Services.AddScoped<NoticiasDatabaseInitializer>();
builder.Services.AddScoped<BusquedaTextoCompletoNoticiasService>();

builder.Services.AddScoped<DispositivoConexionService>();
builder.Services.AddScoped<DispositivosConexionDatabaseInitializer>();
builder.Services.AddScoped<UmbralesAlertasService>();
builder.Services.AddScoped<CapasSueloMapaService>();

// Centro Geoespacial: capas climáticas con caché y proveedor configurable.
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

// Módulo de publicación y descarga de versiones Android/Windows.
builder.Services.AddScoped<ActualizacionesDatabaseInitializer>();

builder.Services.AddScoped<AuditRequestContext>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddScoped<AuditTransactionInterceptor>();

// Seguridad de usuarios y control de vigencia de sesiones.
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

var rutaRecursos = Path.Combine(
    Directory.GetCurrentDirectory(),
    "resources",
    "uploads",
    "users",
    "img");

Directory.CreateDirectory(rutaRecursos);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(rutaRecursos),
    RequestPath = "/resources/uploads/users/img"
});

var rutaTerrenos = Path.Combine(
    Directory.GetCurrentDirectory(),
    "resources",
    "uploads",
    "terrenos");

Directory.CreateDirectory(rutaTerrenos);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(rutaTerrenos),
    RequestPath = "/resources/uploads/terrenos"
});

var rutaAlbumBotanico = Path.Combine(
    Directory.GetCurrentDirectory(),
    "resources",
    "uploads",
    "album-botanico");

Directory.CreateDirectory(rutaAlbumBotanico);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(rutaAlbumBotanico),
    RequestPath = "/resources/uploads/album-botanico"
});

var rutaCategoriasAlbum = Path.Combine(
    Directory.GetCurrentDirectory(),
    "resources",
    "uploads",
    "categorias-album");

Directory.CreateDirectory(rutaCategoriasAlbum);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(rutaCategoriasAlbum),
    RequestPath = "/resources/uploads/categorias-album"
});

var rutaNoticias = Path.Combine(
    Directory.GetCurrentDirectory(),
    "resources",
    "uploads",
    "noticias");

Directory.CreateDirectory(rutaNoticias);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(rutaNoticias),
    RequestPath = "/resources/uploads/noticias"
});

app.UseRouting();

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

    ControlAnalisisDatabaseInitializer controlAnalisisInitializer =
        scope.ServiceProvider
            .GetRequiredService<ControlAnalisisDatabaseInitializer>();

    await controlAnalisisInitializer.InicializarAsync();

    DispositivosConexionDatabaseInitializer dispositivosInitializer =
        scope.ServiceProvider
            .GetRequiredService<DispositivosConexionDatabaseInitializer>();

    await dispositivosInitializer.InicializarAsync();

    AnalisisSueloDatabaseInitializer analisisInitializer =
        scope.ServiceProvider
            .GetRequiredService<AnalisisSueloDatabaseInitializer>();

    await analisisInitializer.InicializarAsync();

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
