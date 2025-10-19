using CONATRADEC_API.Models;
using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    // --- Matriz plana (SELECT exacto) ---
    public class MatrizRowDto
    {
        public int rolId { get; set; }
        public string nombreRol { get; set; } = null!;
        public bool rolActivo { get; set; }
        public int permisoId { get; set; }
        public string nombrePermiso { get; set; } = null!;
        public bool permisoActivo { get; set; }
        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }

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

    // --- CRUD / Upsert por NOMBRE (sin IDs) ---
    public class RolPermisoCreateByNameDto
    {
        public string nombreRol { get; set; } = null!;
        public string nombrePermiso { get; set; } = null!;
        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }
    public class RolPermisoUpdateByNameDto : RolPermisoCreateByNameDto { }

    public class BulkUpsertByNameDto
    {
        public string nombreRol { get; set; } = null!;
        public List<RolPermisoCreateByNameDto> items { get; set; } = new();
    }

    // --- Vista por INTERFAZ (como tu imagen) ---
    public class RolFlagsPorInterfazDto
    {
        public int rolId { get; set; }
        public string nombreRol { get; set; } = null!;
        public bool rolActivo { get; set; }
        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }

    public class BulkUpsertPorInterfazDto
    {
        public string nombrePermiso { get; set; } = null!;
        public List<ItemRolFlagsDto> roles { get; set; } = new();
    }
    public class ItemRolFlagsDto
    {
        public string nombreRol { get; set; } = null!;
        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }

}
