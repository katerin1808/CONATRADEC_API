using CONATRADEC_API.Models;
using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{

    // Rol “ligero”
    public class RolLiteDto
    {
        public int rolId { get; set; }
        public string nombreRol { get; set; } = string.Empty;
    }

    // Permiso con acciones
    public class InterfazPermisoDto
    {
        public int interfazId { get; set; }
        public string nombreIntefaz { get; set; } = string.Empty;

        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }

    // Rol con su lista de permisos (este es el que usa el controller)
    public class RolConPermisosDto
    {
        public RolLiteDto rol { get; set; } = new();
        public List<InterfazPermisoDto> interfaz { get; set; } = new();
    }

    // (Opcional) DTO para filtrar en el stream POST
    public class RolFiltroRequest
    {
        public int? rolId { get; set; }
        public string? nombreRol { get; set; }
        public bool incluirInactivosRol { get; set; } = false;
        public bool incluirInactivosInterfaz { get; set; } = false;
    }

    public class AgregarPermisoPorNombreRequest
    {
        public string nombreRol { get; set; } = string.Empty;
        public string nombreInterfaz { get; set; } = string.Empty;

        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }

}




