using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    public class Usuario
    {


        [Key]
        
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int UsuarioId { get; set; }

        [Required, MaxLength(100)]
        public string nombreUsuario { get; set; } = string.Empty;

        // Guardaremos: PBKDF2$<iter>$<saltB64>$<hashB64>
        [Required, MaxLength(400)]
        public string claveHashUsuario { get; set; } = string.Empty;

        [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
        [MaxLength(20)]
        public string? telefonoUsuario { get; set; }

        [MaxLength(200)]
        public string? correoUsuario { get; set; }

        public bool activo { get; set; } = true;

        // FK
        public int rolId { get; set; }
        [ForeignKey(nameof(rolId))]
        public Rol Rol { get; set; } = null!;
    }
}
