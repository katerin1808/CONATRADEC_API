using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class TipoFotografiaIAV2Respuesta
    {
        public int TipoFotografiaIAId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string InstruccionIA { get; set; } = string.Empty;
        public int Orden { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCreacionUtc { get; set; }
        public DateTime FechaModificacionUtc { get; set; }
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class TipoFotografiaIAV2CrearRequest
    {
        [Required, MaxLength(40)]
        public string Codigo { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Descripcion { get; set; } = string.Empty;

        [Required, MaxLength(2000)]
        public string InstruccionIA { get; set; } = string.Empty;

        [Range(1, 999)]
        public int Orden { get; set; } = 1;
    }

    public sealed class TipoFotografiaIAV2ActualizarRequest
    {
        [Required, MaxLength(40)]
        public string Codigo { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Descripcion { get; set; } = string.Empty;

        [Required, MaxLength(2000)]
        public string InstruccionIA { get; set; } = string.Empty;

        [Range(1, 999)]
        public int Orden { get; set; } = 1;

        [Required]
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class TipoFotografiaIAV2EstadoRequest
    {
        [Required]
        public string RowVersion { get; set; } = string.Empty;
    }
}
