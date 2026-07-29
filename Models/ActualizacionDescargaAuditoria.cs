using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.Models
{
    public sealed class ActualizacionDescargaAuditoria
    {
        public long ActualizacionDescargaAuditoriaId { get; set; }

        public int? ActualizacionLlaveDescargaId { get; set; }

        public int? ActualizacionAplicacionId { get; set; }

        [MaxLength(64)]
        public string OperacionId { get; set; } = string.Empty;

        [MaxLength(30)]
        public string Resultado { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Detalle { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Plataforma { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Canal { get; set; } = string.Empty;

        [MaxLength(30)]
        public string VersionNombre { get; set; } = string.Empty;

        public long? VersionCodigo { get; set; }

        [MaxLength(260)]
        public string NombreArchivo { get; set; } = string.Empty;

        [MaxLength(80)]
        public string IpCliente { get; set; } = string.Empty;

        [MaxLength(500)]
        public string EncabezadoForwardedFor { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string AgenteUsuario { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Navegador { get; set; } = string.Empty;

        [MaxLength(100)]
        public string SistemaOperativo { get; set; } = string.Empty;

        [MaxLength(80)]
        public string TipoDispositivo { get; set; } = string.Empty;

        [MaxLength(100)]
        public string IdentificadorDispositivoWeb { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Destinatario { get; set; } = string.Empty;

        public int? UsuarioGeneradorId { get; set; }

        public DateTime FechaUtc { get; set; }
    }
}
