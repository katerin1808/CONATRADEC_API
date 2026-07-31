namespace CONATRADEC_API.DTOs;

/// <summary>
/// Pronóstico meteorológico diario asociado a un terreno.
/// Los datos provienen de Open-Meteo y las alertas son reglas
/// interpretativas de CONATRADEC.
/// </summary>
public sealed class PronosticoClimaTerrenoDto
{
    public bool Disponible { get; set; }

    public string Mensaje { get; set; } = string.Empty;

    public int TerrenoId { get; set; }

    public string CodigoTerreno { get; set; } = string.Empty;

    public string Direccion { get; set; } = string.Empty;

    public string Municipio { get; set; } = string.Empty;

    public string Departamento { get; set; } = string.Empty;

    public decimal Latitud { get; set; }

    public decimal Longitud { get; set; }

    public string Proveedor { get; set; } = "Open-Meteo";

    public string Licencia { get; set; } =
        "Weather data by Open-Meteo";

    public DateTime ActualizadoUtc { get; set; } =
        DateTime.UtcNow;

    public int DiasSolicitados { get; set; } = 7;

    public PronosticoClimaResumenDto Resumen { get; set; } =
        new();

    public List<PronosticoClimaAlertaDto> Alertas { get; set; } =
        [];

    public List<PronosticoClimaDiaDto> Dias { get; set; } =
        [];
}

public sealed class PronosticoClimaResumenDto
{
    public decimal? TemperaturaMaximaPeriodo { get; set; }

    public decimal? TemperaturaMinimaPeriodo { get; set; }

    public decimal? PrecipitacionTotal { get; set; }

    public int? ProbabilidadLluviaMaxima { get; set; }

    public decimal? RafagaMaxima { get; set; }

    public decimal? EvapotranspiracionTotal { get; set; }

    public int DiasConLluvia { get; set; }

    public string NivelRiesgo { get; set; } = "NORMAL";

    public string MensajeRiesgo { get; set; } =
        "Sin alertas meteorológicas relevantes.";
}

public sealed class PronosticoClimaDiaDto
{
    public DateOnly Fecha { get; set; }

    public int? CodigoClima { get; set; }

    public string Condicion { get; set; } = string.Empty;

    public string ResumenNarrativo { get; set; } = string.Empty;

    public decimal? TemperaturaMaxima { get; set; }

    public decimal? TemperaturaMinima { get; set; }

    public decimal? SensacionMaxima { get; set; }

    public decimal? SensacionMinima { get; set; }

    public decimal? HumedadMinima { get; set; }

    public decimal? HumedadMaxima { get; set; }

    public decimal? HumedadPromedio { get; set; }

    public decimal? NubosidadPromedio { get; set; }

    public int? ProbabilidadPrecipitacion { get; set; }

    public decimal? Precipitacion { get; set; }

    public decimal? Lluvia { get; set; }

    public decimal? HorasPrecipitacion { get; set; }

    public decimal? VelocidadVientoMaxima { get; set; }

    public decimal? RafagaMaxima { get; set; }

    public decimal? DireccionVientoDominante { get; set; }

    public decimal? EvapotranspiracionEt0 { get; set; }

    public decimal? IndiceUvMaximo { get; set; }

    public decimal? TemperaturaSueloPromedio { get; set; }

    public decimal? HumedadSueloSuperficialPromedio { get; set; }

    public decimal? HumedadSueloTresCmPromedio { get; set; }

    public decimal? DeficitPresionVaporMaximo { get; set; }

    public string RiesgoTormenta { get; set; } = "BAJO";

    public string Amanecer { get; set; } = string.Empty;

    public string Atardecer { get; set; } = string.Empty;

    // Se conserva para compatibilidad con clientes anteriores.
    public List<string> Recomendaciones { get; set; } = [];

    public List<PronosticoClimaRecomendacionDto>
        RecomendacionesDetalladas { get; set; } = [];

    public List<PronosticoClimaPeriodoDto> Periodos { get; set; } = [];

    public List<PronosticoClimaAlertaDto> Alertas { get; set; } =
        [];
}


public sealed class PronosticoClimaRecomendacionDto
{
    public string Clave { get; set; } = string.Empty;

    public string Titulo { get; set; } = string.Empty;

    public string Mensaje { get; set; } = string.Empty;

    public string Fuente { get; set; } =
        "Regla automática de CONATRADEC";

    public List<PronosticoClimaEvidenciaDto> Evidencias
    {
        get;
        set;
    } = [];
}

public sealed class PronosticoClimaEvidenciaDto
{
    public string Indicador { get; set; } = string.Empty;

    public decimal ValorObservado { get; set; }

    public string Operador { get; set; } = string.Empty;

    public decimal Umbral { get; set; }

    public string Unidad { get; set; } = string.Empty;

    public string FuenteDato { get; set; } = "Open-Meteo";

    public string ReglaAplicada { get; set; } = string.Empty;
}

public sealed class PronosticoClimaPeriodoDto
{
    public string Clave { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;

    public string RangoHorario { get; set; } = string.Empty;

    public int? CodigoClima { get; set; }

    public string Condicion { get; set; } = string.Empty;

    public decimal? TemperaturaPromedio { get; set; }

    public decimal? TemperaturaMaxima { get; set; }

    public decimal? TemperaturaMinima { get; set; }

    public decimal? SensacionPromedio { get; set; }

    public decimal? HumedadPromedio { get; set; }

    public decimal? NubosidadPromedio { get; set; }

    public int? ProbabilidadPrecipitacionMaxima { get; set; }

    public decimal? PrecipitacionTotal { get; set; }

    public decimal? LluviaTotal { get; set; }

    public decimal? VelocidadVientoPromedio { get; set; }

    public decimal? VelocidadVientoMaxima { get; set; }

    public decimal? RafagaMaxima { get; set; }

    public decimal? DireccionVientoDominante { get; set; }

    public string RiesgoTormenta { get; set; } = "BAJO";
}

public sealed class PronosticoClimaAlertaDto
{
    public string Clave { get; set; } = string.Empty;

    public string Nivel { get; set; } = "ATENCION";

    public string Titulo { get; set; } = string.Empty;

    public string Mensaje { get; set; } = string.Empty;

    public DateOnly? FechaInicio { get; set; }

    public DateOnly? FechaFin { get; set; }
}
