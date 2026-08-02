using Microsoft.Extensions.Options;

namespace CONATRADEC_API.Services
{
    /// <summary>
    /// Resuelve y protege todas las rutas físicas del banco de imágenes.
    /// Centralizar esta lógica impide que cada controlador dependa de la
    /// carpeta donde fue publicada la API.
    /// </summary>
    public sealed class ImageStoragePathService
    {
        public const string PrefijoPublico =
            "resources/uploads/";

        private static readonly string[] CarpetasConocidas =
        [
            "users/img",
            "terrenos",
            "album-botanico",
            "categorias-album",
            "noticias",
            ".miniaturas"
        ];

        private readonly IWebHostEnvironment environment;
        private readonly ImageStorageOptions options;
        private readonly ILogger<ImageStoragePathService> logger;
        private readonly object initializationLock = new();
        private bool initialized;

        public ImageStoragePathService(
            IWebHostEnvironment environment,
            IOptions<ImageStorageOptions> options,
            ILogger<ImageStoragePathService> logger)
        {
            this.environment = environment;
            this.options = options.Value;
            this.logger = logger;

            RootPath = ResolverRaizPersistente(
                this.options.RootPath);

            LegacyRootPath = Path.GetFullPath(
                Path.Combine(
                    environment.ContentRootPath,
                    "resources",
                    "uploads"));
        }

        public string RootPath { get; }

        public string LegacyRootPath { get; }

        public void Inicializar()
        {
            if (initialized)
                return;

            lock (initializationLock)
            {
                if (initialized)
                    return;

                Directory.CreateDirectory(RootPath);

                foreach (string carpeta in CarpetasConocidas)
                    Directory.CreateDirectory(ObtenerCarpeta(carpeta));

                if (options.MigrateLegacyFilesOnStartup)
                    MigrarArchivosAnteriores();

                ValidarEscritura();

                logger.LogInformation(
                    "Almacenamiento persistente de imágenes activo en {RootPath}.",
                    RootPath);

                initialized = true;
            }
        }

        public string ObtenerCarpeta(string carpetaRelativa)
        {
            string relativa = NormalizarRutaInterna(
                carpetaRelativa,
                permitirVacia: true);

            return CombinarDentroDeRaiz(relativa);
        }

        public string ResolverRutaPublica(string? rutaOUrl)
        {
            if (!TryResolverRutaPublica(
                    rutaOUrl,
                    out string rutaFisica))
            {
                throw new ArgumentException(
                    "La ruta de la imagen no pertenece al almacenamiento local.");
            }

            return rutaFisica;
        }

        public bool TryResolverRutaPublica(
            string? rutaOUrl,
            out string rutaFisica)
        {
            rutaFisica = string.Empty;

            if (string.IsNullOrWhiteSpace(rutaOUrl))
                return false;

            string valor;

            try
            {
                valor = Uri.UnescapeDataString(
                    rutaOUrl.Trim());
            }
            catch
            {
                return false;
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
                .TrimStart('/');

            int posicion = valor.IndexOf(
                PrefijoPublico,
                StringComparison.OrdinalIgnoreCase);

            if (posicion < 0)
                return false;

            string relativa = valor[
                (posicion + PrefijoPublico.Length)..];

            try
            {
                relativa = NormalizarRutaInterna(
                    relativa,
                    permitirVacia: false);

                rutaFisica = CombinarDentroDeRaiz(relativa);
                return true;
            }
            catch
            {
                rutaFisica = string.Empty;
                return false;
            }
        }

        public bool ArchivoExiste(string? rutaOUrl) =>
            TryResolverRutaPublica(
                rutaOUrl,
                out string rutaFisica) &&
            File.Exists(rutaFisica);

        private string ResolverRaizPersistente(
            string? configuredPath)
        {
            string valor = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(
                    "..",
                    "CONATRADEC_DATA",
                    "uploads")
                : configuredPath.Trim();

            return Path.GetFullPath(
                Path.IsPathRooted(valor)
                    ? valor
                    : Path.Combine(
                        environment.ContentRootPath,
                        valor));
        }

        private void MigrarArchivosAnteriores()
        {
            if (!Directory.Exists(LegacyRootPath) ||
                RutasIguales(LegacyRootPath, RootPath))
            {
                return;
            }

            int copiados = 0;
            int existentes = 0;

            foreach (string archivoOrigen in
                     Directory.EnumerateFiles(
                         LegacyRootPath,
                         "*",
                         SearchOption.AllDirectories))
            {
                string relativa = Path.GetRelativePath(
                    LegacyRootPath,
                    archivoOrigen);

                string archivoDestino = CombinarDentroDeRaiz(
                    relativa.Replace(
                        Path.DirectorySeparatorChar,
                        '/'));

                string? carpetaDestino =
                    Path.GetDirectoryName(archivoDestino);

                if (!string.IsNullOrWhiteSpace(carpetaDestino))
                    Directory.CreateDirectory(carpetaDestino);

                if (File.Exists(archivoDestino))
                {
                    existentes++;
                    continue;
                }

                File.Copy(
                    archivoOrigen,
                    archivoDestino,
                    overwrite: false);

                copiados++;
            }

            if (copiados > 0 || existentes > 0)
            {
                logger.LogInformation(
                    "Migración de imágenes completada. Copiados: {Copiados}; ya existentes: {Existentes}.",
                    copiados,
                    existentes);
            }
        }

        private void ValidarEscritura()
        {
            string archivoPrueba = Path.Combine(
                RootPath,
                $".write-test-{Guid.NewGuid():N}.tmp");

            try
            {
                File.WriteAllText(
                    archivoPrueba,
                    DateTime.UtcNow.ToString("O"));

                File.Delete(archivoPrueba);
            }
            catch (Exception ex)
            {
                string mensaje =
                    "La carpeta persistente de imágenes no permite escritura: " +
                    RootPath +
                    ". Revise la ruta configurada y los permisos del usuario de IIS.";

                if (options.FailIfNotWritable)
                {
                    throw new InvalidOperationException(
                        mensaje,
                        ex);
                }

                logger.LogError(
                    ex,
                    "{Message}",
                    mensaje);
            }
        }

        private string CombinarDentroDeRaiz(
            string rutaRelativa)
        {
            string rutaFisica = Path.GetFullPath(
                Path.Combine(
                    RootPath,
                    rutaRelativa.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

            string prefijoSeguro = RootPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            if (!rutaFisica.StartsWith(
                    prefijoSeguro,
                    StringComparison.OrdinalIgnoreCase) &&
                !RutasIguales(rutaFisica, RootPath))
            {
                throw new ArgumentException(
                    "La ruta solicitada está fuera del almacenamiento permitido.");
            }

            return rutaFisica;
        }

        private static string NormalizarRutaInterna(
            string? ruta,
            bool permitirVacia)
        {
            string valor = (ruta ?? string.Empty)
                .Replace('\\', '/')
                .Trim('/');

            if (string.IsNullOrWhiteSpace(valor))
            {
                if (permitirVacia)
                    return string.Empty;

                throw new ArgumentException(
                    "La ruta de la imagen está vacía.");
            }

            string[] segmentos = valor.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            if (segmentos.Any(segmento =>
                    segmento is "." or ".." ||
                    segmento.IndexOfAny(
                        Path.GetInvalidFileNameChars()) >= 0))
            {
                throw new ArgumentException(
                    "La ruta de la imagen no es válida.");
            }

            return string.Join('/', segmentos);
        }

        private static bool RutasIguales(
            string primera,
            string segunda) =>
            string.Equals(
                Path.GetFullPath(primera)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                Path.GetFullPath(segunda)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }
}
