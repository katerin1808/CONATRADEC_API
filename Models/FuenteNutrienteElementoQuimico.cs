using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{

    [Table("fuenteNutrienteElementoQuimico")]
    public class FuenteNutrienteElementoQuimico
    {
        [Key]
        public int fuenteNutrienteElementoQuimicoId { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal cantidadAporte { get; set; }

        public bool activo { get; set; } = true;

        public int fuenteNutrientesId { get; set; }

        public int elementoQuimicosId { get; set; }

        [ForeignKey("fuenteNutrientesId")]
        public FuenteNutriente? fuenteNutriente { get; set; }

        [ForeignKey("elementoQuimicosId")]
        public ElementoQuimico? elementoQuimico { get; set; }
    }
}


