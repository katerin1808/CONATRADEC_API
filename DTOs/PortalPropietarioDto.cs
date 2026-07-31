using static CONATRADEC_API.DTOs.CentroGeoespacialDto;

namespace CONATRADEC_API.DTOs;

public sealed class PortalPropietarioResumenDto
{
    public bool Vinculado { get; set; }

    public string Mensaje { get; set; } =
        string.Empty;

    public PortalPropietarioDatosDto? Propietario
    {
        get;
        set;
    }

    public PortalPropietarioTotalesDto Resumen
    {
        get;
        set;
    } = new();

    public List<PortalPropietarioTerrenoDto> Terrenos
    {
        get;
        set;
    } = [];
}

public sealed class PortalPropietarioDatosDto
{
    public int PropietarioId { get; set; }

    public string Identificacion { get; set; } =
        string.Empty;

    public string NombreCompleto { get; set; } =
        string.Empty;

    public string? Telefono { get; set; }

    public string? Correo { get; set; }

    public string? Direccion { get; set; }
}

public sealed class PortalPropietarioTotalesDto
{
    public int TotalTerrenos { get; set; }

    public decimal TotalManzanas { get; set; }

    public int TotalPlantas { get; set; }

    public decimal ProduccionEstimadaQuintales { get; set; }

    public int TotalAnalisis { get; set; }
}

public sealed class PortalPropietarioTerrenoDto
{
    public int TerrenoId { get; set; }

    public string CodigoTerreno { get; set; } =
        string.Empty;

    public string Direccion { get; set; } =
        string.Empty;

    public decimal ExtensionManzanas { get; set; }

    public DateTime FechaIngreso { get; set; }

    public int CantidadPlantas { get; set; }

    public decimal CantidadQuintalesOro { get; set; }

    public decimal Latitud { get; set; }

    public decimal Longitud { get; set; }

    public string Municipio { get; set; } =
        string.Empty;

    public string Departamento { get; set; } =
        string.Empty;

    public int TotalAnalisis { get; set; }

    public DateTime? FechaUltimoAnalisis { get; set; }
}

public sealed class PortalCentroGeoespacialDto
{
    public bool Vinculado { get; set; }

    public string Mensaje { get; set; } =
        string.Empty;

    public DateTime ActualizadoUtc { get; set; } =
        DateTime.UtcNow;

    public PortalPropietarioDatosDto? Propietario
    {
        get;
        set;
    }

    public PortalCentroGeoespacialResumenDto Resumen
    {
        get;
        set;
    } = new();

    public List<PortalCentroTerrenoDto> Terrenos
    {
        get;
        set;
    } = [];

    public List<PortalCentroNutrienteDto> NutrientesDisponibles
    {
        get;
        set;
    } = [];

    public ClimaMapaRespuestaDto Clima
    {
        get;
        set;
    } = new();
}

public sealed class PortalCentroGeoespacialResumenDto
{
    public int TotalTerrenos { get; set; }

    public decimal ExtensionTotalManzanas { get; set; }

    public int EstadoCritico { get; set; }

    public int RequierenAtencion { get; set; }

    public int EstadoEstable { get; set; }

    public int SinAnalisis { get; set; }

    public int AlertasActivas { get; set; }

    public decimal? TemperaturaPromedioLocal { get; set; }

    public decimal? HumedadPromedioLocal { get; set; }

    public decimal? PrecipitacionMaximaLocal { get; set; }

    public decimal? VientoMaximoLocal { get; set; }
}

public sealed class PortalCentroTerrenoDto
{
    public int TerrenoId { get; set; }

    public string CodigoTerreno { get; set; } =
        string.Empty;

    public string Direccion { get; set; } =
        string.Empty;

    public decimal ExtensionManzanas { get; set; }

    public DateTime FechaIngreso { get; set; }

    public int CantidadPlantas { get; set; }

    public decimal CantidadQuintalesOro { get; set; }

    public decimal Latitud { get; set; }

    public decimal Longitud { get; set; }

    public int MunicipioId { get; set; }

    public string Municipio { get; set; } =
        string.Empty;

    public int DepartamentoId { get; set; }

    public string Departamento { get; set; } =
        string.Empty;

    public int TotalAnalisis { get; set; }

    public int? AnalisisSueloCalculoId { get; set; }

    public DateTime? FechaUltimoAnalisis { get; set; }

    public decimal? Ph { get; set; }

    public decimal? MateriaOrganica { get; set; }

    public decimal? AcidezTotal { get; set; }

    public decimal? Cice { get; set; }

    public decimal? SaturacionBases { get; set; }

    public string RecomendacionGeneral { get; set; } =
        string.Empty;

    public string Observacion { get; set; } =
        string.Empty;

    public string Nivel { get; set; } =
        "SIN_ANALISIS";

    public string Estado { get; set; } =
        "Sin análisis";

    public string ColorEstado { get; set; } =
        "#94A3B8";

    public List<PortalCentroElementoDto> Elementos
    {
        get;
        set;
    } = [];

    public List<PortalCentroAlertaDto> Alertas
    {
        get;
        set;
    } = [];
}

public sealed class PortalCentroElementoDto
{
    public int ElementoQuimicosId { get; set; }

    public string Simbolo { get; set; } =
        string.Empty;

    public string Nombre { get; set; } =
        string.Empty;

    public decimal Valor { get; set; }

    public string Unidad { get; set; } =
        string.Empty;

    public string Clasificacion { get; set; } =
        string.Empty;
}

public sealed class PortalCentroAlertaDto
{
    public string Clave { get; set; } =
        string.Empty;

    public string Nivel { get; set; } =
        "ATENCION";

    public string Titulo { get; set; } =
        string.Empty;

    public string Mensaje { get; set; } =
        string.Empty;

    public decimal? Valor { get; set; }

    public decimal? Umbral { get; set; }
}

public sealed class PortalCentroNutrienteDto
{
    public int ElementoQuimicosId { get; set; }

    public string Simbolo { get; set; } =
        string.Empty;

    public string Nombre { get; set; } =
        string.Empty;

    public string Clave =>
        $"nutriente-{ElementoQuimicosId}";
}

public sealed class PortalHistorialTerrenoDto
{
    public int TerrenoId { get; set; }

    public string CodigoTerreno { get; set; } =
        string.Empty;

    public string Direccion { get; set; } =
        string.Empty;

    public string Municipio { get; set; } =
        string.Empty;

    public string Departamento { get; set; } =
        string.Empty;

    public decimal ExtensionManzanas { get; set; }

    public decimal ProduccionQuintalesOro { get; set; }

    public List<PortalHistorialAnalisisDto> Analisis
    {
        get;
        set;
    } = [];
}

public sealed class PortalHistorialAnalisisDto
{
    public int AnalisisSueloCalculoId { get; set; }

    public int AnalisisSueloId { get; set; }

    public string Identificador { get; set; } =
        string.Empty;

    public DateOnly FechaLaboratorio { get; set; }

    public DateTime FechaRegistro { get; set; }

    public decimal Ph { get; set; }

    public decimal? MateriaOrganica { get; set; }

    public decimal? AcidezTotal { get; set; }

    public decimal? Cice { get; set; }

    public decimal? SaturacionBases { get; set; }

    public string Nivel { get; set; } =
        string.Empty;

    public string Estado { get; set; } =
        string.Empty;

    public string RecomendacionGeneral { get; set; } =
        string.Empty;

    public string Observacion { get; set; } =
        string.Empty;

    public List<PortalCentroElementoDto> Elementos
    {
        get;
        set;
    } = [];
}
