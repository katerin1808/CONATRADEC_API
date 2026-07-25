namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// Respuesta paginada utilizada únicamente por la pantalla
    /// administrativa de fuentes de nutrientes.
    /// </summary>
    public sealed class FuenteNutrientePaginaResponse
    {
        public List<FuenteNutrienteConElementosRespuestaDto> Items
        {
            get;
            set;
        } = new();

        public int PaginaActual { get; set; }

        public int TamanoPagina { get; set; }

        public int TotalRegistros { get; set; }

        public int TotalPaginas { get; set; }
    }
}
