using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.DTOs
{
    public class UsuarioCrearDto
    {
        [Required, MaxLength(100)]
        public string nombreUsuario { get; set; } = default!;

        [Required, MaxLength(150)]
        public string nombreCompletoUsuario { get; set; } = default!;

        [Required, EmailAddress, MaxLength(150)]
        public string correoUsuario { get; set; } = default!;

        [MaxLength(25)]
        public string? telefonoUsuario { get; set; }

        public DateOnly? fechaNacimientoUsuario { get; set; }

        // === Lógica Interno/Externo controlada por backend ===
        [Required]
        public bool esInterno { get; set; }

        // Solo válido si es interno; para externos se ignora y se fuerza "Invitado"
        public int? rolId { get; set; }

        // Relaciones opcionales
        public int? municipioId { get; set; }

        [MaxLength(50)]
        public string identificacionUsuario { get; set; } = "";

        // Contraseña en texto plano
        [Required, MinLength(6)]
        public string clave { get; set; } = default!;

        // 🔹 NUEVO (obligatorio)
        /*[Required, MaxLength(300)]
        public string urlImagenUsuario { get; set; } = default!;*/
    }
    public class UsuarioActualizarDto
    {
        [Required, MaxLength(150)]
        public string nombreCompletoUsuario { get; set; } = default!;

        [Required, EmailAddress, MaxLength(150)]
        public string correoUsuario { get; set; } = default!;

        [MaxLength(25)]
        public string? telefonoUsuario { get; set; }

        public DateOnly? fechaNacimientoUsuario { get; set; }

        // Cambiar tipo (interno/externo)
        [Required]
        public bool esInterno { get; set; }

        // Si es interno y deseas cambiar rol
        public int? rolId { get; set; }

        // Cambiar contraseña (opcional)
        public string? nuevaClave { get; set; }

        public int? municipioId { get; set; }

        [MaxLength(50)]
        public string identificacionUsuario { get; set; } = "";

        public bool? activo { get; set; }

        // 🔹 NUEVO (obligatorio)
        [Required, MaxLength(500)]
        public string urlImagenUsuario { get; set; } = default!;
    }

    public class UsuarioReadDto
    {
        public int UsuarioId { get; set; }
        public string nombreUsuario { get; set; } = default!;
        public string nombreCompletoUsuario { get; set; } = default!;
        public string correoUsuario { get; set; } = default!;
        public string? telefonoUsuario { get; set; }
        public DateOnly? fechaNacimientoUsuario { get; set; }
       
        [MaxLength(50)]
        public string? identificacionUsuario { get; set; }

        public int rolId { get; set; }
        public int procedenciaId { get; set; }
        public int? municipioId { get; set; }

        // Información derivada para front
        public string rolNombre { get; set; } = string.Empty;
        public string procedenciaNombre { get; set; } = string.Empty;
        public bool esInterno { get; set; }

        public string? urlImagenUsuario { get; set; } = null;
    }
}
