using System.Globalization;
using System.Text.Json;
using CONATRADEC_API.DTOs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using static CONATRADEC_API.DTOs.CentroGeoespacialDto;

namespace CONATRADEC_API.Services;

public sealed class ClimaMapaOptions
{
    public const string Seccion = "CentroGeoespacial:Clima";

    public string BaseUrl { get; set; } = "https://api.open-meteo.com/";
    public string ApiKey { get; set; } = string.Empty;
    public int MinutosCache { get; set; } = 20;
    public int SegundosTimeout { get; set; } = 18;
    public bool Habilitado { get; set; } = true;
}

/// <summary>
/// Obtiene una cuadrícula meteorológica nacional y la conserva en memoria.
/// El portal puede consumir temperatura, humedad, lluvia y viento desde una
/// sola respuesta, evitando cuatro consultas independientes al proveedor.
/// </summary>
public sealed class ClimaMapaService
{
    private const string ClaveCache = "centro-geoespacial:clima:nicaragua:v1";
    private static readonly SemaphoreSlim Semaforo = new(1, 1);

    private readonly HttpClient httpClient;
    private readonly IMemoryCache cache;
    private readonly ClimaMapaOptions options;
    private readonly ILogger<ClimaMapaService> logger;

    public ClimaMapaService(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<ClimaMapaOptions> options,
        ILogger<ClimaMapaService> logger)
    {
        this.httpClient = httpClient;
        this.cache = cache;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<ClimaMapaRespuestaDto> ObtenerAsync(
        bool forzarActualizacion = false,
        CancellationToken cancellationToken = default)
    {
        if (!options.Habilitado)
        {
            return CrearNoDisponible(
                "La capa meteorológica está deshabilitada en la configuración.");
        }

        if (!forzarActualizacion &&
            cache.TryGetValue(
                ClaveCache,
                out ClimaMapaRespuestaDto? almacenado) &&
            almacenado is not null)
        {
            return almacenado;
        }

        await Semaforo.WaitAsync(cancellationToken);

        try
        {
            if (!forzarActualizacion &&
                cache.TryGetValue(
                    ClaveCache,
                    out almacenado) &&
                almacenado is not null)
            {
                return almacenado;
            }

            ClimaMapaRespuestaDto respuesta =
                await ConsultarProveedorAsync(cancellationToken);

            TimeSpan duracion = respuesta.Disponible
                ? TimeSpan.FromMinutes(Math.Clamp(options.MinutosCache, 5, 120))
                : TimeSpan.FromMinutes(2);

            cache.Set(
                ClaveCache,
                respuesta,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = duracion,
                    Size = 1
                });

            return respuesta;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "El proveedor meteorológico agotó el tiempo de espera.");

            return CrearNoDisponible(
                "El proveedor meteorológico tardó demasiado en responder. " +
                "Los terrenos continúan disponibles.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "No fue posible actualizar la capa meteorológica.");

            return CrearNoDisponible(
                "No fue posible actualizar el clima en este momento. " +
                "Los terrenos continúan disponibles.");
        }
        finally
        {
            Semaforo.Release();
        }
    }

    private async Task<ClimaMapaRespuestaDto> ConsultarProveedorAsync(
        CancellationToken cancellationToken)
    {
        List<(decimal Latitud, decimal Longitud)> puntos =
            ConstruirCuadriculaNacional();

        string latitudes = string.Join(
            ',',
            puntos.Select(item => item.Latitud.ToString(
                "0.####",
                CultureInfo.InvariantCulture)));

        string longitudes = string.Join(
            ',',
            puntos.Select(item => item.Longitud.ToString(
                "0.####",
                CultureInfo.InvariantCulture)));

        string ruta =
            "v1/forecast" +
            $"?latitude={latitudes}" +
            $"&longitude={longitudes}" +
            "&current=temperature_2m,apparent_temperature," +
            "relative_humidity_2m,precipitation,weather_code," +
            "cloud_cover,wind_speed_10m" +
            "&temperature_unit=celsius" +
            "&wind_speed_unit=kmh" +
            "&precipitation_unit=mm" +
            "&timezone=America%2FManagua" +
            "&forecast_days=1";

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            ruta += "&apikey=" + Uri.EscapeDataString(options.ApiKey.Trim());
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        timeoutCts.CancelAfter(
            TimeSpan.FromSeconds(Math.Clamp(options.SegundosTimeout, 5, 60)));

        using HttpResponseMessage response = await httpClient.GetAsync(
            ruta,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutCts.Token);

        response.EnsureSuccessStatusCode();

        await using Stream contenido =
            await response.Content.ReadAsStreamAsync(timeoutCts.Token);

        using JsonDocument documento =
            await JsonDocument.ParseAsync(
                contenido,
                cancellationToken: timeoutCts.Token);

        List<ClimaPuntoMapaDto> resultados =
            ParsearRespuesta(documento.RootElement, puntos);

        if (resultados.Count == 0)
        {
            return CrearNoDisponible(
                "El proveedor respondió sin datos meteorológicos utilizables.");
        }

        return new ClimaMapaRespuestaDto
        {
            Disponible = true,
            Mensaje = "Condiciones meteorológicas actuales estimadas para Nicaragua.",
            Proveedor = "Open-Meteo",
            Licencia = "Weather data by Open-Meteo",
            ActualizadoUtc = DateTime.UtcNow,
            TemperaturaMinima = Minimo(resultados.Select(item => item.Temperatura)),
            TemperaturaMaxima = Maximo(resultados.Select(item => item.Temperatura)),
            HumedadMinima = Minimo(resultados.Select(item => item.HumedadRelativa)),
            HumedadMaxima = Maximo(resultados.Select(item => item.HumedadRelativa)),
            PrecipitacionMaxima = Maximo(resultados.Select(item => item.Precipitacion)),
            VientoMaximo = Maximo(resultados.Select(item => item.VelocidadViento)),
            Puntos = resultados
        };
    }

    private static List<ClimaPuntoMapaDto> ParsearRespuesta(
        JsonElement raiz,
        IReadOnlyList<(decimal Latitud, decimal Longitud)> solicitados)
    {
        var resultado = new List<ClimaPuntoMapaDto>();

        if (raiz.ValueKind == JsonValueKind.Array)
        {
            int indice = 0;

            foreach (JsonElement ubicacion in raiz.EnumerateArray())
            {
                (decimal Latitud, decimal Longitud) solicitado =
                    indice < solicitados.Count
                        ? solicitados[indice]
                        : default;

                ClimaPuntoMapaDto? punto =
                    ParsearUbicacion(ubicacion, solicitado);

                if (punto is not null)
                    resultado.Add(punto);

                indice++;
            }
        }
        else if (raiz.ValueKind == JsonValueKind.Object)
        {
            ClimaPuntoMapaDto? punto = ParsearUbicacion(
                raiz,
                solicitados.Count > 0 ? solicitados[0] : default);

            if (punto is not null)
                resultado.Add(punto);
        }

        return resultado;
    }

    private static ClimaPuntoMapaDto? ParsearUbicacion(
        JsonElement ubicacion,
        (decimal Latitud, decimal Longitud) solicitado)
    {
        if (!ubicacion.TryGetProperty("current", out JsonElement actual) ||
            actual.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        decimal latitud = LeerDecimal(ubicacion, "latitude") ?? solicitado.Latitud;
        decimal longitud = LeerDecimal(ubicacion, "longitude") ?? solicitado.Longitud;

        return new ClimaPuntoMapaDto
        {
            Latitud = latitud,
            Longitud = longitud,
            Temperatura = LeerDecimal(actual, "temperature_2m"),
            TemperaturaAparente = LeerDecimal(actual, "apparent_temperature"),
            HumedadRelativa = LeerDecimal(actual, "relative_humidity_2m"),
            Precipitacion = LeerDecimal(actual, "precipitation"),
            VelocidadViento = LeerDecimal(actual, "wind_speed_10m"),
            Nubosidad = LeerDecimal(actual, "cloud_cover"),
            CodigoClima = LeerEntero(actual, "weather_code"),
            FechaObservacion = LeerFecha(actual, "time")
        };
    }

    private static decimal? LeerDecimal(JsonElement elemento, string propiedad)
    {
        if (!elemento.TryGetProperty(propiedad, out JsonElement valor))
            return null;

        if (valor.ValueKind == JsonValueKind.Number && valor.TryGetDecimal(out decimal numero))
            return numero;

        if (valor.ValueKind == JsonValueKind.String &&
            decimal.TryParse(
                valor.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out numero))
        {
            return numero;
        }

        return null;
    }

    private static int? LeerEntero(JsonElement elemento, string propiedad)
    {
        if (!elemento.TryGetProperty(propiedad, out JsonElement valor))
            return null;

        return valor.ValueKind == JsonValueKind.Number && valor.TryGetInt32(out int numero)
            ? numero
            : null;
    }

    private static DateTimeOffset? LeerFecha(JsonElement elemento, string propiedad)
    {
        if (!elemento.TryGetProperty(propiedad, out JsonElement valor) ||
            valor.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            valor.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTimeOffset fecha)
            ? fecha
            : null;
    }

    private static decimal? Minimo(IEnumerable<decimal?> valores)
    {
        decimal[] disponibles = valores.Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToArray();

        return disponibles.Length == 0 ? null : disponibles.Min();
    }

    private static decimal? Maximo(IEnumerable<decimal?> valores)
    {
        decimal[] disponibles = valores.Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToArray();

        return disponibles.Length == 0 ? null : disponibles.Max();
    }

    private static ClimaMapaRespuestaDto CrearNoDisponible(string mensaje) =>
        new()
        {
            Disponible = false,
            Mensaje = mensaje,
            ActualizadoUtc = DateTime.UtcNow,
            Puntos = []
        };

    private static List<(decimal Latitud, decimal Longitud)>
        ConstruirCuadriculaNacional()
    {
        var resultado = new List<(decimal Latitud, decimal Longitud)>();

        // La malla se recorta con un contorno simplificado de Nicaragua.
        // El propósito es consultar puntos terrestres representativos sin
        // depender de una llamada GIS adicional desde el backend.
        (double Latitud, double Longitud)[] contorno =
        [
            (15.02, -87.68),
            (15.08, -86.45),
            (15.00, -85.20),
            (14.88, -84.10),
            (14.60, -83.15),
            (13.75, -83.22),
            (12.80, -83.50),
            (11.78, -83.72),
            (10.72, -83.90),
            (10.70, -85.65),
            (11.02, -86.77),
            (11.68, -87.18),
            (12.72, -87.70),
            (13.75, -87.75)
        ];

        for (double latitud = 10.85; latitud <= 14.95; latitud += 0.42)
        {
            for (double longitud = -87.55; longitud <= -83.25; longitud += 0.48)
            {
                if (!PuntoDentroPoligono(latitud, longitud, contorno))
                    continue;

                resultado.Add((
                    decimal.Round((decimal)latitud, 4),
                    decimal.Round((decimal)longitud, 4)));
            }
        }

        return resultado;
    }

    private static bool PuntoDentroPoligono(
        double latitud,
        double longitud,
        IReadOnlyList<(double Latitud, double Longitud)> poligono)
    {
        bool dentro = false;
        int anterior = poligono.Count - 1;

        for (int actual = 0; actual < poligono.Count; actual++)
        {
            (double latActual, double lonActual) = poligono[actual];
            (double latAnterior, double lonAnterior) = poligono[anterior];

            bool cruza =
                ((latActual > latitud) != (latAnterior > latitud)) &&
                (longitud <
                    (lonAnterior - lonActual) *
                    (latitud - latActual) /
                    ((latAnterior - latActual) == 0
                        ? double.Epsilon
                        : latAnterior - latActual) +
                    lonActual);

            if (cruza)
                dentro = !dentro;

            anterior = actual;
        }

        return dentro;
    }
}
