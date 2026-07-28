using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.Models
{
    public sealed class ActualizacionAplicacion
    {
        public int ActualizacionAplicacionId { get; set; }

        [MaxLength(20)]
        public string Plataforma { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Canal { get; set; } = string.Empty;

        [MaxLength(30)]
        public string VersionNombre { get; set; } = string.Empty;

        public long VersionCodigo { get; set; }

        [MaxLength(4000)]
        public string NotasVersion { get; set; } = string.Empty;

        public bool Obligatoria { get; set; }

        public long? VersionMinimaCodigo { get; set; }

        [MaxLength(20)]
        public string Estado { get; set; } = string.Empty;

        [MaxLength(260)]
        public string NombreArchivo { get; set; } = string.Empty;

        [MaxLength(260)]
        public string NombreArchivoAlmacenado { get; set; } = string.Empty;

        [MaxLength(700)]
        public string RutaArchivo { get; set; } = string.Empty;

        [MaxLength(150)]
        public string TipoContenido { get; set; } = string.Empty;

        public long TamanoBytes { get; set; }

        [MaxLength(64)]
        public string HashSha256 { get; set; } = string.Empty;

        public int UsuarioCreacionId { get; set; }

        public int UsuarioUltimaModificacionId { get; set; }

        public DateTime FechaCreacionUtc { get; set; }

        public DateTime FechaUltimaModificacionUtc { get; set; }

        public DateTime? FechaPublicacionUtc { get; set; }

        public bool Activo { get; set; }
    }
}
