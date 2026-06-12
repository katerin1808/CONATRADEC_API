using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
 
        [Table("enmiendaCalcarea", Schema = "dbo")]
        public class EnmiendaCalcarea
        {
            [Key]
            public int enmiendaCalcareaId { get; set; }

            public string nombreAnalisis { get; set; } = string.Empty;

            public int fuenteNutrientesId { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal ph { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal ca { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal mg { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal k { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal acidezTotal { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal saturacionDeseada { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal prnt { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal sumaBases { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal cice { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal saturacionActual { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal necesidadEncaladoTonHa { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal necesidadEncaladoKgHa { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal necesidadEncaladoLbHa { get; set; }

            public DateTime fechaCreacion { get; set; } = DateTime.Now;

            public bool activo { get; set; } = true;

            [ForeignKey(nameof(fuenteNutrientesId))]
            public FuenteNutriente? fuenteNutriente { get; set; }
        }
    }