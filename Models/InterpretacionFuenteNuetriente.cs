using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("interpretacionFuenteNutriente", Schema = "dbo")]
    public class InterpretacionFuenteNutriente
    {
        [Key]
        public int interpretacionFuenteNutrienteId { get; set; }

        // DECIMAL(10,0) -> decimal con precisión 10
        [Required, MaxLength(10)]
        public decimal? precioHistorico { get; set; }

        // FK a interpretacion
        public int? interpretacionId { get; set; }
        public Interpretacion? Interpretacion { get; set; }

        // FK a fuenteNutriente
        public int? fuenteNutrientesId { get; set; }
        public FuenteNutriente? FuenteNutriente { get; set; }
        public bool activo { get; set; }
    }
}