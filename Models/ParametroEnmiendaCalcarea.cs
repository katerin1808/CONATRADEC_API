using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("parametroEnmiendaCalcarea", Schema = "dbo")]
    public class ParametroEnmiendaCalcarea
    {
        [Key]
        public int parametroEnmiendaCalcareaId { get; set; }

        public int fuenteNutrientesId { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal saturacionBasesDeseada { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal prnt { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal factorTonHaALbHa { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal factorHaAMz { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal factorTonHaAKgHa { get; set; }

        [Required]
        [MaxLength(200)]
        public string descripcionParametro { get; set; } = null!;

        public bool activo { get; set; } = true;

        public FuenteNutriente FuenteNutriente { get; set; } = null!;
    }
}