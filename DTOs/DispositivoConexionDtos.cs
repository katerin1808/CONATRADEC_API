using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class ReportarDispositivoConexionRequest
    {
        [Required, MaxLength(64)]
        public string InstalacionId { get; set; } = string.Empty;

        [Required, MaxLength(64)]
        public string SesionId { get; set; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int UsuarioId { get; set; }

        [Required, MaxLength(30)]
        public string Plataforma { get; set; } = string.Empty;

        [MaxLength(30)]
        public string TipoDispositivo { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Fabricante { get; set; } = string.Empty;

        [MaxLength(150)]
        public string Modelo { get; set; } = string.Empty;

        [MaxLength(150)]
        public string NombreDispositivo { get; set; } = string.Empty;

        [MaxLength(100)]
        public string SistemaOperativo { get; set; } = string.Empty;

        [MaxLength(50)]
        public string VersionSistema { get; set; } = string.Empty;

        [MaxLength(50)]
        public string VersionApp { get; set; } = string.Empty;

        [MaxLength(50)]
        public string BuildApp { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Idioma { get; set; } = string.Empty;

        [MaxLength(100)]
        public string TipoConexion { get; set; } = string.Empty;

        [MaxLength(500)]
        public string PaginaActual { get; set; } = string.Empty;
    }

    public sealed class DesconectarDispositivoConexionRequest
    {
        [Required, MaxLength(64)]
        public string InstalacionId { get; set; } = string.Empty;

        [Required, MaxLength(64)]
        public string SesionId { get; set; } = string.Empty;

        [MaxLength(150)]
        public string Motivo { get; set; } = string.Empty;
    }

    public sealed class ReportarDispositivoConexionResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int DispositivoConexionId { get; set; }
        public DateTime UltimoLatidoUtc { get; set; }
        public DateTime ConsideradoConectadoHastaUtc { get; set; }
    }

    public sealed class DispositivoConexionListadoDto
    {
        public int DispositivoConexionId { get; set; }
        public string InstalacionId { get; set; } = string.Empty;
        public string SesionId { get; set; } = string.Empty;
        public int UsuarioId { get; set; }
        public string UsuarioNombre { get; set; } = string.Empty;
        public string CorreoUsuario { get; set; } = string.Empty;
        public string RolNombre { get; set; } = string.Empty;
        public string Plataforma { get; set; } = string.Empty;
        public string TipoDispositivo { get; set; } = string.Empty;
        public string Fabricante { get; set; } = string.Empty;
        public string Modelo { get; set; } = string.Empty;
        public string NombreDispositivo { get; set; } = string.Empty;
        public string SistemaOperativo { get; set; } = string.Empty;
        public string VersionSistema { get; set; } = string.Empty;
        public string VersionApp { get; set; } = string.Empty;
        public string BuildApp { get; set; } = string.Empty;
        public string Idioma { get; set; } = string.Empty;
        public string TipoConexion { get; set; } = string.Empty;
        public string PaginaActual { get; set; } = string.Empty;
        public string DireccionIp { get; set; } = string.Empty;
        public DateTime FechaRegistroUtc { get; set; }
        public DateTime FechaInicioSesionUtc { get; set; }
        public DateTime UltimoLatidoUtc { get; set; }
        public DateTime? FechaDesconexionUtc { get; set; }
        public bool Conectado { get; set; }
        public int SegundosDesdeUltimoLatido { get; set; }
        public int CantidadSesiones { get; set; }
    }

    public sealed class DispositivosConexionPaginadaDto
    {
        public List<DispositivoConexionListadoDto> Items { get; set; } = new();
        public int Pagina { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalRegistros { get; set; }
        public int TotalPaginas { get; set; }
        public int MinutosTolerancia { get; set; }
        public DateTime FechaConsultaUtc { get; set; }
    }

    public sealed class DispositivosConexionResumenDto
    {
        public int TotalConectados { get; set; }
        public int UsuariosConectados { get; set; }
        public int AndroidConectados { get; set; }
        public int WindowsConectados { get; set; }
        public int OtrosConectados { get; set; }
        public int TotalDispositivosRegistrados { get; set; }
        public int TotalSesionesRegistradas { get; set; }
        public int DispositivosActivosUltimas24Horas { get; set; }
        public int MinutosTolerancia { get; set; }
        public DateTime FechaConsultaUtc { get; set; }
        public DateTime? UltimoLatidoRecibidoUtc { get; set; }
    }
}
