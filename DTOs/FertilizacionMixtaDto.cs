namespace CONATRADEC_API.DTOs
{
    public class FertilizacionMixtaDto
    {
        // Entrada para SOLO CALCULAR fertilización mixta
        public class FertilizacionMixtaCrearDto
        {
            public string? observacion { get; set; }

            // Exportables recibidos desde el cálculo de requerimiento anual
            public List<FertilizacionMixtaElementoCrearDto> elementos { get; set; } = new();

            // Puede recibir una sola fuente o varias
            public List<FertilizacionMixtaFuenteCrearDto> fuentes { get; set; } = new();
        }

        public class FertilizacionMixtaElementoCrearDto
        {
            public int elementoQuimicosId { get; set; }

            public decimal exportable { get; set; }
        }

        public class FertilizacionMixtaFuenteCrearDto
        {
            public int fuenteNutrientesId { get; set; }

            public decimal cantidadQq { get; set; }
        }

        // Respuesta de SOLO CALCULAR
        // No devuelve IDs porque todavía no se ha guardado nada
        public class FertilizacionMixtaRespuestaDto
        {
            public string observacion { get; set; } = string.Empty;

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