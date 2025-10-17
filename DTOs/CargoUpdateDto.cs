using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public class CargoUpdateDto
    {

        [Required, MaxLength(50)]
        public string nombreCargo { get; set; } = null!;

        [MaxLength(500)]
        public string descripcionCargo { get; set; } = null!;
    }
}
