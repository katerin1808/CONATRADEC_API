using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public class FuenteNutrienteElementoQuimicoListarDto
    {
        public int fuenteNutrienteElementoQuimicoId { get; set; }
        public decimal cantidadAporte { get; set; }
        public bool activo { get; set; }

        public int fuenteNutrientesId { get; set; }
        public string nombreNutriente { get; set; } = string.Empty;
        public string descripcionNutriente { get; set; } = string.Empty;
        public decimal precioNutriente { get; set; }

        public int elementoQuimicosId { get; set; }
        public string nombreElementoQuimico { get; set; } = string.Empty;
        public string simboloElementoQuimico { get; set; } = string.Empty;
    }

    public class FuenteNutrienteElementoQuimicoCrearDto
    {
        [Required(ErrorMessage = "La cantidad de aporte es obligatoria.")]
        public decimal cantidadAporte { get; set; }

        [Required(ErrorMessage = "La fuente nutriente es obligatoria.")]
        public int fuenteNutrientesId { get; set; }

        [Required(ErrorMessage = "El elemento químico es obligatorio.")]
        public int elementoQuimicosId { get; set; }
    }

    public class FuenteNutrienteElementoQuimicoActualizarDto
    {
        [Required(ErrorMessage = "El id del registro es obligatorio.")]
        public int fuenteNutrienteElementoQuimicoId { get; set; }

        [Required(ErrorMessage = "La cantidad de aporte es obligatoria.")]
        public decimal cantidadAporte { get; set; }

        [Required(ErrorMessage = "La fuente nutriente es obligatoria.")]
        public int fuenteNutrientesId { get; set; }

        [Required(ErrorMessage = "El elemento químico es obligatorio.")]
        public int elementoQuimicosId { get; set; }
    }

    public class HabilitarEnmiendaCalcareaDto
    {
        public decimal prnt { get; set; }
        public string? descripcionParametro { get; set; }
    }
}
