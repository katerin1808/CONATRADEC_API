namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// Respuesta estándar para listados paginados de la administración web.
    /// </summary>
    public class ResultadoPaginadoDto<T>
    {
        public List<T> Items { get; set; } = new();
        public int Pagina { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalRegistros { get; set; }
        public int TotalPaginas { get; set; }
        public bool TienePaginaAnterior => Pagina > 1;
        public bool TienePaginaSiguiente => Pagina < TotalPaginas;
    }

    public sealed class PropietarioPaginadoItemDto
    {
        public int PropietarioId { get; set; }
        public string Identificacion { get; set; } = string.Empty;
        public string NombreCompleto { get; set; } = string.Empty;
        public string? Telefono { get; set; }
        public string? Correo { get; set; }
        public string? Direccion { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaRegistroUtc { get; set; }
        public int TotalTerrenos { get; set; }
        public int? UsuarioPortalId { get; set; }
        public string? UsuarioPortal { get; set; }
    }

    public sealed class ResumenSeguimientoAlertasDto
    {
        public int Total { get; set; }
        public int Pendientes { get; set; }
        public int EnProceso { get; set; }
        public int Atendidas { get; set; }
        public int Descartadas { get; set; }
        public int Cerradas => Atendidas + Descartadas;
    }

    public sealed class SeguimientosPaginadosDto
        : ResultadoPaginadoDto<SeguimientoAlertaResponse>
    {
        public ResumenSeguimientoAlertasDto Resumen { get; set; } = new();
    }
}
