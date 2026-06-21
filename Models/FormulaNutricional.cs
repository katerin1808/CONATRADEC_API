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

            public ICollection<FormulaNutricionalDetalle>? detalles { get; set; }
        
    }

}