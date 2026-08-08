using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class ControlFitosanitarioResumenDto
    {
        public int TotalInspecciones { get; set; }
        public int Abiertas { get; set; }
        public int EtapaTecnicaAbierta { get; set; }
        public int PendientesAnalizador { get; set; }
        public int PendientesAprobacion { get; set; }
        public int Cerradas { get; set; }
        public int TotalFotografias { get; set; }
        public int FotografiasProcesando { get; set; }
        public int FotografiasErrorIA { get; set; }
        public int FotografiasDevueltas { get; set; }
        public int FotografiasNoConcluyentes { get; set; }
        public int FotografiasRechazadas { get; set; }
        public int FotografiasAprobadas { get; set; }
        public int FotografiasPublicadasAlbum { get; set; }
        public int Mas48HorasAnalizador { get; set; }
        public int Mas48HorasAprobador { get; set; }
        public int BloqueosActivos { get; set; }
        public decimal? PromedioHorasCierre { get; set; }
    }

    public sealed class ControlFitosanitarioPaginaDto
    {
        public List<ControlFitosanitarioItemDto> Items { get; set; } = [];
        public int Pagina { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalRegistros { get; set; }
        public int TotalPaginas => TamanoPagina <= 0
            ? 0
            : (int)Math.Ceiling(TotalRegistros / (double)TamanoPagina);
    }

    public sealed class ControlFitosanitarioItemDto
    {
        public int InspeccionId { get; set; }
        public string NombreInspeccion { get; set; } = string.Empty;
        public string CodigoTerreno { get; set; } = string.Empty;
        public string Propietario { get; set; } = string.Empty;
        public string Departamento { get; set; } = string.Empty;
        public int UsuarioTecnicoId { get; set; }
        public string Tecnico { get; set; } = string.Empty;
        public DateTime FechaRegistroSistemaUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public bool EtapaTecnicaFinalizada { get; set; }
        public bool CerradaDefinitiva { get; set; }
        public int TotalFotografias { get; set; }
        public int Pendientes { get; set; }
        public int ConError { get; set; }
        public int Finalizadas { get; set; }
        public int? UsuarioAnalizadorId { get; set; }
        public string Analizador { get; set; } = string.Empty;
        public int? UsuarioAprobadorId { get; set; }
        public string Aprobador { get; set; } = string.Empty;
        public int? BloqueoAnalizadorUsuarioId { get; set; }
        public string BloqueoAnalizadorUsuario { get; set; } = string.Empty;
        public DateTime? BloqueoAnalizadorExpiraUtc { get; set; }
        public int? BloqueoAprobadorUsuarioId { get; set; }
        public string BloqueoAprobadorUsuario { get; set; } = string.Empty;
        public DateTime? BloqueoAprobadorExpiraUtc { get; set; }
        public DateTime? UltimaActividadUtc { get; set; }
    }

    public sealed class ControlFitosanitarioAuditoriaDto
    {
        public int InspeccionId { get; set; }
        public string NombreInspeccion { get; set; } = string.Empty;
        public string CodigoTerreno { get; set; } = string.Empty;
        public int UsuarioTecnicoId { get; set; }
        public string Tecnico { get; set; } = string.Empty;
        public DateTime FechaRegistroSistemaUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public bool EtapaTecnicaFinalizada { get; set; }
        public DateTime? FechaFinEtapaTecnicaUtc { get; set; }
        public bool CerradaDefinitiva { get; set; }
        public DateTime? FechaCierreDefinitivoUtc { get; set; }
        public int? UsuarioAnalizadorId { get; set; }
        public string Analizador { get; set; } = string.Empty;
        public int? UsuarioAprobadorId { get; set; }
        public string Aprobador { get; set; } = string.Empty;
        public ControlFitosanitarioBloqueoDto? BloqueoAnalizador { get; set; }
        public ControlFitosanitarioBloqueoDto? BloqueoAprobador { get; set; }
        public List<ControlFitosanitarioFotoComparacionDto> Fotografias { get; set; } = [];
        public List<ControlFitosanitarioEventoDto> Eventos { get; set; } = [];
    }

    public sealed class ControlFitosanitarioBloqueoDto
    {
        public string Etapa { get; set; } = string.Empty;
        public int UsuarioId { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public DateTime FechaAdquisicionUtc { get; set; }
        public DateTime UltimoHeartbeatUtc { get; set; }
        public DateTime ExpiraUtc { get; set; }
    }

    public sealed class ControlFitosanitarioFotoComparacionDto
    {
        public int FotografiaId { get; set; }
        public int Orden { get; set; }
        public string TipoFotografia { get; set; } = string.Empty;
        public string UrlImagen { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public DateTime? FechaIdentificacionCampo { get; set; }
        public DateTime? FechaAnalisisIAUtc { get; set; }
        public DateTime? FechaAnalisisHumanoUtc { get; set; }
        public DateTime? FechaAprobacionUtc { get; set; }
        public string ModeloIA { get; set; } = string.Empty;
        public int IntentosIA { get; set; }
        public string DiagnosticoIA { get; set; } = string.Empty;
        public string DiagnosticoHumano { get; set; } = string.Empty;
        public string DiagnosticoFinal { get; set; } = string.Empty;
        public string DecisionFinal { get; set; } = string.Empty;
        public bool CoincidenciaIAFinal { get; set; }
    }

    public sealed class ControlFitosanitarioEventoDto
    {
        public DateTime FechaUtc { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public int? FotografiaId { get; set; }
        public int UsuarioId { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string Accion { get; set; } = string.Empty;
        public string EstadoAnterior { get; set; } = string.Empty;
        public string EstadoNuevo { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;
    }

    public sealed class ControlFitosanitarioUsuarioDto
    {
        public int UsuarioId { get; set; }
        public string NombreCompleto { get; set; } = string.Empty;
        public string NombreUsuario { get; set; } = string.Empty;
    }

    public sealed class ControlFitosanitarioReasignarRequest
    {
        [Required, MaxLength(20)]
        public string Etapa { get; set; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int UsuarioNuevoId { get; set; }

        [Required, MinLength(8), MaxLength(1000)]
        public string Motivo { get; set; } = string.Empty;
    }

    public sealed class ControlFitosanitarioLiberarBloqueoRequest
    {
        [Required, MaxLength(20)]
        public string Etapa { get; set; } = string.Empty;

        [Required, MinLength(8), MaxLength(1000)]
        public string Motivo { get; set; } = string.Empty;
    }

    public sealed class ControlFitosanitarioOperacionDto
    {
        public int InspeccionId { get; set; }
        public string Etapa { get; set; } = string.Empty;
        public int? UsuarioAnteriorId { get; set; }
        public string UsuarioAnterior { get; set; } = string.Empty;
        public int? UsuarioNuevoId { get; set; }
        public string UsuarioNuevo { get; set; } = string.Empty;
        public string Motivo { get; set; } = string.Empty;
        public DateTime FechaUtc { get; set; }
    }

    public sealed class ControlFitosanitarioRendimientoIADto
    {
        public int FotografiasAnalizadas { get; set; }
        public int FotografiasConResultadoFinal { get; set; }
        public int CoincidenciasExactas { get; set; }
        public int CorregidasPorHumano { get; set; }
        public int NoConcluyentes { get; set; }
        public int Rechazadas { get; set; }
        public int ErroresIA { get; set; }
        public decimal PorcentajeCoincidencia { get; set; }
        public List<ControlFitosanitarioModeloIADto> Modelos { get; set; } = [];
    }

    public sealed class ControlFitosanitarioModeloIADto
    {
        public string Modelo { get; set; } = string.Empty;
        public int FotografiasAnalizadas { get; set; }
        public int ConResultadoFinal { get; set; }
        public int CoincidenciasExactas { get; set; }
        public int CorregidasPorHumano { get; set; }
        public decimal PorcentajeCoincidencia { get; set; }
    }
}
