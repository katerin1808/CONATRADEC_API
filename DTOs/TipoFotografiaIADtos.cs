using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class TipoFotografiaIARespuesta
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
    }

    public sealed class TipoFotografiaIACrearRequest
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

    public sealed class TipoFotografiaIAActualizarRequest
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
}
