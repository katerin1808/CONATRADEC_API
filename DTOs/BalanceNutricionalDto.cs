namespace CONATRADEC_API.DTOs
{
    public class BalanceNutricionalDto
    {
        public class BalanceNutricionalCrearDto
        {
            public string? nombreFormula { get; set; }
            public int totalPlantas { get; set; }
            public int totalAplicaciones { get; set; }
            public List<BalanceNutricionalItemDto> items { get; set; } = new();
        }

        public class BalanceNutricionalItemDto
        {
            public int elementoQuimicosId { get; set; }
            public int fuenteNutrientesId { get; set; }
            public decimal requerimientoLibras { get; set; }
        }

        public class BalanceNutricionalRespuestaDto
        {
            public int balanceNutricionalId { get; set; }
            public string? nombreFormula { get; set; }

            public decimal totalMezclaLb { get; set; }
            public decimal totalMezclaOz { get; set; }

            public decimal librasPorDosAplicaciones { get; set; }
            public decimal librasPorTresAplicaciones { get; set; }

            public int totalPlantas { get; set; }
            public decimal dosisPlantaAnualOz { get; set; }

            public AplicacionResumenDto dosAplicaciones { get; set; } = new();
            public AplicacionResumenDto tresAplicaciones { get; set; } = new();

            public List<BalanceNutricionalDetalleRespuestaDto> detalle { get; set; } = new();
        }

        public class AplicacionResumenDto
        {
            public decimal dosisPlantaOz { get; set; }
        }

        public class BalanceNutricionalDetalleRespuestaDto
        {
            public string fuente { get; set; } = string.Empty;
            public string elemento { get; set; } = string.Empty;

            public decimal requerimientoLibras { get; set; }

            public decimal librasAnuales { get; set; }
            public decimal onzasAnuales { get; set; }

            public decimal dosAplicaciones { get; set; }
            public decimal tresAplicaciones { get; set; }
        }
    }
}