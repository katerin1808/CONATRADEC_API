using System.Collections.Generic;
namespace CONATRADEC_API.DTOs
{
    public class RolLiteDto
    {
        public int rolId { get; set; }
        public string nombreRol { get; set; } = string.Empty;
    }

    /// <summary>
    /// Permisos de una página principal.
    /// nombreInterfaz es el código interno usado para validar.
    /// nombreAmigableInterfaz es el texto presentado al usuario.
    /// </summary>
    public class InterfazPermisoDto
    {
        public int interfazId { get; set; }

        public string nombreInterfaz { get; set; } = string.Empty;

        public string nombreAmigableInterfaz { get; set; } =
            string.Empty;

        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }

    public class RolConPermisosDto
    {
        public RolLiteDto rol { get; set; } = new();

        public List<InterfazPermisoDto> interfaz { get; set; } =
            new();
    }

    public class RolFiltroRequest
    {
        public int? rolId { get; set; }
        public string? nombreRol { get; set; }
        public bool incluirInactivosRol { get; set; }
        public bool incluirInactivosInterfaz { get; set; }
    }

    public class AgregarPermisoPorNombreRequest
    {
        public string nombreRol { get; set; } = string.Empty;

        /// <summary>
        /// Código interno de la interfaz.
        /// </summary>
        public string nombreInterfaz { get; set; } = string.Empty;

        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }
}
