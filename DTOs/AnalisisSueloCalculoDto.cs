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

        [Required]
        public decimal materiaOrganica { get; set; }

        public int unidadMedidaMateriaOrganicaId { get; set; }

        public decimal acidezTotal { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Debe ingresar al menos un elemento químico.")]
        public List<AnalisisSueloElementoEntradaDto> elementosQuimicos { get; set; } = new();

    
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
        public decimal acidezTotal { get; set; }

        public decimal materiaOrganica { get; set; }

        public int unidadMedidaMateriaOrganicaId { get; set; }

        public List<ResultadoElementoCalculoDto> elementos { get; set; } = new();


        public string recomendacionGeneral { get; set; } = null!;

        public List<string> observaciones { get; set; } = new();
    }

    public class ResultadoElementoCalculoDto
    {
        public int elementoQuimicosId { get; set; }

        public string simboloElementoQuimico { get; set; } = null!;

        public string nombreElementoQuimico { get; set; } = null!;

        public decimal cantidadIngresada { get; set; }

        public decimal? cantidadConvertidaLbMz { get; set; }

        public decimal? extraccionPorQQOro { get; set; }

        public decimal? extraccionPorProduccion { get; set; }

        public decimal? rangoMinimo { get; set; }

        public decimal? rangoMaximo { get; set; }

        public decimal? rangoMinimoLbMz { get; set; }

        public decimal? rangoMaximoLbMz { get; set; }

        public decimal? requerimientoCalculado { get; set; }

        public string? unidadBase { get; set; }

        public int? unidadMedidaResultadoId { get; set; }

        public string? unidadResultado { get; set; }

        public string? clasificacion { get; set; }

        public string observacion { get; set; } = null!;
    }

 

    public class AporteFuenteElementoDto
    {
        public int elementoQuimicosId { get; set; }

        public string simboloElementoQuimico { get; set; } = null!;

        public decimal porcentajeAporte { get; set; }

        public decimal cantidadAportada { get; set; }
    }


    public class AporteOrganicoElementoDto
    {
        public int elementoQuimicosId { get; set; }

        public string simboloElementoQuimico { get; set; } = null!;

        public decimal cantidadAportePorUnidad { get; set; }

        public decimal cantidadAportada { get; set; }
    }
}