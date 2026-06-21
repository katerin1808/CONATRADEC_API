using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("formulaNutricionalAporte")]
    public class FormulaNutricionalAporte
    {
        public int formulaNutricionalAporteId { get; set; }

        public int formulaNutricionalDetalleId { get; set; }

        public int elementoQuimicosId { get; set; }

        public decimal valor { get; set; }

        public bool activo { get; set; } = true;

        [ForeignKey(nameof(formulaNutricionalDetalleId))]
        public FormulaNutricionalDetalle? formulaNutricionalDetalle { get; set; }

        [ForeignKey(nameof(elementoQuimicosId))]
        public ElementoQuimico? elementoQuimico { get; set; }
    }
}
