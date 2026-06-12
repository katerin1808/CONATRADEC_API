using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("balanceNutricionalDetalle")]
    public class BalanceNutricionalDetalle
    {
        [Key]
        public int balanceNutricionalDetalleId { get; set; }

        public int balanceNutricionalId { get; set; }
        public int fuenteNutrientesId { get; set; }
        public int elementoQuimicosId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal requerimientoLibras { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal librasFuenteAnual { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal librasFuentePorAplicacion { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal quintalesAnuales { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal precioPorQuintal { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal subtotalFuente { get; set; }


        public bool activo { get; set; } = true;

        [ForeignKey(nameof(balanceNutricionalId))]
        public BalanceNutricional? balanceNutricional { get; set; }

        [ForeignKey(nameof(fuenteNutrientesId))]
        public FuenteNutriente? fuenteNutriente { get; set; }

        [ForeignKey(nameof(elementoQuimicosId))]
        public ElementoQuimico? elementoQuimico { get; set; }
    }
}
