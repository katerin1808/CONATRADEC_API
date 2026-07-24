using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class CategoriaPublicacionGuardarDto
    {
        [Required(ErrorMessage = "El nombre del tipo de publicación es obligatorio.")]
        [MaxLength(80, ErrorMessage = "El nombre puede contener como máximo 80 caracteres.")]
        public string nombreCategoriaPublicacion { get; set; } = string.Empty;

        [MaxLength(250, ErrorMessage = "La descripción puede contener como máximo 250 caracteres.")]
        public string descripcionCategoriaPublicacion { get; set; } = string.Empty;

        [Required(ErrorMessage = "El color es obligatorio.")]
        [RegularExpression(
            "^#[0-9A-Fa-f]{6}$",
            ErrorMessage = "El color debe tener el formato hexadecimal #RRGGBB.")]
        public string colorHex { get; set; } = "#3B655B";

        [Range(0, 9999, ErrorMessage = "El orden debe estar entre 0 y 9999.")]
        public int orden { get; set; }
    }

    public sealed class CategoriaPublicacionEstadoDto
    {
        public bool activo { get; set; }
    }
}
