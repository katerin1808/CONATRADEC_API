namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// DTOs utilizados por el dashboard del portal administrativo web.
    /// </summary>
    public static class DashboardWebDto
    {
        public sealed class ResumenDto
        {
            public int totalTerrenos { get; set; }
            public int totalAnalisis { get; set; }
            public int usuariosActivos { get; set; }

            /*
             * El módulo de diagnóstico por IA todavía no tiene una tabla
             * integrada en este backend. Por ahora se devuelve cero y, cuando
             * se implemente el historial de diagnósticos, este valor se
             * calculará desde esa tabla.
             */
            public int totalDiagnosticos { get; set; }

            public List<AnalisisMesDto> analisisPorMes { get; set; } = new();
        }

        public sealed class AnalisisMesDto
        {
            public string mes { get; set; } = string.Empty;
            public int cantidad { get; set; }
        }
    }
}
