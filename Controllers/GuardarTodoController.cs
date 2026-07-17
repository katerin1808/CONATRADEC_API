using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/guardar-todo")]
    public class GuardarTodoController : ControllerBase
    {
        private readonly DBContext _db;

        public GuardarTodoController(DBContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> GuardarTodo(
            [FromBody] GuardarTodoDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var errorValidacion = ValidarSolicitud(dto);

            if (errorValidacion != null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = errorValidacion
                });
            }

            await using var transaccion =
                await _db.Database.BeginTransactionAsync();

            try
            {
                var datosAnalisis = dto.datosAnalisis;
                var resultadoAnual = dto.requerimientoAnual;
                var resultadoFormula = dto.balanceNutricional.resultado;
                var resultadoEnmienda = dto.enmiendaCalcarea.resultado;
                var resultadoMixta = dto.fertilizacionMixta;

                string identificador =
                    datosAnalisis.identificadorAnalisisSuelo
                        .Trim()
                        .ToUpperInvariant();

                bool identificadorExiste =
                    await _db.AnalisisSuelos.AnyAsync(x =>
                        x.identificadorAnalisisSuelo == identificador);

                if (identificadorExiste)
                {
                    await transaccion.RollbackAsync();

                    return Conflict(new
                    {
                        success = false,
                        message =
                            "Ya existe un análisis de suelo con ese identificador."
                    });
                }

                // =====================================================
                // 1. ANÁLISIS DE SUELO ORIGINAL
                // =====================================================

                var analisisSuelo = new AnalisisSuelo
                {
                    fechaAnalisisSuelo =
                        datosAnalisis.fechaAnalisisSuelo,

                    fechaCreacionAnalisisSuelo =
                        DateTime.Now,

                    laboratorioAnalasisSuelo =
                        datosAnalisis.laboratorioAnalasisSuelo
                            .Trim()
                            .ToUpperInvariant(),

                    identificadorAnalisisSuelo =
                        identificador,

                    activo = true
                };

                _db.AnalisisSuelos.Add(analisisSuelo);
                await _db.SaveChangesAsync();

                var elementosIngresados =
                    datosAnalisis.elementosQuimicos.Select(elemento =>
                        new AnalisisSueloElementoQuimico
                        {
                            analisisSueloId =
                                analisisSuelo.analisisSueloId,

                            elementoQuimicosId =
                                elemento.elementoQuimicosId,

                            unidadMedidaId =
                                elemento.unidadMedidaId,

                            cantidadElemento =
                                Math.Round(
                                    elemento.cantidadElemento,
                                    4),

                            activo = true
                        })
                        .ToList();

                _db.AnalisisSueloElementos.AddRange(
                    elementosIngresados);

                await _db.SaveChangesAsync();

                // =====================================================
                // 2. RESULTADO DEL REQUERIMIENTO ANUAL
                // =====================================================

                var analisisSueloCalculo =
                    new AnalisisSueloCalculo
                    {
                        cantidadQuintalesOro =
                            Math.Round(
                                resultadoAnual.cantidadQuintalesOro,
                                4),

                        tamanoFinca =
                            Math.Round(
                                resultadoAnual.tamanoFinca,
                                4),

                        phAnalisisSuelo =
                            Math.Round(resultadoAnual.ph, 4),

                        materiaOrganica =
                            Math.Round(
                                resultadoAnual.materiaOrganica,
                                4),

                        acidezTotal =
                            resultadoAnual.acidezTotal.HasValue
                                ? Math.Round(
                                    resultadoAnual.acidezTotal.Value,
                                    4)
                                : 0,

                        recomendacionGeneral =
                            resultadoAnual.recomendacionGeneral ??
                            string.Empty,

                        observacion =
                            string.Join(
                                " | ",
                                resultadoAnual.observaciones ??
                                new List<string>()),

                        fechaCalculo = DateTime.Now,
                        activo = true,

                        analisisSueloId =
                            analisisSuelo.analisisSueloId,

                        terrenoId =
                            resultadoAnual.terrenoId,

                        tipoCultivoId =
                            resultadoAnual.tipoCultivoId,

                        tipoAnalisisSueloId =
                            resultadoAnual.tipoAnalisisSueloId,

                        usuarioId =
                            datosAnalisis.usuarioId,

                        unidadMedidaMateriaOrganicaId =
                            resultadoAnual
                                .unidadMedidaMateriaOrganicaId
                    };

                _db.AnalisisSueloCalculos.Add(
                    analisisSueloCalculo);

                await _db.SaveChangesAsync();

                var elementosCalculados =
                    resultadoAnual.elementos.Select(elemento =>
                        new AnalisisSueloCalculoElementoQuimico
                        {
                            analisisSueloCalculoId =
                                analisisSueloCalculo
                                    .analisisSueloCalculoId,

                            elementoQuimicosId =
                                elemento.elementoQuimicosId,

                            unidadMedidaId =
                                elemento.unidadMedidaResultadoId,

                            cantidadIngresada =
                                Math.Round(
                                    elemento.cantidadIngresada,
                                    4),

                            cantidadConvertidaLbMz =
                                RedondearNullable(
                                    elemento.cantidadConvertidaLbMz),

                            requerimientoCalculado =
                                RedondearNullable(
                                    elemento.requerimientoCalculado),

                            clasificacion =
                                elemento.clasificacion ??
                                string.Empty,

                            observacion =
                                elemento.observacion ??
                                string.Empty,

                            activo = true
                        })
                        .ToList();

                _db.AnalisisSueloCalculoElementoQuimicos
                    .AddRange(elementosCalculados);

                await _db.SaveChangesAsync();

                // =====================================================
                // 3. BALANCE / FÓRMULA NUTRICIONAL
                // =====================================================

                var formula = new FormulaNutricional
                {
                    analisisSueloCalculoId =
                        analisisSueloCalculo
                            .analisisSueloCalculoId,

                    nombreFormula =
                        resultadoFormula.nombreFormula?
                            .Trim() ?? string.Empty,

                    fechaCreacion = DateTime.Now,

                    totalLibras =
                        Math.Round(resultadoFormula.totalLibras, 4),

                    mezclaTotalQq =
                        Math.Round(resultadoFormula.mezclaTotalQq, 4),

                    totalPlantas =
                        resultadoFormula.totalPlantas,

                    totalAplicaciones =
                        resultadoFormula.totalAplicaciones,

                    totalOnzas =
                        Math.Round(resultadoFormula.totalOnzas, 4),

                    precioTotalFormula =
                        Math.Round(
                            resultadoFormula.precioTotalFormula,
                            4),

                    precioPorAplicacion =
                        Math.Round(
                            resultadoFormula.precioPorAplicacion,
                            4),

                    dosisPlantaAnualOz =
                        Math.Round(
                            resultadoFormula.dosisPlantaAnualOz,
                            4),

                    dosisPlantaPorAplicacionOz =
                        Math.Round(
                            resultadoFormula
                                .dosisPlantaPorAplicacionOz,
                            4),

                    terrenoId =
                        dto.balanceNutricional.terrenoId,

                    activo = true
                };

                _db.formulaNutricional.Add(formula);
                await _db.SaveChangesAsync();

                var elementosActivos =
                    await _db.elementoQuimico
                        .Where(x => x.activo)
                        .ToListAsync();

                var elementosPorSimbolo = elementosActivos
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(
                            x.simboloElementoQuimico))
                    .GroupBy(x =>
                        NormalizarSimbolo(
                            x.simboloElementoQuimico))
                    .ToDictionary(
                        grupo => grupo.Key,
                        grupo => grupo.First()
                            .elementoQuimicosId);

                for (int indice = 0;
                     indice < dto.balanceNutricional.items.Count;
                     indice++)
                {
                    var item =
                        dto.balanceNutricional.items[indice];

                    var detalleResultado =
                        resultadoFormula.detalle[indice];

                    var formulaDetalle =
                        new FormulaNutricionalDetalle
                        {
                            formulaNutricionalId =
                                formula.formulaNutricionalId,

                            fuenteNutrientesId =
                                item.fuenteNutrientesId,

                            elementoQuimicosId =
                                item.elementoQuimicosId,

                            libras =
                                Math.Round(detalleResultado.lb, 4),

                            qq =
                                Math.Round(detalleResultado.qq, 4),

                            requerimientoLibras =
                                Math.Round(
                                    detalleResultado
                                        .requerimientoLibras,
                                    4),

                            precioPorQuintal =
                                Math.Round(
                                    detalleResultado
                                        .precioPorQuintal,
                                    4),

                            subtotalFuente =
                                Math.Round(
                                    detalleResultado
                                        .subtotalFuente,
                                    4),

                            onzasAnuales =
                                Math.Round(
                                    detalleResultado
                                        .onzasAnuales,
                                    4),

                            onzasPorAplicacion =
                                Math.Round(
                                    detalleResultado
                                        .onzasPorAplicacion,
                                    4),

                            activo = true
                        };

                    _db.formulaNutricionalDetalle.Add(
                        formulaDetalle);

                    await _db.SaveChangesAsync();

                    foreach (var aporte in
                             detalleResultado.aportes)
                    {
                        string simbolo =
                            NormalizarSimbolo(aporte.Key);

                        if (!elementosPorSimbolo.TryGetValue(
                                simbolo,
                                out int elementoAporteId))
                        {
                            throw new InvalidOperationException(
                                $"No se encontró el elemento químico del aporte '{aporte.Key}'.");
                        }

                        _db.formulaNutricionalAporte.Add(
                            new FormulaNutricionalAporte
                            {
                                formulaNutricionalDetalleId =
                                    formulaDetalle
                                        .formulaNutricionalDetalleId,

                                elementoQuimicosId =
                                    elementoAporteId,

                                valor =
                                    Math.Round(aporte.Value, 4),

                                activo = true
                            });
                    }

                    await _db.SaveChangesAsync();
                }

                // =====================================================
                // 4. ENMIENDA CALCÁREA
                // =====================================================

                var enmienda = new EnmiendaCalcarea
                {
                    analisisSueloCalculoId =
                        analisisSueloCalculo
                            .analisisSueloCalculoId,

                    nombreAnalisis =
                        resultadoEnmienda.nombreAnalisis?
                            .Trim() ?? string.Empty,

                    fuenteNutrientesId =
                        dto.enmiendaCalcarea
                            .fuenteNutrientesId,

                    terrenoId =
                        resultadoEnmienda.terrenoId,

                    totalPlantas =
                        resultadoEnmienda.totalPlantas,

                    totalAplicaciones =
                        resultadoEnmienda.totalAplicaciones,

                    ph = Math.Round(resultadoEnmienda.ph, 4),
                    ca = Math.Round(resultadoEnmienda.ca, 4),
                    mg = Math.Round(resultadoEnmienda.mg, 4),
                    k = Math.Round(resultadoEnmienda.k, 4),

                    acidezTotal =
                        Math.Round(
                            resultadoEnmienda.acidezTotal,
                            4),

                    saturacionDeseada =
                        Math.Round(
                            resultadoEnmienda.saturacionDeseada,
                            4),

                    prnt =
                        Math.Round(resultadoEnmienda.prnt, 4),

                    sumaBases =
                        Math.Round(
                            resultadoEnmienda.sumaBases,
                            4),

                    cice =
                        Math.Round(resultadoEnmienda.cice, 4),

                    saturacionActual =
                        Math.Round(
                            resultadoEnmienda.saturacionActual,
                            4),

                    necesidadEncaladoTonHa =
                        Math.Round(
                            resultadoEnmienda
                                .necesidadEncaladoTonHa,
                            4),

                    necesidadEncaladoKgHa =
                        Math.Round(
                            resultadoEnmienda
                                .necesidadEncaladoKgHa,
                            4),

                    necesidadEncaladoLbHa =
                        Math.Round(
                            resultadoEnmienda
                                .necesidadEncaladoLbHa,
                            4),

                    necesidadEncaladoLbMz =
                        Math.Round(
                            resultadoEnmienda
                                .necesidadEncaladoLbMz,
                            4),

                    necesidadEncaladoOzMz =
                        Math.Round(
                            resultadoEnmienda
                                .necesidadEncaladoOzMz,
                            4),

                    dosisPlantaAnualOz =
                        Math.Round(
                            resultadoEnmienda
                                .dosisPlantaAnualOz,
                            4),

                    dosisPlantaPorAplicacionOz =
                        Math.Round(
                            resultadoEnmienda
                                .dosisPlantaPorAplicacionOz,
                            4),

                    fechaCreacion = DateTime.Now,
                    activo = true
                };

                _db.enmiendaCalcarea.Add(enmienda);
                await _db.SaveChangesAsync();

                // =====================================================
                // 5. FERTILIZACIÓN MIXTA
                // =====================================================

                var fertilizacionMixta =
                    new FertilizacionMixta
                    {
                        analisisSueloCalculoId =
                            analisisSueloCalculo
                                .analisisSueloCalculoId,

                        fechaCalculo = DateTime.Now,

                        observacion =
                            resultadoMixta.observacion ??
                            string.Empty,

                        activo = true
                    };

                _db.fertilizacionMixta.Add(
                    fertilizacionMixta);

                await _db.SaveChangesAsync();

                var fuentesMixtas =
                    resultadoMixta.fuentes.Select(fuente =>
                        new FertilizacionMixtaFuente
                        {
                            fertilizacionMixtaId =
                                fertilizacionMixta
                                    .fertilizacionMixtaId,

                            fuenteNutrientesId =
                                fuente.fuenteNutrientesId,

                            cantidadQq =
                                Math.Round(fuente.cantidadQq, 4),

                            activo = true
                        })
                        .ToList();

                _db.fertilizacionMixtaFuente.AddRange(
                    fuentesMixtas);

                var detallesMixtos =
                    resultadoMixta.detalles.Select(detalle =>
                        new FertilizacionMixtaDetalle
                        {
                            fertilizacionMixtaId =
                                fertilizacionMixta
                                    .fertilizacionMixtaId,

                            elementoQuimicosId =
                                detalle.elementoQuimicosId,

                            requerimientoOriginal =
                                Math.Round(detalle.exportable, 4),

                            aporteOrganico =
                                Math.Round(
                                    detalle.aporteOrganico,
                                    4),

                            diferencia =
                                Math.Round(detalle.diferencia, 4),

                            deficit =
                                Math.Round(detalle.deficit, 4),

                            sobrante =
                                Math.Round(detalle.sobrante, 4),

                            activo = true
                        })
                        .ToList();

                _db.fertilizacionMixtaDetalle.AddRange(
                    detallesMixtos);

                await _db.SaveChangesAsync();
                await transaccion.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message =
                        "El análisis completo fue guardado correctamente.",
                    data = new GuardarTodoRespuestaDto
                    {
                        analisisSueloId =
                            analisisSuelo.analisisSueloId,

                        analisisSueloCalculoId =
                            analisisSueloCalculo
                                .analisisSueloCalculoId,

                        formulaNutricionalId =
                            formula.formulaNutricionalId,

                        enmiendaCalcareaId =
                            enmienda.enmiendaCalcareaId,

                        fertilizacionMixtaId =
                            fertilizacionMixta
                                .fertilizacionMixtaId
                    }
                });
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync();

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error al guardar el análisis completo.",
                    detail = ex.Message,
                    inner =
                        ex.InnerException?.Message ??
                        string.Empty
                });
            }
        }

        private static string? ValidarSolicitud(
            GuardarTodoDto dto)
        {
            if (dto.datosAnalisis == null)
                return "Debe enviar los datos del análisis.";

            if (string.IsNullOrWhiteSpace(
                    dto.datosAnalisis
                        .identificadorAnalisisSuelo))
                return "El identificador del análisis es obligatorio.";

            if (string.IsNullOrWhiteSpace(
                    dto.datosAnalisis
                        .laboratorioAnalasisSuelo))
                return "El laboratorio del análisis es obligatorio.";

            if (dto.datosAnalisis.elementosQuimicos == null ||
                !dto.datosAnalisis.elementosQuimicos.Any())
                return "Debe enviar los elementos originales del análisis.";

            if (dto.requerimientoAnual == null)
                return "Debe enviar el resultado del requerimiento anual.";

            if (dto.requerimientoAnual.elementos == null ||
                !dto.requerimientoAnual.elementos.Any())
                return "El requerimiento anual no contiene elementos calculados.";

            if (dto.balanceNutricional == null ||
                dto.balanceNutricional.resultado == null)
                return "Debe enviar el resultado del balance nutricional.";

            if (dto.balanceNutricional.items == null ||
                !dto.balanceNutricional.items.Any())
                return "Debe enviar los IDs de los detalles de la fórmula nutricional.";

            if (dto.balanceNutricional.resultado.detalle == null ||
                !dto.balanceNutricional.resultado.detalle.Any())
                return "El balance nutricional no contiene detalles.";

            if (dto.balanceNutricional.items.Count !=
                dto.balanceNutricional.resultado.detalle.Count)
                return "La cantidad de items no coincide con los detalles calculados de la fórmula.";

            if (dto.enmiendaCalcarea == null ||
                dto.enmiendaCalcarea.resultado == null)
                return "Debe enviar el resultado de la enmienda calcárea.";

            if (dto.enmiendaCalcarea.fuenteNutrientesId <= 0)
                return "La fuente de la enmienda calcárea no es válida.";

            if (dto.fertilizacionMixta == null)
                return "Debe enviar el resultado de fertilización mixta.";

            if (dto.fertilizacionMixta.fuentes == null ||
                !dto.fertilizacionMixta.fuentes.Any())
                return "La fertilización mixta no contiene fuentes.";

            if (dto.fertilizacionMixta.detalles == null ||
                !dto.fertilizacionMixta.detalles.Any())
                return "La fertilización mixta no contiene detalles.";

            if (dto.requerimientoAnual.terrenoId !=
                dto.balanceNutricional.terrenoId)
                return "El terreno del requerimiento anual no coincide con el de la fórmula nutricional.";

            if (dto.enmiendaCalcarea.resultado.terrenoId.HasValue &&
                dto.enmiendaCalcarea.resultado.terrenoId.Value !=
                dto.requerimientoAnual.terrenoId)
                return "El terreno de la enmienda no coincide con el requerimiento anual.";

            return null;
        }

        private static decimal? RedondearNullable(
            decimal? valor)
        {
            return valor.HasValue
                ? Math.Round(valor.Value, 4)
                : 0;
        }

        private static string NormalizarSimbolo(
            string? simbolo)
        {
            return (simbolo ?? string.Empty)
                .Trim()
                .ToLowerInvariant();
        }
    }
}
