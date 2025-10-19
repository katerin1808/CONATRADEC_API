using CONATRADEC_API.Models;
using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
 

    // --- Stream agrupado por rol ---
    public class RolLiteDto { public int rolId { get; set; } public string nombreRol { get; set; } = null!; }
    public class InterfazPermisoDto
    {
        public int permisoId { get; set; }
        public string nombrePermiso { get; set; } = null!;
        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }
    public class RolConPermisosDto
    {
        public RolLiteDto rol { get; set; } = null!;
        public List<InterfazPermisoDto> permisos { get; set; } = new();
      
    }

    public class RolConPermisoDto
    {
        public RolLiteDto rol { get; set; } = new RolLiteDto();
        public List<InterfazPermisoDto> permisos { get; set; } = new List<InterfazPermisoDto>();
    }

    public class RolDto
    {
        public int rolId { get; set; }
        public string nombreRol { get; set; } = string.Empty;
    }

    public class InterfazPermisosDto
    {
        public int permisoId { get; set; }
        public string nombrePermiso { get; set; } = string.Empty;

        // Acciones del permiso
        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }
  
}


