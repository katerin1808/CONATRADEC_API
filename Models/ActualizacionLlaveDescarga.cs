using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.Models
{
    public sealed class ActualizacionLlaveDescarga
    {
        public int ActualizacionLlaveDescargaId { get; set; }

        [MaxLength(64)]
        public string HashLlave { get; set; } = string.Empty;

        [MaxLength(4)]
        public string UltimosCaracteres { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Plataforma { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Canal { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Estado { get; set; } = "ACTIVA";

        [MaxLength(200)]
        public string Destinatario { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Observacion { get; set; } = string.Empty;

        public int CantidadMaximaUsos { get; set; } = 1;

        public int CantidadUsos { get; set; }

        public int UsuarioCreacionId { get; set; }

        public int? UsuarioRevocacionId { get; set; }

        public DateTime FechaCreacionUtc { get; set; }

        public DateTime FechaExpiracionUtc { get; set; }

        public DateTime? FechaUltimoUsoUtc { get; set; }

        public DateTime? FechaRevocacionUtc { get; set; }

        public bool Activo { get; set; } = true;
    }
}
