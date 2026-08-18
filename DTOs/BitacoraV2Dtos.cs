namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// Respuesta paginada estable de la bitácora. CorteConsultaUtc congela el
    /// conjunto visible durante una búsqueda para que los nuevos registros de
    /// auditoría no desplacen páginas ya consultadas.
    /// </summary>
    public sealed class BitacoraPaginadaV2Dto
    {
        public List<BitacoraListadoDto> Items { get; set; } = new();
        public int Pagina { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalRegistros { get; set; }
        public int TotalPaginas { get; set; }
        public DateTime CorteConsultaUtc { get; set; }
    }
}
