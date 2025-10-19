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

    

}
