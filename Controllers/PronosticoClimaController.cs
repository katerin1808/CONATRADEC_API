using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace CONATRADEC_API.Controllers;

/// <summary>
/// Pronóstico agrícola diario para los terrenos de CONATRADEC.
///
/// El endpoint privado nunca recibe propietarioId. La relación autorizada
/// se resuelve en el servidor mediante usuarioPropietario y
/// propietarioTerreno.
/// </summary>
[ApiController]
[Authorize]
[Route("api/pronostico-clima")]
public sealed class PronosticoClimaController : ControllerBase
{
    private const string InterfazPortal =
        "PortalAdministrativoWeb";

    private const string InterfazPortalPropietario =
        "PortalPropietarioPage";

    private static readonly SemaphoreSlim Semaforo =
        new(1, 1);

    private readonly DBContext db;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IMemoryCache cache;
    private readonly ClimaMapaOptions options;
    private readonly PermisoApiService permisos;
    private readonly ILogger<PronosticoClimaController> logger;

    public PronosticoClimaController(
        DBContext db,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<ClimaMapaOptions> options,
        PermisoApiService permisos,
        ILogger<PronosticoClimaController> logger)
    {
        this.db = db;
        this.httpClientFactory = httpClientFactory;
        this.cache = cache;
        this.options = options.Value;
        this.permisos = permisos;
        this.logger = logger;
    }

    /// <summary>
    /// Pronóstico para usuarios del Centro Geoespacial nacional.
    /// </summary>
    [HttpGet("terrenos/{terrenoId:int}")]
    public async Task<IActionResult>
        ObtenerPronosticoTerreno(
            int terrenoId,
            [FromQuery] int dias = 7,
            [FromQuery] bool forzarActualizacion = false,
            CancellationToken cancellationToken = default)
    {
        TerrenoPronosticoBase? terreno = await db.Terreno
            .AsNoTracking()
            .Where(item =>
                item.activo &&
                item.terrenoId == terrenoId)
            .Select(item => new TerrenoPronosticoBase
            {
                TerrenoId = item.terrenoId,
                CodigoTerreno = item.codigoTerreno,
                Direccion = item.direccionTerreno,
                Municipio = item.Municipio.NombreMunicipio,
                Departamento =
                    item.Municipio.Departamento.NombreDepartamento,
                Latitud = item.latitud,
                Longitud = item.longitud
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (terreno is null)
        {
            return NotFound(new
            {
                success = false,
                message = "No se encontró el terreno solicitado."
            });
        }

        PronosticoClimaTerrenoDto respuesta =
            await ObtenerPronosticoAsync(
                terreno,
                dias,
                forzarActualizacion,
                cancellationToken);

        return Ok(respuesta);
    }

    /// <summary>
    /// Pronóstico privado. Solo permite consultar terrenos vinculados
    /// con el propietario de la cuenta autenticada.
    /// </summary>
    [HttpGet("mi-terreno/{terrenoId:int}")]
    public async Task<IActionResult>
        ObtenerPronosticoMiTerreno(
            int terrenoId,
            [FromQuery] int dias = 7,
            [FromQuery] bool forzarActualizacion = false,
            CancellationToken cancellationToken = default)
    {
        int? usuarioId = ObtenerUsuarioId();

        if (!usuarioId.HasValue)
        {
            return Unauthorized(new
            {
                success = false,
                message =
                    "No fue posible identificar al usuario autenticado."
            });
        }

        IActionResult? acceso =
            await ValidarAccesoPortalAsync(
                usuarioId.Value,
                cancellationToken);

        if (acceso is not null)
            return acceso;

        TerrenoPronosticoBase? terreno =
            await ObtenerTerrenoPropietarioAsync(
                usuarioId.Value,
                terrenoId,
                cancellationToken);

        if (terreno is null)
        {
            return NotFound(new
            {
                success = false,
                message =
                    "El terreno no pertenece al propietario vinculado " +
                    "con la cuenta autenticada."
            });
        }

        PronosticoClimaTerrenoDto respuesta =
            await ObtenerPronosticoAsync(
                terreno,
                dias,
                forzarActualizacion,
                cancellationToken);

        return Ok(respuesta);
    }

    private async Task<PronosticoClimaTerrenoDto>
        ObtenerPronosticoAsync(
            TerrenoPronosticoBase terreno,
            int dias,
            bool forzarActualizacion,
            CancellationToken cancellationToken)
    {
        dias = Math.Clamp(dias, 1, 16);

        if (!CoordenadaValida(
                terreno.Latitud,
                terreno.Longitud))
        {
            return CrearNoDisponible(
                terreno,
                dias,
                "El terreno no tiene coordenadas válidas para consultar " +
                "el pronóstico meteorológico.");
        }

        if (!options.Habilitado)
        {
            return CrearNoDisponible(
                terreno,
                dias,
                "El proveedor meteorológico está deshabilitado en la " +
                "configuración del servidor.");
        }

        string cacheKey = CrearClaveCache(
            terreno.Latitud,
            terreno.Longitud,
            dias);

        if (!forzarActualizacion &&
            cache.TryGetValue(
                cacheKey,
                out PronosticoClimaTerrenoDto? almacenado) &&
            almacenado is not null)
        {
            return CopiarIdentidadTerreno(
                almacenado,
                terreno);
        }

        await Semaforo.WaitAsync(cancellationToken);

        try
        {
            if (!forzarActualizacion &&
                cache.TryGetValue(
                    cacheKey,
                    out almacenado) &&
                almacenado is not null)
            {
                return CopiarIdentidadTerreno(
                    almacenado,
                    terreno);
            }

            PronosticoClimaTerrenoDto respuesta =
                await ConsultarOpenMeteoAsync(
                    terreno,
                    dias,
                    cancellationToken);

            TimeSpan duracion = respuesta.Disponible
                ? TimeSpan.FromMinutes(
                    Math.Clamp(
                        options.MinutosCache * 3,
                        30,
                        180))
                : TimeSpan.FromMinutes(3);

            cache.Set(
                cacheKey,
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
                "El proveedor meteorológico agotó el tiempo de espera " +
                "al consultar el terreno {TerrenoId}.",
                terreno.TerrenoId);

            return CrearNoDisponible(
                terreno,
                dias,
                "El proveedor meteorológico tardó demasiado en responder.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "No fue posible consultar el pronóstico del terreno " +
                "{TerrenoId}.",
                terreno.TerrenoId);

            return CrearNoDisponible(
                terreno,
                dias,
                "No fue posible actualizar el pronóstico en este momento.");
        }
        finally
        {
            Semaforo.Release();
        }
    }

    private async Task<PronosticoClimaTerrenoDto>
        ConsultarOpenMeteoAsync(
            TerrenoPronosticoBase terreno,
            int dias,
            CancellationToken cancellationToken)
    {
        string baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? "https://api.open-meteo.com/"
            : options.BaseUrl.Trim();

        baseUrl = baseUrl.TrimEnd('/');

        string latitud = terreno.Latitud.ToString(
            "0.######",
            CultureInfo.InvariantCulture);

        string longitud = terreno.Longitud.ToString(
            "0.######",
            CultureInfo.InvariantCulture);

        string daily =
            "weather_code," +
            "temperature_2m_max,temperature_2m_min," +
            "apparent_temperature_max,apparent_temperature_min," +
            "precipitation_probability_max," +
            "precipitation_sum,rain_sum,precipitation_hours," +
            "wind_speed_10m_max,wind_gusts_10m_max," +
            "wind_direction_10m_dominant," +
            "et0_fao_evapotranspiration,uv_index_max," +
            "sunrise,sunset";

        string hourly =
            "temperature_2m,apparent_temperature," +
            "relative_humidity_2m,precipitation_probability," +
            "precipitation,rain,weather_code,cloud_cover," +
            "wind_speed_10m,wind_direction_10m,wind_gusts_10m," +
            "cape,soil_temperature_0cm," +
            "soil_moisture_0_to_1cm," +
            "soil_moisture_1_to_3cm," +
            "vapour_pressure_deficit";

        string ruta =
            $"{baseUrl}/v1/forecast" +
            $"?latitude={latitud}" +
            $"&longitude={longitud}" +
            $"&daily={daily}" +
            $"&hourly={hourly}" +
            "&temperature_unit=celsius" +
            "&wind_speed_unit=kmh" +
            "&precipitation_unit=mm" +
            "&timezone=America%2FManagua" +
            "&cell_selection=land" +
            $"&forecast_days={dias}";

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            ruta +=
                "&apikey=" +
                Uri.EscapeDataString(options.ApiKey.Trim());
        }

        HttpClient client =
            httpClientFactory.CreateClient();

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "CONATRADEC-PronosticoAgricola/1.1");

        using var timeoutCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutCts.CancelAfter(
            TimeSpan.FromSeconds(
                Math.Clamp(
                    options.SegundosTimeout,
                    8,
                    60)));

        using HttpResponseMessage response =
            await client.GetAsync(
                ruta,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

        response.EnsureSuccessStatusCode();

        await using Stream contenido =
            await response.Content.ReadAsStreamAsync(
                timeoutCts.Token);

        using JsonDocument documento =
            await JsonDocument.ParseAsync(
                contenido,
                cancellationToken: timeoutCts.Token);

        return ParsearPronostico(
            documento.RootElement,
            terreno,
            dias);
    }

    private static PronosticoClimaTerrenoDto ParsearPronostico(
        JsonElement raiz,
        TerrenoPronosticoBase terreno,
        int dias)
    {
        if (!raiz.TryGetProperty(
                "daily",
                out JsonElement diario) ||
            diario.ValueKind != JsonValueKind.Object)
        {
            return CrearNoDisponible(
                terreno,
                dias,
                "El proveedor respondió sin datos diarios utilizables.");
        }

        Dictionary<DateOnly, AcumuladorHorario> horarios =
            ParsearDatosHorarios(raiz);

        int totalDias = ObtenerLongitudArreglo(
            diario,
            "time");

        var resultadoDias =
            new List<PronosticoClimaDiaDto>();

        for (int indice = 0;
             indice < Math.Min(totalDias, dias);
             indice++)
        {
            DateOnly? fecha =
                LeerFechaArreglo(
                    diario,
                    "time",
                    indice);

            if (!fecha.HasValue)
                continue;

            horarios.TryGetValue(
                fecha.Value,
                out AcumuladorHorario? acumulado);

            var dia = new PronosticoClimaDiaDto
            {
                Fecha = fecha.Value,
                CodigoClima =
                    LeerEnteroArreglo(
                        diario,
                        "weather_code",
                        indice),
                TemperaturaMaxima =
                    LeerDecimalArreglo(
                        diario,
                        "temperature_2m_max",
                        indice),
                TemperaturaMinima =
                    LeerDecimalArreglo(
                        diario,
                        "temperature_2m_min",
                        indice),
                SensacionMaxima =
                    LeerDecimalArreglo(
                        diario,
                        "apparent_temperature_max",
                        indice),
                SensacionMinima =
                    LeerDecimalArreglo(
                        diario,
                        "apparent_temperature_min",
                        indice),
                ProbabilidadPrecipitacion =
                    LeerEnteroArreglo(
                        diario,
                        "precipitation_probability_max",
                        indice),
                Precipitacion =
                    LeerDecimalArreglo(
                        diario,
                        "precipitation_sum",
                        indice),
                Lluvia =
                    LeerDecimalArreglo(
                        diario,
                        "rain_sum",
                        indice),
                HorasPrecipitacion =
                    LeerDecimalArreglo(
                        diario,
                        "precipitation_hours",
                        indice),
                VelocidadVientoMaxima =
                    LeerDecimalArreglo(
                        diario,
                        "wind_speed_10m_max",
                        indice),
                RafagaMaxima =
                    LeerDecimalArreglo(
                        diario,
                        "wind_gusts_10m_max",
                        indice),
                DireccionVientoDominante =
                    LeerDecimalArreglo(
                        diario,
                        "wind_direction_10m_dominant",
                        indice),
                EvapotranspiracionEt0 =
                    LeerDecimalArreglo(
                        diario,
                        "et0_fao_evapotranspiration",
                        indice),
                IndiceUvMaximo =
                    LeerDecimalArreglo(
                        diario,
                        "uv_index_max",
                        indice),
                Amanecer =
                    LeerHoraArreglo(
                        diario,
                        "sunrise",
                        indice),
                Atardecer =
                    LeerHoraArreglo(
                        diario,
                        "sunset",
                        indice),
                HumedadMinima =
                    acumulado?.MinimoHumedad,
                HumedadMaxima =
                    acumulado?.MaximoHumedad,
                HumedadPromedio =
                    acumulado?.PromedioHumedad,
                TemperaturaSueloPromedio =
                    acumulado?.PromedioTemperaturaSuelo,
                HumedadSueloSuperficialPromedio =
                    acumulado?.PromedioHumedadSueloSuperficial,
                HumedadSueloTresCmPromedio =
                    acumulado?.PromedioHumedadSueloTresCm,
                DeficitPresionVaporMaximo =
                    acumulado?.MaximoDeficitPresionVapor,
                NubosidadPromedio =
                    acumulado?.PromedioNubosidad,
                RiesgoTormenta =
                    acumulado?.RiesgoTormenta ?? "BAJO",
                Periodos =
                    acumulado?.ConstruirPeriodos() ?? []
            };

            dia.Condicion =
                DescribirCodigoClima(
                    dia.CodigoClima);

            dia.ResumenNarrativo =
                ConstruirResumenNarrativo(dia);

            dia.RecomendacionesDetalladas =
                ConstruirRecomendacionesDetalladas(dia);

            // Compatibilidad con clientes que todavía esperan texto simple.
            dia.Recomendaciones =
                dia.RecomendacionesDetalladas
                    .Select(item => item.Mensaje)
                    .ToList();

            dia.Alertas =
                ConstruirAlertasDia(dia);

            resultadoDias.Add(dia);
        }

        if (resultadoDias.Count == 0)
        {
            return CrearNoDisponible(
                terreno,
                dias,
                "El proveedor respondió sin días de pronóstico utilizables.");
        }

        List<PronosticoClimaAlertaDto> alertas =
            resultadoDias
                .SelectMany(item => item.Alertas)
                .ToList();

        AgregarAlertaPeriodoSeco(
            resultadoDias,
            alertas);

        PronosticoClimaResumenDto resumen =
            ConstruirResumen(
                resultadoDias,
                alertas);

        return new PronosticoClimaTerrenoDto
        {
            Disponible = true,
            Mensaje =
                "Pronóstico agrícola diario calculado para las " +
                "coordenadas del terreno.",
            TerrenoId = terreno.TerrenoId,
            CodigoTerreno = terreno.CodigoTerreno,
            Direccion = terreno.Direccion,
            Municipio = terreno.Municipio,
            Departamento = terreno.Departamento,
            Latitud = terreno.Latitud,
            Longitud = terreno.Longitud,
            Proveedor = "Open-Meteo",
            Licencia = "Weather data by Open-Meteo",
            ActualizadoUtc = DateTime.UtcNow,
            DiasSolicitados = resultadoDias.Count,
            Resumen = resumen,
            Alertas = alertas
                .OrderByDescending(item =>
                    item.Nivel == "CRITICA")
                .ThenBy(item =>
                    item.FechaInicio)
                .ToList(),
            Dias = resultadoDias
        };
    }

    private static Dictionary<DateOnly, AcumuladorHorario>
        ParsearDatosHorarios(JsonElement raiz)
    {
        var resultado =
            new Dictionary<DateOnly, AcumuladorHorario>();

        if (!raiz.TryGetProperty(
                "hourly",
                out JsonElement horario) ||
            horario.ValueKind != JsonValueKind.Object)
        {
            return resultado;
        }

        int total = ObtenerLongitudArreglo(
            horario,
            "time");

        for (int indice = 0;
             indice < total;
             indice++)
        {
            DateTime? fechaHora =
                LeerFechaHoraCompletaArreglo(
                    horario,
                    "time",
                    indice);

            if (!fechaHora.HasValue)
                continue;

            DateOnly fecha =
                DateOnly.FromDateTime(fechaHora.Value);

            if (!resultado.TryGetValue(
                    fecha,
                    out AcumuladorHorario? acumulador))
            {
                acumulador = new AcumuladorHorario();
                resultado[fecha] = acumulador;
            }

            acumulador.Agregar(
                fechaHora.Value,
                LeerDecimalArreglo(
                    horario,
                    "temperature_2m",
                    indice),
                LeerDecimalArreglo(
                    horario,
                    "apparent_temperature",
                    indice),
                LeerDecimalArreglo(
                    horario,
                    "relative_humidity_2m",
                    indice),
                LeerEnteroArreglo(
                    horario,
                    "precipitation_probability",
                    indice),
                LeerDecimalArreglo(
                    horario,
                    "precipitation",
                    indice),
                LeerDecimalArreglo(
                    horario,
                    "rain",
                    indice),
                LeerEnteroArreglo(
                    horario,
                    "weather_code",
                    indice),
                LeerDecimalArreglo(
                    horario,
                    "cloud_cover",
                    indice),
                LeerDecimalArreglo(
                    horario,
                    "wind_speed_10m",
                    indice),
                LeerDecimalArreglo(
                    horario,
                    "wind_direction_10m",
                    indice),
                LeerDecimalArreglo(
                    horario,
                    "wind_gusts_10m",
                    indice),
                LeerDecimalArreglo(
                    horario,
                    "cape",
                    indice),
                LeerDecimalArreglo(
                    horario,
                    "soil_temperature_0cm",
                    indice),
                LeerDecimalArreglo(
                    horario,
                    "soil_moisture_0_to_1cm",
                    indice),
                LeerDecimalArreglo(
                    horario,
                    "soil_moisture_1_to_3cm",
                    indice),
                LeerDecimalArreglo(
                    horario,
                    "vapour_pressure_deficit",
                    indice));
        }

        return resultado;
    }

    private static string ConstruirResumenNarrativo(
        PronosticoClimaDiaDto dia)
    {
        var partes = new List<string>();

        if (dia.TemperaturaMaxima.HasValue)
        {
            partes.Add(
                dia.TemperaturaMaxima.Value >= 34
                    ? "ambiente caluroso"
                    : dia.TemperaturaMaxima.Value >= 30
                        ? "ambiente cálido"
                        : "temperatura moderada");
        }

        if (dia.ProbabilidadPrecipitacion >= 75)
        {
            partes.Add("alta probabilidad de lluvia");
        }
        else if (dia.ProbabilidadPrecipitacion >= 45)
        {
            partes.Add("posibles lluvias aisladas");
        }
        else
        {
            partes.Add("baja probabilidad de lluvia");
        }

        if (dia.RafagaMaxima >= 40)
            partes.Add("ráfagas fuertes");
        else if (dia.VelocidadVientoMaxima >= 25)
            partes.Add("viento moderado");

        if (dia.NubosidadPromedio >= 75)
            partes.Add("cielo mayormente cubierto");
        else if (dia.NubosidadPromedio >= 40)
            partes.Add("nubosidad variable");

        string detalle = string.Join(
            ", ",
            partes);

        if (string.IsNullOrWhiteSpace(detalle))
            return dia.Condicion;

        return $"{dia.Condicion}, con {detalle}.";
    }

    private static List<PronosticoClimaRecomendacionDto>
        ConstruirRecomendacionesDetalladas(
            PronosticoClimaDiaDto dia)
    {
        var recomendaciones =
            new List<PronosticoClimaRecomendacionDto>();

        var evidenciasLluvia =
            new List<PronosticoClimaEvidenciaDto>();

        if (dia.ProbabilidadPrecipitacion is int probabilidad &&
            probabilidad >= 70)
        {
            evidenciasLluvia.Add(CrearEvidencia(
                "Probabilidad de precipitación",
                probabilidad,
                "≥",
                70,
                "%"));
        }

        if (dia.Precipitacion is decimal precipitacion &&
            precipitacion >= 10)
        {
            evidenciasLluvia.Add(CrearEvidencia(
                "Precipitación diaria",
                precipitacion,
                "≥",
                10,
                "mm"));
        }

        if (evidenciasLluvia.Count > 0)
        {
            recomendaciones.Add(CrearRecomendacion(
                "EVITAR_APLICACION_ANTES_LLUVIA",
                "Evitar aplicaciones antes de la lluvia",
                "Evite aplicar fertilizantes o productos antes de las " +
                "horas de mayor lluvia prevista.",
                evidenciasLluvia));
        }

        var evidenciasViento =
            new List<PronosticoClimaEvidenciaDto>();

        if (dia.RafagaMaxima is decimal rafaga &&
            rafaga >= 40)
        {
            evidenciasViento.Add(CrearEvidencia(
                "Ráfaga máxima",
                rafaga,
                "≥",
                40,
                "km/h"));
        }

        if (dia.VelocidadVientoMaxima is decimal viento &&
            viento >= 30)
        {
            evidenciasViento.Add(CrearEvidencia(
                "Velocidad máxima del viento",
                viento,
                "≥",
                30,
                "km/h"));
        }

        if (evidenciasViento.Count > 0)
        {
            recomendaciones.Add(CrearRecomendacion(
                "EVITAR_APLICACIONES_CON_VIENTO",
                "Evitar aplicaciones con viento fuerte",
                "Evite aplicaciones foliares durante las horas de mayor " +
                "viento y asegure materiales livianos.",
                evidenciasViento));
        }

        if (dia.IndiceUvMaximo is decimal indiceUv &&
            indiceUv >= 8)
        {
            recomendaciones.Add(CrearRecomendacion(
                "LABORES_FUERA_HORAS_UV",
                "Priorizar horarios de menor radiación",
                "Priorice las labores de campo durante las primeras horas " +
                "de la mañana o al final de la tarde.",
                [
                    CrearEvidencia(
                        "Índice UV máximo",
                        indiceUv,
                        "≥",
                        8,
                        string.Empty)
                ]));
        }

        if (dia.HumedadPromedio is decimal humedad &&
            humedad >= 85 &&
            dia.ProbabilidadPrecipitacion is int probabilidadHongos &&
            probabilidadHongos >= 60)
        {
            recomendaciones.Add(CrearRecomendacion(
                "MONITOREO_HONGOS",
                "Monitorear enfermedades fúngicas",
                "Monitoree signos de roya y otras enfermedades fúngicas " +
                "por la combinación de humedad alta y lluvia.",
                [
                    CrearEvidencia(
                        "Humedad relativa promedio",
                        humedad,
                        "≥",
                        85,
                        "%"),
                    CrearEvidencia(
                        "Probabilidad de precipitación",
                        probabilidadHongos,
                        "≥",
                        60,
                        "%")
                ]));
        }

        var evidenciasHidricas =
            new List<PronosticoClimaEvidenciaDto>();

        if (dia.EvapotranspiracionEt0 is decimal et0 &&
            et0 >= 5.5m)
        {
            evidenciasHidricas.Add(CrearEvidencia(
                "Evapotranspiración ET₀",
                et0,
                "≥",
                5.5m,
                "mm"));
        }

        if (dia.DeficitPresionVaporMaximo is decimal vpd &&
            vpd >= 1.6m)
        {
            evidenciasHidricas.Add(CrearEvidencia(
                "Déficit de presión de vapor",
                vpd,
                "≥",
                1.6m,
                "kPa"));
        }

        if (evidenciasHidricas.Count > 0)
        {
            recomendaciones.Add(CrearRecomendacion(
                "REVISAR_NECESIDADES_HIDRICAS",
                "Revisar necesidades hídricas",
                "Revise la humedad del suelo y las necesidades hídricas " +
                "del cultivo.",
                evidenciasHidricas));
        }

        if (recomendaciones.Count == 0)
        {
            recomendaciones.Add(CrearRecomendacion(
                "MONITOREO_NORMAL",
                "Mantener monitoreo normal",
                "No se observan restricciones meteorológicas importantes; " +
                "mantenga el monitoreo normal del terreno.",
                []));
        }

        return recomendaciones;
    }

    private static PronosticoClimaRecomendacionDto
        CrearRecomendacion(
            string clave,
            string titulo,
            string mensaje,
            List<PronosticoClimaEvidenciaDto> evidencias) =>
        new()
        {
            Clave = clave,
            Titulo = titulo,
            Mensaje = mensaje,
            Fuente = "Regla automática de CONATRADEC",
            Evidencias = evidencias
        };

    private static PronosticoClimaEvidenciaDto
        CrearEvidencia(
            string indicador,
            decimal valorObservado,
            string operador,
            decimal umbral,
            string unidad)
    {
        string sufijo =
            string.IsNullOrWhiteSpace(unidad)
                ? string.Empty
                : $" {unidad}";

        return new PronosticoClimaEvidenciaDto
        {
            Indicador = indicador,
            ValorObservado = valorObservado,
            Operador = operador,
            Umbral = umbral,
            Unidad = unidad,
            FuenteDato = "Open-Meteo",
            ReglaAplicada =
                $"{indicador}: {valorObservado:N1}{sufijo} " +
                $"{operador} {umbral:N1}{sufijo}"
        };
    }

    private static List<PronosticoClimaAlertaDto>
        ConstruirAlertasDia(
            PronosticoClimaDiaDto dia)
    {
        var alertas =
            new List<PronosticoClimaAlertaDto>();

        if (dia.Precipitacion >= 30)
        {
            alertas.Add(CrearAlerta(
                "LLUVIA_INTENSA",
                "CRITICA",
                "Lluvia intensa prevista",
                $"Se estiman {dia.Precipitacion:N1} mm de " +
                "precipitación durante el día.",
                dia.Fecha));
        }
        else if (dia.ProbabilidadPrecipitacion >= 80 &&
                 dia.Precipitacion >= 10)
        {
            alertas.Add(CrearAlerta(
                "LLUVIA_PROBABLE",
                "ATENCION",
                "Alta probabilidad de lluvia",
                $"Existe {dia.ProbabilidadPrecipitacion}% de " +
                $"probabilidad y se estiman {dia.Precipitacion:N1} mm.",
                dia.Fecha));
        }

        if (dia.RafagaMaxima >= 40)
        {
            alertas.Add(CrearAlerta(
                "RAFAGAS_FUERTES",
                "CRITICA",
                "Ráfagas fuertes",
                $"Se esperan ráfagas de hasta " +
                $"{dia.RafagaMaxima:N1} km/h.",
                dia.Fecha));
        }
        else if (dia.VelocidadVientoMaxima >= 30)
        {
            alertas.Add(CrearAlerta(
                "VIENTO_FUERTE",
                "ATENCION",
                "Viento fuerte",
                $"La velocidad máxima prevista es " +
                $"{dia.VelocidadVientoMaxima:N1} km/h.",
                dia.Fecha));
        }

        if (dia.HumedadPromedio >= 85 &&
            dia.ProbabilidadPrecipitacion >= 60)
        {
            alertas.Add(CrearAlerta(
                "HUMEDAD_FUNGICA",
                "ATENCION",
                "Humedad alta persistente",
                "La combinación de humedad alta y lluvia puede " +
                "favorecer enfermedades fúngicas.",
                dia.Fecha));
        }

        if (dia.EvapotranspiracionEt0 >= 5.5m)
        {
            alertas.Add(CrearAlerta(
                "ET0_ALTA",
                "ATENCION",
                "Alta evapotranspiración",
                $"La ET₀ prevista es " +
                $"{dia.EvapotranspiracionEt0:N1} mm; revise las " +
                "necesidades hídricas del cultivo.",
                dia.Fecha));
        }

        if (dia.DeficitPresionVaporMaximo >= 1.6m)
        {
            alertas.Add(CrearAlerta(
                "VPD_ALTO",
                "ATENCION",
                "Posible estrés hídrico",
                $"El déficit de presión de vapor puede alcanzar " +
                $"{dia.DeficitPresionVaporMaximo:N2} kPa.",
                dia.Fecha));
        }

        if (dia.IndiceUvMaximo >= 8)
        {
            alertas.Add(CrearAlerta(
                "UV_MUY_ALTO",
                "ATENCION",
                "Radiación UV muy alta",
                $"El índice UV máximo previsto es " +
                $"{dia.IndiceUvMaximo:N1}.",
                dia.Fecha));
        }

        return alertas;
    }

    private static void AgregarAlertaPeriodoSeco(
        IReadOnlyList<PronosticoClimaDiaDto> dias,
        ICollection<PronosticoClimaAlertaDto> alertas)
    {
        List<PronosticoClimaDiaDto> primeros =
            dias.Take(5).ToList();

        if (primeros.Count < 4)
            return;

        decimal totalPrecipitacion =
            primeros.Sum(item =>
                item.Precipitacion ?? 0);

        int probabilidadMaxima =
            primeros.Max(item =>
                item.ProbabilidadPrecipitacion ?? 0);

        if (totalPrecipitacion < 5 &&
            probabilidadMaxima < 45)
        {
            alertas.Add(new PronosticoClimaAlertaDto
            {
                Clave = "PERIODO_SECO",
                Nivel = "ATENCION",
                Titulo = "Periodo seco previsto",
                Mensaje =
                    "No se prevén lluvias significativas durante los " +
                    "próximos cinco días. Revise humedad del suelo y " +
                    "necesidades de riego.",
                FechaInicio = primeros.First().Fecha,
                FechaFin = primeros.Last().Fecha
            });
        }
    }

    private static PronosticoClimaResumenDto ConstruirResumen(
        IReadOnlyList<PronosticoClimaDiaDto> dias,
        IReadOnlyList<PronosticoClimaAlertaDto> alertas)
    {
        bool critica =
            alertas.Any(item =>
                item.Nivel == "CRITICA");

        bool atencion =
            alertas.Count > 0;

        return new PronosticoClimaResumenDto
        {
            TemperaturaMaximaPeriodo =
                Maximo(dias.Select(item =>
                    item.TemperaturaMaxima)),
            TemperaturaMinimaPeriodo =
                Minimo(dias.Select(item =>
                    item.TemperaturaMinima)),
            PrecipitacionTotal =
                dias.Sum(item =>
                    item.Precipitacion ?? 0),
            ProbabilidadLluviaMaxima =
                dias.Max(item =>
                    item.ProbabilidadPrecipitacion ?? 0),
            RafagaMaxima =
                Maximo(dias.Select(item =>
                    item.RafagaMaxima)),
            EvapotranspiracionTotal =
                dias.Sum(item =>
                    item.EvapotranspiracionEt0 ?? 0),
            DiasConLluvia =
                dias.Count(item =>
                    (item.Precipitacion ?? 0) >= 0.5m),
            NivelRiesgo =
                critica
                    ? "CRITICA"
                    : atencion
                        ? "ATENCION"
                        : "NORMAL",
            MensajeRiesgo =
                critica
                    ? "El pronóstico contiene condiciones que requieren " +
                      "atención prioritaria."
                    : atencion
                        ? "Existen condiciones que conviene vigilar."
                        : "Sin alertas meteorológicas relevantes."
        };
    }

    private async Task<TerrenoPronosticoBase?>
        ObtenerTerrenoPropietarioAsync(
            int usuarioId,
            int terrenoId,
            CancellationToken cancellationToken)
    {
        DbConnection connection =
            db.Database.GetDbConnection();

        bool cerrar =
            connection.State != ConnectionState.Open;

        if (cerrar)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using DbCommand command =
                connection.CreateCommand();

            command.CommandText =
                """
                SELECT TOP (1)
                    t.terrenoId,
                    t.codigoTerreno,
                    t.direccionTerreno,
                    ISNULL(m.nombreMunicipio, N''),
                    ISNULL(d.nombreDepartamento, N''),
                    t.latitud,
                    t.longitud
                FROM dbo.usuarioPropietario up
                INNER JOIN dbo.propietarioTerreno pt
                    ON pt.propietarioId = up.propietarioId
                   AND pt.activo = 1
                INNER JOIN dbo.terreno t
                    ON t.terrenoId = pt.terrenoId
                   AND t.activo = 1
                LEFT JOIN dbo.municipio m
                    ON m.municipioId = t.municipioId
                LEFT JOIN dbo.departamento d
                    ON d.departamentoId = m.departamentoId
                WHERE up.usuarioId = @usuarioId
                  AND up.activo = 1
                  AND t.terrenoId = @terrenoId;
                """;

            AgregarParametro(
                command,
                "@usuarioId",
                usuarioId);

            AgregarParametro(
                command,
                "@terrenoId",
                terrenoId);

            await using DbDataReader reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                return null;

            return new TerrenoPronosticoBase
            {
                TerrenoId = reader.GetInt32(0),
                CodigoTerreno =
                    reader.IsDBNull(1)
                        ? string.Empty
                        : reader.GetString(1),
                Direccion =
                    reader.IsDBNull(2)
                        ? string.Empty
                        : reader.GetString(2),
                Municipio =
                    reader.IsDBNull(3)
                        ? string.Empty
                        : reader.GetString(3),
                Departamento =
                    reader.IsDBNull(4)
                        ? string.Empty
                        : reader.GetString(4),
                Latitud =
                    reader.IsDBNull(5)
                        ? 0
                        : reader.GetDecimal(5),
                Longitud =
                    reader.IsDBNull(6)
                        ? 0
                        : reader.GetDecimal(6)
            };
        }
        finally
        {
            if (cerrar)
                await connection.CloseAsync();
        }
    }

    private async Task<IActionResult?> ValidarAccesoPortalAsync(
        int usuarioId,
        CancellationToken cancellationToken)
    {
        ResultadoPermisoApi accesoPortal =
            await permisos.ValidarAsync(
                usuarioId,
                InterfazPortal,
                TipoPermisoApi.Leer,
                cancellationToken);

        if (!accesoPortal.Permitido)
        {
            return StatusCode(
                accesoPortal.CodigoEstado,
                new
                {
                    success = false,
                    message = accesoPortal.Mensaje
                });
        }

        ResultadoPermisoApi accesoPropietario =
            await permisos.ValidarAsync(
                usuarioId,
                InterfazPortalPropietario,
                TipoPermisoApi.Leer,
                cancellationToken);

        if (!accesoPropietario.Permitido)
        {
            return StatusCode(
                accesoPropietario.CodigoEstado,
                new
                {
                    success = false,
                    message = accesoPropietario.Mensaje
                });
        }

        return null;
    }

    private int? ObtenerUsuarioId()
    {
        string? valor =
            User.FindFirst("uid")?.Value ??
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst("sub")?.Value;

        if (int.TryParse(valor, out int usuarioId) &&
            usuarioId > 0)
        {
            return usuarioId;
        }

        if (Request.Headers.TryGetValue(
                "X-Usuario-Id",
                out var encabezado) &&
            int.TryParse(
                encabezado.ToString(),
                out usuarioId) &&
            usuarioId > 0)
        {
            return usuarioId;
        }

        return null;
    }

    private static PronosticoClimaTerrenoDto CrearNoDisponible(
        TerrenoPronosticoBase terreno,
        int dias,
        string mensaje) =>
        new()
        {
            Disponible = false,
            Mensaje = mensaje,
            TerrenoId = terreno.TerrenoId,
            CodigoTerreno = terreno.CodigoTerreno,
            Direccion = terreno.Direccion,
            Municipio = terreno.Municipio,
            Departamento = terreno.Departamento,
            Latitud = terreno.Latitud,
            Longitud = terreno.Longitud,
            ActualizadoUtc = DateTime.UtcNow,
            DiasSolicitados = dias,
            Dias = [],
            Alertas = []
        };

    private static PronosticoClimaTerrenoDto CopiarIdentidadTerreno(
        PronosticoClimaTerrenoDto almacenado,
        TerrenoPronosticoBase terreno)
    {
        almacenado.TerrenoId = terreno.TerrenoId;
        almacenado.CodigoTerreno = terreno.CodigoTerreno;
        almacenado.Direccion = terreno.Direccion;
        almacenado.Municipio = terreno.Municipio;
        almacenado.Departamento = terreno.Departamento;
        return almacenado;
    }

    private static string CrearClaveCache(
        decimal latitud,
        decimal longitud,
        int dias) =>
        "pronostico-clima:" +
        $"{decimal.Round(latitud, 4)}:" +
        $"{decimal.Round(longitud, 4)}:" +
        $"{dias}:v1";

    private static bool CoordenadaValida(
        decimal latitud,
        decimal longitud) =>
        latitud >= 10.45m &&
        latitud <= 15.35m &&
        longitud >= -88.15m &&
        longitud <= -82.25m;

    private static void AgregarParametro(
        DbCommand command,
        string nombre,
        object valor)
    {
        DbParameter parametro =
            command.CreateParameter();

        parametro.ParameterName = nombre;
        parametro.Value = valor;
        parametro.DbType = DbType.Int32;

        command.Parameters.Add(parametro);
    }

    private static int ObtenerLongitudArreglo(
        JsonElement objeto,
        string propiedad)
    {
        if (!objeto.TryGetProperty(
                propiedad,
                out JsonElement arreglo) ||
            arreglo.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return arreglo.GetArrayLength();
    }

    private static JsonElement? ObtenerElementoArreglo(
        JsonElement objeto,
        string propiedad,
        int indice)
    {
        if (!objeto.TryGetProperty(
                propiedad,
                out JsonElement arreglo) ||
            arreglo.ValueKind != JsonValueKind.Array ||
            indice < 0 ||
            indice >= arreglo.GetArrayLength())
        {
            return null;
        }

        return arreglo[indice];
    }

    private static decimal? LeerDecimalArreglo(
        JsonElement objeto,
        string propiedad,
        int indice)
    {
        JsonElement? elemento =
            ObtenerElementoArreglo(
                objeto,
                propiedad,
                indice);

        if (!elemento.HasValue ||
            elemento.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (elemento.Value.ValueKind == JsonValueKind.Number &&
            elemento.Value.TryGetDecimal(out decimal numero))
        {
            return numero;
        }

        if (elemento.Value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(
                elemento.Value.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out numero))
        {
            return numero;
        }

        return null;
    }

    private static int? LeerEnteroArreglo(
        JsonElement objeto,
        string propiedad,
        int indice)
    {
        decimal? valor =
            LeerDecimalArreglo(
                objeto,
                propiedad,
                indice);

        return valor.HasValue
            ? Convert.ToInt32(
                Math.Round(
                    valor.Value,
                    0,
                    MidpointRounding.AwayFromZero))
            : null;
    }

    private static DateOnly? LeerFechaArreglo(
        JsonElement objeto,
        string propiedad,
        int indice)
    {
        JsonElement? elemento =
            ObtenerElementoArreglo(
                objeto,
                propiedad,
                indice);

        if (!elemento.HasValue ||
            elemento.Value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateOnly.TryParse(
            elemento.Value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly fecha)
                ? fecha
                : null;
    }

    private static DateTime? LeerFechaHoraCompletaArreglo(
        JsonElement objeto,
        string propiedad,
        int indice)
    {
        JsonElement? elemento =
            ObtenerElementoArreglo(
                objeto,
                propiedad,
                indice);

        if (!elemento.HasValue ||
            elemento.Value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTime.TryParse(
            elemento.Value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTime fecha)
                ? fecha
                : null;
    }

    private static DateOnly? LeerFechaHoraArreglo(
        JsonElement objeto,
        string propiedad,
        int indice)
    {
        JsonElement? elemento =
            ObtenerElementoArreglo(
                objeto,
                propiedad,
                indice);

        if (!elemento.HasValue ||
            elemento.Value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTime.TryParse(
            elemento.Value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTime fecha)
                ? DateOnly.FromDateTime(fecha)
                : null;
    }

    private static string LeerHoraArreglo(
        JsonElement objeto,
        string propiedad,
        int indice)
    {
        JsonElement? elemento =
            ObtenerElementoArreglo(
                objeto,
                propiedad,
                indice);

        if (!elemento.HasValue ||
            elemento.Value.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return DateTime.TryParse(
            elemento.Value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTime fecha)
                ? fecha.ToString(
                    "hh:mm tt",
                    CultureInfo.GetCultureInfo("es-NI"))
                : string.Empty;
    }

    private static PronosticoClimaAlertaDto CrearAlerta(
        string clave,
        string nivel,
        string titulo,
        string mensaje,
        DateOnly fecha) =>
        new()
        {
            Clave = clave,
            Nivel = nivel,
            Titulo = titulo,
            Mensaje = mensaje,
            FechaInicio = fecha,
            FechaFin = fecha
        };

    private static decimal? Minimo(
        IEnumerable<decimal?> valores)
    {
        decimal[] disponibles =
            valores
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
                .ToArray();

        return disponibles.Length > 0
            ? disponibles.Min()
            : null;
    }

    private static decimal? Maximo(
        IEnumerable<decimal?> valores)
    {
        decimal[] disponibles =
            valores
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
                .ToArray();

        return disponibles.Length > 0
            ? disponibles.Max()
            : null;
    }

    private static string DescribirCodigoClima(
        int? codigo) =>
        codigo switch
        {
            0 => "Despejado",
            1 => "Principalmente despejado",
            2 => "Parcialmente nublado",
            3 => "Nublado",
            45 or 48 => "Neblina",
            51 or 53 or 55 => "Llovizna",
            56 or 57 => "Llovizna helada",
            61 => "Lluvia ligera",
            63 => "Lluvia moderada",
            65 => "Lluvia fuerte",
            66 or 67 => "Lluvia helada",
            71 or 73 or 75 or 77 => "Nieve",
            80 => "Chubascos ligeros",
            81 => "Chubascos moderados",
            82 => "Chubascos fuertes",
            85 or 86 => "Chubascos de nieve",
            95 => "Tormenta",
            96 or 99 => "Tormenta con granizo",
            _ => "Condición variable"
        };

    private sealed class TerrenoPronosticoBase
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

        public decimal Latitud { get; set; }

        public decimal Longitud { get; set; }
    }

    private sealed class AcumuladorHorario
    {
        private readonly List<decimal> humedades = [];
        private readonly List<decimal> temperaturasSuelo = [];
        private readonly List<decimal> humedadesSueloSuperficial = [];
        private readonly List<decimal> humedadesSueloTresCm = [];
        private readonly List<decimal> deficitPresionVapor = [];
        private readonly List<decimal> nubosidades = [];
        private readonly List<decimal> capes = [];
        private readonly List<int> probabilidadesPrecipitacion = [];
        private readonly List<int> codigosClima = [];

        private readonly Dictionary<string, AcumuladorPeriodo> periodos =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["MANANA"] = new AcumuladorPeriodo(
                    "MANANA",
                    "Mañana",
                    "05:00–11:59"),
                ["TARDE"] = new AcumuladorPeriodo(
                    "TARDE",
                    "Tarde",
                    "12:00–17:59"),
                ["NOCHE"] = new AcumuladorPeriodo(
                    "NOCHE",
                    "Noche",
                    "18:00–04:59")
            };

        public decimal? MinimoHumedad =>
            Minimo(humedades);

        public decimal? MaximoHumedad =>
            Maximo(humedades);

        public decimal? PromedioHumedad =>
            Promedio(humedades);

        public decimal? PromedioTemperaturaSuelo =>
            Promedio(temperaturasSuelo);

        public decimal? PromedioHumedadSueloSuperficial =>
            Promedio(humedadesSueloSuperficial);

        public decimal? PromedioHumedadSueloTresCm =>
            Promedio(humedadesSueloTresCm);

        public decimal? MaximoDeficitPresionVapor =>
            Maximo(deficitPresionVapor);

        public decimal? PromedioNubosidad =>
            Promedio(nubosidades);

        public string RiesgoTormenta =>
            CalcularRiesgoTormenta(
                capes.Count > 0 ? capes.Max() : null,
                probabilidadesPrecipitacion.Count > 0
                    ? probabilidadesPrecipitacion.Max()
                    : null,
                codigosClima);

        public void Agregar(
            DateTime fechaHora,
            decimal? temperatura,
            decimal? sensacion,
            decimal? humedad,
            int? probabilidadPrecipitacion,
            decimal? precipitacion,
            decimal? lluvia,
            int? codigoClima,
            decimal? nubosidad,
            decimal? viento,
            decimal? direccionViento,
            decimal? rafaga,
            decimal? cape,
            decimal? temperaturaSuelo,
            decimal? humedadSueloSuperficial,
            decimal? humedadSueloTresCm,
            decimal? vpd)
        {
            AgregarSiExiste(humedades, humedad);
            AgregarSiExiste(temperaturasSuelo, temperaturaSuelo);
            AgregarSiExiste(
                humedadesSueloSuperficial,
                humedadSueloSuperficial);
            AgregarSiExiste(
                humedadesSueloTresCm,
                humedadSueloTresCm);
            AgregarSiExiste(deficitPresionVapor, vpd);
            AgregarSiExiste(nubosidades, nubosidad);
            AgregarSiExiste(capes, cape);

            if (probabilidadPrecipitacion.HasValue)
                probabilidadesPrecipitacion.Add(
                    probabilidadPrecipitacion.Value);

            if (codigoClima.HasValue)
                codigosClima.Add(codigoClima.Value);

            string clave = fechaHora.Hour switch
            {
                >= 5 and < 12 => "MANANA",
                >= 12 and < 18 => "TARDE",
                _ => "NOCHE"
            };

            periodos[clave].Agregar(
                temperatura,
                sensacion,
                humedad,
                probabilidadPrecipitacion,
                precipitacion,
                lluvia,
                codigoClima,
                nubosidad,
                viento,
                direccionViento,
                rafaga,
                cape);
        }

        public List<PronosticoClimaPeriodoDto> ConstruirPeriodos() =>
            [
                periodos["MANANA"].Construir(),
                periodos["TARDE"].Construir(),
                periodos["NOCHE"].Construir()
            ];
    }

    private sealed class AcumuladorPeriodo
    {
        private readonly string clave;
        private readonly string nombre;
        private readonly string rangoHorario;
        private readonly List<decimal> temperaturas = [];
        private readonly List<decimal> sensaciones = [];
        private readonly List<decimal> humedades = [];
        private readonly List<int> probabilidadesPrecipitacion = [];
        private readonly List<decimal> precipitaciones = [];
        private readonly List<decimal> lluvias = [];
        private readonly List<int> codigosClima = [];
        private readonly List<decimal> nubosidades = [];
        private readonly List<decimal> vientos = [];
        private readonly List<decimal> rafagas = [];
        private readonly List<decimal> capes = [];
        private decimal? direccionVientoDominante;
        private decimal velocidadDireccionDominante = decimal.MinValue;

        public AcumuladorPeriodo(
            string clave,
            string nombre,
            string rangoHorario)
        {
            this.clave = clave;
            this.nombre = nombre;
            this.rangoHorario = rangoHorario;
        }

        public void Agregar(
            decimal? temperatura,
            decimal? sensacion,
            decimal? humedad,
            int? probabilidadPrecipitacion,
            decimal? precipitacion,
            decimal? lluvia,
            int? codigoClima,
            decimal? nubosidad,
            decimal? viento,
            decimal? direccionViento,
            decimal? rafaga,
            decimal? cape)
        {
            AgregarSiExiste(temperaturas, temperatura);
            AgregarSiExiste(sensaciones, sensacion);
            AgregarSiExiste(humedades, humedad);
            AgregarSiExiste(precipitaciones, precipitacion);
            AgregarSiExiste(lluvias, lluvia);
            AgregarSiExiste(nubosidades, nubosidad);
            AgregarSiExiste(vientos, viento);
            AgregarSiExiste(rafagas, rafaga);
            AgregarSiExiste(capes, cape);

            if (probabilidadPrecipitacion.HasValue)
                probabilidadesPrecipitacion.Add(
                    probabilidadPrecipitacion.Value);

            if (codigoClima.HasValue)
                codigosClima.Add(codigoClima.Value);

            if (viento.HasValue &&
                direccionViento.HasValue &&
                viento.Value > velocidadDireccionDominante)
            {
                velocidadDireccionDominante = viento.Value;
                direccionVientoDominante = direccionViento.Value;
            }
        }

        public PronosticoClimaPeriodoDto Construir()
        {
            int? codigo = CodigoMasSevero(codigosClima);
            int? probabilidad =
                probabilidadesPrecipitacion.Count > 0
                    ? probabilidadesPrecipitacion.Max()
                    : null;
            decimal? cape =
                capes.Count > 0
                    ? capes.Max()
                    : null;

            return new PronosticoClimaPeriodoDto
            {
                Clave = clave,
                Nombre = nombre,
                RangoHorario = rangoHorario,
                CodigoClima = codigo,
                Condicion = DescribirCodigoClima(codigo),
                TemperaturaPromedio = Promedio(temperaturas),
                TemperaturaMaxima = Maximo(temperaturas),
                TemperaturaMinima = Minimo(temperaturas),
                SensacionPromedio = Promedio(sensaciones),
                HumedadPromedio = Promedio(humedades),
                NubosidadPromedio = Promedio(nubosidades),
                ProbabilidadPrecipitacionMaxima = probabilidad,
                PrecipitacionTotal = Suma(precipitaciones),
                LluviaTotal = Suma(lluvias),
                VelocidadVientoPromedio = Promedio(vientos),
                VelocidadVientoMaxima = Maximo(vientos),
                RafagaMaxima = Maximo(rafagas),
                DireccionVientoDominante = direccionVientoDominante,
                RiesgoTormenta = CalcularRiesgoTormenta(
                    cape,
                    probabilidad,
                    codigosClima)
            };
        }
    }

    private static string CalcularRiesgoTormenta(
        decimal? cape,
        int? probabilidadPrecipitacion,
        IReadOnlyCollection<int> codigos)
    {
        if (codigos.Any(item => item is 95 or 96 or 99))
            return "ALTO";

        if (cape >= 1000 && probabilidadPrecipitacion >= 60)
            return "ALTO";

        if (cape >= 500 || probabilidadPrecipitacion >= 75)
            return "MEDIO";

        return "BAJO";
    }

    private static int? CodigoMasSevero(
        IReadOnlyCollection<int> codigos)
    {
        if (codigos.Count == 0)
            return null;

        int Prioridad(int codigo) => codigo switch
        {
            99 => 100,
            96 => 99,
            95 => 98,
            82 => 90,
            65 => 85,
            81 => 80,
            63 => 75,
            80 => 70,
            61 => 65,
            57 => 60,
            55 => 55,
            53 => 50,
            51 => 45,
            48 => 40,
            45 => 39,
            3 => 30,
            2 => 20,
            1 => 10,
            0 => 0,
            _ => 1
        };

        return codigos
            .OrderByDescending(Prioridad)
            .First();
    }

    private static void AgregarSiExiste(
        ICollection<decimal> destino,
        decimal? valor)
    {
        if (valor.HasValue)
            destino.Add(valor.Value);
    }

    private static decimal? Promedio(
        IReadOnlyCollection<decimal> valores) =>
        valores.Count > 0
            ? decimal.Round(valores.Average(), 3)
            : null;

    private static decimal? Minimo(
        IReadOnlyCollection<decimal> valores) =>
        valores.Count > 0
            ? valores.Min()
            : null;

    private static decimal? Maximo(
        IReadOnlyCollection<decimal> valores) =>
        valores.Count > 0
            ? valores.Max()
            : null;

    private static decimal? Suma(
        IReadOnlyCollection<decimal> valores) =>
        valores.Count > 0
            ? decimal.Round(valores.Sum(), 3)
            : null;

}