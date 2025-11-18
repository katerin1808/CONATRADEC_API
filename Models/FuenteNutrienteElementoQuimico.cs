using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("fuenteNutrienteElementoQuimico", Schema = "dbo")]
    public class FuenteNutrienteElementoQuimico
    {
        [Key]
        public int fuenteNutrienteElementoQuimicoId { get; set; }

        // DECIMAL(10,0)
        [Required, MaxLength(10)]
        public decimal cantidadAporte { get; set; }

        // FK a fuenteNutriente
        public int? fuenteNutrientesId { get; set; }
        public FuenteNutriente? FuenteNutriente { get; set; }

        // FK a elementoQuimico
        public int? elementoQuimicosId { get; set; }
        public ElementoQuimico? ElementoQuimico { get; set; }

        public bool activo { get; set; }
    }
}