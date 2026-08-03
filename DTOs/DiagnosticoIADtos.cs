using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class DiagnosticoIACrearRequest
    {
        [MaxLength(50)]
        public string? CodigoTerreno { get; set; }

        [MaxLength(1000)]
        public string? Observacion { get; set; }

        [Required]
        public List<IFormFile> Fotos { get; set; } = [];
    }

    public sealed class DiagnosticoIAClasificarRequest
    {
        [Required, MaxLength(30)]
        public string Decision { get; set; } =
            string.Empty;

        [MaxLength(300)]
        public string? DiagnosticoFinal { get; set; }

        [MaxLength(2000)]
        public string? Observaciones { get; set; }
    }

    public sealed class DiagnosticoIASegundaRevisionRequest
    {
        [Required, MinLength(8), MaxLength(2000)]
        public string RetroalimentacionClasificador { get; set; } =
            string.Empty;

        [MaxLength(300)]
        public string? DiagnosticoPropuestoClasificador { get; set; }
    }

    public sealed class DiagnosticoIADetalleDto
    {
        public int DiagnosticoIAId { get; set; }
        public int? TerrenoId { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public int UsuarioSolicitanteId { get; set; }
        public string UsuarioSolicitante { get; set; } = string.Empty;
        public DateTime FechaSolicitudUtc { get; set; }
        public DateTime? FechaRespuestaIAUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string ModeloGemini { get; set; } = string.Empty;
        public string ObservacionUsuario { get; set; } = string.Empty;
        public bool ImagenValida { get; set; }
        public bool ParecePlantaCafe { get; set; }
        public bool ResultadoConcluyente { get; set; }
        public bool PosibleDanoNoBiotico { get; set; }
        public string DiagnosticoSugerido { get; set; } = string.Empty;
        public string NivelCoincidencia { get; set; } = string.Empty;
        public string Resumen { get; set; } = string.Empty;
        public string PosibleCausaNoBiotica { get; set; } = string.Empty;
        public IReadOnlyList<string> SintomasVisibles { get; set; } = [];
        public IReadOnlyList<string> DiagnosticosAlternativos { get; set; } = [];
        public IReadOnlyList<string> RecomendacionesCaptura { get; set; } = [];
        public IReadOnlyList<string> Advertencias { get; set; } = [];
        public string ErrorAnalisis { get; set; } = string.Empty;
        public bool RequiereValidacionHumana { get; set; }
        public IReadOnlyList<DiagnosticoIAImagenDto> Imagenes { get; set; } = [];
        public IReadOnlyList<DiagnosticoIARevisionDto> RevisionesIA { get; set; } = [];
        public DiagnosticoIARevisionDto? UltimaRevisionIA { get; set; }
        public DiagnosticoIARevisionDto? RevisionVigenteIA { get; set; }
        public DiagnosticoIAValidacionDto? UltimaValidacion { get; set; }
    }

    public sealed class DiagnosticoIAImagenDto
    {
        public int DiagnosticoIAImagenId { get; set; }
        public string UrlImagen { get; set; } = string.Empty;
        public string TipoFotografia { get; set; } = string.Empty;
        public int Orden { get; set; }
    }

    public sealed class DiagnosticoIARevisionDto
    {
        public int DiagnosticoIARevisionId { get; set; }
        public int UsuarioClasificadorId { get; set; }
        public string UsuarioClasificador { get; set; } = string.Empty;
        public string RetroalimentacionClasificador { get; set; } = string.Empty;
        public string DiagnosticoPropuestoClasificador { get; set; } = string.Empty;
        public DateTime FechaSolicitudRevisionUtc { get; set; }
        public DateTime? FechaRespuestaRevisionUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public bool ImagenValida { get; set; }
        public bool ResultadoConcluyente { get; set; }
        public bool MantieneVeredictoOriginal { get; set; }
        public string RelacionConCriterioTecnico { get; set; } = string.Empty;
        public string DiagnosticoRevisado { get; set; } = string.Empty;
        public string NivelCoincidencia { get; set; } = string.Empty;
        public string ResumenRevision { get; set; } = string.Empty;
        public IReadOnlyList<string> EvidenciasApoyo { get; set; } = [];
        public IReadOnlyList<string> EvidenciasContradiccion { get; set; } = [];
        public IReadOnlyList<string> InformacionFaltante { get; set; } = [];
        public IReadOnlyList<string> RecomendacionesCaptura { get; set; } = [];
        public IReadOnlyList<string> Advertencias { get; set; } = [];
        public string ErrorRevision { get; set; } = string.Empty;
    }

    public sealed class DiagnosticoIAValidacionDto
    {
        public int DiagnosticoIAValidacionId { get; set; }
        public int UsuarioClasificadorId { get; set; }
        public string UsuarioClasificador { get; set; } = string.Empty;
        public string Decision { get; set; } = string.Empty;
        public string DiagnosticoFinal { get; set; } = string.Empty;
        public bool? CoincideConGemini { get; set; }
        public string Observaciones { get; set; } = string.Empty;
        public DateTime FechaValidacionUtc { get; set; }
    }
}
