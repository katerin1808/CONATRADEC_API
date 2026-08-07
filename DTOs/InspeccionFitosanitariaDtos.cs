using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class InspeccionFitosanitariaCrearRequest
    {
        [Required, MaxLength(50)]
        public string CodigoTerreno { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Observacion { get; set; }

        [Required, MinLength(1)]
        public List<IFormFile> Fotos { get; set; } = [];

        public List<string> TiposFotografia { get; set; } = [];
        public List<string> FechasIdentificacionCampo { get; set; } = [];
    }

    public sealed class InspeccionFitosanitariaAgregarFotosRequest
    {
        [Required, MinLength(1)]
        public List<IFormFile> Fotos { get; set; } = [];
        public List<string> TiposFotografia { get; set; } = [];
        public List<string> FechasIdentificacionCampo { get; set; } = [];
    }

    public class InspeccionFotosSeleccionadasRequest
    {
        [Required, MinLength(1)]
        public List<int> FotografiaIds { get; set; } = [];
    }

    public sealed class InspeccionFotosRevisionIARequest :
        InspeccionFotosSeleccionadasRequest
    {
        [Required, MinLength(8), MaxLength(2000)]
        public string Retroalimentacion { get; set; } = string.Empty;

        [MaxLength(300)]
        public string? DiagnosticoPropuesto { get; set; }
    }

    public sealed class InspeccionFotosDescarteRequest :
        InspeccionFotosSeleccionadasRequest
    {
        [Required, MinLength(8), MaxLength(1000)]
        public string Motivo { get; set; } = string.Empty;
    }

    public sealed class InspeccionFotoAnalisisHumanoItemRequest
    {
        [Range(1, int.MaxValue)]
        public int FotografiaId { get; set; }
        [Required, MaxLength(30)]
        public string CalidadEvaluacion { get; set; } = "NO_EVALUABLE";
        [Required, MaxLength(40)]
        public string EstadoGeneral { get; set; } = "INDETERMINADA";
        [Required, MaxLength(50)]
        public string CategoriaPrincipal { get; set; } = "NO_APLICA";
        public List<string> CategoriasSecundarias { get; set; } = [];
        [Required, MaxLength(300)]
        public string Diagnostico { get; set; } = string.Empty;
        [MaxLength(80)]
        public string TipoDiagnostico { get; set; } = string.Empty;
        [Required, MaxLength(30)]
        public string Severidad { get; set; } = "NO_EVALUABLE";
        [Required, MaxLength(30)]
        public string NivelCerteza { get; set; } = "NO_DETERMINADO";
        [MaxLength(3000)]
        public string Observaciones { get; set; } = string.Empty;
    }

    public sealed class InspeccionFotosAnalisisHumanoRequest
    {
        [Required, MinLength(1)]
        public List<InspeccionFotoAnalisisHumanoItemRequest> Fotografias { get; set; } = [];
        public bool EnviarAprobacion { get; set; }
    }

    public sealed class InspeccionFotoAprobacionItemRequest
    {
        [Range(1, int.MaxValue)]
        public int FotografiaId { get; set; }
        [Required, MaxLength(40)]
        public string Decision { get; set; } = string.Empty;
        [MaxLength(30)]
        public string CalidadEvaluacionFinal { get; set; } = string.Empty;
        [MaxLength(40)]
        public string EstadoGeneralFinal { get; set; } = string.Empty;
        [MaxLength(50)]
        public string CategoriaPrincipalFinal { get; set; } = string.Empty;
        public List<string> CategoriasSecundariasFinales { get; set; } = [];
        [MaxLength(300)]
        public string DiagnosticoFinal { get; set; } = string.Empty;
        [MaxLength(80)]
        public string TipoDiagnosticoFinal { get; set; } = string.Empty;
        [MaxLength(30)]
        public string SeveridadFinal { get; set; } = string.Empty;
        [MaxLength(30)]
        public string NivelCertezaFinal { get; set; } = string.Empty;
        [MaxLength(3000)]
        public string Observaciones { get; set; } = string.Empty;
        public bool AutorizaPublicacionAlbum { get; set; }
    }

    public sealed class InspeccionFotosAprobacionRequest
    {
        [Required, MinLength(1)]
        public List<InspeccionFotoAprobacionItemRequest> Fotografias { get; set; } = [];
    }

    public sealed class InspeccionFotoPublicarAlbumRequest
    {
        [Range(1, int.MaxValue)]
        public int CategoriaAlbumBotanicoId { get; set; }
        [Range(1, int.MaxValue)]
        public int AlbumBotanicoCafeId { get; set; }
        [MaxLength(500)]
        public string Descripcion { get; set; } = string.Empty;
        public bool EsPortada { get; set; }
        public int Orden { get; set; }
    }

    public sealed class InspeccionOperacionItemDto
    {
        public int FotografiaId { get; set; }
        public bool Exitoso { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string Mensaje { get; set; } = string.Empty;
    }

    public sealed class InspeccionOperacionMasivaDto
    {
        public int TotalSolicitadas { get; set; }
        public int TotalExitosas { get; set; }
        public int TotalConError { get; set; }
        public List<InspeccionOperacionItemDto> Resultados { get; set; } = [];
    }

    public sealed class InspeccionFitosanitariaListaDto
    {
        public int InspeccionId { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public DateTime FechaRegistroSistemaUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public bool EtapaTecnicaFinalizada { get; set; }
        public DateTime? FechaFinEtapaTecnicaUtc { get; set; }
        public bool CerradaDefinitiva { get; set; }
        public DateTime? FechaCierreDefinitivoUtc { get; set; }
        public int TotalFotografias { get; set; }
        public int Pendientes { get; set; }
        public int ConError { get; set; }
        public int Finalizadas { get; set; }
        public string UrlMiniatura { get; set; } = string.Empty;
    }

    public sealed class InspeccionFotoResultadoIADto
    {
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
        public string ResumenImagen { get; set; } = string.Empty;
        public List<string> SintomasVisibles { get; set; } = [];
        public List<string> EvidenciasObservadas { get; set; } = [];
        public List<string> EvidenciasNoObservadas { get; set; } = [];
        public List<string> DiagnosticosAlternativos { get; set; } = [];
        public List<string> InformacionFaltante { get; set; } = [];
        public List<string> RecomendacionesCaptura { get; set; } = [];
        public List<string> Advertencias { get; set; } = [];
        public DateTime? FechaAnalisisIAUtc { get; set; }
    }

    public sealed class InspeccionFotoAnalisisHumanoDto
    {
        public int AnalisisHumanoId { get; set; }
        public int Version { get; set; }
        public int UsuarioAnalizadorId { get; set; }
        public string UsuarioAnalizador { get; set; } = string.Empty;
        public string EstadoRegistro { get; set; } = string.Empty;
        public string CalidadEvaluacion { get; set; } = string.Empty;
        public string EstadoGeneral { get; set; } = string.Empty;
        public string CategoriaPrincipal { get; set; } = string.Empty;
        public List<string> CategoriasSecundarias { get; set; } = [];
        public string Diagnostico { get; set; } = string.Empty;
        public string TipoDiagnostico { get; set; } = string.Empty;
        public string Severidad { get; set; } = string.Empty;
        public string NivelCerteza { get; set; } = string.Empty;
        public string Observaciones { get; set; } = string.Empty;
        public DateTime FechaCreacionUtc { get; set; }
        public DateTime? FechaEnvioUtc { get; set; }
    }

    public sealed class InspeccionFotoAprobacionDto
    {
        public int AprobacionId { get; set; }
        public int UsuarioAprobadorId { get; set; }
        public string UsuarioAprobador { get; set; } = string.Empty;
        public string Decision { get; set; } = string.Empty;
        public string DiagnosticoFinal { get; set; } = string.Empty;
        public string Observaciones { get; set; } = string.Empty;
        public bool AutorizaPublicacionAlbum { get; set; }
        public bool MismoUsuarioQueAnalizo { get; set; }
        public DateTime FechaAprobacionUtc { get; set; }
    }

    public sealed class InspeccionFotoHistorialDto
    {
        public int HistorialId { get; set; }
        public int UsuarioId { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string EstadoAnterior { get; set; } = string.Empty;
        public string EstadoNuevo { get; set; } = string.Empty;
        public string Accion { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;
        public DateTime FechaUtc { get; set; }
    }

    public sealed class InspeccionFotoDto
    {
        public int FotografiaId { get; set; }
        public int Orden { get; set; }
        public string TipoFotografia { get; set; } = string.Empty;
        public string NombreArchivoOriginal { get; set; } = string.Empty;
        public string UrlImagen { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public DateTime? FechaIdentificacionCampo { get; set; }
        public DateTime FechaRegistroSistemaUtc { get; set; }
        public DateTime? FechaAnalisisIAUtc { get; set; }
        public DateTime? FechaAnalisisHumanoUtc { get; set; }
        public DateTime? FechaAprobacionUtc { get; set; }
        public string ModeloIAUtilizado { get; set; } = string.Empty;
        public int IntentosIA { get; set; }
        public string ErrorProcesamiento { get; set; } = string.Empty;
        public bool Descartada { get; set; }
        public string MotivoDescarte { get; set; } = string.Empty;
        public bool PublicadaAlbum { get; set; }
        public InspeccionFotoResultadoIADto? ResultadoIA { get; set; }
        public InspeccionFotoAnalisisHumanoDto? UltimoAnalisisHumano { get; set; }
        public InspeccionFotoAprobacionDto? UltimaAprobacion { get; set; }
        public List<InspeccionFotoHistorialDto> Historial { get; set; } = [];
    }

    public sealed class InspeccionFitosanitariaDetalleDto
    {
        public int InspeccionId { get; set; }
        public int? TerrenoId { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public int UsuarioSolicitanteId { get; set; }
        public string UsuarioSolicitante { get; set; } = string.Empty;
        public string Observacion { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public DateTime FechaRegistroSistemaUtc { get; set; }
        public bool EtapaTecnicaFinalizada { get; set; }
        public DateTime? FechaFinEtapaTecnicaUtc { get; set; }
        public int? UsuarioFinEtapaTecnicaId { get; set; }
        public bool CerradaDefinitiva { get; set; }
        public DateTime? FechaCierreDefinitivoUtc { get; set; }
        public int? UsuarioCierreDefinitivoId { get; set; }
        public int? UsuarioAnalizadorAsignadoId { get; set; }
        public int? UsuarioAprobadorAsignadoId { get; set; }
        public string VersionAsignacion { get; set; } = string.Empty;
        public List<InspeccionFotoDto> Fotografias { get; set; } = [];
        public bool PuedeGestionarSolicitud { get; set; }
        public bool PuedeCerrarInspeccion { get; set; }
        public string MotivoNoPuedeCerrar { get; set; } = string.Empty;
        public bool PuedeAnalizar { get; set; }
        public bool PuedeAprobar { get; set; }
        public bool PuedePublicarAlbum { get; set; }
    }

    public sealed class InspeccionAlbumFichaDto
    {
        public int AlbumBotanicoCafeId { get; set; }
        public int CategoriaAlbumBotanicoId { get; set; }
        public string Titulo { get; set; } = string.Empty;
        public string NombreCientifico { get; set; } = string.Empty;
    }

    public sealed class InspeccionAlbumCategoriaDto
    {
        public int CategoriaAlbumBotanicoId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public List<InspeccionAlbumFichaDto> Fichas { get; set; } = [];
    }

    public sealed class ProveedorIAConfiguracionActualizarRequest
    {
        [Required, MaxLength(40)]
        public string Proveedor { get; set; } = "GEMINI";
        [Required, MaxLength(40)]
        public string Protocolo { get; set; } = "GEMINI_NATIVO";
        [Required, MaxLength(500)]
        public string BaseUrl { get; set; } = string.Empty;
        [Required, MaxLength(300)]
        public string Endpoint { get; set; } = string.Empty;
        [MaxLength(2000)]
        public string? ApiKey { get; set; }
        [Required, MaxLength(160)]
        public string ModeloPrincipal { get; set; } = string.Empty;
        [MaxLength(160)]
        public string ModeloRespaldo { get; set; } = string.Empty;
        [Range(15, 600)]
        public int TimeoutSegundos { get; set; } = 180;
        public bool Activo { get; set; } = true;
    }

    public sealed class ProveedorIAConfiguracionDto
    {
        public string Proveedor { get; set; } = string.Empty;
        public string Protocolo { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string ApiKeyMascara { get; set; } = string.Empty;
        public bool TieneApiKey { get; set; }
        public string ModeloPrincipal { get; set; } = string.Empty;
        public string ModeloRespaldo { get; set; } = string.Empty;
        public int TimeoutSegundos { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaModificacionUtc { get; set; }
        public int? UsuarioModificacionId { get; set; }
    }

    public sealed class ProveedorIAPruebaDto
    {
        public bool Exitoso { get; set; }
        public int CodigoHttp { get; set; }
        public string Proveedor { get; set; } = string.Empty;
        public string Modelo { get; set; } = string.Empty;
        public string Mensaje { get; set; } = string.Empty;
        public long Milisegundos { get; set; }
    }
}
