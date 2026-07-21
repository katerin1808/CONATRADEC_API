namespace CONATRADEC_API.DTOs
{
    public class BitacoraListadoDto
    {
        public Guid BitacoraId { get; set; }
        public DateTime FechaHoraUtc { get; set; }
        public int? UsuarioId { get; set; }
        public string UsuarioNombre { get; set; } = string.Empty;
        public string RolNombre { get; set; } = string.Empty;
        public string Modulo { get; set; } = string.Empty;
        public string Accion { get; set; } = string.Empty;
        public string MetodoHttp { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string PaginaOrigen { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public int CodigoEstado { get; set; }
        public bool Exitoso { get; set; }
        public long DuracionMs { get; set; }
        public int CantidadCambios { get; set; }
    }

    public sealed class BitacoraDetalleDto : BitacoraListadoDto
    {
        public string Parametros { get; set; } = string.Empty;
        public string DireccionIp { get; set; } = string.Empty;
        public string Dispositivo { get; set; } = string.Empty;
        public string Plataforma { get; set; } = string.Empty;
        public string VersionApp { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public List<BitacoraCambioDto> Cambios { get; set; } = new();
    }

    public sealed class BitacoraCambioDto
    {
        public long BitacoraDetalleId { get; set; }
        public DateTime FechaHoraUtc { get; set; }
        public string Entidad { get; set; } = string.Empty;
        public string EntidadId { get; set; } = string.Empty;
        public string Operacion { get; set; } = string.Empty;
        public string ValoresAnteriores { get; set; } = string.Empty;
        public string ValoresNuevos { get; set; } = string.Empty;
        public string PropiedadesModificadas { get; set; } = string.Empty;
    }

    public sealed class BitacoraPaginadaDto
    {
        public List<BitacoraListadoDto> Items { get; set; } = new();
        public int Pagina { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalRegistros { get; set; }
        public int TotalPaginas { get; set; }
    }

    public sealed class BitacoraUsuarioFiltroDto
    {
        public int? UsuarioId { get; set; }
        public string Nombre { get; set; } = string.Empty;
    }

    public sealed class BitacoraCatalogosDto
    {
        public List<string> Acciones { get; set; } = new();
        public List<string> Modulos { get; set; } = new();
        public List<BitacoraUsuarioFiltroDto> Usuarios { get; set; } = new();
    }
}
