using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers;

/// <summary>
/// Devuelve el detalle interno utilizado por el mapa de relaciones.
///
/// Este controlador es únicamente de lectura. No recalcula ni modifica
/// resultados guardados. Para acceder se requiere lectura tanto en
/// Control de análisis como en Mapa de relaciones de análisis.
/// </summary>
[ApiController]
[Route("api/mapa-relaciones-analisis")]
public sealed class MapaRelacionesAnalisisController : ControllerBase
{
    public const string PermisoMapaRelaciones =
        "MapaRelacionesAnalisisWeb";

    private readonly DBContext db;
    private readonly PermisoApiService permisos;

    public MapaRelacionesAnalisisController(
        DBContext db,
        PermisoApiService permisos)
    {
        this.db = db;
        this.permisos = permisos;
    }

    [HttpGet("{analisisSueloId:int}")]
    public async Task<IActionResult> Obtener(
        int analisisSueloId,
        [FromHeader(Name = "X-Usuario-Id")]
        int? usuarioSesionId,
        CancellationToken cancellationToken = default)
    {
        if (analisisSueloId <= 0)
        {
            return BadRequest(new
            {
                success = false,
                message =
                    "El identificador del análisis no es válido."
            });
        }

        IActionResult? acceso = await ValidarAccesoAsync(
            usuarioSesionId,
            cancellationToken);

        if (acceso is not null)
            return acceso;

        int? calculoId = await db.AnalisisSueloCalculos
            .AsNoTracking()
            .Where(x =>
                x.analisisSueloId == analisisSueloId)
            .OrderByDescending(x => x.fechaCalculo)
            .ThenByDescending(x =>
                x.analisisSueloCalculoId)
            .Select(x =>
                (int?)x.analisisSueloCalculoId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!calculoId.HasValue)
        {
            return NotFound(new
            {
                success = false,
                message =
                    "El análisis no existe o no posee un cálculo asociado."
            });
        }

        int analisisSueloCalculoId =
            calculoId.Value;

        FormulaCabeceraFila? formula =
            await db.formulaNutricional
                .AsNoTracking()
                .Where(x =>
                    x.analisisSueloCalculoId ==
                    analisisSueloCalculoId)
                .OrderByDescending(x =>
                    x.fechaCreacion)
                .ThenByDescending(x =>
                    x.formulaNutricionalId)
                .Select(x =>
                    new FormulaCabeceraFila
                    {
                        FormulaNutricionalId =
                            x.formulaNutricionalId,
                        NombreFormula =
                            x.nombreFormula,
                        FechaCreacion =
                            x.fechaCreacion,
                        TotalLibras =
                            x.totalLibras,
                        MezclaTotalQq =
                            x.mezclaTotalQq,
                        PrecioTotalFormula =
                            x.precioTotalFormula,
                        TotalPlantas =
                            x.totalPlantas,
                        TotalAplicaciones =
                            x.totalAplicaciones,
                        EsComplementoMixta =
                            x.esComplementoFertilizacionMixta,
                        Activo =
                            x.activo
                    })
                .FirstOrDefaultAsync(
                    cancellationToken);

        List<FormulaFuenteRespuesta>
            fuentesFormula = [];

        if (formula is not null)
        {
            List<FormulaDetalleFila> detalles =
                await (
                    from detalle in
                        db.formulaNutricionalDetalle
                            .AsNoTracking()
                    join fuente in
                        db.fuenteNutriente
                            .AsNoTracking()
                        on detalle.fuenteNutrientesId
                        equals fuente.fuenteNutrientesId
                    join elemento in
                        db.elementoQuimico
                            .AsNoTracking()
                        on detalle.elementoQuimicosId
                        equals elemento.elementoQuimicosId
                    where detalle.formulaNutricionalId ==
                          formula.FormulaNutricionalId
                    select new FormulaDetalleFila
                    {
                        FormulaNutricionalDetalleId =
                            detalle.formulaNutricionalDetalleId,
                        FuenteNutrientesId =
                            detalle.fuenteNutrientesId,
                        NombreFuente =
                            fuente.nombreNutriente,
                        ElementoObjetivoId =
                            detalle.elementoQuimicosId,
                        ElementoObjetivoSimbolo =
                            elemento.simboloElementoQuimico,
                        ElementoObjetivoNombre =
                            elemento.nombreElementoQuimico,
                        Libras =
                            detalle.libras,
                        Qq =
                            detalle.qq,
                        RequerimientoLibras =
                            detalle.requerimientoLibras,
                        PrecioPorQuintal =
                            detalle.precioPorQuintal,
                        SubtotalFuente =
                            detalle.subtotalFuente,
                        OnzasAnuales =
                            detalle.onzasAnuales,
                        OnzasPorAplicacion =
                            detalle.onzasPorAplicacion,
                        Activo =
                            detalle.activo
                    })
                .ToListAsync(cancellationToken);

            int[] idsDetalles = detalles
                .Select(x =>
                    x.FormulaNutricionalDetalleId)
                .Distinct()
                .ToArray();

            List<FormulaAporteFila> aportes = [];

            if (idsDetalles.Length > 0)
            {
                aportes = await (
                    from aporte in
                        db.formulaNutricionalAporte
                            .AsNoTracking()
                    join elemento in
                        db.elementoQuimico
                            .AsNoTracking()
                        on aporte.elementoQuimicosId
                        equals elemento.elementoQuimicosId
                    where idsDetalles.Contains(
                              aporte.formulaNutricionalDetalleId)
                    select new FormulaAporteFila
                    {
                        FormulaNutricionalDetalleId =
                            aporte.formulaNutricionalDetalleId,
                        ElementoQuimicosId =
                            aporte.elementoQuimicosId,
                        Simbolo =
                            elemento.simboloElementoQuimico,
                        Nombre =
                            elemento.nombreElementoQuimico,
                        Valor =
                            aporte.valor,
                        Activo =
                            aporte.activo
                    })
                .ToListAsync(cancellationToken);
            }

            fuentesFormula = detalles
                .GroupBy(x => new
                {
                    x.FuenteNutrientesId,
                    x.NombreFuente
                })
                .OrderBy(x =>
                    x.Key.NombreFuente)
                .Select(grupo =>
                {
                    int[] idsGrupo = grupo
                        .Select(x =>
                            x.FormulaNutricionalDetalleId)
                        .Distinct()
                        .ToArray();

                    List<FormulaAporteRespuesta>
                        aportesGrupo = aportes
                            .Where(x =>
                                idsGrupo.Contains(
                                    x.FormulaNutricionalDetalleId))
                            .GroupBy(x => new
                            {
                                x.ElementoQuimicosId,
                                x.Simbolo,
                                x.Nombre
                            })
                            .OrderBy(x =>
                                x.Key.Nombre)
                            .Select(x =>
                                new FormulaAporteRespuesta
                                {
                                    ElementoQuimicosId =
                                        x.Key.ElementoQuimicosId,
                                    Simbolo =
                                        x.Key.Simbolo,
                                    Nombre =
                                        x.Key.Nombre,
                                    Valor =
                                        x.Sum(item =>
                                            item.Valor),
                                    Activo =
                                        x.Any(item =>
                                            item.Activo)
                                })
                            .ToList();

                    return new FormulaFuenteRespuesta
                    {
                        FuenteNutrientesId =
                            grupo.Key.FuenteNutrientesId,
                        NombreFuente =
                            grupo.Key.NombreFuente,
                        CantidadDetalles =
                            grupo.Count(),
                        Libras =
                            grupo.Sum(x =>
                                x.Libras),
                        Qq =
                            grupo.Sum(x =>
                                x.Qq),
                        RequerimientoLibras =
                            grupo.Sum(x =>
                                x.RequerimientoLibras),
                        PrecioPorQuintal =
                            grupo.Max(x =>
                                x.PrecioPorQuintal),
                        SubtotalFuente =
                            grupo.Sum(x =>
                                x.SubtotalFuente),
                        OnzasAnuales =
                            grupo.Sum(x =>
                                x.OnzasAnuales),
                        OnzasPorAplicacion =
                            grupo.Sum(x =>
                                x.OnzasPorAplicacion),
                        Activo =
                            grupo.Any(x =>
                                x.Activo),
                        ElementosObjetivo =
                            grupo
                                .Select(x =>
                                    new ElementoReferenciaRespuesta
                                    {
                                        ElementoQuimicosId =
                                            x.ElementoObjetivoId,
                                        Simbolo =
                                            x.ElementoObjetivoSimbolo,
                                        Nombre =
                                            x.ElementoObjetivoNombre
                                    })
                                .GroupBy(x =>
                                    x.ElementoQuimicosId)
                                .Select(x =>
                                    x.First())
                                .OrderBy(x =>
                                    x.Nombre)
                                .ToList(),
                        Aportes =
                            aportesGrupo
                    };
                })
                .ToList();
        }

        MixtaCabeceraFila? mixta =
            await db.fertilizacionMixta
                .AsNoTracking()
                .Where(x =>
                    x.analisisSueloCalculoId ==
                    analisisSueloCalculoId)
                .OrderByDescending(x =>
                    x.fechaCalculo)
                .ThenByDescending(x =>
                    x.fertilizacionMixtaId)
                .Select(x =>
                    new MixtaCabeceraFila
                    {
                        FertilizacionMixtaId =
                            x.fertilizacionMixtaId,
                        FechaCalculo =
                            x.fechaCalculo,
                        Observacion =
                            x.observacion,
                        EsComplementoBalance =
                            x.esComplementoBalance,
                        Activo =
                            x.activo
                    })
                .FirstOrDefaultAsync(
                    cancellationToken);

        List<MixtaFuenteRespuesta>
            fuentesMixta = [];

        List<MixtaResultadoRespuesta>
            resultadosMixta = [];

        if (mixta is not null)
        {
            List<MixtaFuenteFila> fuentes =
                await (
                    from relacion in
                        db.fertilizacionMixtaFuente
                            .AsNoTracking()
                    join fuente in
                        db.fuenteNutriente
                            .AsNoTracking()
                        on relacion.fuenteNutrientesId
                        equals fuente.fuenteNutrientesId
                    where relacion.fertilizacionMixtaId ==
                          mixta.FertilizacionMixtaId
                    select new MixtaFuenteFila
                    {
                        FertilizacionMixtaFuenteId =
                            relacion.fertilizacionMixtaFuenteId,
                        FuenteNutrientesId =
                            relacion.fuenteNutrientesId,
                        NombreFuente =
                            fuente.nombreNutriente,
                        CantidadQq =
                            relacion.cantidadQq,
                        Activo =
                            relacion.activo
                    })
                .OrderBy(x =>
                    x.NombreFuente)
                .ToListAsync(cancellationToken);

            List<MixtaDetalleFila> detalles =
                await (
                    from detalle in
                        db.fertilizacionMixtaDetalle
                            .AsNoTracking()
                    join elemento in
                        db.elementoQuimico
                            .AsNoTracking()
                        on detalle.elementoQuimicosId
                        equals elemento.elementoQuimicosId
                    where detalle.fertilizacionMixtaId ==
                          mixta.FertilizacionMixtaId
                    select new MixtaDetalleFila
                    {
                        FertilizacionMixtaDetalleId =
                            detalle.fertilizacionMixtaDetalleId,
                        ElementoQuimicosId =
                            detalle.elementoQuimicosId,
                        Simbolo =
                            elemento.simboloElementoQuimico,
                        Nombre =
                            elemento.nombreElementoQuimico,
                        RequerimientoOriginal =
                            detalle.requerimientoOriginal,
                        AporteOrganico =
                            detalle.aporteOrganico,
                        Diferencia =
                            detalle.diferencia,
                        Deficit =
                            detalle.deficit,
                        Sobrante =
                            detalle.sobrante,
                        Activo =
                            detalle.activo
                    })
                .OrderBy(x =>
                    x.Nombre)
                .ToListAsync(cancellationToken);

            int[] fuentesIds = fuentes
                .Select(x =>
                    x.FuenteNutrientesId)
                .Distinct()
                .ToArray();

            int[] elementosIds = detalles
                .Select(x =>
                    x.ElementoQuimicosId)
                .Distinct()
                .ToArray();

            List<ComposicionFuenteFila>
                composiciones = [];

            if (fuentesIds.Length > 0 &&
                elementosIds.Length > 0)
            {
                composiciones = await (
                    from composicion in
                        db.fuenteNutrienteElementoQuimico
                            .AsNoTracking()
                    join elemento in
                        db.elementoQuimico
                            .AsNoTracking()
                        on composicion.elementoQuimicosId
                        equals elemento.elementoQuimicosId
                    where fuentesIds.Contains(
                              composicion.fuenteNutrientesId) &&
                          elementosIds.Contains(
                              composicion.elementoQuimicosId) &&
                          composicion.activo
                    select new ComposicionFuenteFila
                    {
                        FuenteNutrientesId =
                            composicion.fuenteNutrientesId,
                        ElementoQuimicosId =
                            composicion.elementoQuimicosId,
                        Simbolo =
                            elemento.simboloElementoQuimico,
                        Nombre =
                            elemento.nombreElementoQuimico,
                        AportePorUnidad =
                            composicion.cantidadAporte
                    })
                .ToListAsync(cancellationToken);
            }

            fuentesMixta = fuentes
                .Select(fuente =>
                {
                    List<MixtaAporteFuenteRespuesta>
                        aportesFuente = composiciones
                            .Where(x =>
                                x.FuenteNutrientesId ==
                                fuente.FuenteNutrientesId)
                            .OrderBy(x =>
                                x.Nombre)
                            .Select(x =>
                                new MixtaAporteFuenteRespuesta
                                {
                                    ElementoQuimicosId =
                                        x.ElementoQuimicosId,
                                    Simbolo =
                                        x.Simbolo,
                                    Nombre =
                                        x.Nombre,
                                    AportePorUnidad =
                                        x.AportePorUnidad,
                                    CantidadQq =
                                        fuente.CantidadQq,
                                    AporteTotal =
                                        fuente.CantidadQq *
                                        x.AportePorUnidad
                                })
                            .ToList();

                    return new MixtaFuenteRespuesta
                    {
                        FertilizacionMixtaFuenteId =
                            fuente.FertilizacionMixtaFuenteId,
                        FuenteNutrientesId =
                            fuente.FuenteNutrientesId,
                        NombreFuente =
                            fuente.NombreFuente,
                        CantidadQq =
                            fuente.CantidadQq,
                        Activo =
                            fuente.Activo,
                        Aportes =
                            aportesFuente
                    };
                })
                .ToList();

            resultadosMixta = detalles
                .Select(detalle =>
                {
                    decimal aporteReconstruido =
                        fuentesMixta
                            .SelectMany(x =>
                                x.Aportes)
                            .Where(x =>
                                x.ElementoQuimicosId ==
                                detalle.ElementoQuimicosId)
                            .Sum(x =>
                                x.AporteTotal);

                    return new MixtaResultadoRespuesta
                    {
                        FertilizacionMixtaDetalleId =
                            detalle.FertilizacionMixtaDetalleId,
                        ElementoQuimicosId =
                            detalle.ElementoQuimicosId,
                        Simbolo =
                            detalle.Simbolo,
                        Nombre =
                            detalle.Nombre,
                        RequerimientoOriginal =
                            detalle.RequerimientoOriginal,
                        AporteOrganico =
                            detalle.AporteOrganico,
                        Diferencia =
                            detalle.Diferencia,
                        Deficit =
                            detalle.Deficit,
                        Sobrante =
                            detalle.Sobrante,
                        AporteReconstruido =
                            aporteReconstruido,
                        DiferenciaReconstruccion =
                            aporteReconstruido -
                            detalle.AporteOrganico,
                        Activo =
                            detalle.Activo
                    };
                })
                .ToList();
        }

        object? formulaRespuesta =
            formula is null
                ? null
                : new
                {
                    formulaNutricionalId =
                        formula.FormulaNutricionalId,
                    nombreFormula =
                        formula.NombreFormula,
                    fechaCreacion =
                        formula.FechaCreacion,
                    totalLibras =
                        formula.TotalLibras,
                    mezclaTotalQq =
                        formula.MezclaTotalQq,
                    precioTotalFormula =
                        formula.PrecioTotalFormula,
                    totalPlantas =
                        formula.TotalPlantas,
                    totalAplicaciones =
                        formula.TotalAplicaciones,
                    esComplementoMixta =
                        formula.EsComplementoMixta,
                    activo =
                        formula.Activo,
                    fuentes =
                        fuentesFormula
                };

        object? mixtaRespuesta =
            mixta is null
                ? null
                : new
                {
                    fertilizacionMixtaId =
                        mixta.FertilizacionMixtaId,
                    fechaCalculo =
                        mixta.FechaCalculo,
                    observacion =
                        mixta.Observacion,
                    esComplementoBalance =
                        mixta.EsComplementoBalance,
                    activo =
                        mixta.Activo,
                    fuentes =
                        fuentesMixta,
                    resultados =
                        resultadosMixta
                };

        return Ok(new
        {
            success = true,
            message =
                "Relaciones internas obtenidas correctamente.",
            data = new
            {
                analisisSueloId,
                analisisSueloCalculoId,
                formula =
                    formulaRespuesta,
                fertilizacionMixta =
                    mixtaRespuesta
            }
        });
    }

    private async Task<IActionResult?> ValidarAccesoAsync(
        int? usuarioSesionId,
        CancellationToken cancellationToken)
    {
        ResultadoPermisoApi accesoMapa =
            await permisos.ValidarAsync(
                usuarioSesionId,
                PermisoMapaRelaciones,
                TipoPermisoApi.Leer,
                cancellationToken);

        if (!accesoMapa.Permitido)
        {
            return StatusCode(
                accesoMapa.CodigoEstado,
                new
                {
                    success = false,
                    message =
                        "No tiene permiso para consultar el mapa de relaciones."
                });
        }

        ResultadoPermisoApi accesoAuditoria =
            await permisos.ValidarAsync(
                usuarioSesionId,
                AuditoriaAnalisisController.PermisoAuditoria,
                TipoPermisoApi.Leer,
                cancellationToken);

        if (!accesoAuditoria.Permitido)
        {
            return StatusCode(
                accesoAuditoria.CodigoEstado,
                new
                {
                    success = false,
                    message =
                        "No tiene permiso para consultar el control de análisis."
                });
        }

        return null;
    }

    private sealed class FormulaCabeceraFila
    {
        public int FormulaNutricionalId { get; init; }
        public string NombreFormula { get; init; } =
            string.Empty;
        public DateTime FechaCreacion { get; init; }
        public decimal TotalLibras { get; init; }
        public decimal MezclaTotalQq { get; init; }
        public decimal PrecioTotalFormula { get; init; }
        public int TotalPlantas { get; init; }
        public int TotalAplicaciones { get; init; }
        public bool EsComplementoMixta { get; init; }
        public bool Activo { get; init; }
    }

    private sealed class FormulaDetalleFila
    {
        public int FormulaNutricionalDetalleId { get; init; }
        public int FuenteNutrientesId { get; init; }
        public string NombreFuente { get; init; } =
            string.Empty;
        public int ElementoObjetivoId { get; init; }
        public string ElementoObjetivoSimbolo { get; init; } =
            string.Empty;
        public string ElementoObjetivoNombre { get; init; } =
            string.Empty;
        public decimal Libras { get; init; }
        public decimal Qq { get; init; }
        public decimal RequerimientoLibras { get; init; }
        public decimal PrecioPorQuintal { get; init; }
        public decimal SubtotalFuente { get; init; }
        public decimal OnzasAnuales { get; init; }
        public decimal OnzasPorAplicacion { get; init; }
        public bool Activo { get; init; }
    }

    private sealed class FormulaAporteFila
    {
        public int FormulaNutricionalDetalleId { get; init; }
        public int ElementoQuimicosId { get; init; }
        public string Simbolo { get; init; } =
            string.Empty;
        public string Nombre { get; init; } =
            string.Empty;
        public decimal Valor { get; init; }
        public bool Activo { get; init; }
    }

    private sealed class FormulaFuenteRespuesta
    {
        public int FuenteNutrientesId { get; init; }
        public string NombreFuente { get; init; } =
            string.Empty;
        public int CantidadDetalles { get; init; }
        public decimal Libras { get; init; }
        public decimal Qq { get; init; }
        public decimal RequerimientoLibras { get; init; }
        public decimal PrecioPorQuintal { get; init; }
        public decimal SubtotalFuente { get; init; }
        public decimal OnzasAnuales { get; init; }
        public decimal OnzasPorAplicacion { get; init; }
        public bool Activo { get; init; }
        public List<ElementoReferenciaRespuesta>
            ElementosObjetivo { get; init; } = [];
        public List<FormulaAporteRespuesta>
            Aportes { get; init; } = [];
    }

    private sealed class FormulaAporteRespuesta
    {
        public int ElementoQuimicosId { get; init; }
        public string Simbolo { get; init; } =
            string.Empty;
        public string Nombre { get; init; } =
            string.Empty;
        public decimal Valor { get; init; }
        public bool Activo { get; init; }
    }

    private sealed class ElementoReferenciaRespuesta
    {
        public int ElementoQuimicosId { get; init; }
        public string Simbolo { get; init; } =
            string.Empty;
        public string Nombre { get; init; } =
            string.Empty;
    }

    private sealed class MixtaCabeceraFila
    {
        public int FertilizacionMixtaId { get; init; }
        public DateTime FechaCalculo { get; init; }
        public string? Observacion { get; init; }
        public bool EsComplementoBalance { get; init; }
        public bool Activo { get; init; }
    }

    private sealed class MixtaFuenteFila
    {
        public int FertilizacionMixtaFuenteId { get; init; }
        public int FuenteNutrientesId { get; init; }
        public string NombreFuente { get; init; } =
            string.Empty;
        public decimal CantidadQq { get; init; }
        public bool Activo { get; init; }
    }

    private sealed class MixtaDetalleFila
    {
        public int FertilizacionMixtaDetalleId { get; init; }
        public int ElementoQuimicosId { get; init; }
        public string Simbolo { get; init; } =
            string.Empty;
        public string Nombre { get; init; } =
            string.Empty;
        public decimal RequerimientoOriginal { get; init; }
        public decimal AporteOrganico { get; init; }
        public decimal Diferencia { get; init; }
        public decimal Deficit { get; init; }
        public decimal Sobrante { get; init; }
        public bool Activo { get; init; }
    }

    private sealed class ComposicionFuenteFila
    {
        public int FuenteNutrientesId { get; init; }
        public int ElementoQuimicosId { get; init; }
        public string Simbolo { get; init; } =
            string.Empty;
        public string Nombre { get; init; } =
            string.Empty;
        public decimal AportePorUnidad { get; init; }
    }

    private sealed class MixtaFuenteRespuesta
    {
        public int FertilizacionMixtaFuenteId { get; init; }
        public int FuenteNutrientesId { get; init; }
        public string NombreFuente { get; init; } =
            string.Empty;
        public decimal CantidadQq { get; init; }
        public bool Activo { get; init; }
        public List<MixtaAporteFuenteRespuesta>
            Aportes { get; init; } = [];
    }

    private sealed class MixtaAporteFuenteRespuesta
    {
        public int ElementoQuimicosId { get; init; }
        public string Simbolo { get; init; } =
            string.Empty;
        public string Nombre { get; init; } =
            string.Empty;
        public decimal AportePorUnidad { get; init; }
        public decimal CantidadQq { get; init; }
        public decimal AporteTotal { get; init; }
    }

    private sealed class MixtaResultadoRespuesta
    {
        public int FertilizacionMixtaDetalleId { get; init; }
        public int ElementoQuimicosId { get; init; }
        public string Simbolo { get; init; } =
            string.Empty;
        public string Nombre { get; init; } =
            string.Empty;
        public decimal RequerimientoOriginal { get; init; }
        public decimal AporteOrganico { get; init; }
        public decimal Diferencia { get; init; }
        public decimal Deficit { get; init; }
        public decimal Sobrante { get; init; }
        public decimal AporteReconstruido { get; init; }
        public decimal DiferenciaReconstruccion { get; init; }
        public bool Activo { get; init; }
    }
}
