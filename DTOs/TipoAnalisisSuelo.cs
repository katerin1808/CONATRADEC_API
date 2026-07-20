using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("tipoAnalisisSuelo", Schema = "dbo")]
    public class TipoAnalisisSuelo
    {
        [Key]
        public int tipoAnalisisSueloId { get; set; }

        [Required]
        [MaxLength(100)]
        public string nombreTipoAnalisisSuelo { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string descripcionTipoAnalisisSuelo { get; set; } = null!;

        public bool activo { get; set; } = true;
    }

    public class CrearTipoAnalisisSueloDto
    {
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [MaxLength(100)]
        public string nombreTipoAnalisisSuelo { get; set; } = string.Empty;

        [Required(ErrorMessage = "La descripción es obligatoria.")]
        [MaxLength(200)]
        public string descripcionTipoAnalisisSuelo { get; set; } = string.Empty;
    }

    // DTO para actualizar:
    // No contiene ID ni activo.
    public class ActualizarTipoAnalisisSueloDto
    {
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [MaxLength(100)]
        public string nombreTipoAnalisisSuelo { get; set; } = string.Empty;

        [Required(ErrorMessage = "La descripción es obligatoria.")]
        [MaxLength(200)]
        public string descripcionTipoAnalisisSuelo { get; set; } = string.Empty;
    }
}