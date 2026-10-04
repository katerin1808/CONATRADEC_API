namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// Solicitud V2 para resolver la clasificación del Álbum Botánico de un
    /// diagnóstico individual dentro de una fotografía fitosanitaria.
    /// </summary>
    public sealed class ResolverClasificacionDiagnosticoFitosanitarioV2Request
    {
        public string DiagnosticoClave { get; set; } = string.Empty;
        public string Etapa { get; set; } = "ANALIZADOR";
        public string Accion { get; set; } = "CONFIRMAR";
        public int? CategoriaAlbumBotanicoId { get; set; }
        public int? AlbumBotanicoCafeId { get; set; }
        public bool ProponerSubcategoria { get; set; }
        public string Categoria { get; set; } = string.Empty;
        public string Subcategoria { get; set; } = string.Empty;
        public string NombreCientifico { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Sintomas { get; set; } = string.Empty;
        public string Motivo { get; set; } = string.Empty;
    }

    public sealed class SincronizarPublicacionesDiagnosticosRequest
    {
        public List<PublicacionDiagnosticoSeleccionRequest> Diagnosticos { get; set; } = [];
    }

    public sealed class PublicacionDiagnosticoSeleccionRequest
    {
        public string DiagnosticoClave { get; set; } = string.Empty;
        public bool Publicar { get; set; }
        public string Descripcion { get; set; } = string.Empty;
    }

    public sealed class PublicarDiagnosticosResueltosRequest
    {
        public string Descripcion { get; set; } = string.Empty;
    }

    public sealed class PublicacionDiagnosticoEstadoDto
    {
        public int FotografiaId { get; set; }
        public string DiagnosticoClave { get; set; } = string.Empty;
        public string DiagnosticoIdOrigenIA { get; set; } = string.Empty;
        public int OrdenDiagnostico { get; set; }
        public bool EsPrincipal { get; set; }
        public string Diagnostico { get; set; } = string.Empty;
        public string CategoriaIA { get; set; } = string.Empty;
        public string TipoDiagnosticoIA { get; set; } = string.Empty;

        public int? CategoriaAlbumBotanicoIdSugerida { get; set; }
        public int? AlbumBotanicoCafeIdSugerido { get; set; }
        public string CategoriaSugerida { get; set; } = string.Empty;
        public string SubcategoriaSugerida { get; set; } = string.Empty;
        public string NombreCientificoSugerido { get; set; } = string.Empty;
        public bool CoincideCatalogo { get; set; }
        public bool RequiereDecision { get; set; }

        public int? CategoriaAlbumBotanicoIdSeleccionada { get; set; }
        public int? AlbumBotanicoCafeIdSeleccionado { get; set; }
        public string CategoriaSeleccionada { get; set; } = string.Empty;
        public string SubcategoriaSeleccionada { get; set; } = string.Empty;

        public string AccionHumana { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public string FuenteVigente { get; set; } = string.Empty;
        public bool Activo { get; set; }

        public bool PublicadaActiva { get; set; }
        public bool TuvoPublicacion { get; set; }
        public bool PublicarEnAlbum { get; set; }
        public int? DiagnosticoIAAlbumPublicacionDiagnosticoId { get; set; }
        public int? AlbumBotanicoCafeFotoId { get; set; }
        public DateTime? FechaPublicacionUtc { get; set; }
        public string DescripcionPublicacion { get; set; } = string.Empty;

        public string ColorMarcador { get; set; } = "#E53935";
        public List<AlbumFitosanitarioLesionDto> Lesiones { get; set; } = [];
        public int AnchoImagen { get; set; }
        public int AltoImagen { get; set; }

        public bool TieneSenalizacion => Lesiones.Count > 0;
        public int TotalLesiones => Lesiones.Count;
        public bool PuedePublicarse =>
            Activo &&
            !string.Equals(
                Estado,
                "DESCARTADA_DIAGNOSTICO",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                Estado,
                "RESUELTA_APROBADOR",
                StringComparison.OrdinalIgnoreCase) &&
            CategoriaAlbumBotanicoIdSeleccionada is > 0 &&
            AlbumBotanicoCafeIdSeleccionado is > 0;
    }

    public sealed class AlbumFitosanitarioLesionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public List<int> Box2d { get; set; } = [];
    }

    public sealed class AlbumFitosanitarioCapaDto
    {
        public string DiagnosticoClave { get; set; } = string.Empty;
        public string Diagnostico { get; set; } = string.Empty;
        public bool EsPrincipal { get; set; }
        public string ColorMarcador { get; set; } = "#E53935";
        public List<AlbumFitosanitarioLesionDto> Lesiones { get; set; } = [];
    }

    public sealed class AlbumFotoSenalizacionDto
    {
        public int AlbumBotanicoCafeFotoId { get; set; }
        public int AlbumBotanicoCafeId { get; set; }
        public int DiagnosticoIAId { get; set; }
        public int DiagnosticoIAImagenId { get; set; }
        public int? TerrenoId { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public int AnchoImagen { get; set; }
        public int AltoImagen { get; set; }
        public List<AlbumFitosanitarioCapaDto> Capas { get; set; } = [];

        public bool EsFitosanitaria => true;
        public bool TieneSenalizacion =>
            Capas.Any(capa => capa.Lesiones.Count > 0);
    }
}
