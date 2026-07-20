using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("fertilizacionMixta")]
    public class FertilizacionMixta
    {
        [Key]
        public int fertilizacionMixtaId { get; set; }

        public int analisisSueloCalculoId { get; set; }

        public DateTime fechaCalculo { get; set; } = DateTime.Now;

        public string? observacion { get; set; }

        // Permite diferenciar una fertilización mixta independiente de la que
        // descuenta sus aportes del balance comercial. El reporte usa este
        // indicador para generar el balance ajustado y el resumen económico.
        public bool esComplementoBalance { get; set; }

        public bool activo { get; set; } = true;

        [ForeignKey(nameof(analisisSueloCalculoId))]
        public AnalisisSueloCalculo? analisisSueloCalculo { get; set; }

        public ICollection<FertilizacionMixtaFuente>? fuentes { get; set; }

        public ICollection<FertilizacionMixtaDetalle>? detalles { get; set; }
       


    }
}
