using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("elementoQuimico", Schema = "dbo")]
    public class ElementoQuimico
    {
        [Key]
        public int elementoQuimicosId { get; set; }

        [Required, MaxLength(10)] // ajusta según el tamaño real de tu CHAR en la BD
        public string simboloElementoQuimico { get; set; } = null!;

        [Required, MaxLength(80)]
        public string nombreElementoQuimico { get; set; } = null!;

        // Ajustamos decimal (ej: DECIMAL(10,2))
        public decimal pesoEquivalentEelementoQuimico { get; set; }

        public bool activo { get; set; } = true;
    }
}
