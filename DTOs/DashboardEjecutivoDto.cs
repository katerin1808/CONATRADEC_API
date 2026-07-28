namespace CONATRADEC_API.DTOs
{
    public sealed class DashboardEjecutivoDto
    {
        public DateTime fechaConsultaUtc { get; set; }

        public int totalTerrenos { get; set; }
        public int terrenosConAnalisis { get; set; }
        public int terrenosSinAnalisis { get; set; }
        public decimal extensionTotalManzanas { get; set; }
        public decimal produccionEstimadaQuintalesOro { get; set; }

        public int totalAnalisis { get; set; }
        public int analisisMesActual { get; set; }
        public int analisisUltimos30Dias { get; set; }

        public int usuariosActivos { get; set; }
        public int usuariosInternos { get; set; }
        public int usuariosExternos { get; set; }

        public int dispositivosConectados { get; set; }
        public int usuariosConectados { get; set; }

        public int alertasCriticas { get; set; }
        public int alertasAtencion { get; set; }
        public int terrenosNormales { get; set; }

        public int seguimientosPendientes { get; set; }
        public int seguimientosEnProceso { get; set; }
        public int seguimientosAtendidos { get; set; }
        public int seguimientosDescartados { get; set; }
        public int seguimientosSinAsignar { get; set; }

        public decimal porcentajeTerrenosAnalizados { get; set; }
        public decimal porcentajeSeguimientosCerrados { get; set; }

        public List<DashboardSerieMesDto> analisisPorMes { get; set; } = [];
        public List<DashboardDepartamentoDto> departamentos { get; set; } = [];
        public List<DashboardAlertaDto> alertasRecientes { get; set; } = [];
        public List<DashboardIndicadorAlertaDto> distribucionAlertas { get; set; } = [];
        public List<DashboardTecnicoDto> tecnicos { get; set; } = [];
    }

    public sealed class DashboardSerieMesDto
    {
        public string mes { get; set; } = string.Empty;
        public int cantidad { get; set; }
    }

    public sealed class DashboardDepartamentoDto
    {
        public string departamento { get; set; } = string.Empty;
        public int terrenos { get; set; }
        public int terrenosAnalizados { get; set; }
        public decimal extensionManzanas { get; set; }
        public decimal coberturaAnalisisPorcentaje { get; set; }
    }

    public sealed class DashboardAlertaDto
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
        public int? seguimientoId { get; set; }
        public string? estadoSeguimiento { get; set; }
        public string? responsable { get; set; }
    }

    public sealed class DashboardIndicadorAlertaDto
    {
        public string nombre { get; set; } = string.Empty;
        public int cantidad { get; set; }
        public string nivel { get; set; } = string.Empty;
        public string descripcion { get; set; } = string.Empty;
    }

    public sealed class DashboardTecnicoDto
    {
        public int usuarioId { get; set; }
        public string nombre { get; set; } = string.Empty;
        public int pendientes { get; set; }
        public int enProceso { get; set; }
        public int atendidos { get; set; }
        public int totalAbiertos { get; set; }
    }
}
