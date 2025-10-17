using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public class CargoCreateDto
    {
        [Required(ErrorMessage = "El nombre del cargo es obligatorio.")]
        [MaxLength(50, ErrorMessage = "El nombre del cargo no puede tener más de 50 caracteres.")]
        public string nombreCargo { get; set; } = null!;

        [MaxLength(500, ErrorMessage = "La descripción no puede tener más de 500 caracteres.")]
        public string? descripcionCargo { get; set; }
    }
}
