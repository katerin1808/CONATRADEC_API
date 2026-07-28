namespace CONATRADEC_API.DTOs
{
    public class AuthDtos
    {
        /// <summary>
        /// DTO de respuesta al iniciar sesión correctamente.
        /// </summary>
        public class UsuarioLoginResponseDto
        {
            public int UsuarioId { get; set; }
            public string nombreUsuario { get; set; } = default!;
            public string nombreCompletoUsuario { get; set; } = default!;
            public string correoUsuario { get; set; } = default!;
            public bool activo { get; set; }

            public int rolId { get; set; }
            public string rolNombre { get; set; } = default!;

            public int procedenciaId { get; set; }
            public string procedenciaNombre { get; set; } = default!;
            public bool esInterno { get; set; }

            public string? token { get; set; }
            public string urlImagenUsuario { get; set; } = string.Empty;

            /// <summary>
            /// Versión que identifica la vigencia de la sesión.
            /// </summary>
            public int versionSesion { get; set; }

            public List<PermisoInterfazDto> permisos { get; set; } = new();
        }

        public class PermisoInterfazDto
        {
            public int interfazId { get; set; }
            public string nombreInterfaz { get; set; } = default!;
            public bool? leer { get; set; }
            public bool? agregar { get; set; }
            public bool? actualizar { get; set; }
            public bool? eliminar { get; set; }
        }
    }
}
