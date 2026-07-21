using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public class UsuarioCrearDto
    {
        [Required(ErrorMessage = "Ingrese el nombre de usuario.")]
        [MaxLength(100, ErrorMessage = "El nombre de usuario no puede superar 100 caracteres.")]
        public string nombreUsuario { get; set; } = default!;

        [Required(ErrorMessage = "Ingrese el nombre completo del usuario.")]
        [MaxLength(150, ErrorMessage = "El nombre completo no puede superar 150 caracteres.")]
        public string nombreCompletoUsuario { get; set; } = default!;

        [Required(ErrorMessage = "Ingrese el correo electrónico.")]
        [EmailAddress(ErrorMessage = "Ingrese un correo electrónico válido.")]
        [MaxLength(150, ErrorMessage = "El correo no puede superar 150 caracteres.")]
        public string correoUsuario { get; set; } = default!;

        [Required(ErrorMessage = "Ingrese el teléfono.")]
        [RegularExpression(@"^\d{8}$", ErrorMessage = "El teléfono debe contener exactamente 8 dígitos.")]
        public string? telefonoUsuario { get; set; }

        [Required(ErrorMessage = "Seleccione la fecha de nacimiento.")]
        public DateOnly? fechaNacimientoUsuario { get; set; }

        [Required]
        public bool esInterno { get; set; }

        public int? rolId { get; set; }
        public int? municipioId { get; set; }

        [Required(ErrorMessage = "Ingrese la identificación del usuario.")]
        [RegularExpression(
            @"^\d{3}-\d{6}-\d{4}[A-Za-z]$",
            ErrorMessage = "La identificación debe tener el formato 001-080701-1050R.")]
        [MaxLength(18)]
        public string identificacionUsuario { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingrese una contraseña.")]
        [RegularExpression(
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&.#_\-]).{8,}$",
            ErrorMessage = "La contraseña debe tener al menos 8 caracteres e incluir mayúscula, minúscula, número y símbolo.")]
        public string clave { get; set; } = default!;
    }

    public class UsuarioActualizarDto
    {
        [Required(ErrorMessage = "Ingrese el nombre completo del usuario.")]
        [MaxLength(150)]
        public string nombreCompletoUsuario { get; set; } = default!;

        [Required(ErrorMessage = "Ingrese el correo electrónico.")]
        [EmailAddress(ErrorMessage = "Ingrese un correo electrónico válido.")]
        [MaxLength(150)]
        public string correoUsuario { get; set; } = default!;

        [Required(ErrorMessage = "Ingrese el teléfono.")]
        [RegularExpression(@"^\d{8}$", ErrorMessage = "El teléfono debe contener exactamente 8 dígitos.")]
        public string? telefonoUsuario { get; set; }

        [Required(ErrorMessage = "Seleccione la fecha de nacimiento.")]
        public DateOnly? fechaNacimientoUsuario { get; set; }

        [Required]
        public bool esInterno { get; set; }

        public int? rolId { get; set; }

        [RegularExpression(
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&.#_\-]).{8,}$",
            ErrorMessage = "La nueva contraseña debe tener al menos 8 caracteres e incluir mayúscula, minúscula, número y símbolo.")]
        public string? nuevaClave { get; set; }

        public int? municipioId { get; set; }

        [Required(ErrorMessage = "Ingrese la identificación del usuario.")]
        [RegularExpression(
            @"^\d{3}-\d{6}-\d{4}[A-Za-z]$",
            ErrorMessage = "La identificación debe tener el formato 001-080701-1050R.")]
        [MaxLength(18)]
        public string identificacionUsuario { get; set; } = string.Empty;

        public bool? activo { get; set; }

        // Se deja opcional: la actualización de datos no debe fallar cuando el usuario no tiene imagen.
        [MaxLength(500)]
        public string? urlImagenUsuario { get; set; }
    }

    public class UsuarioReadDto
    {
        public int UsuarioId { get; set; }
        public string nombreUsuario { get; set; } = default!;
        public string nombreCompletoUsuario { get; set; } = default!;
        public string correoUsuario { get; set; } = default!;
        public string? telefonoUsuario { get; set; }
        public DateOnly? fechaNacimientoUsuario { get; set; }
        public string? identificacionUsuario { get; set; }
        public int rolId { get; set; }
        public int procedenciaId { get; set; }
        public int? municipioId { get; set; }
        public string rolNombre { get; set; } = string.Empty;
        public string procedenciaNombre { get; set; } = string.Empty;
        public bool esInterno { get; set; }
        public string? urlImagenUsuario { get; set; }
    }

    public class UsuarioActualizarClaveDto
    {
        [Required(ErrorMessage = "Ingrese la contraseña actual.")]
        public string claveActual { get; set; } = null!;

        [Required(ErrorMessage = "Ingrese la nueva contraseña.")]
        [RegularExpression(
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&.#_\-]).{8,}$",
            ErrorMessage = "La nueva contraseña debe tener al menos 8 caracteres e incluir mayúscula, minúscula, número y símbolo.")]
        public string nuevaClave { get; set; } = null!;
    }
}
