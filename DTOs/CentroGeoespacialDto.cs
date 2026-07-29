namespace CONATRADEC_API.DTOs;

/// <summary>
/// Contratos públicos del Centro Geoespacial CONATRADEC.
/// Se mantienen independientes del mapa agrícola principal para permitir
/// agregar capas sin modificar contratos existentes.
/// </summary>
public static class CentroGeoespacialDto
{
    public sealed class CentroGeoespacialCapasRespuestaDto
    {
        public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
        public List<CapaMapaDto> Capas { get; set; } = [];
    }

    public sealed class CapaMapaDto
    {
        public string Clave { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public string Icono { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string TipoVisualizacion { get; set; } = string.Empty;
        public bool Disponible { get; set; }
        public bool ActivaPorDefecto { get; set; }
        public int Orden { get; set; }
        public string? Mensaje { get; set; }
    }

    public sealed class ClimaMapaRespuestaDto
    {
        public bool Disponible { get; set; }
        public string Mensaje { get; set; } = string.Empty;
        public string Proveedor { get; set; } = "Open-Meteo";
        public string Licencia { get; set; } = "Weather data by Open-Meteo";
        public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
        public string UnidadTemperatura { get; set; } = "°C";
        public string UnidadPrecipitacion { get; set; } = "mm";
        public string UnidadViento { get; set; } = "km/h";
        public decimal? TemperaturaMinima { get; set; }
        public decimal? TemperaturaMaxima { get; set; }
        public decimal? HumedadMinima { get; set; }
        public decimal? HumedadMaxima { get; set; }
        public decimal? PrecipitacionMaxima { get; set; }
        public decimal? VientoMaximo { get; set; }
        public List<ClimaPuntoMapaDto> Puntos { get; set; } = [];
    }

    public sealed class ClimaPuntoMapaDto
    {
        public decimal Latitud { get; set; }
        public decimal Longitud { get; set; }
        public decimal? Temperatura { get; set; }
        public decimal? TemperaturaAparente { get; set; }
        public decimal? HumedadRelativa { get; set; }
        public decimal? Precipitacion { get; set; }
        public decimal? VelocidadViento { get; set; }
        public decimal? Nubosidad { get; set; }
        public int? CodigoClima { get; set; }
        public DateTimeOffset? FechaObservacion { get; set; }
    }

    public sealed class CapaSueloMapaRespuestaDto
    {
        public bool Disponible { get; set; }
        public string Mensaje { get; set; } = string.Empty;
        public string Clave { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public string NivelAgrupacion { get; set; } = "DEPARTAMENTO";
        public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
        public decimal? Minimo { get; set; }
        public decimal? Maximo { get; set; }
        public int TotalRegiones { get; set; }
        public int TotalTerrenosAnalizados { get; set; }
        public List<RangoLeyendaMapaDto> Leyenda { get; set; } = [];
        public List<ResumenTerritorialSueloMapaDto> Regiones { get; set; } = [];

        // Se conservan temporalmente para mantener compatibilidad con
        // clientes anteriores. Las capas nuevas ya no dibujan puntos.
        public int TotalPuntos { get; set; }
        public List<PuntoSueloMapaDto> Puntos { get; set; } = [];
    }

    public sealed class ResumenTerritorialSueloMapaDto
    {
        public string TipoTerritorio { get; set; } = string.Empty;
        public string NombreTerritorio { get; set; } = string.Empty;
        public int DepartamentoId { get; set; }
        public string Departamento { get; set; } = string.Empty;
        public int? MunicipioId { get; set; }
        public string Municipio { get; set; } = string.Empty;
        public decimal Promedio { get; set; }
        public decimal Minimo { get; set; }
        public decimal Maximo { get; set; }
        public int TerrenosAnalizados { get; set; }
        public string Clasificacion { get; set; } = string.Empty;
        public string Color { get; set; } = "#3B655B";
        public DateTime FechaMasReciente { get; set; }
        public bool MuestraLimitada { get; set; }
    }

    public sealed class PuntoSueloMapaDto
    {
        public int TerrenoId { get; set; }
        public int AnalisisSueloCalculoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Productor { get; set; } = string.Empty;
        public int DepartamentoId { get; set; }
        public string Departamento { get; set; } = string.Empty;
        public int MunicipioId { get; set; }
        public string Municipio { get; set; } = string.Empty;
        public decimal Latitud { get; set; }
        public decimal Longitud { get; set; }
        public decimal Valor { get; set; }
        public string Clasificacion { get; set; } = string.Empty;
        public string Color { get; set; } = "#3B655B";
        public DateTime FechaAnalisis { get; set; }
    }

    public sealed class RangoLeyendaMapaDto
    {
        public string Etiqueta { get; set; } = string.Empty;
        public string Color { get; set; } = "#3B655B";
        public decimal? Desde { get; set; }
        public decimal? Hasta { get; set; }
    }

    public sealed class HistorialTerrenoMapaDto
    {
        public int TerrenoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Productor { get; set; } = string.Empty;
        public string Departamento { get; set; } = string.Empty;
        public string Municipio { get; set; } = string.Empty;
        public decimal ExtensionManzanas { get; set; }
        public decimal ProduccionQuintalesOro { get; set; }
        public List<AnalisisTerrenoMapaDto> Analisis { get; set; } = [];
    }

    public sealed class AnalisisTerrenoMapaDto
    {
        public int AnalisisSueloCalculoId { get; set; }
        public int AnalisisSueloId { get; set; }
        public string Identificador { get; set; } = string.Empty;
        public DateOnly FechaLaboratorio { get; set; }
        public DateTime FechaRegistro { get; set; }
        public decimal Ph { get; set; }
        public decimal? MateriaOrganica { get; set; }
        public decimal? AcidezTotal { get; set; }
        public decimal? Cice { get; set; }
        public decimal? SaturacionBases { get; set; }
        public string Nivel { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public string RecomendacionGeneral { get; set; } = string.Empty;
        public string Observacion { get; set; } = string.Empty;
        public List<ElementoAnalisisTerrenoMapaDto> Elementos { get; set; } = [];
    }

    public sealed class ElementoAnalisisTerrenoMapaDto
    {
        public int ElementoQuimicosId { get; set; }
        public string Simbolo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal Valor { get; set; }
        public string Unidad { get; set; } = string.Empty;
        public string Clasificacion { get; set; } = string.Empty;
    }
}
