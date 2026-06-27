using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
  
        [Table("fertilizacionMixtaFuente")]
        public class FertilizacionMixtaFuente
        {
            [Key]
            public int fertilizacionMixtaFuenteId { get; set; }

            public int fertilizacionMixtaId { get; set; }

            public int fuenteNutrientesId { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal cantidadQq { get; set; }

            public bool activo { get; set; } = true;

            [ForeignKey(nameof(fertilizacionMixtaId))]
            public FertilizacionMixta? fertilizacionMixta { get; set; }

            [ForeignKey(nameof(fuenteNutrientesId))]
            public FuenteNutriente? fuenteNutriente { get; set; }
        }
    
}
