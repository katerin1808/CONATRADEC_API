using CONATRADEC_API.Models;
using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{


    // ============================================================
    //  Rol “ligero”
    // ============================================================
    public class RolLiteDto
    {
        public int rolId { get; set; }

        [Required(ErrorMessage = "El nombre del rol es requerido.")]
        public string nombreRol { get; set; } = string.Empty;
    }

    // ============================================================
    //  Interfaz con acciones (antes llamado Permiso)
    // ============================================================
    public class InterfazPermisoDto
    {
        public int interfazId { get; set; }

        [Required(ErrorMessage = "El nombre de la interfaz es requerido.")]
        public string nombreInterfaz { get; set; } = string.Empty;

        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }

    // ============================================================
    //  Rol con su lista de interfaz (usado por el controller)
    // ============================================================
    public class RolConInterfazDto
    {
        [Required]
        public RolLiteDto rol { get; set; } = new();

        [Required]
        public List<InterfazPermisoDto> interfaz { get; set; } = new();
    }

    // ============================================================
    //  DTO opcional para filtrar en el stream POST
    // ============================================================
    public class RolFiltroRequest
    {
        public int? rolId { get; set; }
        public string? nombreRol { get; set; }
        public bool incluirInactivosRol { get; set; } = false;
        public bool incluirInactivosInterfaz { get; set; } = false;
    }

    // ============================================================
    //  DTO para agregar o actualizar por nombre de Rol e Interfaz
    // ============================================================
    public class AgregarInterfazPorNombreRequest
    {
        [Required(ErrorMessage = "El nombre del rol es requerido.")]
        public string nombreRol { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre de la interfaz es requerido.")]
        public string nombreInterfaz { get; set; } = string.Empty;

        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }

}




