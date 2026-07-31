using CONATRADEC_API.Models;
using CONATRADEC_API.Reportes;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace CONATRADEC_API.Services
{
    /// <summary>
    /// Construye el modelo común utilizado por el PDF y el Excel.
    ///
    /// El requerimiento anual se vuelve a normalizar con la producción
    /// guardada en la cabecera del análisis. De esta forma, el reporte no
    /// reutiliza un requerimiento antiguo calculado con otra producción.
    /// </summary>
    public sealed class AnalisisReporteDatosService
    {
        private readonly DBContext _db;
        private readonly UnidadConversionService _unidadConversionService;

        public AnalisisReporteDatosService(DBContext db)
        {
            _db = db;
            _unidadConversionService =
                new UnidadConversionService(db);
        }

        public async Task<AnalisisReporte?> ObtenerAsync(
            int analisisSueloCalculoId,
            CancellationToken cancellationToken = default)
        {
            var calculo = await _db.AnalisisSueloCalculos
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.analisisSueloCalculoId == analisisSueloCalculoId &&
                         x.activo,
                    cancellationToken);

            if (calculo == null)
                return null;

            var analisis = await _db.AnalisisSuelos
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.analisisSueloId == calculo.analisisSueloId && x.activo,
                    cancellationToken);

            if (analisis == null)
                return null;

            var terreno = await _db.Terreno
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.terrenoId == calculo.terrenoId,
                    cancellationToken);

            string propietario = terreno == null
                ? string.Empty
                : await _db.PropietarioTerrenos
                    .AsNoTracking()
                    .Where(relacion =>
                        relacion.terrenoId == terreno.terrenoId &&
                        relacion.activo &&
                        relacion.Propietario.activo)
                    .OrderByDescending(relacion =>
                        relacion.fechaAsignacionUtc)
                    .Select(relacion =>
                        relacion.Propietario.nombreCompleto)
                    .FirstOrDefaultAsync(cancellationToken) ??
                  string.Empty;

            var tipoCultivo = await _db.TipoCultivos
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.tipoCultivoId == calculo.tipoCultivoId,
                    cancellationToken);

            var tipoAnalisis = await _db.TipoAnalisisSuelos
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.tipoAnalisisSueloId == calculo.tipoAnalisisSueloId,
                    cancellationToken);

            string responsable = string.Empty;
            if (calculo.usuarioId.HasValue)
            {
                responsable = await _db.Usuarios
                    .AsNoTracking()
                    .Where(x => x.UsuarioId == calculo.usuarioId.Value)
                    .Select(x => x.nombreCompletoUsuario)
                    .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
            }

            string unidadMateriaOrganica = string.Empty;
            if (calculo.unidadMedidaMateriaOrganicaId.HasValue)
            {
                unidadMateriaOrganica = await _db.UnidadMedidas
                    .AsNoTracking()
                    .Where(x =>
                        x.unidadMedidaId ==
                        calculo.unidadMedidaMateriaOrganicaId.Value)
                    .Select(x => x.nombreUnidadMedida)
                    .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
            }

            AnalisisReporte reporte = new()
            {
                AnalisisSueloCalculoId = calculo.analisisSueloCalculoId,
                Identificador = analisis.identificadorAnalisisSuelo,
                FechaAnalisis = analisis.fechaAnalisisSuelo,
                FechaCalculo = calculo.fechaCalculo,
                Laboratorio = analisis.laboratorioAnalasisSuelo,
                Cliente = propietario,
                Terreno = FormatearTerreno(
                    terreno?.codigoTerreno,
                    terreno?.direccionTerreno,
                    calculo.terrenoId),
                TipoCultivo = tipoCultivo?.nombreTipoCultivo ??
                    $"Cultivo #{calculo.tipoCultivoId}",
                TipoAnalisis = tipoAnalisis?.nombreTipoAnalisisSuelo ??
                    $"Tipo #{calculo.tipoAnalisisSueloId}",
                Responsable = responsable,
                ProduccionQqOro = calculo.cantidadQuintalesOro,
                TamanoFincaMz = calculo.tamanoFinca,
                Ph = calculo.phAnalisisSuelo,
                MateriaOrganica = calculo.materiaOrganica,
                UnidadMateriaOrganica = unidadMateriaOrganica,
                AcidezTotal = calculo.acidezTotal,
                RecomendacionGeneral = calculo.recomendacionGeneral ?? string.Empty,
                Observaciones = SepararObservaciones(calculo.observacion)
            };

            await CargarValoresLaboratorioAsync(
                reporte,
                analisis.analisisSueloId,
                cancellationToken);

            await CargarRequerimientosAsync(
                reporte,
                calculo,
                cancellationToken);

            reporte.Balance = await CargarBalanceAsync(
                analisisSueloCalculoId,
                cancellationToken);

            reporte.Enmienda = await CargarEnmiendaAsync(
                analisisSueloCalculoId,
                cancellationToken);

            reporte.FertilizacionMixta = await CargarMixtaAsync(
                analisisSueloCalculoId,
                reporte.Balance,
                cancellationToken);

            return reporte;
        }

        private async Task CargarValoresLaboratorioAsync(
            AnalisisReporte reporte,
            int analisisSueloId,
            CancellationToken cancellationToken)
        {
            var filas = await (
                from valor in _db.AnalisisSueloElementos.AsNoTracking()
                join elemento in _db.elementoQuimico.AsNoTracking()
                    on valor.elementoQuimicosId equals elemento.elementoQuimicosId
                join unidad in _db.UnidadMedidas.AsNoTracking()
                    on valor.unidadMedidaId equals unidad.unidadMedidaId
                where valor.analisisSueloId == analisisSueloId && valor.activo
                orderby elemento.nombreElementoQuimico
                select new
                {
                    elemento.nombreElementoQuimico,
                    elemento.simboloElementoQuimico,
                    valor.cantidadElemento,
                    unidad.nombreUnidadMedida
                }).ToListAsync(cancellationToken);

            reporte.ValoresLaboratorio = filas
                .Select(x => new AnalisisReporteValorLaboratorio
                {
                    Elemento = FormatearElemento(
                        x.nombreElementoQuimico,
                        x.simboloElementoQuimico),
                    Cantidad = x.cantidadElemento,
                    Unidad = x.nombreUnidadMedida
                })
                .ToList();
        }

        private async Task CargarRequerimientosAsync(
            AnalisisReporte reporte,
            AnalisisSueloCalculo calculo,
            CancellationToken cancellationToken)
        {
            var filas = await (
                from valor in _db.AnalisisSueloCalculoElementoQuimicos.AsNoTracking()
                join elemento in _db.elementoQuimico.AsNoTracking()
                    on valor.elementoQuimicosId equals elemento.elementoQuimicosId
                where valor.analisisSueloCalculoId ==
                          calculo.analisisSueloCalculoId &&
                      valor.activo
                select new
                {
                    valor.elementoQuimicosId,
                    elemento.nombreElementoQuimico,
                    elemento.simboloElementoQuimico,
                    valor.cantidadIngresada,
                    valor.cantidadConvertidaLbMz,
                    valor.requerimientoCalculado,
                    valor.clasificacion,
                    valor.observacion
                }).ToListAsync(cancellationToken);

            List<int> elementosIds = filas
                .Select(x => x.elementoQuimicosId)
                .Distinct()
                .ToList();

            List<ParametroExtraccionNutrienteCafe>
                extraccionesLista =
                    await _db.ParametroExtraccionNutrienteCafe
                        .AsNoTracking()
                        .Where(x =>
                            x.activo &&
                            elementosIds.Contains(x.elementoQuimicosId))
                        .ToListAsync(cancellationToken);

            Dictionary<int, ParametroExtraccionNutrienteCafe>
                extracciones =
                    extraccionesLista
                        .GroupBy(x => x.elementoQuimicosId)
                        .ToDictionary(
                            x => x.Key,
                            x => x.First());

            List<ParametroRangoNutrienteCultivo>
                rangosLista =
                    await _db.ParametroRangoNutrienteCultivo
                        .AsNoTracking()
                        .Where(x =>
                            x.activo &&
                            x.tipoCultivoId == calculo.tipoCultivoId &&
                            elementosIds.Contains(x.elementoQuimicosId))
                        .ToListAsync(cancellationToken);

            Dictionary<int, ParametroRangoNutrienteCultivo>
                rangos =
                    rangosLista
                        .GroupBy(x => x.elementoQuimicosId)
                        .ToDictionary(
                            x => x.Key,
                            x => x.First());

            UnidadMedida? unidadKgHa =
                await _db.UnidadMedidas
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.activo &&
                            x.nombreUnidadMedida.Trim().ToLower() == "kg/ha",
                        cancellationToken);

            decimal? materiaOrganicaPorcentaje =
                await ObtenerMateriaOrganicaPorcentajeAsync(
                    calculo,
                    cancellationToken);

            var requerimientos = new List<AnalisisReporteRequerimiento>();

            foreach (var fila in filas)
            {
                decimal? rangoMinimoLbMz = null;
                decimal? rangoMaximoLbMz = null;
                decimal? requerimiento = fila.requerimientoCalculado;
                string observacion = fila.observacion ?? string.Empty;

                try
                {
                    if (unidadKgHa != null &&
                        materiaOrganicaPorcentaje.HasValue &&
                        extracciones.TryGetValue(
                            fila.elementoQuimicosId,
                            out ParametroExtraccionNutrienteCafe? extraccion) &&
                        rangos.TryGetValue(
                            fila.elementoQuimicosId,
                            out ParametroRangoNutrienteCultivo? rango))
                    {
                        ResultadoConversionUnidad minimo =
                            await _unidadConversionService
                                .ConvertirElementoALbMzAsync(
                                    fila.elementoQuimicosId,
                                    unidadKgHa.unidadMedidaId,
                                    rango.valorMinimo,
                                    materiaOrganicaPorcentaje.Value,
                                    cancellationToken);

                        ResultadoConversionUnidad maximo =
                            await _unidadConversionService
                                .ConvertirElementoALbMzAsync(
                                    fila.elementoQuimicosId,
                                    unidadKgHa.unidadMedidaId,
                                    rango.valorMaximo,
                                    materiaOrganicaPorcentaje.Value,
                                    cancellationToken);

                        rangoMinimoLbMz = minimo.valorConvertido;
                        rangoMaximoLbMz = maximo.valorConvertido;

                        decimal extraccionProduccion =
                            Math.Round(
                                calculo.cantidadQuintalesOro *
                                extraccion.cantidadExtraidaPorQQOro,
                                4);

                        requerimiento = Math.Round(
                            rangoMaximoLbMz.Value +
                            extraccionProduccion,
                            4);

                        observacion = CrearObservacionRequerimiento(
                            fila.simboloElementoQuimico,
                            fila.clasificacion,
                            fila.cantidadConvertidaLbMz,
                            rangoMinimoLbMz,
                            rangoMaximoLbMz,
                            requerimiento);
                    }
                }
                catch
                {
                    /*
                     * Un reporte histórico no debe dejar de generarse si una
                     * parametrización fue modificada o desactivada. En ese
                     * caso se conserva el valor almacenado originalmente.
                     */
                }

                requerimientos.Add(
                    new AnalisisReporteRequerimiento
                    {
                        Elemento = FormatearElemento(
                            fila.nombreElementoQuimico,
                            fila.simboloElementoQuimico),
                        CantidadIngresada = fila.cantidadIngresada,
                        CantidadConvertidaLbMz =
                            fila.cantidadConvertidaLbMz,
                        RequerimientoLbMz = requerimiento,
                        UnidadResultado = "lb/Mz",
                        Clasificacion =
                            fila.clasificacion ?? string.Empty,
                        Observacion = observacion
                    });
            }

            reporte.Requerimientos = requerimientos
                .OrderByDescending(x => x.RequerimientoLbMz ?? 0)
                .ThenBy(
                    x => x.Elemento,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private async Task<decimal?>
            ObtenerMateriaOrganicaPorcentajeAsync(
                AnalisisSueloCalculo calculo,
                CancellationToken cancellationToken)
        {
            if (!calculo.unidadMedidaMateriaOrganicaId.HasValue ||
                !calculo.materiaOrganica.HasValue ||
                calculo.materiaOrganica.Value <= 0)
            {
                return null;
            }

            try
            {
                return await _unidadConversionService
                    .ConvertirMateriaOrganicaAPorcentajeAsync(
                        calculo.materiaOrganica.Value,
                        calculo.unidadMedidaMateriaOrganicaId.Value,
                        cancellationToken);
            }
            catch
            {
                return null;
            }
        }

        private async Task<AnalisisReporteBalance?> CargarBalanceAsync(
            int analisisSueloCalculoId,
            CancellationToken cancellationToken)
        {
            var formula = await _db.formulaNutricional
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.analisisSueloCalculoId == analisisSueloCalculoId &&
                         x.activo,
                    cancellationToken);

            if (formula == null)
                return null;

            var filas = await (
                from detalle in _db.formulaNutricionalDetalle.AsNoTracking()
                join fuente in _db.fuenteNutriente.AsNoTracking()
                    on detalle.fuenteNutrientesId equals fuente.fuenteNutrientesId
                join elemento in _db.elementoQuimico.AsNoTracking()
                    on detalle.elementoQuimicosId equals elemento.elementoQuimicosId
                where detalle.formulaNutricionalId == formula.formulaNutricionalId &&
                      detalle.activo
                orderby fuente.nombreNutriente
                select new
                {
                    detalle.formulaNutricionalDetalleId,
                    detalle.fuenteNutrientesId,
                    detalle.elementoQuimicosId,
                    fuente.nombreNutriente,
                    elemento.nombreElementoQuimico,
                    elemento.simboloElementoQuimico,
                    detalle.requerimientoLibras,
                    detalle.libras,
                    detalle.qq,
                    detalle.precioPorQuintal,
                    detalle.subtotalFuente,
                    detalle.onzasAnuales,
                    detalle.onzasPorAplicacion
                }).ToListAsync(cancellationToken);

            List<int> detalleIds = filas
                .Select(x => x.formulaNutricionalDetalleId)
                .ToList();

            var aportesGuardados = detalleIds.Count == 0
                ? new List<AporteFormulaReporteFila>()
                : await (
                    from aporte in _db.formulaNutricionalAporte.AsNoTracking()
                    join elemento in _db.elementoQuimico.AsNoTracking()
                        on aporte.elementoQuimicosId equals elemento.elementoQuimicosId
                    where detalleIds.Contains(aporte.formulaNutricionalDetalleId) &&
                          aporte.activo
                    select new AporteFormulaReporteFila
                    {
                        FormulaNutricionalDetalleId =
                            aporte.formulaNutricionalDetalleId,
                        Simbolo = elemento.simboloElementoQuimico,
                        Valor = aporte.valor
                    }).ToListAsync(cancellationToken);

            List<AnalisisReporteBalanceDetalle> detalles = filas
                .Select(x =>
                {
                    decimal quintalesComprar = Math.Ceiling(x.qq);
                    Dictionary<string, decimal> aportes = aportesGuardados
                        .Where(a =>
                            a.FormulaNutricionalDetalleId ==
                            x.formulaNutricionalDetalleId)
                        .GroupBy(a => FormatearSimbolo(a.Simbolo))
                        .ToDictionary(
                            grupo => grupo.Key,
                            grupo => Math.Round(grupo.Sum(a => a.Valor), 4));

                    return new AnalisisReporteBalanceDetalle
                    {
                        FormulaNutricionalDetalleId =
                            x.formulaNutricionalDetalleId,
                        FuenteNutrientesId = x.fuenteNutrientesId,
                        ElementoQuimicosId = x.elementoQuimicosId,
                        Fuente = x.nombreNutriente,
                        Elemento = FormatearElemento(
                            x.nombreElementoQuimico,
                            x.simboloElementoQuimico),
                        RequerimientoLibras = x.requerimientoLibras,
                        Libras = x.libras,
                        LibrasPorAplicacion = formula.totalAplicaciones > 0
                            ? x.libras / formula.totalAplicaciones
                            : 0,
                        QuintalesExactos = x.qq,
                        QuintalesComprar = quintalesComprar,
                        PrecioPorQuintal = x.precioPorQuintal,
                        SubtotalExacto = x.subtotalFuente,
                        CostoCompra = quintalesComprar * x.precioPorQuintal,
                        OnzasAnuales = x.onzasAnuales,
                        OnzasPorAplicacion = x.onzasPorAplicacion,
                        Aportes = aportes
                    };
                })
                .ToList();

            Dictionary<string, decimal> formulaComercial = new();
            if (detalleIds.Count > 0)
            {
                formulaComercial = aportesGuardados
                    .GroupBy(x => FormatearSimbolo(x.Simbolo))
                    .ToDictionary(
                        grupo => grupo.Key,
                        grupo => formula.mezclaTotalQq > 0
                            ? Math.Round(
                                grupo.Sum(x => x.Valor) /
                                formula.mezclaTotalQq,
                                4)
                            : 0);
            }

            decimal costoRealCompra = detalles.Sum(x => x.CostoCompra);

            return new AnalisisReporteBalance
            {
                NombreFormula = formula.nombreFormula,
                TotalLibras = formula.totalLibras,
                MezclaTotalQq = formula.mezclaTotalQq,
                TotalOnzas = formula.totalOnzas,
                TotalPlantas = formula.totalPlantas,
                TotalAplicaciones = formula.totalAplicaciones,
                DosisPlantaAnualOz = formula.dosisPlantaAnualOz,
                DosisPlantaPorAplicacionOz = formula.dosisPlantaPorAplicacionOz,
                PrecioExactoReferencia = detalles.Sum(x => x.SubtotalExacto),
                CostoRealCompra = costoRealCompra,
                PrecioPorAplicacion = formula.totalAplicaciones > 0
                    ? costoRealCompra / formula.totalAplicaciones
                    : 0,
                FormulaComercial = formulaComercial,
                Detalles = detalles
            };
        }

        private async Task<AnalisisReporteEnmienda?> CargarEnmiendaAsync(
            int analisisSueloCalculoId,
            CancellationToken cancellationToken)
        {
            var enmienda = await _db.enmiendaCalcarea
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.analisisSueloCalculoId == analisisSueloCalculoId &&
                         x.activo,
                    cancellationToken);

            if (enmienda == null)
                return null;

            string fuente = await _db.fuenteNutriente
                .AsNoTracking()
                .Where(x => x.fuenteNutrientesId == enmienda.fuenteNutrientesId)
                .Select(x => x.nombreNutriente)
                .FirstOrDefaultAsync(cancellationToken) ??
                $"Fuente #{enmienda.fuenteNutrientesId}";

            return new AnalisisReporteEnmienda
            {
                NombreAnalisis = enmienda.nombreAnalisis,
                Fuente = fuente,
                TotalPlantas = enmienda.totalPlantas,
                TotalAplicaciones = enmienda.totalAplicaciones,
                Ph = enmienda.ph,
                Calcio = enmienda.ca,
                Magnesio = enmienda.mg,
                Potasio = enmienda.k,
                AcidezTotal = enmienda.acidezTotal,
                Cice = enmienda.cice,
                SaturacionActual = enmienda.saturacionActual,
                SaturacionDeseada = enmienda.saturacionDeseada,
                Prnt = enmienda.prnt,
                NecesidadEncaladoTonHa = enmienda.necesidadEncaladoTonHa,
                NecesidadEncaladoKgHa = enmienda.necesidadEncaladoKgHa,
                NecesidadEncaladoLbHa = enmienda.necesidadEncaladoLbHa,
                NecesidadEncaladoLbMz = enmienda.necesidadEncaladoLbMz,
                DosisPlantaAnualOz = enmienda.dosisPlantaAnualOz,
                DosisPlantaPorAplicacionOz = enmienda.dosisPlantaPorAplicacionOz
            };
        }

        private async Task<AnalisisReporteFertilizacionMixta?> CargarMixtaAsync(
            int analisisSueloCalculoId,
            AnalisisReporteBalance? balance,
            CancellationToken cancellationToken)
        {
            var mixta = await _db.fertilizacionMixta
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.analisisSueloCalculoId == analisisSueloCalculoId &&
                         x.activo,
                    cancellationToken);

            if (mixta == null)
                return null;

            var filasFuentes = await (
                from item in _db.fertilizacionMixtaFuente.AsNoTracking()
                join fuente in _db.fuenteNutriente.AsNoTracking()
                    on item.fuenteNutrientesId equals fuente.fuenteNutrientesId
                where item.fertilizacionMixtaId == mixta.fertilizacionMixtaId &&
                      item.activo
                orderby fuente.nombreNutriente
                select new
                {
                    item.fuenteNutrientesId,
                    Fuente = fuente.nombreNutriente,
                    item.cantidadQq,
                    PrecioPorQq = fuente.precioNutriente
                }).ToListAsync(cancellationToken);

            List<AnalisisReporteMixtaFuente> fuentes = filasFuentes
                .Select(x => new AnalisisReporteMixtaFuente
                {
                    FuenteNutrientesId = x.fuenteNutrientesId,
                    Fuente = x.Fuente,
                    CantidadQq = x.cantidadQq,
                    PrecioPorQq = x.PrecioPorQq,
                    Costo = x.cantidadQq * x.PrecioPorQq
                })
                .ToList();

            var filas = await (
                from item in _db.fertilizacionMixtaDetalle.AsNoTracking()
                join elemento in _db.elementoQuimico.AsNoTracking()
                    on item.elementoQuimicosId equals elemento.elementoQuimicosId
                where item.fertilizacionMixtaId == mixta.fertilizacionMixtaId &&
                      item.activo
                orderby elemento.nombreElementoQuimico
                select new
                {
                    item.elementoQuimicosId,
                    elemento.nombreElementoQuimico,
                    elemento.simboloElementoQuimico,
                    item.requerimientoOriginal,
                    item.aporteOrganico,
                    item.diferencia,
                    item.deficit,
                    item.sobrante
                }).ToListAsync(cancellationToken);

            List<AnalisisReporteMixtaDetalle> detalles = filas
                .Select(x => new AnalisisReporteMixtaDetalle
                {
                    ElementoQuimicosId = x.elementoQuimicosId,
                    Elemento = FormatearElemento(
                        x.nombreElementoQuimico,
                        x.simboloElementoQuimico),
                    RequerimientoOriginal = x.requerimientoOriginal,
                    AporteOrganico = x.aporteOrganico,
                    Diferencia = x.diferencia,
                    Deficit = x.deficit,
                    Sobrante = x.sobrante
                })
                .ToList();

            List<int> fuentesIds = fuentes
                .Select(x => x.FuenteNutrientesId)
                .Distinct()
                .ToList();

            List<int> elementosIds = detalles
                .Select(x => x.ElementoQuimicosId)
                .Distinct()
                .ToList();

            var composiciones = fuentesIds.Count == 0 || elementosIds.Count == 0
                ? new List<AporteMixtaReporteFila>()
                : await (
                    from aporte in _db.fuenteNutrienteElementoQuimico.AsNoTracking()
                    join fuente in _db.fuenteNutriente.AsNoTracking()
                        on aporte.fuenteNutrientesId equals fuente.fuenteNutrientesId
                    join elemento in _db.elementoQuimico.AsNoTracking()
                        on aporte.elementoQuimicosId equals elemento.elementoQuimicosId
                    where fuentesIds.Contains(aporte.fuenteNutrientesId) &&
                          elementosIds.Contains(aporte.elementoQuimicosId) &&
                          aporte.activo
                    select new AporteMixtaReporteFila
                    {
                        FuenteNutrientesId = aporte.fuenteNutrientesId,
                        ElementoQuimicosId = aporte.elementoQuimicosId,
                        Fuente = fuente.nombreNutriente,
                        Elemento = elemento.simboloElementoQuimico,
                        AportePorQq = aporte.cantidadAporte
                    }).ToListAsync(cancellationToken);

            Dictionary<int, AnalisisReporteMixtaFuente> fuentePorId = fuentes
                .ToDictionary(x => x.FuenteNutrientesId);

            List<AnalisisReporteMixtaAporteFuente> aportesPorFuente = composiciones
                .Select(x =>
                {
                    AnalisisReporteMixtaFuente fuente =
                        fuentePorId[x.FuenteNutrientesId];

                    return new AnalisisReporteMixtaAporteFuente
                    {
                        FuenteNutrientesId = x.FuenteNutrientesId,
                        ElementoQuimicosId = x.ElementoQuimicosId,
                        Fuente = x.Fuente,
                        Elemento = FormatearSimbolo(x.Elemento),
                        CantidadQq = fuente.CantidadQq,
                        AportePorQq = x.AportePorQq,
                        AporteTotal = fuente.CantidadQq * x.AportePorQq
                    };
                })
                .OrderBy(x => x.Fuente)
                .ThenBy(x => x.Elemento)
                .ToList();

            AnalisisReporteBalanceAjustado? balanceAjustado = null;
            AnalisisReporteResumenEconomico? resumenEconomico = null;

            if (mixta.esComplementoBalance && balance != null)
            {
                balanceAjustado = ConstruirBalanceAjustado(
                    balance,
                    detalles);

                decimal costoMixta = fuentes.Sum(x => x.Costo);
                decimal costoTotal =
                    costoMixta + balanceAjustado.CostoRealCompra;

                resumenEconomico = new AnalisisReporteResumenEconomico
                {
                    CostoComercialOriginal = balance.CostoRealCompra,
                    CostoFertilizacionMixta = costoMixta,
                    CostoComercialAjustado =
                        balanceAjustado.CostoRealCompra,
                    CostoTotalFinal = costoTotal,
                    DiferenciaEconomica =
                        balance.CostoRealCompra - costoTotal
                };
            }

            return new AnalisisReporteFertilizacionMixta
            {
                Observacion = mixta.observacion ?? string.Empty,
                EsComplementoBalance = mixta.esComplementoBalance,
                Fuentes = fuentes,
                Detalles = detalles,
                AportesPorFuente = aportesPorFuente,
                BalanceAjustado = balanceAjustado,
                ResumenEconomico = resumenEconomico
            };
        }

        private static AnalisisReporteBalanceAjustado ConstruirBalanceAjustado(
            AnalisisReporteBalance balance,
            IReadOnlyCollection<AnalisisReporteMixtaDetalle> detallesMixta)
        {
            List<AnalisisReporteCompraAjustada> detalles = balance.Detalles
                .Select(original =>
                {
                    AnalisisReporteMixtaDetalle? mixta = detallesMixta
                        .FirstOrDefault(x =>
                            x.ElementoQuimicosId ==
                            original.ElementoQuimicosId);

                    decimal aporteOrganico = mixta?.AporteOrganico ?? 0;
                    decimal requerimientoAjustado = Math.Max(
                        original.RequerimientoLibras - aporteOrganico,
                        0);
                    decimal quintalesAjustados =
                        requerimientoAjustado / 100m;
                    decimal factor = original.QuintalesExactos > 0
                        ? quintalesAjustados / original.QuintalesExactos
                        : 0;
                    decimal quintalesComprar =
                        Math.Ceiling(quintalesAjustados);

                    Dictionary<string, decimal> aportes = original.Aportes
                        .ToDictionary(
                            x => x.Key,
                            x => Math.Round(x.Value * factor, 4));

                    return new AnalisisReporteCompraAjustada
                    {
                        FuenteNutrientesId = original.FuenteNutrientesId,
                        ElementoQuimicosId = original.ElementoQuimicosId,
                        Fuente = original.Fuente,
                        Elemento = original.Elemento,
                        RequerimientoOriginalLb =
                            original.RequerimientoLibras,
                        AporteOrganicoLb = aporteOrganico,
                        RequerimientoAjustadoLb = requerimientoAjustado,
                        QuintalesOriginales = original.QuintalesExactos,
                        QuintalesAjustados = quintalesAjustados,
                        ReduccionQuintales = Math.Max(
                            original.QuintalesExactos - quintalesAjustados,
                            0),
                        PrecioPorQq = original.PrecioPorQuintal,
                        QuintalesComprar = quintalesComprar,
                        SubtotalExacto =
                            quintalesAjustados * original.PrecioPorQuintal,
                        CostoCompra =
                            quintalesComprar * original.PrecioPorQuintal,
                        Aportes = aportes
                    };
                })
                .ToList();

            decimal totalLibras =
                detalles.Sum(x => x.RequerimientoAjustadoLb);
            decimal mezclaTotalQq = totalLibras / 100m;
            decimal totalOnzas = totalLibras * 16m;
            decimal costoReal = detalles.Sum(x => x.CostoCompra);

            Dictionary<string, decimal> formulaComercial = detalles
                .SelectMany(x => x.Aportes)
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    grupo => grupo.Key,
                    grupo => mezclaTotalQq > 0
                        ? Math.Round(
                            grupo.Sum(x => x.Value) / mezclaTotalQq,
                            4)
                        : 0,
                    StringComparer.OrdinalIgnoreCase);

            return new AnalisisReporteBalanceAjustado
            {
                NombreFormula = $"{balance.NombreFormula} - Ajustado",
                TotalLibras = totalLibras,
                MezclaTotalQq = mezclaTotalQq,
                TotalOnzas = totalOnzas,
                TotalPlantas = balance.TotalPlantas,
                TotalAplicaciones = balance.TotalAplicaciones,
                DosisPlantaAnualOz = balance.TotalPlantas > 0
                    ? totalOnzas / balance.TotalPlantas
                    : 0,
                DosisPlantaPorAplicacionOz =
                    balance.TotalPlantas > 0 &&
                    balance.TotalAplicaciones > 0
                        ? totalOnzas /
                          balance.TotalPlantas /
                          balance.TotalAplicaciones
                        : 0,
                PrecioExactoReferencia =
                    detalles.Sum(x => x.SubtotalExacto),
                CostoRealCompra = costoReal,
                PrecioPorAplicacion = balance.TotalAplicaciones > 0
                    ? costoReal / balance.TotalAplicaciones
                    : 0,
                FormulaComercial = formulaComercial,
                Detalles = detalles
            };
        }

        private static string CrearObservacionRequerimiento(
            string? simbolo,
            string? clasificacion,
            decimal? cantidadConvertidaLbMz,
            decimal? rangoMinimoLbMz,
            decimal? rangoMaximoLbMz,
            decimal? requerimientoCalculado)
        {
            if (!cantidadConvertidaLbMz.HasValue ||
                !rangoMinimoLbMz.HasValue ||
                !rangoMaximoLbMz.HasValue ||
                !requerimientoCalculado.HasValue)
            {
                return string.Empty;
            }

            return
                $"Elemento {(simbolo ?? string.Empty).Trim()}: " +
                $"clasificación {(clasificacion ?? string.Empty).Trim()}. " +
                $"Cantidad convertida: {F(cantidadConvertidaLbMz.Value)} lb/Mz. " +
                $"Rango de referencia: {F(rangoMinimoLbMz.Value)} - " +
                $"{F(rangoMaximoLbMz.Value)} lb/Mz. " +
                $"Requerimiento anual calculado: " +
                $"{F(requerimientoCalculado.Value)} lb/Mz.";
        }

        private static string F(decimal valor) =>
            valor.ToString(
                "0.####",
                CultureInfo.InvariantCulture);

        private static string FormatearElemento(string? nombre, string? simbolo)
        {
            string nombreLimpio = nombre?.Trim() ?? string.Empty;
            string simboloLimpio = simbolo?.Trim() ?? string.Empty;

            if (nombreLimpio.Length > 0 && simboloLimpio.Length > 0)
                return $"{nombreLimpio} ({simboloLimpio})";

            return nombreLimpio.Length > 0 ? nombreLimpio : simboloLimpio;
        }

        private static string FormatearSimbolo(string? simbolo)
        {
            string normalizado = (simbolo ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace(" ", string.Empty);

            return normalizado switch
            {
                "CA" => "Ca",
                "MG" => "Mg",
                "ZN" => "Zn",
                _ => normalizado.Length > 0 ? normalizado : "Nutriente"
            };
        }

        private static string FormatearTerreno(
            string? codigo,
            string? direccion,
            int terrenoId)
        {
            string codigoLimpio = codigo?.Trim() ?? string.Empty;
            string direccionLimpia = direccion?.Trim() ?? string.Empty;

            if (codigoLimpio.Length > 0 && direccionLimpia.Length > 0)
                return $"{codigoLimpio} · {direccionLimpia}";

            if (codigoLimpio.Length > 0)
                return codigoLimpio;

            if (direccionLimpia.Length > 0)
                return direccionLimpia;

            return $"Terreno #{terrenoId}";
        }

        private static List<string> SepararObservaciones(string? observacion) =>
            string.IsNullOrWhiteSpace(observacion)
                ? new List<string>()
                : observacion
                    .Split(" | ", StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();

        private sealed class AporteFormulaReporteFila
        {
            public int FormulaNutricionalDetalleId { get; set; }
            public string Simbolo { get; set; } = string.Empty;
            public decimal Valor { get; set; }
        }

        private sealed class AporteMixtaReporteFila
        {
            public int FuenteNutrientesId { get; set; }
            public int ElementoQuimicosId { get; set; }
            public string Fuente { get; set; } = string.Empty;
            public string Elemento { get; set; } = string.Empty;
            public decimal AportePorQq { get; set; }
        }
    }
}