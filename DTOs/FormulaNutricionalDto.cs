namespace CONATRADEC_API.DTOs
{
    public class FormulaNutricionalDto
    {
        public class FormulaNutricionalCrearDto
        {
            public string? nombreFormula { get; set; }
            public List<FormulaNutricionalItemDto> items { get; set; } = new();
        }

        public class FormulaNutricionalItemDto
        {
            public int fuenteNutrientesId { get; set; }
            public int elementoQuimicosId { get; set; }
            public decimal libras { get; set; }
        }

        public class FormulaNutricionalRespuestaDto
        {
            public int formulaNutricionalId { get; set; }
            public string? nombreFormula { get; set; }
            public decimal totalLibras { get; set; }
            public decimal mezclaTotalQq { get; set; }

            public Dictionary<string, decimal> formulaComercial { get; set; } = new();

            public List<FormulaNutricionalDetalleRespuestaDto> detalle { get; set; } = new();
        }

        public class FormulaNutricionalDetalleRespuestaDto
        {
            public string fuente { get; set; } = string.Empty;
            public string elemento { get; set; } = string.Empty;
            public decimal lb { get; set; }
            public decimal qq { get; set; }

            public Dictionary<string, decimal> aportes { get; set; } = new();
        }
    }
}
