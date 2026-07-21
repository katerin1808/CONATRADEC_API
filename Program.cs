using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using QuestPDF.Infrastructure;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddScoped<AnalisisSueloCalculoService>();
builder.Services.AddScoped<AnalisisReporteDatosService>();
builder.Services.AddDbContext<DBContext>(o =>
{
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
    .EnableSensitiveDataLogging()
    .LogTo(Console.WriteLine);
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Community aplica a individuos, proyectos FOSS, organizaciones sin fines
// de lucro y organizaciones con ingresos anuales inferiores a USD 1 millón.
// Cambie el tipo de licencia si su organización no cumple esas condiciones.
QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

var rutaRecursos = Path.Combine(Directory.GetCurrentDirectory(), "resources", "uploads", "users", "img");
Directory.CreateDirectory(rutaRecursos); // la crea si no existe la carpeta de resources

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(rutaRecursos),
    RequestPath = "/resources/uploads/users/img",
  
});// sirve /resources/uploads/users/img
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
    "album-botanico"
);

Directory.CreateDirectory(rutaAlbumBotanico);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(rutaAlbumBotanico),
    RequestPath = "/resources/uploads/album-botanico"
});
app.MapControllers();

app.Run();
