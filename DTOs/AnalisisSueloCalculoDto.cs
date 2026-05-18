using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public class AnalisisSueloCalculoRequestDto
    {
        [Required]
        public int terrenoId { get; set; }

        [Required]
        public int tipoCultivoId { get; set; }

        [Required]
        public int tipoAnalisisSueloId { get; set; }

        public int? usuarioId { get; set; }

        [Required]
        public decimal cantidadQuintalesOro { get; set; }

        [Required]
        public decimal tamanoFinca { get; set; }

        [Required]
        public decimal ph { get; set; }

        public decimal? acidezTotal { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Debe ingresar al menos un elemento químico.")]
        public List<AnalisisSueloElementoEntradaDto> elementosQuimicos { get; set; } = new();

        public List<FuenteOrganicaEntradaDto> fuentesOrganicas { get; set; } = new();
    }

    public class AnalisisSueloGuardarRequestDto : AnalisisSueloCalculoRequestDto
    {
        [Required]
        public DateOnly fechaAnalisisSuelo { get; set; }

        [Required]
        [MaxLength(80)]
        public string laboratorioAnalasisSuelo { get; set; } = null!;

        [Required]
        [MaxLength(50)]
        public string identificadorAnalisisSuelo { get; set; } = null!;
    }

    public class AnalisisSueloElementoEntradaDto
    {
        [Required]
        public int elementoQuimicosId { get; set; }

        [Required]
        public int unidadMedidaId { get; set; }

        [Required]
        public decimal cantidadElemento { get; set; }
    }

    public class FuenteOrganicaEntradaDto
    {
        [Required]
        public int fuenteNutrientesId { get; set; }

        [Required]
        public decimal cantidadAplicada { get; set; }
    }

    public class AnalisisSueloCalculoResponseDto
    {
        public int terrenoId { get; set; }

        public int tipoCultivoId { get; set; }
        public string tipoCultivo { get; set; } = null!;

        public int tipoAnalisisSueloId { get; set; }
        public string tipoAnalisisSuelo { get; set; } = null!;

        public decimal cantidadQuintalesOro { get; set; }
        public decimal tamanoFinca { get; set; }

        public decimal ph { get; set; }
        public decimal? acidezTotal { get; set; }

        public List<ResultadoElementoCalculoDto> elementos { get; set; } = new();

        public List<ResultadoFuenteFertilizanteDto> fuentesFertilizantes { get; set; } = new();

        public ResultadoEnmiendaCalcareaDto? enmiendaCalcarea { get; set; }

        public List<ResultadoFuenteOrganicaDto> fuentesOrganicas { get; set; } = new();

        public string recomendacionGeneral { get; set; } = null!;

        public List<string> observaciones { get; set; } = new();
    }

    public class ResultadoElementoCalculoDto
    {
        public int elementoQuimicosId { get; set; }

        public string simboloElementoQuimico { get; set; } = null!;

        public string nombreElementoQuimico { get; set; } = null!;

        public decimal cantidadIngresada { get; set; }

        public decimal? extraccionPorQQOro { get; set; }

        public decimal? extraccionPorProduccion { get; set; }

        public decimal? rangoMinimo { get; set; }

        public decimal? rangoMaximo { get; set; }

        public decimal? requerimientoCalculado { get; set; }

        public string? unidadBase { get; set; }

        public string observacion { get; set; } = null!;
    }

    public class ResultadoFuenteFertilizanteDto
    {
        public int fuenteNutrientesId { get; set; }

        public string nombreNutriente { get; set; } = null!;

        public decimal cantidadFuente { get; set; }

        public string unidadResultado { get; set; } = "lb";

        public List<AporteFuenteElementoDto> aportes { get; set; } = new();
    }

    public class AporteFuenteElementoDto
    {
        public int elementoQuimicosId { get; set; }

        public string simboloElementoQuimico { get; set; } = null!;

        public decimal porcentajeAporte { get; set; }

        public decimal cantidadAportada { get; set; }
    }

    public class ResultadoEnmiendaCalcareaDto
    {
        public int fuenteNutrientesId { get; set; }

        public string nombreFuente { get; set; } = null!;

        public decimal calcio { get; set; }

        public decimal magnesio { get; set; }

        public decimal potasio { get; set; }

        public decimal acidezTotal { get; set; }

        public decimal cice { get; set; }

        public decimal sumaBases { get; set; }

        public decimal saturacionBasesActual { get; set; }

        public decimal saturacionBasesDeseada { get; set; }

        public decimal prnt { get; set; }

        public decimal necesidadEnmiendaTonHa { get; set; }

        public decimal necesidadEnmiendaLbHa { get; set; }

        public decimal necesidadEnmiendaKgHa { get; set; }

        public decimal necesidadEnmiendaLbMz { get; set; }
    }

    public class ResultadoFuenteOrganicaDto
    {
        public int fuenteNutrientesId { get; set; }

        public string nombreFuente { get; set; } = null!;

        public decimal cantidadAplicada { get; set; }

        public List<AporteOrganicoElementoDto> aportes { get; set; } = new();
    }

    public class AporteOrganicoElementoDto
    {
        public int elementoQuimicosId { get; set; }

        public string simboloElementoQuimico { get; set; } = null!;

        public decimal cantidadAportePorUnidad { get; set; }

        public decimal cantidadAportada { get; set; }
    }
}