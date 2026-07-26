using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class ConfiguracionFormularioAnalisisDto
    {
        public int unidadResultadoId { get; set; }

        public string unidadResultado { get; set; } =
            "lb/Mz";

        public List<UnidadConversionConfiguradaDto>
            unidadesMateriaOrganica { get; set; } = new();

        public List<ElementoConfiguracionUnidadesDto>
            elementos { get; set; } = new();
    }

    public sealed class ElementoConfiguracionUnidadesDto
    {
        public int elementoQuimicosId { get; set; }

        public string simboloElementoQuimico { get; set; } =
            string.Empty;

        public string nombreElementoQuimico { get; set; } =
            string.Empty;

        public decimal pesoEquivalenteElementoQuimico { get; set; }

        public int? unidadPredeterminadaId { get; set; }

        public List<UnidadConversionConfiguradaDto>
            unidades { get; set; } = new();
    }

    public sealed class UnidadConversionConfiguradaDto
    {
        public int configuracionId { get; set; }

        public int unidadMedidaId { get; set; }

        public string nombreUnidadMedida { get; set; } =
            string.Empty;

        public string codigoFormulaConversion { get; set; } =
            "LINEAL";

        public decimal factorPrincipal { get; set; } = 1m;

        public decimal factorSecundario { get; set; } = 1m;

        public decimal factorTerciario { get; set; } = 1m;

        public decimal divisor { get; set; } = 1m;

        public decimal desplazamiento { get; set; }

        public bool unidadPredeterminada { get; set; }

        public bool visibleEnFormulario { get; set; } = true;

        public int orden { get; set; }

        public string observacion { get; set; } =
            string.Empty;

        public bool activo { get; set; }
    }

    public sealed class GuardarConfiguracionElementoUnidadesDto
    {
        [Required]
        [MinLength(
            1,
            ErrorMessage =
                "Debe configurar al menos una unidad.")]
        public List<GuardarUnidadConversionDto>
            unidades { get; set; } = new();
    }

    public sealed class GuardarConfiguracionMateriaOrganicaDto
    {
        [Required]
        [MinLength(
            1,
            ErrorMessage =
                "Debe configurar al menos una unidad.")]
        public List<GuardarUnidadConversionDto>
            unidades { get; set; } = new();
    }

    public sealed class GuardarUnidadConversionDto
    {
        [Range(
            1,
            int.MaxValue,
            ErrorMessage =
                "La unidad de medida no es válida.")]
        public int unidadMedidaId { get; set; }

        [Required]
        [MaxLength(50)]
        public string codigoFormulaConversion { get; set; } =
            "LINEAL";

        public decimal factorPrincipal { get; set; } = 1m;

        public decimal factorSecundario { get; set; } = 1m;

        public decimal factorTerciario { get; set; } = 1m;

        public decimal divisor { get; set; } = 1m;

        public decimal desplazamiento { get; set; }

        public bool unidadPredeterminada { get; set; }

        public bool visibleEnFormulario { get; set; } = true;

        public int orden { get; set; }

        [MaxLength(300)]
        public string? observacion { get; set; }

        public bool activo { get; set; } = true;
    }

    public sealed class ProbarConversionUnidadDto
    {
        [Required]
        [MaxLength(30)]
        public string contexto { get; set; } =
            "ELEMENTO";

        public int? elementoQuimicosId { get; set; }

        [Range(
            1,
            int.MaxValue,
            ErrorMessage =
                "La unidad de medida no es válida.")]
        public int unidadMedidaId { get; set; }

        [Range(
            0,
            double.MaxValue,
            ErrorMessage =
                "El valor reportado no puede ser negativo.")]
        public decimal valorReportado { get; set; }

        public decimal? materiaOrganicaPorcentaje { get; set; }
    }

    public sealed class ResultadoPruebaConversionDto
    {
        public string contexto { get; set; } =
            string.Empty;

        public int? elementoQuimicosId { get; set; }

        public string elemento { get; set; } =
            string.Empty;

        public int unidadOrigenId { get; set; }

        public string unidadOrigen { get; set; } =
            string.Empty;

        public int unidadDestinoId { get; set; }

        public string unidadDestino { get; set; } =
            string.Empty;

        public decimal valorReportado { get; set; }

        public decimal valorConvertido { get; set; }

        public string codigoFormulaConversion { get; set; } =
            string.Empty;

        public string descripcion { get; set; } =
            string.Empty;
    }

    public sealed class FormulaConversionDisponibleDto
    {
        public string codigo { get; set; } =
            string.Empty;

        public string nombre { get; set; } =
            string.Empty;

        public string descripcion { get; set; } =
            string.Empty;

        public bool requiereElementoQuimico { get; set; }

        public bool requiereMateriaOrganica { get; set; }
    }
}
