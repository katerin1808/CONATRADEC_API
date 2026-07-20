using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public class ParametroExtraccionNutrienteDto
    {
        public class CrearParametroExtraccionNutrienteCafeDto
        {
            public int elementoQuimicosId { get; set; }

            public decimal cantidadExtraidaPorQQOro { get; set; }

            [Required(ErrorMessage = "La descripción es obligatoria.")]
            [MaxLength(150)]
            public string descripcionParametro { get; set; } = string.Empty;
        }

        public class ActualizarParametroExtraccionNutrienteCafeDto
        {
            public int elementoQuimicosId { get; set; }

            public decimal cantidadExtraidaPorQQOro { get; set; }

            [Required(ErrorMessage = "La descripción es obligatoria.")]
            [MaxLength(150)]
            public string descripcionParametro { get; set; } = string.Empty;
        }
    }
}
