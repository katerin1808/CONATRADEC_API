namespace CONATRADEC_API.DTOs
{
    public class FertilizacionMixtaDto
    {
        public class FertilizacionMixtaCrearDto
        {
            public int analisisSueloCalculoId { get; set; }

            public string? observacion { get; set; }

            public List<FertilizacionMixtaFuenteCrearDto> fuentes { get; set; } = new();
        }

        public class FertilizacionMixtaFuenteCrearDto
        {
            public int fuenteNutrientesId { get; set; }

            public decimal cantidadQq { get; set; }
        }

        public class FertilizacionMixtaRespuestaDto
        {
            public int fertilizacionMixtaId { get; set; }

            public int analisisSueloCalculoId { get; set; }

            public DateTime fechaCalculo { get; set; }

            public string? observacion { get; set; }

            public List<FertilizacionMixtaFuenteRespuestaDto> fuentes { get; set; } = new();

            public List<FertilizacionMixtaDetalleRespuestaDto> detalles { get; set; } = new();
        }

        public class FertilizacionMixtaFuenteRespuestaDto
        {
            public int fuenteNutrientesId { get; set; }

            public string nombreFuente { get; set; } = string.Empty;

            public decimal cantidadQq { get; set; }
        }

        public class FertilizacionMixtaDetalleRespuestaDto
        {
            public int elementoQuimicosId { get; set; }

            public string elemento { get; set; } = string.Empty;

            public decimal exportable { get; set; }

            public decimal aporteOrganico { get; set; }

            public decimal diferencia { get; set; }

            public decimal deficit { get; set; }

            public decimal sobrante { get; set; }

            public List<FertilizacionMixtaFuenteElementoDetalleDto> fuentes { get; set; } = new();
        }

        public class FertilizacionMixtaFuenteElementoDetalleDto
        {
            public int fuenteNutrientesId { get; set; }

            public string nombreFuente { get; set; } = string.Empty;

            public decimal cantidadQq { get; set; }

            public decimal aportePorUnidad { get; set; }

            public decimal aporteTotal { get; set; }
        }
    }
}