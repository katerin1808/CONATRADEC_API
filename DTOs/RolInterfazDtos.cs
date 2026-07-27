using System.Collections.Generic;

namespace CONATRADEC_API.DTOs
{
    public class RolLiteDto
    {
        public int rolId { get; set; }
        public string nombreRol { get; set; } = string.Empty;

        /// <summary>
        /// Indica que el rol es el rol administrativo protegido.
        /// Sus permisos no pueden modificarse desde ninguna plataforma.
        /// </summary>
        public bool esAdministrador { get; set; }
    }

    public class InterfazPermisoDto
    {
        public int interfazId { get; set; }

        /// <summary>
        /// Código interno estable utilizado por la autorización.
        /// </summary>
        public string nombreInterfaz { get; set; } = string.Empty;

        /// <summary>
        /// Texto visible para el usuario en la matriz.
        /// </summary>
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
        public string nombreInterfaz { get; set; } = string.Empty;
        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }
    }
}
