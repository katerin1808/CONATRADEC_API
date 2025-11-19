namespace CONATRADEC_API.DTOs
{
    public class FuenteNutrienteElementoQuimicoDto
    {

        public class FuenteNutrienteElementoQuimicoListarDto
        {
            public int fuenteNutrienteElementoQuimicoId { get; set; }
            public decimal cantidadAporte { get; set; }

            public int fuenteNutrientesId { get; set; }
            public string? nombreNutriente { get; set; }

            public int elementoQuimicosId { get; set; }
            public string nombreElementoQuimico { get; set; }
        }


        public class FuenteNutrienteElementoQuimicoCrearDto
        {
            public decimal cantidadAporte { get; set; }
            public int fuenteNutrientesId { get; set; }
            public int  elementoQuimicosId { get; set; }
        }

        public class FuenteNutrienteElementoQuimicoActualizarDto
        {
            public int fuenteNutrienteElementoQuimicoId { get; set; }
            public decimal cantidadAporte { get; set; }
            public int fuenteNutrientesId { get; set; }
            public int elementoQuimicosId { get; set; }
        }
    }
}
