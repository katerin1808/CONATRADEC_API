using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{

    public sealed class DiagnosticoIAConfiguracionActualizarRequest
    {
        [Range(1, 20)]
        public int MaximoRevisionesGemini { get; set; } = 2;

        public bool RevisionesIlimitadas { get; set; }
    }

    public sealed class DiagnosticoIAConfiguracionHistorialDto
    {
        public int DiagnosticoIAConfiguracionHistorialId { get; set; }
        public int MaximoAnterior { get; set; }
        public bool IlimitadasAnterior { get; set; }
        public int MaximoNuevo { get; set; }
        public bool IlimitadasNuevo { get; set; }
        public int UsuarioId { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public DateTime FechaUtc { get; set; }
    }

    public sealed class DiagnosticoIAConfiguracionDto
    {
        public int MaximoRevisionesGemini { get; set; } = 2;
        public bool RevisionesIlimitadas { get; set; }
        public DateTime FechaModificacionUtc { get; set; }
        public int? UsuarioModificacionId { get; set; }
        public string UsuarioModificacion { get; set; } = string.Empty;
        public List<DiagnosticoIAConfiguracionHistorialDto> Historial { get; set; } = [];
    }

    public sealed class DiagnosticoIACrearRequest
    {
        [MaxLength(50)]
        public string? CodigoTerreno { get; set; }

        [MaxLength(1000)]
        public string? Observacion { get; set; }

        [Required]
        public List<IFormFile> Fotos { get; set; } = [];

        public List<string> TiposFotografia { get; set; } = [];
    }

    public sealed class DiagnosticoIAAnularRequest
    {
        [Required, MinLength(8), MaxLength(1000)]
        public string Motivo { get; set; } = string.Empty;
    }

    public sealed class DiagnosticoIASegundaRevisionRequest
    {
        [Required, MaxLength(2000)]
        public string RetroalimentacionAnalizador { get; set; } =
            string.Empty;

        [MaxLength(300)]
        public string? DiagnosticoPropuestoAnalizador { get; set; }
    }

    public sealed class DiagnosticoIAAnalisisHumanoRequest
    {
        [Required, MaxLength(30)]
        public string CalidadEvaluacion { get; set; } =
            string.Empty;

        [Required, MaxLength(40)]
        public string EstadoGeneral { get; set; } =
            string.Empty;

        [Required, MaxLength(50)]
        public string CategoriaPrincipal { get; set; } =
            string.Empty;

        public List<string> CategoriasSecundarias { get; set; } = [];

        [MaxLength(300)]
        public string? DiagnosticoPropuesto { get; set; }

        [MaxLength(80)]
        public string? TipoDiagnostico { get; set; }

        [Required, MaxLength(30)]
        public string SeveridadPropuesta { get; set; } =
            string.Empty;

        [Required, MaxLength(30)]
        public string NivelCerteza { get; set; } =
            string.Empty;

        public List<string> PartesAfectadas { get; set; } = [];

        public List<string> EvidenciasObservadas { get; set; } = [];

        [MaxLength(3000)]
        public string? Observaciones { get; set; }
    }

    public sealed class DiagnosticoIAImagenEvaluacionRequest
    {
        [Range(1, int.MaxValue)]
        public int DiagnosticoIAImagenId { get; set; }

        [Required, MaxLength(30)]
        public string CalidadTecnica { get; set; } =
            string.Empty;

        public bool EsEvidenciaValida { get; set; }

        public bool AptaParaAlbum { get; set; }

        [MaxLength(1000)]
        public string? Observacion { get; set; }
    }

    public sealed class DiagnosticoIAAprobacionRequest
    {
        [Required, MaxLength(40)]
        public string Decision { get; set; } =
            string.Empty;

        [MaxLength(30)]
        public string? CalidadEvaluacionFinal { get; set; }

        [MaxLength(40)]
        public string? EstadoGeneralFinal { get; set; }

        [MaxLength(50)]
        public string? CategoriaPrincipalFinal { get; set; }

        public List<string> CategoriasSecundariasFinales { get; set; } = [];

        [MaxLength(300)]
        public string? DiagnosticoFinal { get; set; }

        [MaxLength(80)]
        public string? TipoDiagnosticoFinal { get; set; }

        [MaxLength(30)]
        public string? SeveridadFinal { get; set; }

        [MaxLength(30)]
        public string? NivelCertezaFinal { get; set; }

        [MaxLength(3000)]
        public string? Observaciones { get; set; }

        public bool AutorizaPublicacionAlbum { get; set; }

        public List<DiagnosticoIAImagenEvaluacionRequest>
            EvaluacionesImagen { get; set; } = [];
    }

    public sealed class DiagnosticoIAPublicarAlbumImagenRequest
    {
        [Range(1, int.MaxValue)]
        public int DiagnosticoIAImagenId { get; set; }

        [MaxLength(500)]
        public string? Descripcion { get; set; }

        public bool EsPortada { get; set; }

        public int Orden { get; set; }
    }

    public sealed class DiagnosticoIAPublicarAlbumRequest
    {
        [Range(1, int.MaxValue)]
        public int CategoriaAlbumBotanicoId { get; set; }

        [Range(1, int.MaxValue)]
        public int AlbumBotanicoCafeId { get; set; }

        [Required, MinLength(1)]
        public List<DiagnosticoIAPublicarAlbumImagenRequest>
            Imagenes { get; set; } = [];
    }

    public sealed class DiagnosticoIAListaDto
    {
        public int DiagnosticoIAId { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public string UsuarioSolicitante { get; set; } = string.Empty;
        public DateTime FechaSolicitudUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string DiagnosticoSugerido { get; set; } = string.Empty;
        public string CategoriaPrincipalIA { get; set; } = string.Empty;
        public string EstadoGeneralIA { get; set; } = string.Empty;
        public string NivelCoincidencia { get; set; } = string.Empty;
        public int TotalImagenes { get; set; }
        public string? UrlMiniatura { get; set; }
        public int? VersionAnalisisActual { get; set; }
        public string? DiagnosticoPropuesto { get; set; }
        public string? Analizador { get; set; }
        public string? Aprobador { get; set; }
        public bool PuedePublicarAlbum { get; set; }
        public int TotalPublicadasAlbum { get; set; }
    }

    public sealed class DiagnosticoIAImagenDto
    {
        public int DiagnosticoIAImagenId { get; set; }
        public string UrlImagen { get; set; } = string.Empty;
        public string TipoFotografia { get; set; } = string.Empty;
        public int Orden { get; set; }
        public string NombreArchivoOriginal { get; set; } = string.Empty;
        public DiagnosticoIAImagenResultadoDto? ResultadoIA { get; set; }
        public DiagnosticoIAImagenEvaluacionDto? UltimaEvaluacion { get; set; }
        public DiagnosticoIAAlbumPublicacionDto? PublicacionAlbum { get; set; }
    }

    public sealed class DiagnosticoIAImagenResultadoDto
    {
        public int DiagnosticoIAImagenResultadoIAId { get; set; }
        public bool ImagenValida { get; set; }
        public bool ParecePlantaCafe { get; set; }
        public bool ResultadoConcluyente { get; set; }
        public string PartePlanta { get; set; } = string.Empty;
        public string CalidadEvaluacion { get; set; } = string.Empty;
        public string EstadoGeneral { get; set; } = string.Empty;
        public string CategoriaPrincipal { get; set; } = string.Empty;
        public List<string> CategoriasSecundarias { get; set; } = [];
        public string DiagnosticoProbable { get; set; } = string.Empty;
        public string TipoDiagnostico { get; set; } = string.Empty;
        public string SeveridadVisual { get; set; } = string.Empty;
        public string NivelCerteza { get; set; } = string.Empty;
        public int? CategoriaAlbumBotanicoIdSugerida { get; set; }
        public int? AlbumBotanicoCafeIdSugerido { get; set; }
        public string CategoriaAlbumSugerida { get; set; } = string.Empty;
        public string ClasificacionAlbumSugerida { get; set; } = string.Empty;
        public string NombreCientificoSugerido { get; set; } = string.Empty;
        public bool CoincideCatalogoAlbum { get; set; }
        public bool RequiereDecisionClasificacion { get; set; }
        public string MotivoClasificacionAlbum { get; set; } = string.Empty;
        public int? CategoriaAlbumBotanicoIdSeleccionada { get; set; }
        public int? AlbumBotanicoCafeIdSeleccionado { get; set; }
        public string CategoriaAlbumSeleccionada { get; set; } = string.Empty;
        public string ClasificacionAlbumSeleccionada { get; set; } = string.Empty;
        public string EstadoClasificacionAlbum { get; set; } = string.Empty;
        public string ResumenImagen { get; set; } = string.Empty;
        public List<string> SintomasVisibles { get; set; } = [];
        public List<string> EvidenciasObservadas { get; set; } = [];
        public List<string> EvidenciasNoObservadas { get; set; } = [];
        public List<string> DiagnosticosAlternativos { get; set; } = [];
        public List<string> InformacionFaltante { get; set; } = [];
        public List<string> RecomendacionesCaptura { get; set; } = [];
        public List<string> Advertencias { get; set; } = [];
        public DateTime FechaResultadoUtc { get; set; }
    }

    public sealed class DiagnosticoIARevisionDto
    {
        public int DiagnosticoIARevisionId { get; set; }
        public int UsuarioAnalizadorId { get; set; }
        public string UsuarioAnalizador { get; set; } = string.Empty;
        public string RetroalimentacionAnalizador { get; set; } = string.Empty;
        public string DiagnosticoPropuestoAnalizador { get; set; } = string.Empty;
        public DateTime FechaSolicitudRevisionUtc { get; set; }
        public DateTime? FechaRespuestaRevisionUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public bool ImagenValida { get; set; }
        public bool ResultadoConcluyente { get; set; }
        public bool MantieneVeredictoOriginal { get; set; }
        public string RelacionConCriterioTecnico { get; set; } = string.Empty;
        public string CalidadEvaluacion { get; set; } = string.Empty;
        public string EstadoGeneral { get; set; } = string.Empty;
        public string CategoriaPrincipal { get; set; } = string.Empty;
        public List<string> CategoriasSecundarias { get; set; } = [];
        public string DiagnosticoRevisado { get; set; } = string.Empty;
        public string TipoDiagnostico { get; set; } = string.Empty;
        public string SeveridadVisual { get; set; } = string.Empty;
        public string NivelCoincidencia { get; set; } = string.Empty;
        public string ResumenRevision { get; set; } = string.Empty;
        public List<string> PartesAfectadas { get; set; } = [];
        public List<string> EvidenciasApoyo { get; set; } = [];
        public List<string> EvidenciasContradiccion { get; set; } = [];
        public List<string> InformacionFaltante { get; set; } = [];
        public List<string> RecomendacionesCaptura { get; set; } = [];
        public List<string> Advertencias { get; set; } = [];
        public string ErrorRevision { get; set; } = string.Empty;
    }

    public sealed class DiagnosticoIAAnalisisHumanoDto
    {
        public int DiagnosticoIAAnalisisHumanoId { get; set; }
        public int UsuarioAnalizadorId { get; set; }
        public string UsuarioAnalizador { get; set; } = string.Empty;
        public int Version { get; set; }
        public string EstadoRegistro { get; set; } = string.Empty;
        public string CalidadEvaluacion { get; set; } = string.Empty;
        public string EstadoGeneral { get; set; } = string.Empty;
        public string CategoriaPrincipal { get; set; } = string.Empty;
        public List<string> CategoriasSecundarias { get; set; } = [];
        public string DiagnosticoPropuesto { get; set; } = string.Empty;
        public string TipoDiagnostico { get; set; } = string.Empty;
        public string SeveridadPropuesta { get; set; } = string.Empty;
        public string NivelCerteza { get; set; } = string.Empty;
        public List<string> PartesAfectadas { get; set; } = [];
        public List<string> EvidenciasObservadas { get; set; } = [];
        public string Observaciones { get; set; } = string.Empty;
        public DateTime FechaCreacionUtc { get; set; }
        public DateTime FechaActualizacionUtc { get; set; }
        public DateTime? FechaEnvioUtc { get; set; }
    }

    public sealed class DiagnosticoIAImagenEvaluacionDto
    {
        public int DiagnosticoIAImagenEvaluacionId { get; set; }
        public int DiagnosticoIAAprobacionId { get; set; }
        public int DiagnosticoIAImagenId { get; set; }
        public int UsuarioAprobadorId { get; set; }
        public string UsuarioAprobador { get; set; } = string.Empty;
        public string CalidadTecnica { get; set; } = string.Empty;
        public bool EsEvidenciaValida { get; set; }
        public bool AptaParaAlbum { get; set; }
        public string Observacion { get; set; } = string.Empty;
        public DateTime FechaEvaluacionUtc { get; set; }
    }

    public sealed class DiagnosticoIAAprobacionDto
    {
        public int DiagnosticoIAAprobacionId { get; set; }
        public int DiagnosticoIAAnalisisHumanoId { get; set; }
        public int UsuarioAprobadorId { get; set; }
        public string UsuarioAprobador { get; set; } = string.Empty;
        public string Decision { get; set; } = string.Empty;
        public string CalidadEvaluacionFinal { get; set; } = string.Empty;
        public string EstadoGeneralFinal { get; set; } = string.Empty;
        public string CategoriaPrincipalFinal { get; set; } = string.Empty;
        public List<string> CategoriasSecundariasFinales { get; set; } = [];
        public string DiagnosticoFinal { get; set; } = string.Empty;
        public string TipoDiagnosticoFinal { get; set; } = string.Empty;
        public string SeveridadFinal { get; set; } = string.Empty;
        public string NivelCertezaFinal { get; set; } = string.Empty;
        public string Observaciones { get; set; } = string.Empty;
        public bool AutorizaPublicacionAlbum { get; set; }
        public bool MismoUsuarioQueAnalizo { get; set; }
        public DateTime FechaAprobacionUtc { get; set; }
        public List<DiagnosticoIAImagenEvaluacionDto> EvaluacionesImagen { get; set; } = [];
    }

    public sealed class DiagnosticoIAAlbumPublicacionDto
    {
        public int DiagnosticoIAAlbumPublicacionId { get; set; }
        public int DiagnosticoIAImagenId { get; set; }
        public int CategoriaAlbumBotanicoId { get; set; }
        public string CategoriaAlbum { get; set; } = string.Empty;
        public int AlbumBotanicoCafeId { get; set; }
        public string RegistroAlbum { get; set; } = string.Empty;
        public int AlbumBotanicoCafeFotoId { get; set; }
        public int UsuarioPublicacionId { get; set; }
        public string UsuarioPublicacion { get; set; } = string.Empty;
        public DateTime FechaPublicacionUtc { get; set; }
        public string DescripcionPublicacion { get; set; } = string.Empty;
        public string RutaFotoAlbum { get; set; } = string.Empty;
        public bool Activo { get; set; }
    }

    public sealed class DiagnosticoIAHistorialDto
    {
        public int DiagnosticoIAHistorialId { get; set; }
        public int UsuarioId { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string EstadoAnterior { get; set; } = string.Empty;
        public string EstadoNuevo { get; set; } = string.Empty;
        public string Accion { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;
        public DateTime FechaUtc { get; set; }
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
        public string CalidadEvaluacionIA { get; set; } = string.Empty;
        public string EstadoGeneralIA { get; set; } = string.Empty;
        public string CategoriaPrincipalIA { get; set; } = string.Empty;
        public List<string> CategoriasSecundariasIA { get; set; } = [];
        public string DiagnosticoSugerido { get; set; } = string.Empty;
        public string TipoDiagnosticoIA { get; set; } = string.Empty;
        public string SeveridadVisualIA { get; set; } = string.Empty;
        public string NivelCoincidencia { get; set; } = string.Empty;
        public string Resumen { get; set; } = string.Empty;
        public List<string> PartesAfectadas { get; set; } = [];
        public List<string> SintomasVisibles { get; set; } = [];
        public List<string> EvidenciasNoObservadas { get; set; } = [];
        public List<string> DiagnosticosAlternativos { get; set; } = [];
        public List<string> InformacionFaltante { get; set; } = [];
        public List<string> RecomendacionesCaptura { get; set; } = [];
        public List<string> Advertencias { get; set; } = [];
        public bool PosibleDanoNoBiotico { get; set; }
        public string PosibleCausaNoBiotica { get; set; } = string.Empty;
        public string ErrorAnalisis { get; set; } = string.Empty;
        public bool RequiereValidacionHumana { get; set; }
        public List<DiagnosticoIAImagenDto> Imagenes { get; set; } = [];
        public List<DiagnosticoIARevisionDto> RevisionesIA { get; set; } = [];
        public DiagnosticoIARevisionDto? UltimaRevisionIA { get; set; }
        public List<DiagnosticoIAAnalisisHumanoDto> AnalisisHumanos { get; set; } = [];
        public DiagnosticoIAAnalisisHumanoDto? AnalisisHumanoActual { get; set; }
        public List<DiagnosticoIAAprobacionDto> Aprobaciones { get; set; } = [];
        public DiagnosticoIAAprobacionDto? UltimaAprobacion { get; set; }
        public List<DiagnosticoIAAlbumPublicacionDto> PublicacionesAlbum { get; set; } = [];
        public List<DiagnosticoIAHistorialDto> Historial { get; set; } = [];
        public bool EsPropietarioSolicitud { get; set; }
        public bool PuedeAnalizar { get; set; }
        public bool PuedeAprobar { get; set; }
        public bool PuedePublicarAlbum { get; set; }
        public int MaximoRevisionesGemini { get; set; } = 2;
        public bool RevisionesGeminiIlimitadas { get; set; }
        public int RevisionesGeminiCompletadas { get; set; }
        public bool PuedeSolicitarRevisionGemini { get; set; }
    }

    public sealed class DiagnosticoIACatalogosDto
    {
        public List<string> CalidadEvaluacion { get; set; } = [];
        public List<string> EstadosGenerales { get; set; } = [];
        public List<string> Categorias { get; set; } = [];
        public List<string> Severidades { get; set; } = [];
        public List<string> NivelesCerteza { get; set; } = [];
        public List<string> DecisionesAprobacion { get; set; } = [];
        public List<string> CalidadesImagen { get; set; } = [];
        public List<string> PartesPlantaSugeridas { get; set; } = [];
        public int MaximoFotografiasPorInspeccion { get; set; }
        public int TamanoBloqueIA { get; set; }
    }

    public sealed class DiagnosticoIAAlbumCategoriaDto
    {
        public int CategoriaAlbumBotanicoId { get; set; }
        public string NombreCategoria { get; set; } = string.Empty;
    }

    public sealed class DiagnosticoIAAlbumRegistroDto
    {
        public int AlbumBotanicoCafeId { get; set; }
        public int CategoriaAlbumBotanicoId { get; set; }
        public string Titulo { get; set; } = string.Empty;
    }

    public sealed class DiagnosticoIAAlbumCatalogoDto
    {
        public List<DiagnosticoIAAlbumCategoriaDto> Categorias { get; set; } = [];
        public List<DiagnosticoIAAlbumRegistroDto> Registros { get; set; } = [];
    }

    public sealed class DiagnosticoIAPublicacionResultadoDto
    {
        public int TotalPublicadas { get; set; }
        public int AlbumBotanicoCafeId { get; set; }
        public List<int> AlbumBotanicoCafeFotoIds { get; set; } = [];
    }
}
