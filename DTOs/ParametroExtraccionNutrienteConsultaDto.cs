namespace CONATRADEC_API.DTOs
{
    public sealed class ParametroExtraccionNutrienteConsultaDto
    {
        public int parametroExtraccionNutrienteCafeId { get; set; }
        public int elementoQuimicosId { get; set; }
        public string nombreElementoQuimico { get; set; } = string.Empty;
        public string simboloElementoQuimico { get; set; } = string.Empty;
        public decimal cantidadExtraidaPorQQOro { get; set; }
        public string descripcionParametro { get; set; } = string.Empty;
        public bool activo { get; set; }
    }

    public sealed class ParametroExtraccionNutrientePaginaResponse
    {
        public List<ParametroExtraccionNutrienteConsultaDto> Items { get; set; } = new();
        public int PaginaActual { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalRegistros { get; set; }
        public int TotalPaginas { get; set; }
    }
}
