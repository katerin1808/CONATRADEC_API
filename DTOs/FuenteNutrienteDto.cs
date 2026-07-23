namespace CONATRADEC_API.DTOs
{
    public class FuenteNutrienteConElementosCrearDto
    {
        public string nombreNutriente { get; set; } =
            string.Empty;

        public string descripcionNutriente { get; set; } =
            string.Empty;

        public decimal precioNutriente { get; set; }

        public List<ElementoFuenteCrearDto>
            elementosQuimicos { get; set; } = new();
    }

    public class ElementoFuenteCrearDto
    {
        public int elementoQuimicosId { get; set; }

        public decimal cantidadAporte { get; set; }
    }

    public class FuenteNutrienteConElementosRespuestaDto
    {
        public int fuenteNutrientesId { get; set; }

        public string nombreNutriente { get; set; } =
            string.Empty;

        public string descripcionNutriente { get; set; } =
            string.Empty;

        public decimal precioNutriente { get; set; }

        public bool activo { get; set; }

        public bool habilitadaEnmiendaCalcarea { get; set; }

        public bool habilitadaFertilizacionMixta { get; set; }

        public decimal? prnt { get; set; }

        public string? descripcionParametro { get; set; }

        public List<ElementoFuenteRespuestaDto>
            elementosQuimicos { get; set; } = new();

        public List<ParametroEnmiendaCalcareaFuenteDto>
            parametrosEnmiendaCalcarea { get; set; } = new();
    }

    public class FuenteFertilizacionMixtaListadoDto
    {
        public int fuenteNutrientesId { get; set; }

        public string nombreNutriente { get; set; } =
            string.Empty;

        public string? descripcionNutriente { get; set; }

        public decimal precioNutriente { get; set; }

        public bool activo { get; set; }

        public List<ElementoFuenteRespuestaDto>
            elementosQuimicos { get; set; } = new();
    }

    public class ParametroEnmiendaCalcareaFuenteDto
    {
        public decimal prnt { get; set; }

        public string? descripcionParametro { get; set; }
    }

    public class ElementoFuenteRespuestaDto
    {
        public int fuenteNutrienteElementoQuimicoId
        {
            get;
            set;
        }

        public int elementoQuimicosId { get; set; }

        public string nombreElementoQuimico { get; set; } =
            string.Empty;

        public string simboloElementoQuimico { get; set; } =
            string.Empty;

        public decimal cantidadAporte { get; set; }
    }

    public class FuenteNutrienteAporteTablaDto
    {
        public int fuenteNutrientesId { get; set; }

        public string fuente { get; set; } =
            string.Empty;

        public decimal n { get; set; }
        public decimal p { get; set; }
        public decimal k { get; set; }
        public decimal ca { get; set; }
        public decimal mg { get; set; }
        public decimal zn { get; set; }
        public decimal s { get; set; }
        public decimal b { get; set; }
    }
}
