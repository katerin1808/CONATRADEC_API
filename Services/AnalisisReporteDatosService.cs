using CONATRADEC_API.Models;
using CONATRADEC_API.Reportes;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Services
{
    public sealed class AnalisisReporteDatosService
    {
        private readonly DBContext _db;

        public AnalisisReporteDatosService(DBContext db)
        {
            _db = db;
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
                Cliente = terreno?.nombrePropietarioTerreno ?? string.Empty,
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
                analisisSueloCalculoId,
                cancellationToken);

            reporte.Balance = await CargarBalanceAsync(
                analisisSueloCalculoId,
                cancellationToken);

            reporte.Enmienda = await CargarEnmiendaAsync(
                analisisSueloCalculoId,
                cancellationToken);

            reporte.FertilizacionMixta = await CargarMixtaAsync(
                analisisSueloCalculoId,
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
            int analisisSueloCalculoId,
            CancellationToken cancellationToken)
        {
            var filas = await (
                from valor in _db.AnalisisSueloCalculoElementoQuimicos.AsNoTracking()
                join elemento in _db.elementoQuimico.AsNoTracking()
                    on valor.elementoQuimicosId equals elemento.elementoQuimicosId
                join unidad in _db.UnidadMedidas.AsNoTracking()
                    on valor.unidadMedidaId equals (int?)unidad.unidadMedidaId
                    into unidades
                from unidad in unidades.DefaultIfEmpty()
                where valor.analisisSueloCalculoId == analisisSueloCalculoId &&
                      valor.activo
                orderby elemento.nombreElementoQuimico
                select new
                {
                    elemento.nombreElementoQuimico,
                    elemento.simboloElementoQuimico,
                    valor.cantidadIngresada,
                    valor.cantidadConvertidaLbMz,
                    valor.requerimientoCalculado,
                    Unidad = unidad == null ? string.Empty : unidad.nombreUnidadMedida,
                    valor.clasificacion,
                    valor.observacion
                }).ToListAsync(cancellationToken);

            reporte.Requerimientos = filas
                .Select(x => new AnalisisReporteRequerimiento
                {
                    Elemento = FormatearElemento(
                        x.nombreElementoQuimico,
                        x.simboloElementoQuimico),
                    CantidadIngresada = x.cantidadIngresada,
                    CantidadConvertidaLbMz = x.cantidadConvertidaLbMz,
                    RequerimientoLbMz = x.requerimientoCalculado,
                    UnidadResultado = string.IsNullOrWhiteSpace(x.Unidad)
                        ? "lb/mz"
                        : x.Unidad,
                    Clasificacion = x.clasificacion ?? string.Empty,
                    Observacion = x.observacion ?? string.Empty
                })
                .ToList();
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
                    fuente.nombreNutriente,
                    elemento.nombreElementoQuimico,
                    elemento.simboloElementoQuimico,
                    detalle.libras,
                    detalle.qq,
                    detalle.precioPorQuintal,
                    detalle.subtotalFuente,
                    detalle.onzasAnuales,
                    detalle.onzasPorAplicacion
                }).ToListAsync(cancellationToken);

            List<AnalisisReporteBalanceDetalle> detalles = filas
                .Select(x =>
                {
                    decimal quintalesComprar = Math.Ceiling(x.qq);

                    return new AnalisisReporteBalanceDetalle
                    {
                        Fuente = x.nombreNutriente,
                        Elemento = FormatearElemento(
                            x.nombreElementoQuimico,
                            x.simboloElementoQuimico),
                        Libras = x.libras,
                        QuintalesExactos = x.qq,
                        QuintalesComprar = quintalesComprar,
                        PrecioPorQuintal = x.precioPorQuintal,
                        SubtotalExacto = x.subtotalFuente,
                        CostoCompra = quintalesComprar * x.precioPorQuintal,
                        OnzasAnuales = x.onzasAnuales,
                        OnzasPorAplicacion = x.onzasPorAplicacion
                    };
                })
                .ToList();

            List<int> detalleIds = filas
                .Select(x => x.formulaNutricionalDetalleId)
                .ToList();

            Dictionary<string, decimal> formulaComercial = new();
            if (detalleIds.Count > 0)
            {
                var aportes = await (
                    from aporte in _db.formulaNutricionalAporte.AsNoTracking()
                    join elemento in _db.elementoQuimico.AsNoTracking()
                        on aporte.elementoQuimicosId equals elemento.elementoQuimicosId
                    where detalleIds.Contains(aporte.formulaNutricionalDetalleId) &&
                          aporte.activo
                    select new
                    {
                        elemento.simboloElementoQuimico,
                        aporte.valor
                    }).ToListAsync(cancellationToken);

                formulaComercial = aportes
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.simboloElementoQuimico)
                        ? "Nutriente"
                        : x.simboloElementoQuimico.Trim())
                    .ToDictionary(x => x.Key, x => x.Sum(y => y.valor));
            }

            return new AnalisisReporteBalance
            {
                NombreFormula = formula.nombreFormula,
                TotalLibras = formula.totalLibras,
                MezclaTotalQq = formula.mezclaTotalQq,
                TotalPlantas = formula.totalPlantas,
                TotalAplicaciones = formula.totalAplicaciones,
                DosisPlantaAnualOz = formula.dosisPlantaAnualOz,
                DosisPlantaPorAplicacionOz = formula.dosisPlantaPorAplicacionOz,
                PrecioExactoReferencia = detalles.Sum(x => x.SubtotalExacto),
                CostoRealCompra = formula.precioTotalFormula,
                PrecioPorAplicacion = formula.precioPorAplicacion,
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

            List<AnalisisReporteMixtaFuente> fuentes = await (
                from item in _db.fertilizacionMixtaFuente.AsNoTracking()
                join fuente in _db.fuenteNutriente.AsNoTracking()
                    on item.fuenteNutrientesId equals fuente.fuenteNutrientesId
                where item.fertilizacionMixtaId == mixta.fertilizacionMixtaId &&
                      item.activo
                orderby fuente.nombreNutriente
                select new AnalisisReporteMixtaFuente
                {
                    Fuente = fuente.nombreNutriente,
                    CantidadQq = item.cantidadQq
                }).ToListAsync(cancellationToken);

            var filas = await (
                from item in _db.fertilizacionMixtaDetalle.AsNoTracking()
                join elemento in _db.elementoQuimico.AsNoTracking()
                    on item.elementoQuimicosId equals elemento.elementoQuimicosId
                where item.fertilizacionMixtaId == mixta.fertilizacionMixtaId &&
                      item.activo
                orderby elemento.nombreElementoQuimico
                select new
                {
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

            return new AnalisisReporteFertilizacionMixta
            {
                Observacion = mixta.observacion ?? string.Empty,
                Fuentes = fuentes,
                Detalles = detalles
            };
        }

        private static string FormatearElemento(string? nombre, string? simbolo)
        {
            string nombreLimpio = nombre?.Trim() ?? string.Empty;
            string simboloLimpio = simbolo?.Trim() ?? string.Empty;

            if (nombreLimpio.Length > 0 && simboloLimpio.Length > 0)
                return $"{nombreLimpio} ({simboloLimpio})";

            return nombreLimpio.Length > 0 ? nombreLimpio : simboloLimpio;
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
    }
}
