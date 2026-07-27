namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// Contratos utilizados por el dashboard ejecutivo del portal web.
    /// </summary>
    public static class DashboardWebDto
    {
        public sealed class ResumenDto
        {
            public DateTime fechaConsultaUtc { get; set; }

            public int totalTerrenos { get; set; }
            public int terrenosConAnalisis { get; set; }
            public int terrenosSinAnalisis { get; set; }

            public int totalAnalisis { get; set; }
            public int analisisMesActual { get; set; }
            public int analisisUltimos30Dias { get; set; }

            public int usuariosActivos { get; set; }
            public int usuariosInternos { get; set; }
            public int usuariosExternos { get; set; }

            public int dispositivosConectados { get; set; }
            public int usuariosConectados { get; set; }

            public decimal extensionTotalManzanas { get; set; }
            public decimal produccionEstimadaQuintalesOro { get; set; }

            public int alertasCriticas { get; set; }
            public int alertasAtencion { get; set; }

            public int terrenosPhCritico { get; set; }
            public int terrenosMateriaOrganicaBaja { get; set; }
            public int terrenosAcidezAlta { get; set; }

            public decimal porcentajeTerrenosAnalizados { get; set; }
            public decimal porcentajePhCritico { get; set; }

            public List<AnalisisMesDto> analisisPorMes { get; set; } = new();
            public List<DepartamentoResumenDto> departamentos { get; set; } = new();
            public List<AlertaTerrenoDto> alertasRecientes { get; set; } = new();
            public List<IndicadorAlertaDto> distribucionAlertas { get; set; } = new();
        }

        public sealed class AnalisisMesDto
        {
            public string mes { get; set; } = string.Empty;
            public int cantidad { get; set; }
        }

        public sealed class DepartamentoResumenDto
        {
            public string departamento { get; set; } = string.Empty;
            public int terrenos { get; set; }
            public int terrenosAnalizados { get; set; }
            public decimal extensionManzanas { get; set; }
            public decimal coberturaAnalisisPorcentaje { get; set; }
        }

        public sealed class AlertaTerrenoDto
        {
            public int terrenoId { get; set; }
            public string codigoTerreno { get; set; } = string.Empty;
            public string propietario { get; set; } = string.Empty;
            public string departamento { get; set; } = string.Empty;
            public string municipio { get; set; } = string.Empty;
            public string nivel { get; set; } = string.Empty;
            public string tipo { get; set; } = string.Empty;
            public string mensaje { get; set; } = string.Empty;
            public decimal? valor { get; set; }
            public string unidad { get; set; } = string.Empty;
            public DateTime fechaAnalisis { get; set; }
        }

        public sealed class IndicadorAlertaDto
        {
            public string nombre { get; set; } = string.Empty;
            public int cantidad { get; set; }
            public string nivel { get; set; } = string.Empty;
            public string descripcion { get; set; } = string.Empty;
        }
    }
}
