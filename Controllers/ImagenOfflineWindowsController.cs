using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Entrega una representación JPEG de las imágenes del Álbum Botánico
    /// exclusivamente para la preparación offline de la aplicación Windows.
    ///
    /// No reemplaza las imágenes WebP existentes ni modifica los endpoints
    /// utilizados por Android, web o por la navegación online.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("imagenes/offline-windows")]
    public sealed class ImagenOfflineWindowsController : ControllerBase
    {
        private readonly ImageStoragePathService storage;
        private readonly ILogger<ImagenOfflineWindowsController> logger;

        public ImagenOfflineWindowsController(
            ImageStoragePathService storage,
            ILogger<ImagenOfflineWindowsController> logger)
        {
            this.storage = storage;
            this.logger = logger;
        }

        /// <summary>
        /// Convierte bajo demanda una imagen pública del Álbum Botánico a JPEG
        /// para que WinUI pueda almacenarla y mostrarla de forma confiable
        /// desde AppDataDirectory cuando no existe conexión.
        /// </summary>
        [HttpGet("jpeg")]
        public async Task<IActionResult> ObtenerJpegAsync(
            [FromQuery] string ruta,
            [FromQuery] int ancho = 720,
            [FromQuery] int alto = 720,
            [FromQuery] int calidad = 78,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(ruta))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La ruta de la imagen es obligatoria."
                });
            }

            if (ancho < 120 || ancho > 1600 ||
                alto < 120 || alto > 1600)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Las dimensiones solicitadas no son válidas."
                });
            }

            if (calidad < 50 || calidad > 90)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La calidad solicitada no es válida."
                });
            }

            string rutaNormalizada;

            try
            {
                rutaNormalizada = NormalizarRutaPublica(ruta);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }

            /*
             * El endpoint queda restringido al Álbum Botánico.
             * No puede utilizarse como conversor genérico de archivos.
             */
            if (!EsRutaAlbumPermitida(rutaNormalizada))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La imagen solicitada no pertenece al Álbum Botánico."
                });
            }

            if (!storage.TryResolverRutaPublica(
                    rutaNormalizada,
                    out string rutaFisica) ||
                !System.IO.File.Exists(rutaFisica))
            {
                return NotFound(new
                {
                    success = false,
                    message = "La fotografía solicitada no fue encontrada."
                });
            }

            try
            {
                await using FileStream input = new(
                    rutaFisica,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    useAsync: true);

                using Image imagen =
                    await Image.LoadAsync(
                        input,
                        cancellationToken);

                imagen.Mutate(x => x.AutoOrient());

                if (imagen.Width > ancho ||
                    imagen.Height > alto)
                {
                    imagen.Mutate(x => x.Resize(
                        new ResizeOptions
                        {
                            Size = new Size(ancho, alto),
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

                byte[] contenido = output.ToArray();

                Response.Headers["Cache-Control"] =
                    "public,max-age=604800";

                Response.Headers["X-Content-Type-Options"] =
                    "nosniff";

                return File(
                    contenido,
                    "image/jpeg");
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "No fue posible convertir a JPEG la imagen offline " +
                    "de Windows {Ruta}.",
                    rutaNormalizada);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message =
                            "No fue posible preparar la fotografía " +
                            "para Windows."
                    });
            }
        }

        private static string NormalizarRutaPublica(string ruta)
        {
            string valor;

            try
            {
                valor = Uri.UnescapeDataString(
                    ruta.Trim());
            }
            catch
            {
                throw new ArgumentException(
                    "La ruta de la imagen no es válida.");
            }

            if (Uri.TryCreate(
                    valor,
                    UriKind.Absolute,
                    out Uri? uri))
            {
                valor = uri.AbsolutePath;
            }

            valor = valor
                .Replace('\\', '/')
                .Trim();

            if (!valor.StartsWith('/'))
                valor = "/" + valor;

            if (valor.Contains(
                    "..",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "La ruta de la imagen no es válida.");
            }

            return valor;
        }

        private static bool EsRutaAlbumPermitida(string ruta) =>
            ruta.StartsWith(
                "/resources/uploads/album-botanico/",
                StringComparison.OrdinalIgnoreCase) ||
            ruta.StartsWith(
                "/resources/uploads/categorias-album/",
                StringComparison.OrdinalIgnoreCase);
    }
}