using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace CONATRADEC_API.Services
{
    public sealed class MiniaturaImagenResult
    {
        public string RutaFisica { get; init; } = string.Empty;
        public DateTimeOffset UltimaModificacion { get; init; }
        public string ETag { get; init; } = string.Empty;
    }

    public class ImageService
    {
        private readonly ImageStoragePathService storage;

        private static readonly ConcurrentDictionary<string, SemaphoreSlim>
            BloqueosPorMiniatura =
                new(StringComparer.OrdinalIgnoreCase);

        private static readonly SemaphoreSlim ProcesamientoMiniaturas =
            new(initialCount: 2, maxCount: 2);

        public ImageService(ImageStoragePathService storage)
        {
            this.storage = storage;
        }

        public async Task<string> GuardarImagenWebpAsync(
            IFormFile archivo,
            string carpeta,
            int anchoMaximo = 1600,
            int altoMaximo = 1600,
            int calidad = 80)
        {
            ValidarArchivo(archivo);

            string nombreArchivo =
                $"{Guid.NewGuid():N}.webp";

            string carpetaNormalizada = carpeta
                .Replace('\\', '/')
                .Trim('/');

            string rutaCarpeta =
                storage.ObtenerCarpeta(carpetaNormalizada);

            Directory.CreateDirectory(rutaCarpeta);

            string rutaFisica = Path.Combine(
                rutaCarpeta,
                nombreArchivo);

            await using Stream stream =
                archivo.OpenReadStream();

            using Image imagen =
                await Image.LoadAsync(stream);

            imagen.Mutate(x => x.AutoOrient());

            if (imagen.Width > anchoMaximo ||
                imagen.Height > altoMaximo)
            {
                imagen.Mutate(x => x.Resize(
                    new ResizeOptions
                    {
                        Size = new Size(
                            anchoMaximo,
                            altoMaximo),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3,
                        Compand = true
                    }));
            }

            await imagen.SaveAsync(
                rutaFisica,
                new WebpEncoder
                {
                    Quality = calidad
                });

            string rutaPublica =
                $"/resources/uploads/{carpetaNormalizada}/" +
                nombreArchivo;

            try
            {
                if (carpetaNormalizada.StartsWith(
                        "album-botanico",
                        StringComparison.OrdinalIgnoreCase) ||
                    carpetaNormalizada.StartsWith(
                        "noticias",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await ObtenerOCrearMiniaturaAsync(
                        rutaPublica,
                        ancho: 720,
                        alto: 480,
                        calidad: 68);
                }
                else if (carpetaNormalizada.Equals(
                             "categorias-album",
                             StringComparison.OrdinalIgnoreCase))
                {
                    await ObtenerOCrearMiniaturaAsync(
                        rutaPublica,
                        ancho: 420,
                        alto: 260,
                        calidad: 65);
                }
            }
            catch
            {
                // La miniatura se volverá a intentar bajo demanda.
            }

            return rutaPublica;
        }

        public async Task<MiniaturaImagenResult?>
            ObtenerOCrearMiniaturaAsync(
                string? rutaRelativa,
                int ancho = 720,
                int alto = 480,
                int calidad = 68,
                CancellationToken cancellationToken = default)
        {
            ValidarParametrosMiniatura(
                ancho,
                alto,
                calidad);

            string rutaNormalizada =
                NormalizarRutaRelativa(rutaRelativa);

            string rutaOriginal =
                storage.ResolverRutaPublica(rutaNormalizada);

            if (!File.Exists(rutaOriginal))
                return null;

            string hashRuta = CalcularHashRuta(
                rutaNormalizada);

            string carpetaMiniaturas =
                storage.ObtenerCarpeta(
                    $".miniaturas/{ancho}x{alto}-q{calidad}");

            Directory.CreateDirectory(carpetaMiniaturas);

            string rutaMiniatura = Path.Combine(
                carpetaMiniaturas,
                $"{hashRuta}.webp");

            if (MiniaturaEstaActualizada(
                    rutaOriginal,
                    rutaMiniatura))
            {
                return CrearResultadoMiniatura(
                    rutaMiniatura);
            }

            SemaphoreSlim bloqueo =
                BloqueosPorMiniatura.GetOrAdd(
                    rutaMiniatura,
                    _ => new SemaphoreSlim(1, 1));

            await bloqueo.WaitAsync(cancellationToken);

            try
            {
                if (MiniaturaEstaActualizada(
                        rutaOriginal,
                        rutaMiniatura))
                {
                    return CrearResultadoMiniatura(
                        rutaMiniatura);
                }

                await ProcesamientoMiniaturas.WaitAsync(
                    cancellationToken);

                try
                {
                    await GenerarMiniaturaAsync(
                        rutaOriginal,
                        rutaMiniatura,
                        ancho,
                        alto,
                        calidad,
                        cancellationToken);
                }
                finally
                {
                    ProcesamientoMiniaturas.Release();
                }

                return CrearResultadoMiniatura(
                    rutaMiniatura);
            }
            finally
            {
                bloqueo.Release();

                if (bloqueo.CurrentCount == 1)
                {
                    BloqueosPorMiniatura.TryRemove(
                        rutaMiniatura,
                        out _);
                }
            }
        }

        public void EliminarImagen(string? rutaRelativa)
        {
            if (string.IsNullOrWhiteSpace(rutaRelativa))
                return;

            string rutaNormalizada;
            string rutaFisica;

            try
            {
                rutaNormalizada =
                    NormalizarRutaRelativa(rutaRelativa);

                rutaFisica =
                    storage.ResolverRutaPublica(rutaNormalizada);
            }
            catch
            {
                return;
            }

            if (File.Exists(rutaFisica))
                File.Delete(rutaFisica);

            EliminarMiniaturasAsociadas(
                rutaNormalizada);
        }

        public bool ImagenExiste(string? rutaOUrl) =>
            storage.ArchivoExiste(rutaOUrl);

        private async Task GenerarMiniaturaAsync(
            string rutaOriginal,
            string rutaMiniatura,
            int ancho,
            int alto,
            int calidad,
            CancellationToken cancellationToken)
        {
            string rutaTemporal =
                rutaMiniatura +
                $".{Guid.NewGuid():N}.tmp";

            try
            {
                await using FileStream stream = new(
                    rutaOriginal,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    useAsync: true);

                using Image imagen =
                    await Image.LoadAsync(
                        stream,
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

                await imagen.SaveAsync(
                    rutaTemporal,
                    new WebpEncoder
                    {
                        Quality = calidad
                    },
                    cancellationToken);

                File.Move(
                    rutaTemporal,
                    rutaMiniatura,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(rutaTemporal))
                {
                    try
                    {
                        File.Delete(rutaTemporal);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string NormalizarRutaRelativa(
            string? rutaRelativa)
        {
            if (string.IsNullOrWhiteSpace(rutaRelativa))
            {
                throw new ArgumentException(
                    "La ruta de la imagen es obligatoria.");
            }

            string valor = Uri.UnescapeDataString(
                rutaRelativa.Trim());

            if (Uri.TryCreate(
                    valor,
                    UriKind.Absolute,
                    out Uri? uri))
            {
                valor = uri.AbsolutePath;
            }

            valor = valor
                .Replace('\\', '/')
                .TrimStart('/');

            if (!valor.StartsWith(
                    "resources/uploads/",
                    StringComparison.OrdinalIgnoreCase) ||
                valor.Contains(
                    "/.miniaturas/",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "La ruta de la imagen no es válida.");
            }

            string extension = Path.GetExtension(valor)
                .ToLowerInvariant();

            if (extension is not
                (".jpg" or ".jpeg" or ".png" or ".webp"))
            {
                throw new ArgumentException(
                    "El archivo solicitado no es una imagen válida.");
            }

            return valor;
        }

        private static void ValidarArchivo(IFormFile archivo)
        {
            if (archivo == null || archivo.Length == 0)
            {
                throw new ArgumentException(
                    "Debe seleccionar una imagen.");
            }

            if (archivo.Length > 8 * 1024 * 1024)
            {
                throw new ArgumentException(
                    "La imagen no puede superar los 8 MB.");
            }

            string extension = Path
                .GetExtension(archivo.FileName)
                .ToLowerInvariant();

            if (extension is not
                (".jpg" or ".jpeg" or ".png" or ".webp"))
            {
                throw new ArgumentException(
                    "Solo se permiten imágenes JPG, JPEG, PNG o WEBP.");
            }
        }

        private static void ValidarParametrosMiniatura(
            int ancho,
            int alto,
            int calidad)
        {
            if (ancho < 80 || ancho > 1200 ||
                alto < 80 || alto > 1200)
            {
                throw new ArgumentException(
                    "Las dimensiones de la miniatura no son válidas.");
            }

            if (calidad < 40 || calidad > 85)
            {
                throw new ArgumentException(
                    "La calidad de la miniatura no es válida.");
            }
        }

        private static bool MiniaturaEstaActualizada(
            string rutaOriginal,
            string rutaMiniatura)
        {
            if (!File.Exists(rutaMiniatura))
                return false;

            return File.GetLastWriteTimeUtc(rutaMiniatura) >=
                   File.GetLastWriteTimeUtc(rutaOriginal);
        }

        private void EliminarMiniaturasAsociadas(
            string rutaNormalizada)
        {
            string raizMiniaturas =
                storage.ObtenerCarpeta(".miniaturas");

            if (!Directory.Exists(raizMiniaturas))
                return;

            string hashRuta = CalcularHashRuta(
                rutaNormalizada);

            foreach (string archivo in Directory.EnumerateFiles(
                         raizMiniaturas,
                         $"{hashRuta}.webp",
                         SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(archivo);
                }
                catch
                {
                }
            }
        }

        private static MiniaturaImagenResult
            CrearResultadoMiniatura(
                string rutaMiniatura)
        {
            FileInfo info = new(rutaMiniatura);

            string materialEtag =
                $"{info.FullName}|{info.Length}|" +
                info.LastWriteTimeUtc.Ticks;

            string etag = Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            materialEtag)))
                .ToLowerInvariant();

            return new MiniaturaImagenResult
            {
                RutaFisica = info.FullName,
                UltimaModificacion =
                    new DateTimeOffset(
                        info.LastWriteTimeUtc),
                ETag = etag
            };
        }

        private static string CalcularHashRuta(
            string rutaNormalizada) =>
            Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            rutaNormalizada
                                .ToLowerInvariant())))
                .ToLowerInvariant();
    }
}
