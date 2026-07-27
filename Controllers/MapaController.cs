using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.MapaWebDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/mapa")]
    public sealed class MapaController : ControllerBase
    {
        private const decimal PhCriticoMaximo = 5.50m;
        private const decimal PhAtencionMaximo = 6.00m;
        private const decimal MateriaOrganicaBajaMaxima = 3.00m;
        private const decimal AcidezAltaMinima = 1.00m;

        private readonly DBContext db;

        public MapaController(DBContext db)
        {
            this.db = db;
        }

        [HttpGet("terrenos")]
        public async Task<ActionResult<List<TerrenoMapaDto>>> ListarTerrenos(
            int? departamentoId = null,
            int? municipioId = null,
            CancellationToken cancellationToken = default)
        {
            MapaInteligenteRespuestaDto respuesta =
                await ObtenerMapaInternoAsync(
                    departamentoId,
                    municipioId,
                    null,
                    null,
                    null,
                    cancellationToken);

            return Ok(respuesta.terrenos);
        }

        [HttpGet("inteligente")]
        public async Task<ActionResult<MapaInteligenteRespuestaDto>>
            ObtenerMapaInteligente(
                [FromQuery] int? departamentoId = null,
                [FromQuery] int? municipioId = null,
                [FromQuery] string? nivel = null,
                [FromQuery] string? indicador = null,
                [FromQuery] string? buscar = null,
                CancellationToken cancellationToken = default)
        {
            MapaInteligenteRespuestaDto respuesta =
                await ObtenerMapaInternoAsync(
                    departamentoId,
                    municipioId,
                    nivel,
                    indicador,
                    buscar,
                    cancellationToken);

            return Ok(respuesta);
        }

        [HttpGet("~/api/alertas-agricolas")]
        public async Task<ActionResult<AlertasAgricolasPaginadaDto>>
            ListarAlertas(
                [FromQuery] int? departamentoId = null,
                [FromQuery] int? municipioId = null,
                [FromQuery] string? nivel = null,
                [FromQuery] string? tipo = null,
                [FromQuery] string? buscar = null,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 20,
                CancellationToken cancellationToken = default)
        {
            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 10, 100);

            MapaInteligenteRespuestaDto mapa =
                await ObtenerMapaInternoAsync(
                    departamentoId,
                    municipioId,
                    null,
                    null,
                    buscar,
                    cancellationToken);

            List<AlertaAgricolaDto> alertas =
                ConstruirAlertas(mapa.terrenos);

            if (!string.IsNullOrWhiteSpace(nivel))
            {
                string nivelNormalizado =
                    nivel.Trim().ToUpperInvariant();

                alertas = alertas
                    .Where(item =>
                        item.nivel == nivelNormalizado)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(tipo))
            {
                string tipoNormalizado =
                    tipo.Trim();

                alertas = alertas
                    .Where(item =>
                        item.tipo.Contains(
                            tipoNormalizado,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            int total = alertas.Count;

            var respuesta = new AlertasAgricolasPaginadaDto
            {
                items = alertas
                    .OrderBy(item =>
                        OrdenNivel(item.nivel))
                    .ThenByDescending(item =>
                        item.fechaAnalisis)
                    .Skip((pagina - 1) * tamanoPagina)
                    .Take(tamanoPagina)
                    .ToList(),
                pagina = pagina,
                tamanoPagina = tamanoPagina,
                totalRegistros = total,
                totalPaginas = total == 0
                    ? 1
                    : (int)Math.Ceiling(
                        total / (double)tamanoPagina),
                criticas = alertas.Count(item =>
                    item.nivel == "CRITICA"),
                atencion = alertas.Count(item =>
                    item.nivel == "ATENCION"),
                sinAnalisis = alertas.Count(item =>
                    item.nivel == "SIN_ANALISIS")
            };

            return Ok(respuesta);
        }

        private async Task<MapaInteligenteRespuestaDto>
            ObtenerMapaInternoAsync(
                int? departamentoId,
                int? municipioId,
                string? nivel,
                string? indicador,
                string? buscar,
                CancellationToken cancellationToken)
        {
            IQueryable<Terreno> query = db.Terreno
                .AsNoTracking()
                .Where(item =>
                    item.activo &&
                    item.latitud >= -90 &&
                    item.latitud <= 90 &&
                    item.longitud >= -180 &&
                    item.longitud <= 180 &&
                    !(item.latitud == 0 &&
                      item.longitud == 0));

            if (departamentoId is > 0)
            {
                query = query.Where(item =>
                    item.Municipio.DepartamentoId ==
                    departamentoId.Value);
            }

            if (municipioId is > 0)
            {
                query = query.Where(item =>
                    item.municipioId ==
                    municipioId.Value);
            }

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();

                query = query.Where(item =>
                    item.codigoTerreno.Contains(texto) ||
                    item.nombrePropietarioTerreno.Contains(texto) ||
                    item.direccionTerreno.Contains(texto));
            }

            var terrenosBase = await query
                .OrderBy(item =>
                    item.codigoTerreno)
                .Select(item => new
                {
                    item.terrenoId,
                    item.codigoTerreno,
                    item.direccionTerreno,
                    item.nombrePropietarioTerreno,
                    item.latitud,
                    item.longitud,
                    item.extensionManzanaTerreno,
                    item.cantidadQuintalesOro,
                    item.municipioId,
                    Municipio =
                        item.Municipio.NombreMunicipio,
                    DepartamentoId =
                        item.Municipio.DepartamentoId,
                    Departamento =
                        item.Municipio.Departamento
                            .NombreDepartamento
                })
                .ToListAsync(cancellationToken);

            List<int> terrenoIds = terrenosBase
                .Select(item => item.terrenoId)
                .ToList();

            List<AnalisisSueloCalculo> calculos =
                terrenoIds.Count == 0
                    ? new List<AnalisisSueloCalculo>()
                    : await db.AnalisisSueloCalculos
                        .AsNoTracking()
                        .Where(item =>
                            item.activo &&
                            terrenoIds.Contains(
                                item.terrenoId))
                        .OrderByDescending(item =>
                            item.fechaCalculo)
                        .ThenByDescending(item =>
                            item.analisisSueloCalculoId)
                        .ToListAsync(cancellationToken);

            Dictionary<int, AnalisisSueloCalculo> ultimoPorTerreno =
                calculos
                    .GroupBy(item => item.terrenoId)
                    .ToDictionary(
                        grupo => grupo.Key,
                        grupo => grupo.First());

            var terrenos = new List<TerrenoMapaDto>();

            foreach (var terreno in terrenosBase)
            {
                ultimoPorTerreno.TryGetValue(
                    terreno.terrenoId,
                    out AnalisisSueloCalculo? calculo);

                List<string> alertas =
                    ConstruirMensajes(calculo);

                string nivelCalculado =
                    CalcularNivel(calculo);

                var item = new TerrenoMapaDto
                {
                    terrenoId = terreno.terrenoId,
                    codigo = terreno.codigoTerreno,
                    nombre = terreno.direccionTerreno,
                    productor =
                        terreno.nombrePropietarioTerreno,
                    latitud = terreno.latitud,
                    longitud = terreno.longitud,
                    departamentoId =
                        terreno.DepartamentoId,
                    departamento =
                        terreno.Departamento,
                    municipioId =
                        terreno.municipioId,
                    municipio =
                        terreno.Municipio,
                    extensionManzanas =
                        terreno.extensionManzanaTerreno,
                    produccionQuintalesOro =
                        terreno.cantidadQuintalesOro,
                    estado = EstadoTexto(nivelCalculado),
                    nivelAlerta = nivelCalculado,
                    ultimoPh = calculo?.phAnalisisSuelo,
                    materiaOrganica =
                        calculo?.materiaOrganica,
                    acidezTotal =
                        calculo?.acidezTotal,
                    fechaUltimoAnalisis =
                        calculo?.fechaCalculo,
                    alertas = alertas,
                    googleMapsUrl =
                        ConstruirGoogleMapsUrl(
                            terreno.latitud,
                            terreno.longitud)
                };

                terrenos.Add(item);
            }

            if (!string.IsNullOrWhiteSpace(nivel))
            {
                string nivelNormalizado =
                    nivel.Trim().ToUpperInvariant();

                terrenos = terrenos
                    .Where(item =>
                        item.nivelAlerta ==
                        nivelNormalizado)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(indicador))
            {
                string indicadorNormalizado =
                    indicador.Trim().ToLowerInvariant();

                terrenos = terrenos
                    .Where(item =>
                        indicadorNormalizado switch
                        {
                            "ph" =>
                                item.ultimoPh.HasValue &&
                                item.ultimoPh.Value <
                                    PhAtencionMaximo,

                            "materia-organica" =>
                                item.materiaOrganica.HasValue &&
                                item.materiaOrganica.Value <
                                    MateriaOrganicaBajaMaxima,

                            "acidez" =>
                                item.acidezTotal.HasValue &&
                                item.acidezTotal.Value >
                                    AcidezAltaMinima,

                            "sin-analisis" =>
                                !item.fechaUltimoAnalisis.HasValue,

                            _ => true
                        })
                    .ToList();
            }

            return new MapaInteligenteRespuestaDto
            {
                terrenos = terrenos,
                resumen = new MapaResumenDto
                {
                    totalTerrenos = terrenos.Count,
                    conAnalisis = terrenos.Count(item =>
                        item.fechaUltimoAnalisis.HasValue),
                    sinAnalisis = terrenos.Count(item =>
                        item.nivelAlerta == "SIN_ANALISIS"),
                    criticos = terrenos.Count(item =>
                        item.nivelAlerta == "CRITICA"),
                    atencion = terrenos.Count(item =>
                        item.nivelAlerta == "ATENCION"),
                    normales = terrenos.Count(item =>
                        item.nivelAlerta == "NORMAL"),
                    extensionVisibleManzanas =
                        decimal.Round(
                            terrenos.Sum(item =>
                                item.extensionManzanas),
                            2)
                }
            };
        }

        private static List<AlertaAgricolaDto>
            ConstruirAlertas(
                IEnumerable<TerrenoMapaDto> terrenos)
        {
            var resultado =
                new List<AlertaAgricolaDto>();

            foreach (TerrenoMapaDto terreno in terrenos)
            {
                if (!terreno.fechaUltimoAnalisis.HasValue)
                {
                    resultado.Add(CrearAlerta(
                        terreno,
                        "SIN_ANALISIS",
                        "Sin análisis",
                        "El terreno no posee un análisis de suelo activo.",
                        null,
                        string.Empty));

                    continue;
                }

                if (terreno.ultimoPh.HasValue &&
                    terreno.ultimoPh.Value <
                    PhCriticoMaximo)
                {
                    resultado.Add(CrearAlerta(
                        terreno,
                        "CRITICA",
                        "pH crítico",
                        "El pH requiere atención inmediata.",
                        terreno.ultimoPh,
                        "pH"));
                }
                else if (terreno.ultimoPh.HasValue &&
                         terreno.ultimoPh.Value <
                         PhAtencionMaximo)
                {
                    resultado.Add(CrearAlerta(
                        terreno,
                        "ATENCION",
                        "pH bajo",
                        "El pH está por debajo del rango operativo.",
                        terreno.ultimoPh,
                        "pH"));
                }

                if (terreno.materiaOrganica.HasValue &&
                    terreno.materiaOrganica.Value <
                    MateriaOrganicaBajaMaxima)
                {
                    resultado.Add(CrearAlerta(
                        terreno,
                        "ATENCION",
                        "Materia orgánica baja",
                        "La materia orgánica requiere seguimiento.",
                        terreno.materiaOrganica,
                        "%"));
                }

                if (terreno.acidezTotal.HasValue &&
                    terreno.acidezTotal.Value >
                    AcidezAltaMinima)
                {
                    resultado.Add(CrearAlerta(
                        terreno,
                        "CRITICA",
                        "Acidez alta",
                        "La acidez total supera el umbral operativo.",
                        terreno.acidezTotal,
                        "meq/100g"));
                }
            }

            return resultado;
        }

        private static AlertaAgricolaDto CrearAlerta(
            TerrenoMapaDto terreno,
            string nivel,
            string tipo,
            string mensaje,
            decimal? valor,
            string unidad) =>
            new()
            {
                terrenoId = terreno.terrenoId,
                codigoTerreno = terreno.codigo,
                propietario = terreno.productor,
                departamentoId =
                    terreno.departamentoId,
                departamento = terreno.departamento,
                municipioId = terreno.municipioId,
                municipio = terreno.municipio,
                nivel = nivel,
                tipo = tipo,
                mensaje = mensaje,
                valor = valor,
                unidad = unidad,
                fechaAnalisis =
                    terreno.fechaUltimoAnalisis,
                latitud = terreno.latitud,
                longitud = terreno.longitud,
                googleMapsUrl =
                    terreno.googleMapsUrl
            };

        private static List<string> ConstruirMensajes(
            AnalisisSueloCalculo? calculo)
        {
            if (calculo == null)
                return ["Sin análisis de suelo"];

            var alertas = new List<string>();

            if (calculo.phAnalisisSuelo <
                PhCriticoMaximo)
            {
                alertas.Add("pH crítico");
            }
            else if (calculo.phAnalisisSuelo <
                     PhAtencionMaximo)
            {
                alertas.Add("pH bajo");
            }

            if (calculo.materiaOrganica.HasValue &&
                calculo.materiaOrganica.Value <
                MateriaOrganicaBajaMaxima)
            {
                alertas.Add("Materia orgánica baja");
            }

            if (calculo.acidezTotal.HasValue &&
                calculo.acidezTotal.Value >
                AcidezAltaMinima)
            {
                alertas.Add("Acidez alta");
            }

            return alertas;
        }

        private static string CalcularNivel(
            AnalisisSueloCalculo? calculo)
        {
            if (calculo == null)
                return "SIN_ANALISIS";

            if (calculo.phAnalisisSuelo <
                    PhCriticoMaximo ||
                (calculo.acidezTotal.HasValue &&
                 calculo.acidezTotal.Value >
                    AcidezAltaMinima))
            {
                return "CRITICA";
            }

            if (calculo.phAnalisisSuelo <
                    PhAtencionMaximo ||
                (calculo.materiaOrganica.HasValue &&
                 calculo.materiaOrganica.Value <
                    MateriaOrganicaBajaMaxima))
            {
                return "ATENCION";
            }

            return "NORMAL";
        }

        private static string EstadoTexto(
            string nivel) =>
            nivel switch
            {
                "CRITICA" => "Estado crítico",
                "ATENCION" => "Requiere atención",
                "NORMAL" => "Estado normal",
                _ => "Sin análisis"
            };

        private static int OrdenNivel(
            string nivel) =>
            nivel switch
            {
                "CRITICA" => 0,
                "ATENCION" => 1,
                "SIN_ANALISIS" => 2,
                _ => 3
            };

        private static string ConstruirGoogleMapsUrl(
            decimal latitud,
            decimal longitud) =>
            "https://www.google.com/maps/dir/?api=1" +
            $"&destination={latitud.ToString(
                System.Globalization.CultureInfo.InvariantCulture)}" +
            "%2C" +
            longitud.ToString(
                System.Globalization.CultureInfo.InvariantCulture) +
            "&travelmode=driving";
    }
}
