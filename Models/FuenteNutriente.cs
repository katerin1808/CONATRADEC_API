using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("fuenteNutriente")]
    public class FuenteNutriente
    {
        [Key]
        public int fuenteNutrientesId { get; set; }

        [Required]
        [StringLength(100)]
        public string nombreNutriente { get; set; } = string.Empty;

        [StringLength(250)]
        public string descripcionNutriente { get; set; } = string.Empty;

        [Column(TypeName = "decimal(10,2)")]
        public decimal precioNutriente { get; set; }

        public bool activo { get; set; } = true;

        public ICollection<FuenteNutrienteElementoQuimico> fuenteNutrienteElementoQuimico { get; set; }
            = new List<FuenteNutrienteElementoQuimico>();
    }
}
