using CONATRADEC_API.Models;
using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public class RolPermisoCreateDto
    {
        public string nombreRol { get; set; } = null!;
        public string nombrePermiso { get; set; } = null!;
        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }

    // Actualizar flags usando nombres
    public class RolPermisoUpdateDto
    {
        public string nombreRol { get; set; } = null!;
        public string nombrePermiso { get; set; } = null!;
        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }

    // Read DTO (mismo que ya usas)
    public class RolPermisoReadDto
    {
        public int rolId { get; set; }
        public int permisoId { get; set; }
        public string nombreRol { get; set; } = null!;
        public string nombrePermiso { get; set; } = null!;
        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }

}
