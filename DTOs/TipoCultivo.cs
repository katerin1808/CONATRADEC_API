using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("tipoCultivo", Schema = "dbo")]
    public class TipoCultivo
    {
        [Key]
        public int tipoCultivoId { get; set; }

        [Required]
        [MaxLength(80)]
        public string nombreTipoCultivo { get; set; } = null!;

        [Required]
        [MaxLength(150)]
        public string descripcionTipoCultivo { get; set; } = null!;

        public bool activo { get; set; } = true;
    }
}