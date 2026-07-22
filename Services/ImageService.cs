using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace CONATRADEC_API.Services
{
    public class ImageService
    {
        private readonly IWebHostEnvironment _environment;

        public ImageService(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public async Task<string> GuardarImagenWebpAsync(
            IFormFile archivo,
            string carpeta,
            int anchoMaximo = 1600,
            int altoMaximo = 1600,
            int calidad = 80)
        {
            if (archivo is null || archivo.Length == 0)
            {
                throw new ArgumentException("Debe seleccionar una imagen.");
            }

            const long tamanioMaximo = 8 * 1024 * 1024;

            if (archivo.Length > tamanioMaximo)
            {
                throw new ArgumentException(
                    "La imagen no puede superar los 8 MB.");
            }

            string[] extensionesPermitidas =
            {
                ".jpg",
                ".jpeg",
                ".png",
                ".webp"
            };

            string extensionOriginal =
                Path.GetExtension(archivo.FileName).ToLowerInvariant();

            if (!extensionesPermitidas.Contains(extensionOriginal))
            {
                throw new ArgumentException(
                    "Solo se permiten imágenes JPG, JPEG, PNG o WEBP.");
            }

            string nombreArchivo = $"{Guid.NewGuid():N}.webp";

            string rutaCarpeta = Path.Combine(
             Directory.GetCurrentDirectory(),
              "resources",
              "uploads",
              carpeta);

            Directory.CreateDirectory(rutaCarpeta);

            string rutaFisica = Path.Combine(
                rutaCarpeta,
                nombreArchivo);

            using Stream stream = archivo.OpenReadStream();

            using Image imagen = await Image.LoadAsync(stream);

            imagen.Mutate(x => x.AutoOrient());

            if (imagen.Width > anchoMaximo ||
                imagen.Height > altoMaximo)
            {
                imagen.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(anchoMaximo, altoMaximo),
                    Mode = ResizeMode.Max
                }));
            }

            WebpEncoder encoder = new()
            {
                Quality = calidad
            };

            await imagen.SaveAsync(rutaFisica, encoder);

            return $"/resources/uploads/{carpeta}/{nombreArchivo}";
        }

        public void EliminarImagen(string? rutaRelativa)
        {
            if (string.IsNullOrWhiteSpace(rutaRelativa))
                return;

            string rutaNormalizada = rutaRelativa
                .TrimStart('/')
                .Replace('/', Path.DirectorySeparatorChar);

            string rutaFisica = Path.Combine(
                _environment.WebRootPath,
                rutaNormalizada);

            if (File.Exists(rutaFisica))
            {
                File.Delete(rutaFisica);
            }
        }
    }
}