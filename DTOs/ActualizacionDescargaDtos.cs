using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class CrearLlaveDescargaDto
    {
        [Required]
        [MaxLength(20)]
        public string Plataforma { get; set; } = "ANDROID";

        [Required]
        [MaxLength(20)]
        public string Canal { get; set; } = "PRODUCCION";

        [Required]
        [MaxLength(200)]
        public string Destinatario { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Observacion { get; set; } = string.Empty;

        [Range(1, 100)]
        public int CantidadMaximaUsos { get; set; } = 1;

        [Range(1, 8760)]
        public int? VigenciaHoras { get; set; } = 24;

        public DateTime? FechaExpiracionUtc { get; set; }
    }

    public sealed class LlaveDescargaCreadaDto
    {
        public int ActualizacionLlaveDescargaId { get; set; }
        public string Llave { get; set; } = string.Empty;
        public string LlaveEnmascarada { get; set; } = string.Empty;
        public string Plataforma { get; set; } = string.Empty;
        public string Canal { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public string Destinatario { get; set; } = string.Empty;
        public string Observacion { get; set; } = string.Empty;
        public int CantidadMaximaUsos { get; set; }
        public int CantidadUsos { get; set; }
        public int UsuarioCreacionId { get; set; }
        public DateTime FechaCreacionUtc { get; set; }
        public DateTime FechaExpiracionUtc { get; set; }
    }

    public sealed class LlaveDescargaListadoDto
    {
        public int ActualizacionLlaveDescargaId { get; set; }
        public string LlaveEnmascarada { get; set; } = string.Empty;
        public string Plataforma { get; set; } = string.Empty;
        public string Canal { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public string Destinatario { get; set; } = string.Empty;
        public string Observacion { get; set; } = string.Empty;
        public int CantidadMaximaUsos { get; set; }
        public int CantidadUsos { get; set; }
        public int UsuarioCreacionId { get; set; }
        public int? UsuarioRevocacionId { get; set; }
        public DateTime FechaCreacionUtc { get; set; }
        public DateTime FechaExpiracionUtc { get; set; }
        public DateTime? FechaUltimoUsoUtc { get; set; }
        public DateTime? FechaRevocacionUtc { get; set; }
    }

    public class ValidarLlaveDescargaDto
    {
        [Required]
        [MaxLength(40)]
        public string Llave { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Plataforma { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Canal { get; set; } = "PRODUCCION";
    }

    public sealed class ValidarLlaveFormularioDto : ValidarLlaveDescargaDto
    {
        [MaxLength(1000)]
        public string UrlRetorno { get; set; } = string.Empty;
    }

    public sealed class DescargaAutorizadaDto
    {
        public int ActualizacionAplicacionId { get; set; }
        public string Plataforma { get; set; } = string.Empty;
        public string Canal { get; set; } = string.Empty;
        public string VersionNombre { get; set; } = string.Empty;
        public long VersionCodigo { get; set; }
        public string NotasVersion { get; set; } = string.Empty;
        public string NombreArchivo { get; set; } = string.Empty;
        public long TamanoBytes { get; set; }
        public string HashSha256 { get; set; } = string.Empty;
        public string UrlDescarga { get; set; } = string.Empty;
        public DateTime FechaExpiracionPermisoUtc { get; set; }
    }

    public sealed class DescargaPortalDto
    {
        public int ActualizacionAplicacionId { get; set; }
        public string Plataforma { get; set; } = string.Empty;
        public string Canal { get; set; } = string.Empty;
        public string VersionNombre { get; set; } = string.Empty;
        public long VersionCodigo { get; set; }
        public string NotasVersion { get; set; } = string.Empty;
        public string NombreArchivo { get; set; } = string.Empty;
        public long TamanoBytes { get; set; }
        public string HashSha256 { get; set; } = string.Empty;
        public DateTime? FechaPublicacionUtc { get; set; }
    }

    public sealed class AuditoriaDescargaDto
    {
        public long ActualizacionDescargaAuditoriaId { get; set; }
        public int? ActualizacionLlaveDescargaId { get; set; }
        public int? ActualizacionAplicacionId { get; set; }
        public string Resultado { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;
        public string Plataforma { get; set; } = string.Empty;
        public string Canal { get; set; } = string.Empty;
        public string VersionNombre { get; set; } = string.Empty;
        public long? VersionCodigo { get; set; }
        public string NombreArchivo { get; set; } = string.Empty;
        public string IpCliente { get; set; } = string.Empty;
        public string Navegador { get; set; } = string.Empty;
        public string SistemaOperativo { get; set; } = string.Empty;
        public string TipoDispositivo { get; set; } = string.Empty;
        public string IdentificadorDispositivoWeb { get; set; } = string.Empty;
        public string Destinatario { get; set; } = string.Empty;
        public int? UsuarioGeneradorId { get; set; }
        public DateTime FechaUtc { get; set; }
    }
}
