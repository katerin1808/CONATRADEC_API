namespace CONATRADEC_API.DTOs
{
    public sealed class RangoNutrienteCultivoResumenDto
    {
        public int tipoCultivoId { get; set; }
        public string nombreCategoria { get; set; } = string.Empty;
        public string descripcionCategoria { get; set; } = string.Empty;
        public int cantidadAportes { get; set; }
    }

    public sealed class RangoNutrienteCultivoPaginaResponse
    {
        public List<RangoNutrienteCultivoResumenDto> Items { get; set; } = new();
        public int PaginaActual { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalRegistros { get; set; }
        public int TotalPaginas { get; set; }
    }

    public sealed class RangoNutrienteConsultaDto
    {
        public int parametroRangoNutrienteCultivoId { get; set; }
        public int tipoCultivoId { get; set; }
        public string nombreTipoCultivo { get; set; } = string.Empty;
        public int elementoQuimicosId { get; set; }
        public string nombreElementoQuimico { get; set; } = string.Empty;
        public string simboloElementoQuimico { get; set; } = string.Empty;
        public decimal valorMinimo { get; set; }
        public decimal valorMaximo { get; set; }
        public string unidadBase { get; set; } = "lb/Mz";
        public string descripcionParametro { get; set; } = string.Empty;
        public bool activo { get; set; }
    }

    public sealed class RangoNutrientePaginaResponse
    {
        public List<RangoNutrienteConsultaDto> Items { get; set; } = new();
        public int PaginaActual { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalRegistros { get; set; }
        public int TotalPaginas { get; set; }
    }

    public sealed class ElementoQuimicoDisponibleDto
    {
        public int elementoQuimicosId { get; set; }
        public string nombreElementoQuimico { get; set; } = string.Empty;
        public string simboloElementoQuimico { get; set; } = string.Empty;
    }
}
