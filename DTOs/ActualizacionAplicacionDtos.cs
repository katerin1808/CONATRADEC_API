using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class ActualizacionSubirDto
    {
        [Required]
        public IFormFile Archivo { get; set; } = null!;

        [Required]
        [MaxLength(20)]
        public string Plataforma { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Canal { get; set; } = "PRODUCCION";

        [Required]
        [MaxLength(30)]
        public string VersionNombre { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long VersionCodigo { get; set; }

        [MaxLength(4000)]
        public string NotasVersion { get; set; } = string.Empty;

        public bool Obligatoria { get; set; }

        [Range(1, long.MaxValue)]
        public long? VersionMinimaCodigo { get; set; }
    }

    public sealed class ActualizacionConfiguracionDto
    {
        [MaxLength(4000)]
        public string NotasVersion { get; set; } = string.Empty;

        public bool Obligatoria { get; set; }

        [Range(1, long.MaxValue)]
        public long? VersionMinimaCodigo { get; set; }
    }

    public sealed class ActualizacionDisponibleDto
    {
        public int ActualizacionAplicacionId { get; set; }
        public string Plataforma { get; set; } = string.Empty;
        public string Canal { get; set; } = string.Empty;
        public string VersionNombre { get; set; } = string.Empty;
        public long VersionCodigo { get; set; }
        public string NotasVersion { get; set; } = string.Empty;
        public bool Obligatoria { get; set; }
        public long? VersionMinimaCodigo { get; set; }
        public string NombreArchivo { get; set; } = string.Empty;
        public string TipoContenido { get; set; } = string.Empty;
        public long TamanoBytes { get; set; }
        public string HashSha256 { get; set; } = string.Empty;
        public string UrlDescarga { get; set; } = string.Empty;
        public DateTime? FechaPublicacionUtc { get; set; }
    }

    public sealed class ActualizacionAdministracionDto
    {
        public int ActualizacionAplicacionId { get; set; }
        public string Plataforma { get; set; } = string.Empty;
        public string Canal { get; set; } = string.Empty;
        public string VersionNombre { get; set; } = string.Empty;
        public long VersionCodigo { get; set; }
        public string NotasVersion { get; set; } = string.Empty;
        public bool Obligatoria { get; set; }
        public long? VersionMinimaCodigo { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string NombreArchivo { get; set; } = string.Empty;
        public string TipoContenido { get; set; } = string.Empty;
        public long TamanoBytes { get; set; }
        public string HashSha256 { get; set; } = string.Empty;
        public int UsuarioCreacionId { get; set; }
        public int UsuarioUltimaModificacionId { get; set; }
        public DateTime FechaCreacionUtc { get; set; }
        public DateTime FechaUltimaModificacionUtc { get; set; }
        public DateTime? FechaPublicacionUtc { get; set; }
        public string UrlDescarga { get; set; } = string.Empty;
    }

    public sealed class SiguienteVersionDto
    {
        public string Plataforma { get; set; } = string.Empty;
        public string Canal { get; set; } = string.Empty;
        public string UltimaVersionNombre { get; set; } = string.Empty;
        public long UltimaVersionCodigo { get; set; }
        public string SiguienteVersionNombre { get; set; } = string.Empty;
        public long SiguienteVersionCodigo { get; set; }
    }
}
