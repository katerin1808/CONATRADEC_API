using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{

    [Table("Rol", Schema = "dbo")]
    public class Rol
    {
        [Key]
        public int rolId { get; set; } = 0;

        [MaxLength(50, ErrorMessage = "El campo {0} debe tener máximo {1} caractéres.")]
        [Required(ErrorMessage = "El campo {0} es obligatorio.")]
        public string nombreRol { get; set; } = "";

        [DataType(DataType.MultilineText)]
        [Display(Name = "Descripción")]
        [MaxLength(500, ErrorMessage = "El campo {0} debe tener máximo {1} caractéres.")]
        public string descripcionRol { get; set; } = "";
        public bool activo { get; set; } = true; // Valor por defecto al crear


        public ICollection<RolInteraz> rolInteraz { get; set; } = new List<RolInteraz>();
        public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
    }

}



