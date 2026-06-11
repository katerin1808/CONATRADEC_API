using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("formulaNutricional")]
    public class FormulaNutricional
    {
        
            [Key]
            public int formulaNutricionalId { get; set; }

            public string? nombreFormula { get; set; }

            public DateTime fechaCreacion { get; set; } = DateTime.Now;

            [Column(TypeName = "decimal(18,4)")]
            public decimal totalLibras { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal mezclaTotalQq { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal n { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal p { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal k { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal ca { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal mg { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal s { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal zn { get; set; }

            [Column(TypeName = "decimal(18,4)")]
            public decimal b { get; set; }

            public bool activo { get; set; } = true;

            public ICollection<FormulaNutricionalDetalle> detalles { get; set; }
                = new List<FormulaNutricionalDetalle>();
        }
    }

