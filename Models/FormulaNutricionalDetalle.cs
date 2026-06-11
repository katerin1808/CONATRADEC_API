using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("formulaNutricionalDetalle")]
    public class FormulaNutricionalDetalle
    {
        [Key]
        public int formulaNutricionalDetalleId { get; set; }

        public int formulaNutricionalId { get; set; }
        public int fuenteNutrientesId { get; set; }
        public int elementoQuimicosId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal libras { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal qq { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal aporteN { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal aporteP { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal aporteK { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal aporteCa { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal aporteMg { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal aporteS { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal aporteZn { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal aporteB { get; set; }

        public bool activo { get; set; } = true;

        [ForeignKey(nameof(formulaNutricionalId))]
        public FormulaNutricional? formulaNutricional { get; set; }

        [ForeignKey(nameof(fuenteNutrientesId))]
        public FuenteNutriente? fuenteNutriente { get; set; }

        [ForeignKey(nameof(elementoQuimicosId))]
        public ElementoQuimico? elementoQuimico { get; set; }
    }
}
