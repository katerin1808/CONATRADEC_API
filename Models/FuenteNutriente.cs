using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("fuenteNutriente", Schema = "dbo")]
    public class FuenteNutriente
    {
        [Key]
        public int fuenteNutrientesId { get; set; }

        [Required, MaxLength(150)]
        public string nombreNutriente { get; set; } = null!;

        [Required, MaxLength(500)]
        public string descripcionNutriente { get; set; } = null!;

        public decimal precioNutriente { get; set; }

        public bool activo { get; set; } = true;
    }
}
