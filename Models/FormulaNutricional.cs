using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("formulaNutricional")]
    public class FormulaNutricional
    {

       
            public int formulaNutricionalId { get; set; }

            public string nombreFormula { get; set; } = string.Empty;

            public DateTime fechaCreacion { get; set; } = DateTime.Now;

            public decimal totalLibras { get; set; }

            public decimal mezclaTotalQq { get; set; }

            public bool activo { get; set; } = true;

            public int totalPlantas { get; set; }

            public int totalAplicaciones { get; set; }

            // El indicador pertenece al balance de fórmula. También se
            // conserva en fertilizacionMixta por compatibilidad con los
            // reportes y registros anteriores.
            public bool esComplementoFertilizacionMixta { get; set; }

           [Column(TypeName = "decimal(18,4)")]
           public decimal totalOnzas { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal dosisPlantaAnualOz { get; set; }
    
           [Column(TypeName = "decimal(18,4)")]
           public decimal dosisPlantaPorAplicacionOz { get; set; }

           [Column(TypeName = "decimal(18,4)")]
           public decimal precioTotalFormula { get; set; }

          [Column(TypeName = "decimal(18,4)")]
           public decimal precioPorAplicacion { get; set; }
 
           public int? terrenoId { get; set; }

          [ForeignKey(nameof(terrenoId))]
           public Terreno? terreno { get; set; }

        public int? analisisSueloCalculoId { get; set; }

        [ForeignKey(nameof(analisisSueloCalculoId))]
        public AnalisisSueloCalculo? analisisSueloCalculo { get; set; }


        public ICollection<FormulaNutricionalDetalle>? detalles { get; set; }
        
    }

}
