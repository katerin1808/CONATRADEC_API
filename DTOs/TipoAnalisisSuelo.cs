using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("tipoAnalisisSuelo", Schema = "dbo")]
    public class TipoAnalisisSuelo
    {
        [Key]
        public int tipoAnalisisSueloId { get; set; }

        [Required]
        [MaxLength(100)]
        public string nombreTipoAnalisisSuelo { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string descripcionTipoAnalisisSuelo { get; set; } = null!;

        public bool activo { get; set; } = true;
    }
}