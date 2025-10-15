using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public class RolUpdateDto
    {

        [Required, MaxLength(50)]
        public string nombreRol { get; set; } = null!;

        [MaxLength(500)]
        public string descripcionRol { get; set; } = null!;

    }
}
