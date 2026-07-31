using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.CentroGeoespacialDto;

namespace CONATRADEC_API.Controllers;

[ApiController]
[Route("api/centro-geoespacial")]
public sealed class CentroGeoespacialController : ControllerBase
{
    private readonly DBContext db;
    private readonly ClimaMapaService climaService;
    private readonly CapasSueloMapaService capasSueloService;
    private readonly UmbralesAlertasService umbralesService;

    public CentroGeoespacialController(
        DBContext db,
        ClimaMapaService climaService,
        CapasSueloMapaService capasSueloService,
        UmbralesAlertasService umbralesService)
    {
        this.db = db;
        this.climaService = climaService;
        this.capasSueloService = capasSueloService;
        this.umbralesService = umbralesService;
    }

    [HttpGet("capas")]
    public async Task<ActionResult<CentroGeoespacialCapasRespuestaDto>>
        ObtenerCapas(CancellationToken cancellationToken = default)
    {
        var capas = new List<CapaMapaDto>
        {
            CrearCapa(
                "departamentos",
                "Límites departamentales",
                "BASE",
                "fa-solid fa-border-all",
                "División político-administrativa de Nicaragua.",
                "POLIGONO",
                true,
                true,
                10),

            CrearCapa(
                "municipios",
                "Límites municipales",
                "BASE",
                "fa-solid fa-draw-polygon",
                "Permite navegar de departamento a municipio.",
                "POLIGONO",
                true,
                false,
                20),

            CrearCapa(
                "terrenos",
                "Terrenos registrados",
                "AGRICOLA",
                "fa-solid fa-seedling",
                "Terrenos georreferenciados con su estado agrícola.",
                "MARCADORES",
                true,
                true,
                30),

            CrearCapa(
                "alertas",
                "Alertas agrícolas",
                "AGRICOLA",
                "fa-solid fa-triangle-exclamation",
                "Resalta terrenos críticos o que requieren atención.",
                "MARCADORES",
                true,
                true,
                40),

            CrearCapa(
                "ph",
                "pH del suelo",
                "SUELOS",
                "fa-solid fa-flask",
                "Promedio territorial del último análisis disponible de cada terreno.",
                "COROPLETICO",
                true,
                false,
                50),

            CrearCapa(
                "materia-organica",
                "Materia orgánica",
                "SUELOS",
                "fa-solid fa-leaf",
                "Promedio territorial de materia orgánica del último análisis de cada terreno.",
                "COROPLETICO",
                true,
                false,
                60),

            CrearCapa(
                "acidez-total",
                "Acidez total",
                "SUELOS",
                "fa-solid fa-vial-circle-check",
                "Promedio territorial de acidez del último análisis de cada terreno.",
                "COROPLETICO",
                true,
                false,
                70),

            CrearCapa(
                "cice",
                "CICE",
                "SUELOS",
                "fa-solid fa-arrows-rotate",
                "Promedio territorial de la capacidad de intercambio catiónico efectiva.",
                "COROPLETICO",
                true,
                false,
                80),

            CrearCapa(
                "saturacion-bases",
                "Saturación de bases",
                "SUELOS",
                "fa-solid fa-chart-pie",
                "Promedio territorial de saturación de bases del último cálculo disponible.",
                "COROPLETICO",
                true,
                false,
                90)
        };

        var elementos = await db.elementoQuimico
            .AsNoTracking()
            .Where(item => item.activo)
            .OrderBy(item => item.elementoQuimicosId)
            .Select(item => new
            {
                item.elementoQuimicosId,
                item.simboloElementoQuimico,
                item.nombreElementoQuimico
            })
            .ToListAsync(cancellationToken);

        int ordenNutriente = 100;

        foreach (var elemento in elementos)
        {
            capas.Add(CrearCapa(
                $"nutriente-{elemento.elementoQuimicosId}",
                $"{elemento.nombreElementoQuimico} ({elemento.simboloElementoQuimico})",
                "SUELOS",
                "fa-solid fa-atom",
                "Disponibilidad promedio de los terrenos analizados en la zona seleccionada.",
                "COROPLETICO",
                true,
                false,
                ordenNutriente));

            ordenNutriente += 10;
        }

        int ordenClima = Math.Max(300, ordenNutriente + 20);

        capas.AddRange(
        [
            CrearCapa(
                "temperatura",
                "Temperatura",
                "CLIMA",
                "fa-solid fa-temperature-half",
                "Mapa térmico de temperatura actual estimada.",
                "MAPA_CALOR",
                true,
                false,
                ordenClima),

            CrearCapa(
                "humedad",
                "Humedad relativa",
                "CLIMA",
                "fa-solid fa-droplet",
                "Mapa de calor de humedad relativa actual.",
                "MAPA_CALOR",
                true,
                false,
                ordenClima + 10),

            CrearCapa(
                "lluvia",
                "Precipitación",
                "CLIMA",
                "fa-solid fa-cloud-rain",
                "Precipitación actual estimada en milímetros.",
                "MAPA_CALOR",
                true,
                false,
                ordenClima + 20),

            CrearCapa(
                "viento",
                "Velocidad del viento",
                "CLIMA",
                "fa-solid fa-wind",
                "Mapa de calor de velocidad del viento.",
                "MAPA_CALOR",
                true,
                false,
                ordenClima + 30),

            CrearCapa(
                "diagnostico-ia",
                "Diagnóstico por IA",
                "IA",
                "fa-solid fa-microscope",
                "Incidencia geográfica de diagnósticos fitosanitarios.",
                "MARCADORES",
                false,
                false,
                ordenClima + 40,
                "Se habilitará al conectar el historial de diagnósticos."),

            CrearCapa(
                "produccion",
                "Producción",
                "PRODUCCION",
                "fa-solid fa-chart-column",
                "Distribución y concentración de producción cafetalera.",
                "MAPA_CALOR",
                false,
                false,
                ordenClima + 50,
                "Preparada para la fase de analítica productiva.")
        ]);

        return Ok(new CentroGeoespacialCapasRespuestaDto
        {
            ActualizadoUtc = DateTime.UtcNow,
            Capas = capas
        });
    }

    [HttpGet("clima")]
    public async Task<ActionResult<ClimaMapaRespuestaDto>> ObtenerClima(
        [FromQuery] bool forzarActualizacion = false,
        CancellationToken cancellationToken = default)
    {
        ClimaMapaRespuestaDto respuesta =
            await climaService.ObtenerAsync(
                forzarActualizacion,
                cancellationToken);

        return Ok(respuesta);
    }

    [HttpGet("suelos/{clave}")]
    public async Task<ActionResult<CapaSueloMapaRespuestaDto>>
        ObtenerCapaSuelo(
            string clave,
            [FromQuery] int? departamentoId = null,
            [FromQuery] int? municipioId = null,
            CancellationToken cancellationToken = default)
    {
        CapaSueloMapaRespuestaDto respuesta =
            await capasSueloService.ObtenerAsync(
                clave,
                departamentoId,
                municipioId,
                cancellationToken);

        return respuesta.Disponible
            ? Ok(respuesta)
            : NotFound(respuesta);
    }

    [HttpGet("terrenos/{terrenoId:int}/historial")]
    public async Task<ActionResult<HistorialTerrenoMapaDto>>
        ObtenerHistorialTerreno(
            int terrenoId,
            [FromQuery] int limite = 20,
            CancellationToken cancellationToken = default)
    {
        limite = Math.Clamp(limite, 1, 100);

        var terreno = await db.Terreno
            .AsNoTracking()
            .Where(item => item.activo && item.terrenoId == terrenoId)
            .Select(item => new
            {
                item.terrenoId,
                item.codigoTerreno,
                item.direccionTerreno,
                NombrePropietario =
                    item.RelacionesPropietario
                        .Where(relacion =>
                            relacion.activo &&
                            relacion.Propietario.activo)
                        .Select(relacion =>
                            relacion.Propietario.nombreCompleto)
                        .FirstOrDefault() ??
                    string.Empty,
                item.extensionManzanaTerreno,
                item.cantidadQuintalesOro,
                Municipio = item.Municipio.NombreMunicipio,
                Departamento = item.Municipio.Departamento.NombreDepartamento
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

        var registros = await (
            from calculo in db.AnalisisSueloCalculos.AsNoTracking()
            join analisis in db.AnalisisSuelos.AsNoTracking()
                on calculo.analisisSueloId equals analisis.analisisSueloId
            where calculo.activo &&
                  analisis.activo &&
                  calculo.terrenoId == terrenoId
            orderby calculo.fechaCalculo descending,
                    calculo.analisisSueloCalculoId descending
            select new
            {
                calculo.analisisSueloCalculoId,
                calculo.analisisSueloId,
                analisis.identificadorAnalisisSuelo,
                analisis.fechaAnalisisSuelo,
                analisis.fechaCreacionAnalisisSuelo,
                calculo.phAnalisisSuelo,
                calculo.materiaOrganica,
                calculo.acidezTotal,
                calculo.recomendacionGeneral,
                calculo.observacion
            })
            .Take(limite)
            .ToListAsync(cancellationToken);

        List<int> calculoIds = registros
            .Select(item => item.analisisSueloCalculoId)
            .ToList();

        var enmiendas = await db.enmiendaCalcarea
            .AsNoTracking()
            .Where(item =>
                item.activo &&
                item.analisisSueloCalculoId.HasValue &&
                calculoIds.Contains(item.analisisSueloCalculoId.Value))
            .OrderByDescending(item => item.fechaCreacion)
            .ThenByDescending(item => item.enmiendaCalcareaId)
            .Select(item => new
            {
                CalculoId = item.analisisSueloCalculoId!.Value,
                item.cice,
                item.saturacionActual
            })
            .ToListAsync(cancellationToken);

        Dictionary<int, (decimal Cice, decimal Saturacion)> enmiendaPorCalculo =
            enmiendas
                .GroupBy(item => item.CalculoId)
                .ToDictionary(
                    grupo => grupo.Key,
                    grupo =>
                    {
                        var item = grupo.First();
                        return (item.cice, item.saturacionActual);
                    });

        var elementos = await db.AnalisisSueloCalculoElementos
            .AsNoTracking()
            .Where(item =>
                item.activo &&
                calculoIds.Contains(item.analisisSueloCalculoId))
            .OrderBy(item => item.ElementoQuimico.nombreElementoQuimico)
            .Select(item => new
            {
                item.analisisSueloCalculoId,
                item.elementoQuimicosId,
                Simbolo = item.ElementoQuimico.simboloElementoQuimico,
                Nombre = item.ElementoQuimico.nombreElementoQuimico,
                Valor = item.cantidadConvertidaLbMz ?? item.cantidadIngresada,
                Unidad = item.cantidadConvertidaLbMz.HasValue
                    ? "lb/Mz"
                    : item.UnidadMedida == null
                        ? string.Empty
                        : item.UnidadMedida.nombreUnidadMedida,
                Clasificacion = item.clasificacion ?? string.Empty
            })
            .ToListAsync(cancellationToken);

        Dictionary<int, List<ElementoAnalisisTerrenoMapaDto>>
            elementosPorCalculo = elementos
                .GroupBy(item => item.analisisSueloCalculoId)
                .ToDictionary(
                    grupo => grupo.Key,
                    grupo => grupo.Select(item =>
                        new ElementoAnalisisTerrenoMapaDto
                        {
                            ElementoQuimicosId = item.elementoQuimicosId,
                            Simbolo = item.Simbolo,
                            Nombre = item.Nombre,
                            Valor = Math.Round(item.Valor, 4),
                            Unidad = item.Unidad,
                            Clasificacion = item.Clasificacion
                        }).ToList());

        UmbralesAlertas umbrales =
            await umbralesService.ObtenerAsync(cancellationToken);

        var respuesta = new HistorialTerrenoMapaDto
        {
            TerrenoId = terreno.terrenoId,
            Codigo = terreno.codigoTerreno,
            Nombre = terreno.direccionTerreno,
            Productor = terreno.NombrePropietario,
            Departamento = terreno.Departamento,
            Municipio = terreno.Municipio,
            ExtensionManzanas = terreno.extensionManzanaTerreno,
            ProduccionQuintalesOro = terreno.cantidadQuintalesOro,
            Analisis = registros.Select(item =>
            {
                string nivel = CalcularNivel(
                    item.phAnalisisSuelo,
                    item.materiaOrganica,
                    item.acidezTotal,
                    umbrales);

                enmiendaPorCalculo.TryGetValue(
                    item.analisisSueloCalculoId,
                    out (decimal Cice, decimal Saturacion) enmienda);

                elementosPorCalculo.TryGetValue(
                    item.analisisSueloCalculoId,
                    out List<ElementoAnalisisTerrenoMapaDto>? listaElementos);

                return new AnalisisTerrenoMapaDto
                {
                    AnalisisSueloCalculoId = item.analisisSueloCalculoId,
                    AnalisisSueloId = item.analisisSueloId,
                    Identificador = item.identificadorAnalisisSuelo,
                    FechaLaboratorio = item.fechaAnalisisSuelo,
                    FechaRegistro = item.fechaCreacionAnalisisSuelo,
                    Ph = item.phAnalisisSuelo,
                    MateriaOrganica = item.materiaOrganica,
                    AcidezTotal = item.acidezTotal,
                    Cice = enmiendaPorCalculo.ContainsKey(
                        item.analisisSueloCalculoId)
                            ? enmienda.Cice
                            : null,
                    SaturacionBases = enmiendaPorCalculo.ContainsKey(
                        item.analisisSueloCalculoId)
                            ? enmienda.Saturacion
                            : null,
                    Nivel = nivel,
                    Estado = EstadoTexto(nivel),
                    RecomendacionGeneral =
                        item.recomendacionGeneral ?? string.Empty,
                    Observacion = item.observacion ?? string.Empty,
                    Elementos = listaElementos ?? []
                };
            }).ToList()
        };

        return Ok(respuesta);
    }

    private static CapaMapaDto CrearCapa(
        string clave,
        string nombre,
        string categoria,
        string icono,
        string descripcion,
        string tipoVisualizacion,
        bool disponible,
        bool activaPorDefecto,
        int orden,
        string? mensaje = null) =>
        new()
        {
            Clave = clave,
            Nombre = nombre,
            Categoria = categoria,
            Icono = icono,
            Descripcion = descripcion,
            TipoVisualizacion = tipoVisualizacion,
            Disponible = disponible,
            ActivaPorDefecto = activaPorDefecto,
            Orden = orden,
            Mensaje = mensaje
        };

    private static string CalcularNivel(
        decimal ph,
        decimal? materiaOrganica,
        decimal? acidezTotal,
        UmbralesAlertas umbrales)
    {
        bool critico =
            ph < umbrales.PhBajoCriticoMaximo ||
            ph >= umbrales.PhAltoCriticoMinimo ||
            (acidezTotal.HasValue &&
             acidezTotal.Value > umbrales.AcidezAltaMinima);

        if (critico)
            return "CRITICA";

        bool atencion =
            ph < umbrales.PhBajoAtencionMaximo ||
            ph >= umbrales.PhAltoAtencionMinimo ||
            (materiaOrganica.HasValue &&
             materiaOrganica.Value < umbrales.MateriaOrganicaBajaMaxima);

        return atencion ? "ATENCION" : "NORMAL";
    }

    private static string EstadoTexto(string nivel) =>
        nivel switch
        {
            "CRITICA" => "Estado crítico",
            "ATENCION" => "Requiere atención",
            "NORMAL" => "Estado normal",
            _ => "Sin análisis"
        };
}
