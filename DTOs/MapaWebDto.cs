namespace CONATRADEC_API.DTOs
{
    public static class MapaWebDto
    {
        public sealed class TerrenoMapaDto
        {
            public int terrenoId { get; set; }
            public string codigo { get; set; } = string.Empty;
            public string nombre { get; set; } = string.Empty;
            public string productor { get; set; } = string.Empty;
            public decimal latitud { get; set; }
            public decimal longitud { get; set; }
            public int departamentoId { get; set; }
            public string departamento { get; set; } = string.Empty;
            public int municipioId { get; set; }
            public string municipio { get; set; } = string.Empty;
            public decimal extensionManzanas { get; set; }
            public decimal produccionQuintalesOro { get; set; }
            public string estado { get; set; } = "Sin análisis";
            public string nivelAlerta { get; set; } = "SIN_ANALISIS";
            public decimal? ultimoPh { get; set; }
            public decimal? materiaOrganica { get; set; }
            public decimal? acidezTotal { get; set; }
            public DateTime? fechaUltimoAnalisis { get; set; }
            public List<string> alertas { get; set; } = new();
            public string googleMapsUrl { get; set; } = string.Empty;
        }

        public sealed class MapaResumenDto
        {
            public int totalTerrenos { get; set; }
            public int conAnalisis { get; set; }
            public int sinAnalisis { get; set; }
            public int criticos { get; set; }
            public int atencion { get; set; }
            public int normales { get; set; }
            public decimal extensionVisibleManzanas { get; set; }
        }

        public sealed class MapaInteligenteRespuestaDto
        {
            public MapaResumenDto resumen { get; set; } = new();
            public List<TerrenoMapaDto> terrenos { get; set; } = new();
        }

        public sealed class AlertaAgricolaDto
        {
            public int terrenoId { get; set; }
            public string codigoTerreno { get; set; } = string.Empty;
            public string propietario { get; set; } = string.Empty;
            public int departamentoId { get; set; }
            public string departamento { get; set; } = string.Empty;
            public int municipioId { get; set; }
            public string municipio { get; set; } = string.Empty;
            public string nivel { get; set; } = string.Empty;
            public string tipo { get; set; } = string.Empty;
            public string mensaje { get; set; } = string.Empty;
            public decimal? valor { get; set; }
            public string unidad { get; set; } = string.Empty;
            public DateTime? fechaAnalisis { get; set; }
            public decimal latitud { get; set; }
            public decimal longitud { get; set; }
            public string googleMapsUrl { get; set; } = string.Empty;
        }

        public sealed class AlertasAgricolasPaginadaDto
        {
            public List<AlertaAgricolaDto> items { get; set; } = new();
            public int pagina { get; set; }
            public int tamanoPagina { get; set; }
            public int totalRegistros { get; set; }
            public int totalPaginas { get; set; }
            public int criticas { get; set; }
            public int atencion { get; set; }
            public int sinAnalisis { get; set; }
        }
    }
}
