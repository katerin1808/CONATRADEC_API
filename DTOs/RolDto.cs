using System;
using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{

    public class RolCreateDto
    {
        [Required(ErrorMessage = "El nombre del rol es obligatorio")]
        [StringLength(50, ErrorMessage = "El nombre del rol no puede superar 50 caracteres")]
        public string nombreRol { get; set; } = null!;


        [DataType(DataType.MultilineText)]
        [Display(Name = "Descripción")]
        [MaxLength(500, ErrorMessage = "El campo {0} debe tener máximo {1} caractéres.")]
        public string descripcionRol { get; set; } = null!;


    }


    public class RolUpdateDto
    {

        [Required, MaxLength(50)]
        public string nombreRol { get; set; } = null!;

        [MaxLength(500)]
        public string descripcionRol { get; set; } = null!;

    }
}
