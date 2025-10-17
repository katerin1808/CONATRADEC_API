using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("Cargo", Schema = "dbo")]
    public class Cargo
    {
        [Key]
        public int cargoId { get; set; }

        [Required, MaxLength(50)]
        public string nombreCargo { get; set; } = null!;

        [MaxLength(500)]
        public string descripcionCargo { get; set; } = null!;

        public bool activo { get; set; } = true;
    }
}
