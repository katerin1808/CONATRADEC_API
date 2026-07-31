using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.CentroGeoespacialDto;

namespace CONATRADEC_API.Services;

/// <summary>
/// Construye capas coropléticas de suelo bajo demanda.
/// Cada terreno aporta únicamente su análisis más reciente y los resultados
/// se promedian por departamento o municipio, según el nivel seleccionado.
/// </summary>
public sealed class CapasSueloMapaService
{
    private const string ColorCritico = "#dc2626";
    private const string ColorAtencion = "#f59e0b";
    private const string ColorAdecuado = "#2f855a";
    private const string ColorAlto = "#2563eb";
    private const string ColorSinClasificar = "#64748b";

    private readonly DBContext db;
    private readonly UmbralesAlertasService umbralesService;

    public CapasSueloMapaService(
        DBContext db,
        UmbralesAlertasService umbralesService)
    {
        this.db = db;
        this.umbralesService = umbralesService;
    }

    public async Task<CapaSueloMapaRespuestaDto> ObtenerAsync(
        string clave,
        int? departamentoId,
        int? municipioId,
        CancellationToken cancellationToken = default)
    {
        string claveNormalizada = NormalizarClave(clave);
        DefinicionCapaSuelo? definicion =
            await ResolverDefinicionAsync(
                claveNormalizada,
                cancellationToken);

        if (definicion is null)
        {
            return new CapaSueloMapaRespuestaDto
            {
                Disponible = false,
                Clave = claveNormalizada,
                Mensaje = "La capa de suelo solicitada no está disponible."
            };
        }

        List<CalculoTerrenoBase> ultimos =
            await ObtenerUltimosCalculosAsync(
                departamentoId,
                municipioId,
                cancellationToken);

        bool agruparPorMunicipio =
            departamentoId is > 0 || municipioId is > 0;

        var respuesta = new CapaSueloMapaRespuestaDto
        {
            Disponible = true,
            Clave = definicion.Clave,
            Nombre = definicion.Nombre,
            Descripcion = definicion.Descripcion,
            Unidad = definicion.Unidad,
            NivelAgrupacion = agruparPorMunicipio
                ? "MUNICIPIO"
                : "DEPARTAMENTO",
            ActualizadoUtc = DateTime.UtcNow
        };

        if (ultimos.Count == 0)
        {
            respuesta.Mensaje =
                "No existen análisis de suelo para los filtros seleccionados.";
            respuesta.Leyenda = CrearLeyendaVacia(definicion);
            return respuesta;
        }

        List<PuntoSueloMapaDto> valoresTerreno = definicion.Tipo switch
        {
            TipoIndicadorSuelo.Ph =>
                await CrearPuntosPhAsync(
                    ultimos,
                    cancellationToken),

            TipoIndicadorSuelo.MateriaOrganica =>
                await CrearPuntosMateriaOrganicaAsync(
                    ultimos,
                    cancellationToken),

            TipoIndicadorSuelo.AcidezTotal =>
                await CrearPuntosAcidezAsync(
                    ultimos,
                    cancellationToken),

            TipoIndicadorSuelo.Cice =>
                await CrearPuntosEnmiendaAsync(
                    ultimos,
                    usarSaturacion: false,
                    cancellationToken),

            TipoIndicadorSuelo.SaturacionBases =>
                await CrearPuntosEnmiendaAsync(
                    ultimos,
                    usarSaturacion: true,
                    cancellationToken),

            TipoIndicadorSuelo.Nutriente when
                definicion.ElementoQuimicoId.HasValue =>
                await CrearPuntosNutrienteAsync(
                    ultimos,
                    definicion.ElementoQuimicoId.Value,
                    cancellationToken),

            _ => []
        };

        if (valoresTerreno.Count == 0)
        {
            respuesta.Mensaje =
                "No se encontraron valores para esta capa en los terrenos filtrados.";
            respuesta.Leyenda = CrearLeyendaVacia(definicion);
            return respuesta;
        }

        List<ResumenTerritorialSueloMapaDto> regiones =
            await CrearResumenesTerritorialesAsync(
                definicion,
                valoresTerreno,
                agruparPorMunicipio,
                cancellationToken);

        respuesta.Regiones = regiones;
        respuesta.TotalRegiones = regiones.Count;
        respuesta.TotalTerrenosAnalizados = valoresTerreno
            .Select(item => item.TerrenoId)
            .Distinct()
            .Count();
        respuesta.TotalPuntos = respuesta.TotalTerrenosAnalizados;
        respuesta.Puntos = [];
        respuesta.Minimo = regiones.Count == 0
            ? null
            : regiones.Min(item => item.Promedio);
        respuesta.Maximo = regiones.Count == 0
            ? null
            : regiones.Max(item => item.Promedio);
        respuesta.Unidad = ResolverUnidad(definicion, respuesta.Unidad);
        respuesta.Leyenda = CrearLeyendaTerritorial(definicion, regiones);

        if (regiones.Count == 0)
        {
            respuesta.Mensaje =
                "No fue posible construir promedios territoriales para la selección actual.";
        }

        return respuesta;
    }

    private async Task<List<CalculoTerrenoBase>>
        ObtenerUltimosCalculosAsync(
            int? departamentoId,
            int? municipioId,
            CancellationToken cancellationToken)
    {
        IQueryable<AnalisisSueloCalculo> query =
            db.AnalisisSueloCalculos
                .AsNoTracking()
                .Where(item =>
                    item.activo &&
                    item.terrenoId > 0);

        if (departamentoId is > 0)
        {
            query = query.Where(item =>
                db.Terreno.Any(terreno =>
                    terreno.activo &&
                    terreno.terrenoId == item.terrenoId &&
                    terreno.Municipio.DepartamentoId ==
                    departamentoId.Value));
        }

        if (municipioId is > 0)
        {
            query = query.Where(item =>
                db.Terreno.Any(terreno =>
                    terreno.activo &&
                    terreno.terrenoId == item.terrenoId &&
                    terreno.municipioId == municipioId.Value));
        }

        List<CalculoTerrenoBase> registros = await (
            from calculo in query
            join terreno in db.Terreno.AsNoTracking()
                on calculo.terrenoId equals terreno.terrenoId
            where terreno.activo
            orderby calculo.fechaCalculo descending,
                    calculo.analisisSueloCalculoId descending
            select new CalculoTerrenoBase
            {
                AnalisisSueloCalculoId =
                    calculo.analisisSueloCalculoId,
                TerrenoId = terreno.terrenoId,
                Codigo = terreno.codigoTerreno,
                Productor =
                    terreno.RelacionesPropietario
                        .Where(relacion =>
                            relacion.activo &&
                            relacion.Propietario.activo)
                        .Select(relacion =>
                            relacion.Propietario.nombreCompleto)
                        .FirstOrDefault() ??
                    string.Empty,
                DepartamentoId =
                    terreno.Municipio.DepartamentoId,
                Departamento =
                    terreno.Municipio.Departamento.NombreDepartamento,
                MunicipioId = terreno.municipioId,
                Municipio = terreno.Municipio.NombreMunicipio,
                Latitud = terreno.latitud,
                Longitud = terreno.longitud,
                FechaAnalisis = calculo.fechaCalculo,
                Ph = calculo.phAnalisisSuelo,
                MateriaOrganica = calculo.materiaOrganica,
                AcidezTotal = calculo.acidezTotal
            })
            .ToListAsync(cancellationToken);

        return registros
            .GroupBy(item => item.TerrenoId)
            .Select(grupo => grupo.First())
            .ToList();
    }

    private async Task<List<PuntoSueloMapaDto>> CrearPuntosPhAsync(
        IReadOnlyCollection<CalculoTerrenoBase> ultimos,
        CancellationToken cancellationToken)
    {
        UmbralesAlertas umbrales =
            await umbralesService.ObtenerAsync(cancellationToken);

        return ultimos.Select(item =>
        {
            (string clasificacion, string color) =
                ClasificarPh(item.Ph, umbrales);

            return CrearPunto(
                item,
                item.Ph,
                clasificacion,
                color);
        }).ToList();
    }

    private async Task<List<PuntoSueloMapaDto>>
        CrearPuntosMateriaOrganicaAsync(
            IReadOnlyCollection<CalculoTerrenoBase> ultimos,
            CancellationToken cancellationToken)
    {
        UmbralesAlertas umbrales =
            await umbralesService.ObtenerAsync(cancellationToken);

        return ultimos
            .Where(item => item.MateriaOrganica.HasValue)
            .Select(item =>
            {
                decimal valor = item.MateriaOrganica!.Value;
                bool baja = valor < umbrales.MateriaOrganicaBajaMaxima;

                return CrearPunto(
                    item,
                    valor,
                    baja ? "BAJA" : "ADECUADA",
                    baja ? ColorAtencion : ColorAdecuado);
            })
            .ToList();
    }

    private async Task<List<PuntoSueloMapaDto>> CrearPuntosAcidezAsync(
        IReadOnlyCollection<CalculoTerrenoBase> ultimos,
        CancellationToken cancellationToken)
    {
        UmbralesAlertas umbrales =
            await umbralesService.ObtenerAsync(cancellationToken);

        return ultimos
            .Where(item => item.AcidezTotal.HasValue)
            .Select(item =>
            {
                decimal valor = item.AcidezTotal!.Value;
                bool alta = valor > umbrales.AcidezAltaMinima;

                return CrearPunto(
                    item,
                    valor,
                    alta ? "ALTA" : "ACEPTABLE",
                    alta ? ColorCritico : ColorAdecuado);
            })
            .ToList();
    }

    private async Task<List<PuntoSueloMapaDto>> CrearPuntosEnmiendaAsync(
        IReadOnlyCollection<CalculoTerrenoBase> ultimos,
        bool usarSaturacion,
        CancellationToken cancellationToken)
    {
        List<int> calculoIds = ultimos
            .Select(item => item.AnalisisSueloCalculoId)
            .ToList();

        var valores = await db.enmiendaCalcarea
            .AsNoTracking()
            .Where(item =>
                item.activo &&
                item.analisisSueloCalculoId.HasValue &&
                calculoIds.Contains(
                    item.analisisSueloCalculoId.Value))
            .OrderByDescending(item => item.fechaCreacion)
            .ThenByDescending(item => item.enmiendaCalcareaId)
            .Select(item => new
            {
                CalculoId = item.analisisSueloCalculoId!.Value,
                item.cice,
                item.saturacionActual
            })
            .ToListAsync(cancellationToken);

        Dictionary<int, (decimal Cice, decimal Saturacion)> porCalculo =
            valores
                .GroupBy(item => item.CalculoId)
                .ToDictionary(
                    grupo => grupo.Key,
                    grupo =>
                    {
                        var item = grupo.First();
                        return (item.cice, item.saturacionActual);
                    });

        var puntos = new List<PuntoSueloMapaDto>();

        foreach (CalculoTerrenoBase item in ultimos)
        {
            if (!porCalculo.TryGetValue(
                    item.AnalisisSueloCalculoId,
                    out (decimal Cice, decimal Saturacion) valor))
            {
                continue;
            }

            decimal dato = usarSaturacion
                ? valor.Saturacion
                : valor.Cice;

            puntos.Add(CrearPunto(
                item,
                dato,
                "DISTRIBUCIÓN RELATIVA",
                ColorSinClasificar));
        }

        return puntos;
    }

    private async Task<List<PuntoSueloMapaDto>>
        CrearPuntosNutrienteAsync(
            IReadOnlyCollection<CalculoTerrenoBase> ultimos,
            int elementoQuimicoId,
            CancellationToken cancellationToken)
    {
        List<int> calculoIds = ultimos
            .Select(item => item.AnalisisSueloCalculoId)
            .ToList();

        Dictionary<int, CalculoTerrenoBase> basePorCalculo =
            ultimos.ToDictionary(
                item => item.AnalisisSueloCalculoId);

        var valores = await db.AnalisisSueloCalculoElementos
            .AsNoTracking()
            .Where(item =>
                item.activo &&
                item.elementoQuimicosId == elementoQuimicoId &&
                calculoIds.Contains(item.analisisSueloCalculoId))
            .Select(item => new
            {
                item.analisisSueloCalculoId,
                item.cantidadIngresada,
                item.cantidadConvertidaLbMz,
                item.clasificacion
            })
            .ToListAsync(cancellationToken);

        var puntos = new List<PuntoSueloMapaDto>();

        foreach (var valor in valores)
        {
            if (!basePorCalculo.TryGetValue(
                    valor.analisisSueloCalculoId,
                    out CalculoTerrenoBase? item))
            {
                continue;
            }

            decimal dato = valor.cantidadConvertidaLbMz ??
                           valor.cantidadIngresada;
            string clasificacion = NormalizarClasificacion(
                valor.clasificacion);

            puntos.Add(CrearPunto(
                item,
                dato,
                string.IsNullOrWhiteSpace(clasificacion)
                    ? "SIN CLASIFICAR"
                    : clasificacion,
                ColorClasificacion(clasificacion)));
        }

        return puntos;
    }

    private async Task<List<ResumenTerritorialSueloMapaDto>>
        CrearResumenesTerritorialesAsync(
            DefinicionCapaSuelo definicion,
            IReadOnlyCollection<PuntoSueloMapaDto> puntos,
            bool agruparPorMunicipio,
            CancellationToken cancellationToken)
    {
        UmbralesAlertas? umbrales = definicion.Tipo is
            TipoIndicadorSuelo.Ph or
            TipoIndicadorSuelo.MateriaOrganica or
            TipoIndicadorSuelo.AcidezTotal
                ? await umbralesService.ObtenerAsync(cancellationToken)
                : null;

        var regiones = new List<ResumenTerritorialSueloMapaDto>();

        if (agruparPorMunicipio)
        {
            foreach (var grupo in puntos.GroupBy(item => new
                     {
                         item.DepartamentoId,
                         item.Departamento,
                         item.MunicipioId,
                         item.Municipio
                     }))
            {
                regiones.Add(CrearResumenTerritorial(
                    definicion,
                    grupo.ToList(),
                    "MUNICIPIO",
                    grupo.Key.Municipio,
                    grupo.Key.DepartamentoId,
                    grupo.Key.Departamento,
                    grupo.Key.MunicipioId,
                    grupo.Key.Municipio,
                    umbrales));
            }
        }
        else
        {
            foreach (var grupo in puntos.GroupBy(item => new
                     {
                         item.DepartamentoId,
                         item.Departamento
                     }))
            {
                regiones.Add(CrearResumenTerritorial(
                    definicion,
                    grupo.ToList(),
                    "DEPARTAMENTO",
                    grupo.Key.Departamento,
                    grupo.Key.DepartamentoId,
                    grupo.Key.Departamento,
                    null,
                    string.Empty,
                    umbrales));
            }
        }

        if (definicion.Tipo is
            TipoIndicadorSuelo.Cice or
            TipoIndicadorSuelo.SaturacionBases)
        {
            AplicarClasificacionRelativa(regiones);
        }

        return regiones
            .OrderBy(item => item.Departamento)
            .ThenBy(item => item.Municipio)
            .ToList();
    }

    private static ResumenTerritorialSueloMapaDto CrearResumenTerritorial(
        DefinicionCapaSuelo definicion,
        IReadOnlyCollection<PuntoSueloMapaDto> puntos,
        string tipoTerritorio,
        string nombreTerritorio,
        int departamentoId,
        string departamento,
        int? municipioId,
        string municipio,
        UmbralesAlertas? umbrales)
    {
        decimal promedio = Math.Round(
            puntos.Average(item => item.Valor),
            4);

        (string clasificacion, string color) =
            ClasificarPromedioTerritorial(
                definicion,
                promedio,
                puntos,
                umbrales);

        int terrenosAnalizados = puntos
            .Select(item => item.TerrenoId)
            .Distinct()
            .Count();

        return new ResumenTerritorialSueloMapaDto
        {
            TipoTerritorio = tipoTerritorio,
            NombreTerritorio = nombreTerritorio,
            DepartamentoId = departamentoId,
            Departamento = departamento,
            MunicipioId = municipioId,
            Municipio = municipio,
            Promedio = promedio,
            Minimo = Math.Round(puntos.Min(item => item.Valor), 4),
            Maximo = Math.Round(puntos.Max(item => item.Valor), 4),
            TerrenosAnalizados = terrenosAnalizados,
            Clasificacion = clasificacion,
            Color = color,
            FechaMasReciente = puntos.Max(item => item.FechaAnalisis),
            MuestraLimitada = terrenosAnalizados < 3
        };
    }

    private static (string Clasificacion, string Color)
        ClasificarPromedioTerritorial(
            DefinicionCapaSuelo definicion,
            decimal promedio,
            IReadOnlyCollection<PuntoSueloMapaDto> puntos,
            UmbralesAlertas? umbrales)
    {
        switch (definicion.Tipo)
        {
            case TipoIndicadorSuelo.Ph when umbrales is not null:
                return ClasificarPh(promedio, umbrales);

            case TipoIndicadorSuelo.MateriaOrganica when umbrales is not null:
                return promedio < umbrales.MateriaOrganicaBajaMaxima
                    ? ("BAJA", ColorAtencion)
                    : ("ADECUADA", ColorAdecuado);

            case TipoIndicadorSuelo.AcidezTotal when umbrales is not null:
                return promedio > umbrales.AcidezAltaMinima
                    ? ("ALTA", ColorCritico)
                    : ("ACEPTABLE", ColorAdecuado);

            case TipoIndicadorSuelo.Nutriente:
                return ClasificacionDominante(puntos);

            default:
                return ("DISTRIBUCIÓN RELATIVA", ColorSinClasificar);
        }
    }

    private static (string Clasificacion, string Color)
        ClasificacionDominante(
            IReadOnlyCollection<PuntoSueloMapaDto> puntos)
    {
        var dominante = puntos
            .GroupBy(item => new
            {
                Clasificacion = string.IsNullOrWhiteSpace(item.Clasificacion)
                    ? "SIN CLASIFICAR"
                    : item.Clasificacion,
                Color = string.IsNullOrWhiteSpace(item.Color)
                    ? ColorSinClasificar
                    : item.Color
            })
            .Select(grupo => new
            {
                grupo.Key.Clasificacion,
                grupo.Key.Color,
                Cantidad = grupo.Count(),
                Prioridad = PrioridadClasificacion(grupo.Key.Clasificacion)
            })
            .OrderByDescending(item => item.Cantidad)
            .ThenBy(item => item.Prioridad)
            .FirstOrDefault();

        return dominante is null
            ? ("SIN CLASIFICAR", ColorSinClasificar)
            : (dominante.Clasificacion, dominante.Color);
    }

    private async Task<DefinicionCapaSuelo?> ResolverDefinicionAsync(
        string clave,
        CancellationToken cancellationToken)
    {
        DefinicionCapaSuelo? fija = clave switch
        {
            "ph" => new(
                clave,
                "pH del suelo",
                "Promedio territorial del último análisis disponible de cada terreno.",
                string.Empty,
                TipoIndicadorSuelo.Ph),

            "materia-organica" => new(
                clave,
                "Materia orgánica",
                "Promedio territorial de materia orgánica del último análisis de cada terreno.",
                "%",
                TipoIndicadorSuelo.MateriaOrganica),

            "acidez-total" or "acidez" => new(
                "acidez-total",
                "Acidez total",
                "Promedio territorial de acidez del último análisis de cada terreno.",
                "meq/100 g",
                TipoIndicadorSuelo.AcidezTotal),

            "cice" => new(
                clave,
                "CICE",
                "Promedio territorial de la capacidad de intercambio catiónico efectiva.",
                "meq/100 g",
                TipoIndicadorSuelo.Cice),

            "saturacion-bases" => new(
                clave,
                "Saturación de bases",
                "Promedio territorial de saturación de bases del último cálculo disponible.",
                "%",
                TipoIndicadorSuelo.SaturacionBases),

            _ => null
        };

        if (fija is not null)
            return fija;

        if (!clave.StartsWith(
                "nutriente-",
                StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(
                clave["nutriente-".Length..],
                out int elementoId))
        {
            return null;
        }

        var elemento = await db.elementoQuimico
            .AsNoTracking()
            .Where(item =>
                item.activo &&
                item.elementoQuimicosId == elementoId)
            .Select(item => new
            {
                item.elementoQuimicosId,
                item.simboloElementoQuimico,
                item.nombreElementoQuimico
            })
            .FirstOrDefaultAsync(cancellationToken);

        return elemento is null
            ? null
            : new DefinicionCapaSuelo(
                clave,
                $"{elemento.nombreElementoQuimico} ({elemento.simboloElementoQuimico})",
                "Disponibilidad promedio de los terrenos analizados en la zona seleccionada.",
                "lb/Mz",
                TipoIndicadorSuelo.Nutriente,
                elemento.elementoQuimicosId);
    }

    private static PuntoSueloMapaDto CrearPunto(
        CalculoTerrenoBase item,
        decimal valor,
        string clasificacion,
        string color) =>
        new()
        {
            TerrenoId = item.TerrenoId,
            AnalisisSueloCalculoId = item.AnalisisSueloCalculoId,
            Codigo = item.Codigo,
            Productor = item.Productor,
            DepartamentoId = item.DepartamentoId,
            Departamento = item.Departamento,
            MunicipioId = item.MunicipioId,
            Municipio = item.Municipio,
            Latitud = item.Latitud,
            Longitud = item.Longitud,
            Valor = Math.Round(valor, 4),
            Clasificacion = clasificacion,
            Color = color,
            FechaAnalisis = item.FechaAnalisis
        };

    private static (string Clasificacion, string Color) ClasificarPh(
        decimal ph,
        UmbralesAlertas umbrales)
    {
        if (ph < umbrales.PhBajoCriticoMaximo)
            return ("MUY ÁCIDO", ColorCritico);

        if (ph < umbrales.PhBajoAtencionMaximo)
            return ("ÁCIDO", ColorAtencion);

        if (ph >= umbrales.PhAltoCriticoMinimo)
            return ("MUY ALCALINO", "#7c3aed");

        if (ph >= umbrales.PhAltoAtencionMinimo)
            return ("ALCALINO", ColorAlto);

        return ("RANGO INTERMEDIO", ColorAdecuado);
    }

    private static void AplicarClasificacionRelativa(
        IList<ResumenTerritorialSueloMapaDto> regiones)
    {
        if (regiones.Count == 0)
            return;

        decimal minimo = regiones.Min(item => item.Promedio);
        decimal maximo = regiones.Max(item => item.Promedio);
        decimal rango = maximo - minimo;

        foreach (ResumenTerritorialSueloMapaDto region in regiones)
        {
            decimal posicion = rango <= 0
                ? 0.5m
                : (region.Promedio - minimo) / rango;

            if (posicion < 0.3333m)
            {
                region.Clasificacion = "BAJO RELATIVO";
                region.Color = "#f59e0b";
            }
            else if (posicion < 0.6666m)
            {
                region.Clasificacion = "MEDIO RELATIVO";
                region.Color = "#22c55e";
            }
            else
            {
                region.Clasificacion = "ALTO RELATIVO";
                region.Color = "#2563eb";
            }
        }
    }

    private static List<RangoLeyendaMapaDto> CrearLeyendaTerritorial(
        DefinicionCapaSuelo definicion,
        IReadOnlyCollection<ResumenTerritorialSueloMapaDto> regiones)
    {
        if (regiones.Count == 0)
            return CrearLeyendaVacia(definicion);

        return regiones
            .GroupBy(item => new
            {
                item.Clasificacion,
                item.Color
            })
            .Select(grupo => new RangoLeyendaMapaDto
            {
                Etiqueta = grupo.Key.Clasificacion,
                Color = grupo.Key.Color,
                Desde = grupo.Min(item => item.Promedio),
                Hasta = grupo.Max(item => item.Promedio)
            })
            .OrderBy(item => item.Desde)
            .ToList();
    }

    private static List<RangoLeyendaMapaDto> CrearLeyendaVacia(
        DefinicionCapaSuelo definicion) =>
        definicion.Tipo switch
        {
            TipoIndicadorSuelo.Ph =>
            [
                new() { Etiqueta = "Muy ácido", Color = ColorCritico },
                new() { Etiqueta = "Ácido", Color = ColorAtencion },
                new() { Etiqueta = "Rango intermedio", Color = ColorAdecuado },
                new() { Etiqueta = "Alcalino", Color = ColorAlto },
                new() { Etiqueta = "Muy alcalino", Color = "#7c3aed" }
            ],

            TipoIndicadorSuelo.MateriaOrganica =>
            [
                new() { Etiqueta = "Baja", Color = ColorAtencion },
                new() { Etiqueta = "Adecuada", Color = ColorAdecuado }
            ],

            TipoIndicadorSuelo.AcidezTotal =>
            [
                new() { Etiqueta = "Aceptable", Color = ColorAdecuado },
                new() { Etiqueta = "Alta", Color = ColorCritico }
            ],

            _ =>
            [
                new() { Etiqueta = "Bajo", Color = ColorAtencion },
                new() { Etiqueta = "Medio", Color = ColorAdecuado },
                new() { Etiqueta = "Alto", Color = ColorAlto }
            ]
        };

    private static string ResolverUnidad(
        DefinicionCapaSuelo definicion,
        string respaldo) =>
        definicion.Tipo == TipoIndicadorSuelo.Nutriente
            ? "lb/Mz"
            : respaldo;

    private static string NormalizarClasificacion(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return string.Empty;

        return valor.Trim().ToUpperInvariant();
    }

    private static string ColorClasificacion(string clasificacion)
    {
        string valor = clasificacion.ToUpperInvariant();

        if (valor.Contains("BAJO") ||
            valor.Contains("DEFIC") ||
            valor.Contains("MUY BAJO"))
        {
            return ColorCritico;
        }

        if (valor.Contains("MEDIO") ||
            valor.Contains("MODER") ||
            valor.Contains("ATENC"))
        {
            return ColorAtencion;
        }

        if (valor.Contains("ALTO") ||
            valor.Contains("EXCES"))
        {
            return ColorAlto;
        }

        if (valor.Contains("ADECU") ||
            valor.Contains("NORMAL") ||
            valor.Contains("OPTIM"))
        {
            return ColorAdecuado;
        }

        return ColorSinClasificar;
    }

    private static int PrioridadClasificacion(string clasificacion)
    {
        string valor = clasificacion.ToUpperInvariant();

        if (valor.Contains("MUY BAJO") ||
            valor.Contains("BAJO") ||
            valor.Contains("DEFIC"))
        {
            return 0;
        }

        if (valor.Contains("MEDIO") ||
            valor.Contains("MODER") ||
            valor.Contains("ATENC"))
        {
            return 1;
        }

        if (valor.Contains("ADECU") ||
            valor.Contains("NORMAL") ||
            valor.Contains("OPTIM"))
        {
            return 2;
        }

        if (valor.Contains("ALTO") ||
            valor.Contains("EXCES"))
        {
            return 3;
        }

        return 4;
    }

    private static string NormalizarClave(string? clave) =>
        (clave ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

    private enum TipoIndicadorSuelo
    {
        Ph,
        MateriaOrganica,
        AcidezTotal,
        Cice,
        SaturacionBases,
        Nutriente
    }

    private sealed record DefinicionCapaSuelo(
        string Clave,
        string Nombre,
        string Descripcion,
        string Unidad,
        TipoIndicadorSuelo Tipo,
        int? ElementoQuimicoId = null);

    private sealed class CalculoTerrenoBase
    {
        public int AnalisisSueloCalculoId { get; set; }
        public int TerrenoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Productor { get; set; } = string.Empty;
        public int DepartamentoId { get; set; }
        public string Departamento { get; set; } = string.Empty;
        public int MunicipioId { get; set; }
        public string Municipio { get; set; } = string.Empty;
        public decimal Latitud { get; set; }
        public decimal Longitud { get; set; }
        public DateTime FechaAnalisis { get; set; }
        public decimal Ph { get; set; }
        public decimal? MateriaOrganica { get; set; }
        public decimal? AcidezTotal { get; set; }
    }
}
