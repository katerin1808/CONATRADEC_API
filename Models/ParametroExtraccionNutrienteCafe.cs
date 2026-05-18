using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("parametroExtraccionNutrienteCafe", Schema = "dbo")]
    public class ParametroExtraccionNutrienteCafe
    {
        [Key]
        public int parametroExtraccionNutrienteCafeId { get; set; }

        public int elementoQuimicosId { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal cantidadExtraidaPorQQOro { get; set; }

        [Required]
        [MaxLength(150)]
        public string descripcionParametro { get; set; } = null!;

        public bool activo { get; set; } = true;

        public ElementoQuimico ElementoQuimico { get; set; } = null!;
    }
}