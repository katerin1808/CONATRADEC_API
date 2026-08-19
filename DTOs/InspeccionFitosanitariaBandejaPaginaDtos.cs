namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// Contrato numerado usado por Solicitudes e Historial. El contrato por
    /// cursor existente se conserva sin cambios para clientes anteriores y
    /// para las bandejas operativas que todavía lo utilizan.
    /// </summary>
    public sealed class InspeccionFitosanitariaBandejaPaginaNumeradaDto
    {
        public List<InspeccionFitosanitariaBandejaItemDto> Items { get; set; } = [];
        public int Pagina { get; set; } = 1;
        public int TamanoPagina { get; set; } = 20;
        public int Total { get; set; }
        public int TotalPaginas { get; set; }
    }
}
