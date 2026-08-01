using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.ResumenTerritorialMapaDto;

namespace CONATRADEC_API.Services;

/// <summary>
/// Construye el resumen agrícola por departamento o municipio.
///
/// Cada terreno aporta solamente su cálculo activo más reciente. Los
/// promedios se ponderan por la extensión registrada del terreno cuando
/// dicha extensión es mayor que cero.
/// </summary>
public sealed class ResumenTerritorialMapaService
{
    private const string ColorCritico =
        "#DC2626";

    private const string ColorAtencion =
        "#F59E0B";

    private const string ColorNormal =
        "#2F855A";

    private const string ColorSinInformacion =
        "#64748B";

    private readonly DBContext db;
    private readonly UmbralesAlertasService umbralesService;

    public ResumenTerritorialMapaService(
        DBContext db,
        UmbralesAlertasService umbralesService)
    {
        this.db = db;
        this.umbralesService = umbralesService;
    }

    public async Task<RespuestaDto> ObtenerAsync(
        int? departamentoId,
        int? municipioId,
        string? nivelAgrupacion,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Terreno> query =
            db.Terreno
                .AsNoTracking()
                .Where(item => item.activo);

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

        List<TerrenoBase> terrenos =
            await query
                .Select(item => new TerrenoBase
                {
                    TerrenoId =
                        item.terrenoId,

                    DepartamentoId =
                        item.Municipio.DepartamentoId,

                    Departamento =
                        item.Municipio.Departamento
                            .NombreDepartamento,

                    MunicipioId =
                        item.municipioId,

                    Municipio =
                        item.Municipio.NombreMunicipio,

                    ExtensionManzanas =
                        item.extensionManzanaTerreno
                })
                .ToListAsync(cancellationToken);

        string nivelResuelto =
            ResolverNivelAgrupacion(
                nivelAgrupacion,
                departamentoId,
                municipioId);

        bool agruparPorMunicipio =
            nivelResuelto == "MUNICIPIO";

        var respuesta = new RespuestaDto
        {
            Disponible = true,
            NivelAgrupacion = nivelResuelto,
            ActualizadoUtc = DateTime.UtcNow,
            TotalTerrenos = terrenos.Count
        };

        if (terrenos.Count == 0)
        {
            respuesta.Mensaje =
                "No existen terrenos activos para el territorio seleccionado.";

            return respuesta;
        }

        List<int> terrenoIds =
            terrenos
                .Select(item => item.TerrenoId)
                .ToList();

        List<RelacionPropietarioBase> relacionesPropietario =
            await db.Terreno
                .AsNoTracking()
                .Where(item =>
                    terrenoIds.Contains(item.terrenoId))
                .SelectMany(item =>
                    item.RelacionesPropietario
                        .Where(relacion =>
                            relacion.activo &&
                            relacion.Propietario.activo)
                        .Select(relacion =>
                            new RelacionPropietarioBase
                            {
                                TerrenoId =
                                    item.terrenoId,

                                PropietarioId =
                                    relacion.propietarioId
                            }))
                .ToListAsync(cancellationToken);

        List<CalculoBase> calculos =
            await db.AnalisisSueloCalculos
                .AsNoTracking()
                .Where(item =>
                    item.activo &&
                    terrenoIds.Contains(item.terrenoId))
                .OrderByDescending(item =>
                    item.fechaCalculo)
                .ThenByDescending(item =>
                    item.analisisSueloCalculoId)
                .Select(item => new CalculoBase
                {
                    AnalisisSueloCalculoId =
                        item.analisisSueloCalculoId,

                    TerrenoId =
                        item.terrenoId,

                    FechaCalculo =
                        item.fechaCalculo,

                    Ph =
                        item.phAnalisisSuelo,

                    MateriaOrganica =
                        item.materiaOrganica,

                    AcidezTotal =
                        item.acidezTotal
                })
                .ToListAsync(cancellationToken);

        Dictionary<int, CalculoBase> ultimoPorTerreno =
            calculos
                .GroupBy(item => item.TerrenoId)
                .ToDictionary(
                    grupo => grupo.Key,
                    grupo => grupo.First());

        List<int> calculoIds =
            ultimoPorTerreno
                .Values
                .Select(item =>
                    item.AnalisisSueloCalculoId)
                .ToList();

        Dictionary<int, EnmiendaBase> enmiendaPorCalculo =
            await ObtenerEnmiendasAsync(
                calculoIds,
                cancellationToken);

        List<ElementoBase> elementos =
            await ObtenerElementosAsync(
                calculoIds,
                cancellationToken);

        UmbralesAlertas umbrales =
            await umbralesService.ObtenerAsync(
                cancellationToken);

        List<TerrenoAnalitico> analiticos =
            terrenos
                .Select(terreno =>
                {
                    ultimoPorTerreno.TryGetValue(
                        terreno.TerrenoId,
                        out CalculoBase? calculo);

                    EnmiendaBase? enmienda = null;

                    if (calculo is not null)
                    {
                        enmiendaPorCalculo.TryGetValue(
                            calculo.AnalisisSueloCalculoId,
                            out enmienda);
                    }

                    return new TerrenoAnalitico
                    {
                        Terreno =
                            terreno,

                        Calculo =
                            calculo,

                        Cice =
                            enmienda?.Cice,

                        SaturacionBases =
                            enmienda?.SaturacionBases,

                        Nivel =
                            CalcularNivel(
                                calculo,
                                umbrales)
                    };
                })
                .ToList();

        List<RegionDto> regiones =
            agruparPorMunicipio
                ? ConstruirMunicipios(
                    analiticos,
                    relacionesPropietario,
                    elementos)
                : ConstruirDepartamentos(
                    analiticos,
                    relacionesPropietario,
                    elementos);

        respuesta.Regiones =
            regiones
                .OrderBy(item =>
                    item.NombreTerritorio)
                .ToList();

        respuesta.TotalRegiones =
            respuesta.Regiones.Count;

        respuesta.Mensaje =
            respuesta.TotalRegiones == 0
                ? "No fue posible construir el resumen territorial."
                : "Resumen construido con el análisis más reciente de cada terreno.";

        return respuesta;
    }

    private List<RegionDto> ConstruirDepartamentos(
        IReadOnlyCollection<TerrenoAnalitico> terrenos,
        IReadOnlyCollection<RelacionPropietarioBase> propietarios,
        IReadOnlyCollection<ElementoBase> elementos) =>
        terrenos
            .GroupBy(item => new
            {
                item.Terreno.DepartamentoId,
                item.Terreno.Departamento
            })
            .Select(grupo =>
                CrearRegion(
                    grupo.ToList(),
                    propietarios,
                    elementos,
                    "DEPARTAMENTO",
                    grupo.Key.DepartamentoId,
                    grupo.Key.Departamento,
                    null,
                    string.Empty,
                    grupo.Key.Departamento))
            .ToList();

    private List<RegionDto> ConstruirMunicipios(
        IReadOnlyCollection<TerrenoAnalitico> terrenos,
        IReadOnlyCollection<RelacionPropietarioBase> propietarios,
        IReadOnlyCollection<ElementoBase> elementos) =>
        terrenos
            .GroupBy(item => new
            {
                item.Terreno.DepartamentoId,
                item.Terreno.Departamento,
                item.Terreno.MunicipioId,
                item.Terreno.Municipio
            })
            .Select(grupo =>
                CrearRegion(
                    grupo.ToList(),
                    propietarios,
                    elementos,
                    "MUNICIPIO",
                    grupo.Key.DepartamentoId,
                    grupo.Key.Departamento,
                    grupo.Key.MunicipioId,
                    grupo.Key.Municipio,
                    grupo.Key.Municipio))
            .ToList();

    private RegionDto CrearRegion(
        IReadOnlyCollection<TerrenoAnalitico> terrenos,
        IReadOnlyCollection<RelacionPropietarioBase> propietarios,
        IReadOnlyCollection<ElementoBase> elementos,
        string tipoTerritorio,
        int departamentoId,
        string departamento,
        int? municipioId,
        string municipio,
        string nombreTerritorio)
    {
        List<int> terrenoIds =
            terrenos
                .Select(item =>
                    item.Terreno.TerrenoId)
                .ToList();

        List<int> calculoIds =
            terrenos
                .Where(item =>
                    item.Calculo is not null)
                .Select(item =>
                    item.Calculo!
                        .AnalisisSueloCalculoId)
                .ToList();

        int total =
            terrenos.Count;

        int conAnalisis =
            terrenos.Count(item =>
                item.Calculo is not null);

        int sinAnalisis =
            total - conAnalisis;

        int criticos =
            terrenos.Count(item =>
                item.Nivel == "CRITICA");

        int atencion =
            terrenos.Count(item =>
                item.Nivel == "ATENCION");

        int normales =
            terrenos.Count(item =>
                item.Nivel == "NORMAL");

        decimal criticosPorcentaje =
            Porcentaje(criticos, total);

        decimal atencionPorcentaje =
            Porcentaje(atencion, total);

        decimal normalesPorcentaje =
            Porcentaje(normales, total);

        decimal sinAnalisisPorcentaje =
            Porcentaje(sinAnalisis, total);

        string estadoTerritorial =
            ResolverEstadoTerritorial(
                conAnalisis,
                criticosPorcentaje,
                atencionPorcentaje);

        var region = new RegionDto
        {
            TipoTerritorio =
                tipoTerritorio,

            DepartamentoId =
                departamentoId,

            Departamento =
                departamento,

            MunicipioId =
                municipioId,

            Municipio =
                municipio,

            NombreTerritorio =
                nombreTerritorio,

            TotalTerrenos =
                total,

            TotalPropietarios =
                propietarios
                    .Where(item =>
                        terrenoIds.Contains(
                            item.TerrenoId))
                    .Select(item =>
                        item.PropietarioId)
                    .Distinct()
                    .Count(),

            ExtensionTotalManzanas =
                decimal.Round(
                    terrenos.Sum(item =>
                        item.Terreno
                            .ExtensionManzanas),
                    2),

            ConAnalisis =
                conAnalisis,

            SinAnalisis =
                sinAnalisis,

            CoberturaAnalisisPorcentaje =
                Porcentaje(
                    conAnalisis,
                    total),

            Criticos =
                criticos,

            Atencion =
                atencion,

            Normales =
                normales,

            CriticosPorcentaje =
                criticosPorcentaje,

            AtencionPorcentaje =
                atencionPorcentaje,

            NormalesPorcentaje =
                normalesPorcentaje,

            SinAnalisisPorcentaje =
                sinAnalisisPorcentaje,

            EstadoTerritorial =
                estadoTerritorial,

            EstadoTexto =
                EstadoTexto(
                    estadoTerritorial),

            Color =
                ColorEstado(
                    estadoTerritorial),

            PhPromedio =
                PromedioPonderado(
                    terrenos,
                    item =>
                        item.Calculo?.Ph),

            MateriaOrganicaPromedio =
                PromedioPonderado(
                    terrenos,
                    item =>
                        item.Calculo?
                            .MateriaOrganica),

            AcidezTotalPromedio =
                PromedioPonderado(
                    terrenos,
                    item =>
                        item.Calculo?
                            .AcidezTotal),

            CicePromedio =
                PromedioPonderado(
                    terrenos,
                    item =>
                        item.Cice),

            SaturacionBasesPromedio =
                PromedioPonderado(
                    terrenos,
                    item =>
                        item.SaturacionBases),

            FechaAnalisisMasReciente =
                terrenos
                    .Where(item =>
                        item.Calculo is not null)
                    .Select(item =>
                        (DateTime?)item.Calculo!
                            .FechaCalculo)
                    .Max(),

            MuestraLimitada =
                conAnalisis < 3 ||
                Porcentaje(
                    conAnalisis,
                    total) < 50m,

            Nutrientes =
                ConstruirNutrientes(
                    terrenos,
                    calculoIds,
                    elementos)
        };

        return region;
    }

    private static List<NutrienteDto> ConstruirNutrientes(
        IReadOnlyCollection<TerrenoAnalitico> terrenos,
        IReadOnlyCollection<int> calculoIds,
        IReadOnlyCollection<ElementoBase> elementos)
    {
        Dictionary<int, TerrenoAnalitico> terrenoPorCalculo =
            terrenos
                .Where(item =>
                    item.Calculo is not null)
                .ToDictionary(
                    item =>
                        item.Calculo!
                            .AnalisisSueloCalculoId);

        var resultado =
            new List<NutrienteDto>();

        foreach (IGrouping<int, ElementoBase> grupo
                 in elementos
                     .Where(item =>
                         calculoIds.Contains(
                             item.AnalisisSueloCalculoId))
                     .GroupBy(item =>
                         item.ElementoQuimicoId))
        {
            List<ElementoBase> disponibles =
                grupo.ToList();

            List<ElementoBase> comparables =
                disponibles
                    .Where(item =>
                        item.CantidadConvertidaLbMz
                            .HasValue)
                    .ToList();

            string unidad =
                "lb/Mz";

            if (comparables.Count == 0)
            {
                IGrouping<string, ElementoBase>? unidadPrincipal =
                    disponibles
                        .GroupBy(item =>
                            string.IsNullOrWhiteSpace(
                                item.Unidad)
                                ? "Sin unidad"
                                : item.Unidad)
                        .OrderByDescending(item =>
                            item.Count())
                        .FirstOrDefault();

                comparables =
                    unidadPrincipal?.ToList() ?? [];

                unidad =
                    unidadPrincipal?.Key ??
                    string.Empty;
            }

            if (comparables.Count == 0)
                continue;

            decimal sumaPonderada = 0;
            decimal sumaPesos = 0;

            foreach (ElementoBase elemento
                     in comparables)
            {
                if (!terrenoPorCalculo.TryGetValue(
                        elemento.AnalisisSueloCalculoId,
                        out TerrenoAnalitico? terreno))
                {
                    continue;
                }

                decimal valor =
                    elemento.CantidadConvertidaLbMz ??
                    elemento.CantidadIngresada;

                decimal peso =
                    terreno.Terreno.ExtensionManzanas > 0
                        ? terreno.Terreno.ExtensionManzanas
                        : 1m;

                sumaPonderada +=
                    valor * peso;

                sumaPesos +=
                    peso;
            }

            if (sumaPesos <= 0)
                continue;

            int bajos =
                comparables.Count(item =>
                    NormalizarClasificacion(
                        item.Clasificacion) ==
                    "BAJO");

            int medios =
                comparables.Count(item =>
                    NormalizarClasificacion(
                        item.Clasificacion) ==
                    "MEDIO");

            int altos =
                comparables.Count(item =>
                    NormalizarClasificacion(
                        item.Clasificacion) ==
                    "ALTO");

            ElementoBase primero =
                comparables[0];

            resultado.Add(new NutrienteDto
            {
                ElementoQuimicoId =
                    grupo.Key,

                Simbolo =
                    primero.Simbolo,

                Nombre =
                    primero.Nombre,

                Unidad =
                    unidad,

                Promedio =
                    decimal.Round(
                        sumaPonderada /
                        sumaPesos,
                        2),

                TerrenosConDato =
                    comparables
                        .Select(item =>
                            item.AnalisisSueloCalculoId)
                        .Distinct()
                        .Count(),

                Bajos =
                    bajos,

                Medios =
                    medios,

                Altos =
                    altos,

                PorcentajeBajo =
                    Porcentaje(
                        bajos,
                        comparables.Count)
            });
        }

        return resultado
            .OrderByDescending(item =>
                item.PorcentajeBajo)
            .ThenBy(item =>
                item.Nombre)
            .ToList();
    }

    private async Task<Dictionary<int, EnmiendaBase>>
        ObtenerEnmiendasAsync(
            IReadOnlyCollection<int> calculoIds,
            CancellationToken cancellationToken)
    {
        if (calculoIds.Count == 0)
            return [];

        List<EnmiendaBase> registros =
            await db.enmiendaCalcarea
                .AsNoTracking()
                .Where(item =>
                    item.activo &&
                    item.analisisSueloCalculoId
                        .HasValue &&
                    calculoIds.Contains(
                        item.analisisSueloCalculoId
                            .Value))
                .OrderByDescending(item =>
                    item.fechaCreacion)
                .ThenByDescending(item =>
                    item.enmiendaCalcareaId)
                .Select(item =>
                    new EnmiendaBase
                    {
                        AnalisisSueloCalculoId =
                            item.analisisSueloCalculoId!
                                .Value,

                        Cice =
                            item.cice,

                        SaturacionBases =
                            item.saturacionActual
                    })
                .ToListAsync(cancellationToken);

        return registros
            .GroupBy(item =>
                item.AnalisisSueloCalculoId)
            .ToDictionary(
                grupo => grupo.Key,
                grupo => grupo.First());
    }

    private async Task<List<ElementoBase>>
        ObtenerElementosAsync(
            IReadOnlyCollection<int> calculoIds,
            CancellationToken cancellationToken)
    {
        if (calculoIds.Count == 0)
            return [];

        return await db.AnalisisSueloCalculoElementos
            .AsNoTracking()
            .Where(item =>
                item.activo &&
                calculoIds.Contains(
                    item.analisisSueloCalculoId))
            .Select(item => new ElementoBase
            {
                AnalisisSueloCalculoId =
                    item.analisisSueloCalculoId,

                ElementoQuimicoId =
                    item.elementoQuimicosId,

                Simbolo =
                    item.ElementoQuimico
                        .simboloElementoQuimico,

                Nombre =
                    item.ElementoQuimico
                        .nombreElementoQuimico,

                CantidadIngresada =
                    item.cantidadIngresada,

                CantidadConvertidaLbMz =
                    item.cantidadConvertidaLbMz,

                Unidad =
                    item.UnidadMedida == null
                        ? string.Empty
                        : item.UnidadMedida
                            .nombreUnidadMedida,

                Clasificacion =
                    item.clasificacion ??
                    string.Empty
            })
            .ToListAsync(cancellationToken);
    }

    private static string ResolverNivelAgrupacion(
        string? nivelAgrupacion,
        int? departamentoId,
        int? municipioId)
    {
        string nivel =
            (nivelAgrupacion ??
             string.Empty)
                .Trim()
                .ToUpperInvariant();

        if (nivel is "MUNICIPIO" or "MUNICIPAL")
            return "MUNICIPIO";

        if (nivel is "DEPARTAMENTO" or "DEPARTAMENTAL")
            return "DEPARTAMENTO";

        return departamentoId is > 0 ||
               municipioId is > 0
            ? "MUNICIPIO"
            : "DEPARTAMENTO";
    }

    private static decimal? PromedioPonderado(
        IEnumerable<TerrenoAnalitico> terrenos,
        Func<TerrenoAnalitico, decimal?> selector)
    {
        decimal suma = 0;
        decimal pesos = 0;

        foreach (TerrenoAnalitico terreno
                 in terrenos)
        {
            decimal? valor =
                selector(terreno);

            if (!valor.HasValue)
                continue;

            decimal peso =
                terreno.Terreno.ExtensionManzanas > 0
                    ? terreno.Terreno.ExtensionManzanas
                    : 1m;

            suma +=
                valor.Value * peso;

            pesos +=
                peso;
        }

        return pesos <= 0
            ? null
            : decimal.Round(
                suma / pesos,
                2);
    }

    private static string CalcularNivel(
        CalculoBase? calculo,
        UmbralesAlertas umbrales)
    {
        if (calculo is null)
            return "SIN_ANALISIS";

        if (calculo.Ph <
                umbrales.PhBajoCriticoMaximo ||
            calculo.Ph >=
                umbrales.PhAltoCriticoMinimo)
        {
            return "CRITICA";
        }

        if (calculo.Ph <
                umbrales.PhBajoAtencionMaximo ||
            calculo.Ph >=
                umbrales.PhAltoAtencionMinimo ||
            (calculo.MateriaOrganica.HasValue &&
             calculo.MateriaOrganica.Value <
                umbrales.MateriaOrganicaBajaMaxima) ||
            (calculo.AcidezTotal.HasValue &&
             calculo.AcidezTotal.Value >
                umbrales.AcidezAltaMinima))
        {
            return "ATENCION";
        }

        return "NORMAL";
    }

    private static string ResolverEstadoTerritorial(
        int conAnalisis,
        decimal criticosPorcentaje,
        decimal atencionPorcentaje)
    {
        if (conAnalisis == 0)
            return "SIN_ANALISIS";

        if (criticosPorcentaje >= 30m)
            return "CRITICA";

        if (atencionPorcentaje >= 30m)
            return "ATENCION";

        return "NORMAL";
    }

    private static string EstadoTexto(
        string estado) =>
        estado switch
        {
            "CRITICA" =>
                "Crítico",

            "ATENCION" =>
                "Requiere atención",

            "NORMAL" =>
                "Condición normal",

            _ =>
                "Sin información suficiente"
        };

    private static string ColorEstado(
        string estado) =>
        estado switch
        {
            "CRITICA" =>
                ColorCritico,

            "ATENCION" =>
                ColorAtencion,

            "NORMAL" =>
                ColorNormal,

            _ =>
                ColorSinInformacion
        };

    private static decimal Porcentaje(
        int cantidad,
        int total) =>
        total <= 0
            ? 0
            : decimal.Round(
                cantidad /
                (decimal)total *
                100m,
                2);

    private static string NormalizarClasificacion(
        string? clasificacion)
    {
        string valor =
            (clasificacion ??
             string.Empty)
                .Trim()
                .ToUpperInvariant();

        if (valor.Contains("BAJO"))
            return "BAJO";

        if (valor.Contains("MEDIO"))
            return "MEDIO";

        if (valor.Contains("ALTO"))
            return "ALTO";

        return string.Empty;
    }

    private sealed class TerrenoBase
    {
        public int TerrenoId { get; set; }

        public int DepartamentoId { get; set; }

        public string Departamento { get; set; } =
            string.Empty;

        public int MunicipioId { get; set; }

        public string Municipio { get; set; } =
            string.Empty;

        public decimal ExtensionManzanas { get; set; }
    }

    private sealed class RelacionPropietarioBase
    {
        public int TerrenoId { get; set; }

        public int PropietarioId { get; set; }
    }

    private sealed class CalculoBase
    {
        public int AnalisisSueloCalculoId { get; set; }

        public int TerrenoId { get; set; }

        public DateTime FechaCalculo { get; set; }

        public decimal Ph { get; set; }

        public decimal? MateriaOrganica { get; set; }

        public decimal? AcidezTotal { get; set; }
    }

    private sealed class EnmiendaBase
    {
        public int AnalisisSueloCalculoId { get; set; }

        public decimal Cice { get; set; }

        public decimal SaturacionBases { get; set; }
    }

    private sealed class ElementoBase
    {
        public int AnalisisSueloCalculoId { get; set; }

        public int ElementoQuimicoId { get; set; }

        public string Simbolo { get; set; } =
            string.Empty;

        public string Nombre { get; set; } =
            string.Empty;

        public decimal CantidadIngresada { get; set; }

        public decimal? CantidadConvertidaLbMz { get; set; }

        public string Unidad { get; set; } =
            string.Empty;

        public string Clasificacion { get; set; } =
            string.Empty;
    }

    private sealed class TerrenoAnalitico
    {
        public TerrenoBase Terreno { get; set; } =
            new();

        public CalculoBase? Calculo { get; set; }

        public decimal? Cice { get; set; }

        public decimal? SaturacionBases { get; set; }

        public string Nivel { get; set; } =
            "SIN_ANALISIS";
    }
}
