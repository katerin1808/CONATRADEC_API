namespace CONATRADEC_API.DTOs
{
    public class ParametroRangoNutrienteCultivoDto
    {

        public class CrearParametroRangoNutrienteCultivoDto
        {
            public int tipoCultivoId { get; set; }
            public int elementoQuimicosId { get; set; }
            public decimal valorMinimo { get; set; }
            public decimal valorMaximo { get; set; }
            public string unidadBase { get; set; } = string.Empty;
            public string descripcionParametro { get; set; } = string.Empty;
        }
        public class ActualizarParametroRangoNutrienteCultivoDto
        {
            public int tipoCultivoId { get; set; }
            public int elementoQuimicosId { get; set; }
            public decimal valorMinimo { get; set; }
            public decimal valorMaximo { get; set; }
            public string unidadBase { get; set; } = string.Empty;
            public string descripcionParametro { get; set; } = string.Empty;
        }
    }
}
