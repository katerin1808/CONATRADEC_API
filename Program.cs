using CONATRADEC_API.Auditing;
using CONATRADEC_API.Endpoints;
using CONATRADEC_API.Filters;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Middleware;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiErrorResponseFilter>();
});

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

builder.Services.AddScoped<
    AnalisisSueloDatabaseInitializer>();

builder.Services.AddScoped<PermisoApiService>();
builder.Services.AddScoped<NoticiasDatabaseInitializer>();
builder.Services.AddScoped<
    BusquedaTextoCompletoNoticiasService>();

builder.Services.AddScoped<DispositivoConexionService>();
builder.Services.AddScoped<
    DispositivosConexionDatabaseInitializer>();

builder.Services.AddScoped<
    UmbralesAlertasService>();

/*
 * Módulo de alertas agrícolas.
 * Este registro es obligatorio para construir
 * SeguimientoAlertasAgricolasController.
 */
builder.Services.AddScoped<
    AlertasAgricolasDatabaseInitializer>();

builder.Services.AddScoped<AuditRequestContext>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddScoped<AuditTransactionInterceptor>();

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
                    .GetRequiredService<
                        AuditSaveChangesInterceptor>(),
                serviceProvider
                    .GetRequiredService<
                        AuditTransactionInterceptor>());

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
                    .GetRequiredService<
                        AuditSaveChangesInterceptor>(),
                serviceProvider
                    .GetRequiredService<
                        AuditTransactionInterceptor>());
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

/*
 * Contexto independiente del módulo de alertas agrícolas.
 * Utiliza la misma base de datos del sistema.
 */
builder.Services.AddDbContext<AlertasAgricolasDbContext>(
    options =>
    {
        options.UseSqlServer(connectionString);
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

QuestPDF.Settings.License =
    LicenseType.Community;

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
    FileProvider =
        new PhysicalFileProvider(rutaRecursos),
    RequestPath =
        "/resources/uploads/users/img"
});

var rutaTerrenos = Path.Combine(
    Directory.GetCurrentDirectory(),
    "resources",
    "uploads",
    "terrenos");

Directory.CreateDirectory(rutaTerrenos);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider =
        new PhysicalFileProvider(rutaTerrenos),
    RequestPath =
        "/resources/uploads/terrenos"
});

var rutaAlbumBotanico = Path.Combine(
    Directory.GetCurrentDirectory(),
    "resources",
    "uploads",
    "album-botanico");

Directory.CreateDirectory(rutaAlbumBotanico);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider =
        new PhysicalFileProvider(
            rutaAlbumBotanico),
    RequestPath =
        "/resources/uploads/album-botanico"
});

var rutaCategoriasAlbum = Path.Combine(
    Directory.GetCurrentDirectory(),
    "resources",
    "uploads",
    "categorias-album");

Directory.CreateDirectory(rutaCategoriasAlbum);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider =
        new PhysicalFileProvider(
            rutaCategoriasAlbum),
    RequestPath =
        "/resources/uploads/categorias-album"
});

var rutaNoticias = Path.Combine(
    Directory.GetCurrentDirectory(),
    "resources",
    "uploads",
    "noticias");

Directory.CreateDirectory(rutaNoticias);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider =
        new PhysicalFileProvider(rutaNoticias),
    RequestPath =
        "/resources/uploads/noticias"
});

app.UseRouting();

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseStatusCodePages(
    async statusCodeContext =>
    {
        HttpResponse response =
            statusCodeContext
                .HttpContext
                .Response;

        if (response.HasStarted ||
            response.ContentLength is > 0 ||
            !string.IsNullOrWhiteSpace(
                response.ContentType))
        {
            return;
        }

        response.ContentType =
            "application/json; charset=utf-8";

        var errorResponse =
            ApiErrorResponseFactory.Create(
                statusCodeContext.HttpContext,
                response.StatusCode);

        await response.WriteAsJsonAsync(
            errorResponse);
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
        CancellationToken cancellationToken =
            default) =>
    {
        try
        {
            MiniaturaImagenResult? miniatura =
                await imageService
                    .ObtenerOCrearMiniaturaAsync(
                        ruta,
                        ancho,
                        alto,
                        calidad,
                        cancellationToken);

            if (miniatura == null)
                return Results.NotFound();

            string etag =
                $"\"{miniatura.ETag}\"";

            context.Response.Headers["ETag"] =
                etag;

            context.Response.Headers[
                    "Cache-Control"] =
                "public,max-age=2592000,immutable";

            context.Response.Headers[
                    "Last-Modified"] =
                miniatura
                    .UltimaModificacion
                    .ToString("R");

            context.Response.Headers[
                    "X-Content-Type-Options"] =
                "nosniff";

            string ifNoneMatch =
                context
                    .Request
                    .Headers["If-None-Match"]
                    .ToString();

            bool noModificada =
                ifNoneMatch
                    .Split(
                        ',',
                        StringSplitOptions
                            .RemoveEmptyEntries |
                        StringSplitOptions
                            .TrimEntries)
                    .Any(value =>
                        string.Equals(
                            value,
                            etag,
                            StringComparison.Ordinal));

            if (noModificada)
            {
                return Results.StatusCode(
                    StatusCodes
                        .Status304NotModified);
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
    AsyncServiceScope scope =
        app.Services.CreateAsyncScope())
{
    DispositivosConexionDatabaseInitializer
        dispositivosInitializer =
            scope.ServiceProvider
                .GetRequiredService<
                    DispositivosConexionDatabaseInitializer>();

    await dispositivosInitializer
        .InicializarAsync();

    AnalisisSueloDatabaseInitializer
        analisisInitializer =
            scope.ServiceProvider
                .GetRequiredService<
                    AnalisisSueloDatabaseInitializer>();

    await analisisInitializer
        .InicializarAsync();

    NoticiasDatabaseInitializer
        noticiasInitializer =
            scope.ServiceProvider
                .GetRequiredService<
                    NoticiasDatabaseInitializer>();

    await noticiasInitializer
        .InicializarAsync();

    /*
     * Crea las tablas y configuraciones del módulo
     * de alertas antes de aceptar solicitudes.
     */
    AlertasAgricolasDatabaseInitializer
        alertasInitializer =
            scope.ServiceProvider
                .GetRequiredService<
                    AlertasAgricolasDatabaseInitializer>();

    await alertasInitializer
        .InicializarAsync();
}

app.Run();
