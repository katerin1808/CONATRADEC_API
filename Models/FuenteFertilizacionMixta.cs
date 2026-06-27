using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
   

        [Table("fuenteFertilizacionMixta")]
        public class FuenteFertilizacionMixta
        {
            [Key]
            public int fuenteFertilizacionMixtaId { get; set; }

            public int fuenteNutrientesId { get; set; }

            public bool activo { get; set; } = true;

            [ForeignKey(nameof(fuenteNutrientesId))]
            public FuenteNutriente? fuenteNutriente { get; set; }
        }
    }
