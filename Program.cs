using CONATRADEC_API.Auditing;
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
    // Todas las respuestas 4xx/5xx de los controladores se convierten
    // al mismo contrato ApiErrorResponse.
    options.Filters.Add<ApiErrorResponseFilter>();
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    // Estandariza también los errores automáticos generados por [ApiController],
    // por ejemplo propiedades obligatorias o tipos de datos inválidos.
    options.InvalidModelStateResponseFactory = context =>
    {
        IDictionary<string, string[]> errors =
            ApiErrorResponseFactory.FromModelState(context.ModelState);

        var response = ApiErrorResponseFactory.Create(
            context.HttpContext,
            StatusCodes.Status400BadRequest,
            message: "Revise los campos indicados e intente nuevamente.",
            errors: errors,
            code: "VALIDATION_ERROR");

        return new BadRequestObjectResult(response);
    };
});

builder.Services.AddScoped<AnalisisSueloCalculoService>();
builder.Services.AddScoped<AnalisisReporteDatosService>();
builder.Services.AddScoped<ImageService>();

// Contexto e interceptores transversales de auditoría.
builder.Services.AddScoped<AuditRequestContext>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddScoped<AuditTransactionInterceptor>();

string connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection") ??
    throw new InvalidOperationException(
        "No se encontró la cadena de conexión DefaultConnection.");

builder.Services.AddDbContext<DBContext>((serviceProvider, options) =>
{
    options
        .UseSqlServer(connectionString)
        .AddInterceptors(
            serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>(),
            serviceProvider.GetRequiredService<AuditTransactionInterceptor>());

    // No se habilita SensitiveDataLogging porque puede exponer valores
    // confidenciales en consola o archivos de logs.
    if (builder.Environment.IsDevelopment())
    {
        options.LogTo(
            Console.WriteLine,
            LogLevel.Information);
    }
});

builder.Services.AddDbContext<BitacoraDbContext>(options =>
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

// Se declara el enrutamiento antes de los middleware transversales.
app.UseRouting();

// Debe envolver los siguientes middleware y controladores para convertir
// cualquier excepción no controlada en una respuesta JSON segura.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Estandariza también respuestas sin cuerpo generadas fuera de un controlador,
// por ejemplo una ruta inexistente, un futuro 401 o un futuro 403.
app.UseStatusCodePages(async statusCodeContext =>
{
    HttpResponse response = statusCodeContext.HttpContext.Response;

    if (response.HasStarted ||
        response.ContentLength is > 0 ||
        !string.IsNullOrWhiteSpace(response.ContentType))
    {
        return;
    }

    response.ContentType = "application/json; charset=utf-8";

    var errorResponse = ApiErrorResponseFactory.Create(
        statusCodeContext.HttpContext,
        response.StatusCode);

    await response.WriteAsJsonAsync(errorResponse);
});

// Debe ejecutarse antes de autorización para que también queden registrados
// futuros 401 y 403 producidos por ASP.NET Core.
app.UseMiddleware<BitacoraMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();
