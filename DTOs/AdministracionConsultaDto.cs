namespace CONATRADEC_API.DTOs
{
    public sealed class UsuarioAdministracionDto
    {
        public int UsuarioId { get; set; }
        public string NombreUsuario { get; set; } = string.Empty;
        public string IdentificacionUsuario { get; set; } = string.Empty;
        public string NombreCompletoUsuario { get; set; } = string.Empty;
        public string CorreoUsuario { get; set; } = string.Empty;
        public string TelefonoUsuario { get; set; } = string.Empty;
        public DateOnly? FechaNacimientoUsuario { get; set; }
        public int? RolId { get; set; }
        public int? ProcedenciaId { get; set; }
        public int? MunicipioId { get; set; }
        public int? DepartamentoId { get; set; }
        public int? PaisId { get; set; }
        public string RolNombre { get; set; } = string.Empty;
        public string ProcedenciaNombre { get; set; } = string.Empty;
        public string MunicipioNombre { get; set; } = string.Empty;
        public string DepartamentoNombre { get; set; } = string.Empty;
        public string PaisNombre { get; set; } = string.Empty;
        public bool EsInterno { get; set; }
        public string UrlImagenUsuario { get; set; } = string.Empty;
    }

    public sealed class UsuarioAdministracionPaginaResponse
    {
        public List<UsuarioAdministracionDto> Items { get; set; } = new();
        public int PaginaActual { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalRegistros { get; set; }
        public int TotalPaginas { get; set; }
    }

    public sealed class RolAdministracionDto
    {
        public int RolId { get; set; }
        public string NombreRol { get; set; } = string.Empty;
        public string DescripcionRol { get; set; } = string.Empty;
        public int CantidadUsuarios { get; set; }
        public int CantidadInterfaces { get; set; }
    }

    public sealed class RolAdministracionPaginaResponse
    {
        public List<RolAdministracionDto> Items { get; set; } = new();
        public int PaginaActual { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalRegistros { get; set; }
        public int TotalPaginas { get; set; }
    }
}
