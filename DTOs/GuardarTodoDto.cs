using System.Text.Json;
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

        /*
         * Metadatos de trazabilidad enviados por Android/Windows.
         * Son opcionales para mantener compatibilidad con versiones anteriores.
         */
        public DateTime? fechaCreacionClienteUtc { get; set; }

        public DateTime? fechaOperacionClienteUtc { get; set; }

        public int? versionRegistro { get; set; }

        public string? origenRegistro { get; set; }

        public string? etagBase { get; set; }

        /*
         * Fotografía exacta del reporte calculado por el cliente.
         * Es especialmente importante para operaciones offline, porque evita
         * reconstruir Balance, Enmienda o Mixta con catálogos modificados.
         */
        public JsonElement? reporteHistoricoCliente { get; set; }

        /*
         * Los cálculos complementarios son opcionales.
         * No deben inicializarse con new(), porque un objeto vacío
         * hace que la API intente guardar un cálculo no seleccionado.
         */
        public FormulaNutricionalGuardarDto? balanceNutricional { get; set; }

        public EnmiendaCalcareaGuardarDto? enmiendaCalcarea { get; set; }

        public FertilizacionMixtaRespuestaDto? fertilizacionMixta { get; set; }
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
