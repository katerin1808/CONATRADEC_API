using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    public class Usuario
    {


        public int UsuarioId { get; set; }
        public string NombreUsuario { get; set; } = default!;
        public string ClaveHashUsuario { get; set; } = default!;
        public string? IdentificacionUsuario { get; set; }
        public string NombreCompletoUsuario { get; set; } = default!;
        public string CorreoUsuario { get; set; } = default!;
        public string? TelefonoUsuario { get; set; }
        public DateOnly? FechaNacimientoUsuario { get; set; }
        public bool Activo { get; set; } = true;

        public int RolId { get; set; }
        public Rol Rol { get; set; } = default!;

        public int ProcedenciaId { get; set; }
        public Procedencia Procedencia { get; set; } = default!;
    }
}
