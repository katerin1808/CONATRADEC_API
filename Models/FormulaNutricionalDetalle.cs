using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("formulaNutricionalDetalle")]
    public class FormulaNutricionalDetalle
    {
        public int formulaNutricionalDetalleId { get; set; }

        public int formulaNutricionalId { get; set; }

        public int fuenteNutrientesId { get; set; }

        public int elementoQuimicosId { get; set; }

        public decimal libras { get; set; }

        public decimal qq { get; set; }

        public bool activo { get; set; } = true;


        [Column(TypeName = "decimal(18,4)")]
        public decimal requerimientoLibras { get; set; }

    

        [Column(TypeName = "decimal(18,4)")]
        public decimal precioPorQuintal { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal subtotalFuente { get; set; }

        public decimal onzasAnuales { get; set; }
        public decimal onzasPorAplicacion { get; set; }

        [ForeignKey(nameof(formulaNutricionalId))]
        public FormulaNutricional? formulaNutricional { get; set; }

        [ForeignKey(nameof(fuenteNutrientesId))]
        public FuenteNutriente? fuenteNutriente { get; set; }

        [ForeignKey(nameof(elementoQuimicosId))]
        public ElementoQuimico? elementoQuimico { get; set; }

        public ICollection<FormulaNutricionalAporte>? aportes { get; set; }
    }
}
