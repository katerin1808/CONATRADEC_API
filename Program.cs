using CONATRADEC_API.Auditing;
using CONATRADEC_API.Middleware;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddScoped<AnalisisSueloCalculoService>();
builder.Services.AddScoped<AnalisisReporteDatosService>();

builder.Services.AddScoped<AuditRequestContext>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();

string connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection") ??
    throw new InvalidOperationException(
        "No se encontró la cadena de conexión DefaultConnection.");

builder.Services.AddDbContext<DBContext>((serviceProvider, options) =>
{
    options
        .UseSqlServer(connectionString)
        .EnableSensitiveDataLogging()
        .LogTo(Console.WriteLine)
        .AddInterceptors(
            serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
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
app.UseAuthorization();

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

app.UseMiddleware<BitacoraMiddleware>();

app.MapControllers();
app.Run();
