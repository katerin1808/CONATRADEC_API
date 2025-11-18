using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("rangoNutrimental", Schema = "dbo")]
    public class RangoNutrimental
    {
        [Key]
        public int rangoNutrimentalId { get; set; }

        public int minimoRangoNutrimental { get; set; }
        public int maximoRangoNutrimental { get; set; }

        public bool activo { get; set; }

        // ======================
        // FK opcionales
        // ======================

        public int? unidadMedidaId { get; set; }
        public UnidadMedida UnidadMedida { get; set; } = null!;

        public int? elementoQuimicoId { get; set; }
        public ElementoQuimico ElementoQuimico { get; set; } = null!;
    }
}
