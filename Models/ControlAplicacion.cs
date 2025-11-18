using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("controlAplicacion", Schema = "dbo")]
    public class ControlAplicacion
    {
        [Key]
        public int controlAplicacionId { get; set; }

        public DateOnly fechaControlAplicacion { get; set; }

        public short numeroControlAplicacion { get; set; }

        public bool activo { get; set; }

        // ===== FK =====
        public int? interpretacionId { get; set; }
        public Interpretacion? Interpretacion { get; set; }
    }
}