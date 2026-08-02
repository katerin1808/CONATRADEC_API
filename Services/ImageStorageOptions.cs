namespace CONATRADEC_API.Services
{
    /// <summary>
    /// Configuración del almacenamiento físico de imágenes.
    /// Las URLs públicas continúan utilizando /resources/uploads/...
    /// aunque los archivos se almacenen fuera de la publicación.
    /// </summary>
    public sealed class ImageStorageOptions
    {
        public const string Seccion = "ImageStorage";

        /// <summary>
        /// Carpeta raíz que contendrá users, terrenos, noticias y álbumes.
        /// Puede ser absoluta o relativa a ContentRootPath.
        /// Si queda vacía se utiliza ../CONATRADEC_DATA/uploads.
        /// </summary>
        public string RootPath { get; set; } = string.Empty;

        /// <summary>
        /// Copia durante el inicio los archivos existentes desde
        /// resources/uploads hacia la carpeta persistente. Nunca reemplaza
        /// un archivo que ya exista en el destino.
        /// </summary>
        public bool MigrateLegacyFilesOnStartup { get; set; } = true;

        /// <summary>
        /// Hace que la API se detenga con un mensaje claro cuando la carpeta
        /// no puede escribirse. Evita iniciar aparentemente bien y fallar al
        /// momento de subir una imagen.
        /// </summary>
        public bool FailIfNotWritable { get; set; } = true;
    }
}
