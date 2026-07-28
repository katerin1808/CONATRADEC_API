using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("usuario", Schema = "dbo")]
    public class Usuario
    {
        [Key]
        public int UsuarioId { get; set; }

        [Required, MaxLength(100)]
        public string nombreUsuario { get; set; } = default!;

        // Formato: PBKDF2$iteraciones$Base64(salt)$Base64(hash)
        [Required, MaxLength(512)]
        public string claveHashUsuario { get; set; } = default!;

        [MaxLength(50)]
        public string? identificacionUsuario { get; set; }

        [Required, MaxLength(150)]
        public string nombreCompletoUsuario { get; set; } = default!;

        [Required, MaxLength(150)]
        public string correoUsuario { get; set; } = default!;

        [MaxLength(25)]
        public string? telefonoUsuario { get; set; }

        public DateOnly? fechaNacimientoUsuario { get; set; }

        public bool activo { get; set; } = true;

        [MaxLength(500)]
        public string? urlImagenUsuario { get; set; }

        /// <summary>
        /// Se incrementa al cambiar el rol o los permisos asociados al rol.
        /// Los clientes comparan este valor para invalidar sesiones antiguas.
        /// </summary>
        public int versionSesion { get; set; } = 1;

        // ===== Relaciones (FK) =====
        public int rolId { get; set; }
        public Rol Rol { get; set; } = default!;

        public int procedenciaId { get; set; }
        public Procedencia Procedencia { get; set; } = default!;

        public int? municipioId { get; set; }
        public Municipio? Municipio { get; set; }

        /// <summary>
        /// DTO usado para iniciar sesión (login).
        /// </summary>
        public class UsuarioLoginDto
        {
            [Required(ErrorMessage = "Debe ingresar el nombre de usuario o correo.")]
            public string usuarioOEmail { get; set; } = default!;

            [Required(ErrorMessage = "Debe ingresar la contraseña.")]
            [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
            public string clave { get; set; } = default!;
        }
    }
}
