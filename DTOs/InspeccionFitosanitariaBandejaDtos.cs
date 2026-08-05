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
        public bool CerradaTecnico { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public string Propietario { get; set; } = string.Empty;
        public string Municipio { get; set; } = string.Empty;
        public string Departamento { get; set; } = string.Empty;
        public DateTime FechaRegistroSistemaUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public int TotalFotografias { get; set; }
        public int Pendientes { get; set; }
        public int ConError { get; set; }
        public int Finalizadas { get; set; }
        public string UrlMiniatura { get; set; } = string.Empty;
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
