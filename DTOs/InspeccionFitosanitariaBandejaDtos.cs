namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// Registro liviano para la bandeja. No incluye los expedientes completos
    /// de las fotografías, por lo que una página mantiene un tamaño pequeño.
    /// </summary>
    public sealed class InspeccionFitosanitariaBandejaItemDto
    {
        public int InspeccionId { get; set; }
        public string NombreInspeccion { get; set; } = string.Empty;

        /// <summary>
        /// Compatibilidad con el cliente MAUI: representa que la etapa del
        /// técnico fue finalizada, no el cierre definitivo del expediente.
        /// </summary>
        public bool CerradaTecnico { get; set; }

        public bool EtapaTecnicaFinalizada { get; set; }
        public bool CerradaDefinitiva { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public string Propietario { get; set; } = string.Empty;
        public string Municipio { get; set; } = string.Empty;
        public string Departamento { get; set; } = string.Empty;
        public int UsuarioTecnicoId { get; set; }
        public string TecnicoNombreCompleto { get; set; } = string.Empty;
        public string TecnicoUsuario { get; set; } = string.Empty;
        public DateTime FechaRegistroSistemaUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public int TotalFotografias { get; set; }
        public int Pendientes { get; set; }
        public int ConError { get; set; }
        public int Finalizadas { get; set; }
        public int RequierenDecisionTecnico { get; set; }
        public int EnviadasRevision { get; set; }
        public int Procesando { get; set; }
        public int Descartadas { get; set; }
        public string UrlMiniatura { get; set; } = string.Empty;
    }

    public sealed class InspeccionFitosanitariaTecnicoFiltroDto
    {
        public int UsuarioTecnicoId { get; set; }
        public string NombreCompleto { get; set; } = string.Empty;
        public string NombreUsuario { get; set; } = string.Empty;
    }

    public sealed class InspeccionFitosanitariaTecnicoAsignacionDto
    {
        public int InspeccionId { get; set; }
        public int UsuarioTecnicoId { get; set; }
        public string NombreCompleto { get; set; } = string.Empty;
        public string NombreUsuario { get; set; } = string.Empty;
    }

    public sealed class InspeccionFitosanitariaTecnicoFiltroRespuestaDto
    {
        public List<InspeccionFitosanitariaTecnicoFiltroDto> Tecnicos { get; set; } = [];
        public List<InspeccionFitosanitariaTecnicoAsignacionDto> Asignaciones { get; set; } = [];
    }

    /// <summary>
    /// Página basada en cursor. El cliente devuelve SiguienteFechaUtc y
    /// SiguienteId para obtener registros posteriores sin usar OFFSET.
    /// </summary>
    public sealed class InspeccionFitosanitariaBandejaPaginaDto
    {
        public List<InspeccionFitosanitariaBandejaItemDto> Items { get; set; } = [];
        public bool HayMas { get; set; }
        public DateTime? SiguienteFechaUtc { get; set; }
        public int? SiguienteId { get; set; }
    }
}
