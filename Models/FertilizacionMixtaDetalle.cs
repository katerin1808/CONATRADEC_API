using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
   
        [Table("fertilizacionMixtaDetalle")]
        public class FertilizacionMixtaDetalle
        {
            [Key]
            public int fertilizacionMixtaDetalleId { get; set; }

            public int fertilizacionMixtaId { get; set; }

            public int elementoQuimicosId { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal requerimientoOriginal { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal aporteOrganico { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal diferencia { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal deficit { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal sobrante { get; set; }

            public bool activo { get; set; } = true;

            [ForeignKey(nameof(fertilizacionMixtaId))]
            public FertilizacionMixta? fertilizacionMixta { get; set; }

            [ForeignKey(nameof(elementoQuimicosId))]
            public ElementoQuimico? elementoQuimico { get; set; }
        }
}
