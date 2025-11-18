using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("fuenteNutrienteControlAplicacion", Schema = "dbo")]
    public class FuenteNutrienteControlAplicacion
    {
        [Key]
        public int fuenteNutrienteControlAplicacionId { get; set; }

        // DECIMAL(10,0) NOT NULL
        [Required]
        public decimal cantidadAplicado { get; set; }

        // DATE NOT NULL
        [Required]
        public DateOnly fechaAplicado { get; set; }

        // DECIMAL(10,0) NOT NULL
        [Required]
        public decimal cantidadPendiente { get; set; }

        // FK: puede ser NULL (según tu diagrama)
        public int? fuenteNutrientesId { get; set; }
        public FuenteNutriente? FuenteNutriente { get; set; }

        // FK: puede ser NULL (según tu diagrama)
        public int? controlAplicacionId { get; set; }
        public ControlAplicacion? ControlAplicacion { get; set; }
        public bool activo { get; set; } = true;
    }
}
