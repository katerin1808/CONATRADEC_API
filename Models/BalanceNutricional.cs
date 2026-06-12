using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("balanceNutricional")]

    public class BalanceNutricional
    {
        [Key]
        public int balanceNutricionalId { get; set; }

        public string? nombreFormula { get; set; }

        public DateTime fechaCreacion { get; set; } = DateTime.Now;

        public int totalPlantas { get; set; }
        public int totalAplicaciones { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal totalLibras { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal totalOnzas { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal onzasPorPlantaAnual { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal onzasPorPlantaPorAplicacion { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal totalMezclaQq { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal precioTotalFormula { get; set; }

        public bool activo { get; set; } = true;

        public ICollection<BalanceNutricionalDetalle> detalles { get; set; }
            = new List<BalanceNutricionalDetalle>();
    }
}

