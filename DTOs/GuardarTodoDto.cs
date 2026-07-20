using static CONATRADEC_API.DTOs.FertilizacionMixtaDto;
using static CONATRADEC_API.DTOs.FormulaNutricionalDto;

namespace CONATRADEC_API.DTOs
{
    public class GuardarTodoDto
    {
        public AnalisisSueloGuardarRequestDto datosAnalisis { get; set; }
            = new();

        public AnalisisSueloCalculoResponseDto requerimientoAnual { get; set; }
            = new();

        public FormulaNutricionalGuardarDto? balanceNutricional { get; set; }
            = new();

        public EnmiendaCalcareaGuardarDto? enmiendaCalcarea { get; set; }
            = new();

        public FertilizacionMixtaRespuestaDto? fertilizacionMixta { get; set; }
            = new();
    }

    public class FormulaNutricionalGuardarDto
    {
        public int terrenoId { get; set; }

        public bool esComplementoFertilizacionMixta { get; set; }

        public FormulaNutricionalRespuestaDto resultado { get; set; }
            = new();

        public List<FormulaNutricionalGuardarItemDto> items { get; set; }
            = new();
    }

    public class FormulaNutricionalGuardarItemDto
    {
        public int fuenteNutrientesId { get; set; }

        public int elementoQuimicosId { get; set; }

        public decimal libras { get; set; }
    }

    public class EnmiendaCalcareaGuardarDto
    {
        public int fuenteNutrientesId { get; set; }

        public EnmiendaCalcareaRespuestaDto resultado { get; set; }
            = new();
    }

    public class GuardarTodoRespuestaDto
    {
        public int analisisSueloId { get; set; }

        public int analisisSueloCalculoId { get; set; }

        public int? formulaNutricionalId { get; set; }

        public int? enmiendaCalcareaId { get; set; }

        public int? fertilizacionMixtaId { get; set; }
    }
}
