using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.DTOs
{
    public class UsuarioDto
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int UsuarioId { get; set; }
        public string nombreUsuario { get; set; } = string.Empty;
        public string? telefonoUsuario { get; set; }
        public string? correoUsuario { get; set; }
        public bool? activo { get; set; }
        public int rolId { get; set; }
        public string nombreRol { get; set; } = string.Empty;

    }

    public class UsuarioListDto
    {
        public string nombreUsuario { get; set; } = string.Empty;
        public string telefonoUsuario { get; set; } = string.Empty;
        public string correoUsuario { get; set; } = string.Empty;
        public string nombreRol { get; set; } = string.Empty;
    }
    public class UsuarioCreateDto
    {
        [Required, StringLength(100)]
        public string nombreUsuario { get; set; } = string.Empty;


        [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
        [Required, StringLength(128, MinimumLength = 6)]
        public string clavePlano { get; set; } = string.Empty;

        [MaxLength(20)]
        [Phone] public string? telefonoUsuario { get; set; }
        [EmailAddress] public string? correoUsuario { get; set; }

        [Required]
        public int rolId { get; set; }
    }

    public class UsuarioUpdateDto
    {
        public string? telefonoUsuario { get; set; }
        public string? correoUsuario { get; set; }
        public bool activo { get; set; } = true;
        public int rolId { get; set; }
    }

    public class UsuarioPasswordDto
    {
        public string clavePlano { get; set; } = string.Empty;
    }

    // Login
    public class LoginRequest
    {
        public string nombreUsuario { get; set; } = string.Empty;
        public string clavePlano { get; set; } = string.Empty;
    }

    public class LoginResponse
    {
        public bool autenticado { get; set; }
        public int usuarioId { get; set; }
        public string nombreUsuario { get; set; } = string.Empty;
        public int rolId { get; set; }
        public string nombreRol { get; set; } = string.Empty;
        public string? token { get; set; } // si luego activas JWT
    }
}
