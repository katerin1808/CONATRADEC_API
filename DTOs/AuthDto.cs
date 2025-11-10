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

            // Datos del rol asignado
            public int rolId { get; set; }
            public string rolNombre { get; set; } = default!;

            // Datos de procedencia (Interno / Externo)
            public int procedenciaId { get; set; }
            public string procedenciaNombre { get; set; } = default!;
            public bool esInterno { get; set; }

            // Opcional: Token (si luego implementas JWT)
            public string? token { get; set; }
        }
    }
}
