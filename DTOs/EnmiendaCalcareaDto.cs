namespace CONATRADEC_API.DTOs
{
   
        public class EnmiendaCalcareaCrearDto
        {
            public string nombreAnalisis { get; set; } = string.Empty;

            public int fuenteNutrientesId { get; set; }

            public decimal ph { get; set; }
            public decimal ca { get; set; }
            public decimal mg { get; set; }
            public decimal k { get; set; }
            public decimal acidezTotal { get; set; }
        }

        public class EnmiendaCalcareaRespuestaDto
        {
            public int enmiendaCalcareaId { get; set; }
            public string nombreAnalisis { get; set; } = string.Empty;

            public string fuenteNutriente { get; set; } = string.Empty;

            public decimal ph { get; set; }
            public decimal ca { get; set; }
            public decimal mg { get; set; }
            public decimal k { get; set; }
            public decimal acidezTotal { get; set; }

            public decimal saturacionDeseada { get; set; }
            public decimal prnt { get; set; }

            public decimal sumaBases { get; set; }
            public decimal cice { get; set; }
            public decimal saturacionActual { get; set; }

            public decimal necesidadEncaladoTonHa { get; set; }
            public decimal necesidadEncaladoKgHa { get; set; }
            public decimal necesidadEncaladoLbHa { get; set; }
        }
}
