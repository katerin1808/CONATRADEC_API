using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("deficienciaNutricional", Schema = "dbo")]
    public class DeficienciaNutricional
    {
        [Key]
        public int deficienciaNutricionalId { get; set; }

        [Required, MaxLength(150)]
        public string urlfotoDeficienciaNutricional { get; set; } = null!;

        [Required, MaxLength(50)]
        public string nombreDeficienciaNutricional { get; set; } = null!;

        [Required, MaxLength(150)]
        public string descripcionDeficienciaNutricional { get; set; } = null!;

        [Required, MaxLength(150)]
        public string recomendacionDeficienciaNutricional { get; set; } = null!;

        public bool activo { get; set; }

        // ===== FK =====
        public int? UsuarioId { get; set; }
        public Usuario? Usuario { get; set; }
    }
}