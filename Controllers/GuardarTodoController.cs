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

                FormulaNutricional? formula = null;
                EnmiendaCalcarea? enmienda = null;
                FertilizacionMixta? fertilizacionMixta = null;

                // =====================================================
                // 3. BALANCE / FÓRMULA NUTRICIONAL - OPCIONAL
                // =====================================================

                if (dto.balanceNutricional != null)
                {
                    var resultadoFormula =
                        dto.balanceNutricional.resultado;

                    formula = new FormulaNutricional
                    {
                        analisisSueloCalculoId =
                            analisisSueloCalculo
                                .analisisSueloCalculoId,

                        nombreFormula =
                            resultadoFormula.nombreFormula?
                                .Trim() ?? string.Empty,

                        fechaCreacion = DateTime.Now,

                        totalLibras =
                            Math.Round(
                                resultadoFormula.totalLibras,
                                4),

                        mezclaTotalQq =
                            Math.Round(
                                resultadoFormula.mezclaTotalQq,
                                4),

                        totalPlantas =
                            resultadoFormula.totalPlantas,

                        totalAplicaciones =
                            resultadoFormula.totalAplicaciones,

                        totalOnzas =
                            Math.Round(
                                resultadoFormula.totalOnzas,
                                4),

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
                         indice <
                         dto.balanceNutricional.items.Count;
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
                                    Math.Round(
                                        detalleResultado.lb,
                                        4),

                                qq =
                                    Math.Round(
                                        detalleResultado.qq,
                                        4),

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
                                        Math.Round(
                                            aporte.Value,
                                            4),

                                    activo = true
                                });
                        }

                        await _db.SaveChangesAsync();
                    }
                }

                // =====================================================
                // 4. ENMIENDA CALCÁREA - OPCIONAL
                // =====================================================

                if (dto.enmiendaCalcarea != null)
                {
                    var resultadoEnmienda =
                        dto.enmiendaCalcarea.resultado;

                    enmienda = new EnmiendaCalcarea
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

                        ph = Math.Round(
                            resultadoEnmienda.ph,
                            4),

                        ca = Math.Round(
                            resultadoEnmienda.ca,
                            4),

                        mg = Math.Round(
                            resultadoEnmienda.mg,
                            4),

                        k = Math.Round(
                            resultadoEnmienda.k,
                            4),

                        acidezTotal =
                            Math.Round(
                                resultadoEnmienda.acidezTotal,
                                4),

                        saturacionDeseada =
                            Math.Round(
                                resultadoEnmienda.saturacionDeseada,
                                4),

                        prnt =
                            Math.Round(
                                resultadoEnmienda.prnt,
                                4),

                        sumaBases =
                            Math.Round(
                                resultadoEnmienda.sumaBases,
                                4),

                        cice =
                            Math.Round(
                                resultadoEnmienda.cice,
                                4),

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
                }

                // =====================================================
                // 5. FERTILIZACIÓN MIXTA - OPCIONAL
                // =====================================================

                if (dto.fertilizacionMixta != null)
                {
                    var resultadoMixta =
                        dto.fertilizacionMixta;

                    fertilizacionMixta =
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
                                    Math.Round(
                                        fuente.cantidadQq,
                                        4),

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
                                    Math.Round(
                                        detalle.exportable,
                                        4),

                                aporteOrganico =
                                    Math.Round(
                                        detalle.aporteOrganico,
                                        4),

                                diferencia =
                                    Math.Round(
                                        detalle.diferencia,
                                        4),

                                deficit =
                                    Math.Round(
                                        detalle.deficit,
                                        4),

                                sobrante =
                                    Math.Round(
                                        detalle.sobrante,
                                        4),

                                activo = true
                            })
                            .ToList();

                    _db.fertilizacionMixtaDetalle.AddRange(
                        detallesMixtos);

                    await _db.SaveChangesAsync();
                }

                await transaccion.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message =
                        "El análisis fue guardado correctamente.",
                    data = new GuardarTodoRespuestaDto
                    {
                        analisisSueloId =
                            analisisSuelo.analisisSueloId,

                        analisisSueloCalculoId =
                            analisisSueloCalculo
                                .analisisSueloCalculoId,

                        formulaNutricionalId =
                            formula?.formulaNutricionalId,

                        enmiendaCalcareaId =
                            enmienda?.enmiendaCalcareaId,

                        fertilizacionMixtaId =
                            fertilizacionMixta?
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
                        "Ocurrió un error al guardar el análisis.",
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

            // Fórmula nutricional opcional.
            if (dto.balanceNutricional != null)
            {
                if (dto.balanceNutricional.resultado == null)
                    return "El balance nutricional no contiene resultado.";

                if (dto.balanceNutricional.items == null ||
                    !dto.balanceNutricional.items.Any())
                    return "Debe enviar los IDs de los detalles de la fórmula nutricional.";

                if (dto.balanceNutricional.resultado.detalle == null ||
                    !dto.balanceNutricional.resultado.detalle.Any())
                    return "El balance nutricional no contiene detalles.";

                if (dto.balanceNutricional.items.Count !=
                    dto.balanceNutricional.resultado.detalle.Count)
                    return "La cantidad de items no coincide con los detalles calculados de la fórmula.";

                if (dto.requerimientoAnual.terrenoId !=
                    dto.balanceNutricional.terrenoId)
                    return "El terreno del requerimiento anual no coincide con el de la fórmula nutricional.";
            }

            // Enmienda calcárea opcional.
            if (dto.enmiendaCalcarea != null)
            {
                if (dto.enmiendaCalcarea.resultado == null)
                    return "La enmienda calcárea no contiene resultado.";

                if (dto.enmiendaCalcarea.fuenteNutrientesId <= 0)
                    return "La fuente de la enmienda calcárea no es válida.";

                if (dto.enmiendaCalcarea.resultado.terrenoId.HasValue &&
                    dto.enmiendaCalcarea.resultado.terrenoId.Value !=
                    dto.requerimientoAnual.terrenoId)
                    return "El terreno de la enmienda no coincide con el requerimiento anual.";
            }

            // Fertilización mixta opcional.
            if (dto.fertilizacionMixta != null)
            {
                if (dto.fertilizacionMixta.fuentes == null ||
                    !dto.fertilizacionMixta.fuentes.Any())
                    return "La fertilización mixta no contiene fuentes.";

                if (dto.fertilizacionMixta.detalles == null ||
                    !dto.fertilizacionMixta.detalles.Any())
                    return "La fertilización mixta no contiene detalles.";
            }

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
    

      // =========================================================
        // LISTAR ANÁLISIS GUARDADOS
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Listar()
        {
            var registros = await (
                from calculo in _db.AnalisisSueloCalculos.AsNoTracking()
                join analisis in _db.AnalisisSuelos.AsNoTracking()
                    on calculo.analisisSueloId equals analisis.analisisSueloId
                where calculo.activo && analisis.activo
                orderby calculo.fechaCalculo descending
                select new
                {
                    calculo.analisisSueloCalculoId,
                    analisis.analisisSueloId,
                    analisis.identificadorAnalisisSuelo,
                    analisis.laboratorioAnalasisSuelo,
                    analisis.fechaAnalisisSuelo,
                    calculo.fechaCalculo,
                    calculo.terrenoId,
                    calculo.tipoCultivoId,
                    calculo.tipoAnalisisSueloId,
                    calculo.cantidadQuintalesOro,
                    calculo.tamanoFinca,
                    calculo.phAnalisisSuelo,
                    calculo.usuarioId,
                    tieneFormulaNutricional = _db.formulaNutricional.Any(x =>
                        x.analisisSueloCalculoId == calculo.analisisSueloCalculoId && x.activo),
                    tieneEnmiendaCalcarea = _db.enmiendaCalcarea.Any(x =>
                        x.analisisSueloCalculoId == calculo.analisisSueloCalculoId && x.activo),
                    tieneFertilizacionMixta = _db.fertilizacionMixta.Any(x =>
                        x.analisisSueloCalculoId == calculo.analisisSueloCalculoId && x.activo)
                }).ToListAsync();

            return Ok(new
            {
                success = true,
                total = registros.Count,
                data = registros
            });
        }

        // =========================================================
        // OBTENER DETALLE COMPLETO
        // =========================================================
        [HttpGet("listardetalle/{id:int}")]
        public async Task<IActionResult> ObtenerPorId(int id)
        {
            var calculo = await _db.AnalisisSueloCalculos
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.analisisSueloCalculoId == id && x.activo);

            if (calculo == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "No se encontró el análisis solicitado."
                });
            }

            var analisis = await _db.AnalisisSuelos
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.analisisSueloId == calculo.analisisSueloId && x.activo);

            if (analisis == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "No se encontraron los datos originales del análisis."
                });
            }

            var elementosOriginales = await _db.AnalisisSueloElementos
                .AsNoTracking()
                .Where(x => x.analisisSueloId == analisis.analisisSueloId && x.activo)
                .Select(x => new
                {
                    x.analisisSueloElementoQuimicoId,
                    x.elementoQuimicosId,
                    x.unidadMedidaId,
                    x.cantidadElemento
                })
                .ToListAsync();

            var elementosCalculados = await _db.AnalisisSueloCalculoElementoQuimicos
                .AsNoTracking()
                .Where(x => x.analisisSueloCalculoId == id && x.activo)
                .Select(x => new
                {
                    x.analisisSueloCalculoElementoQuimicoId,
                    x.elementoQuimicosId,
                    x.unidadMedidaId,
                    x.cantidadIngresada,
                    x.cantidadConvertidaLbMz,
                    x.requerimientoCalculado,
                    x.clasificacion,
                    x.observacion
                })
                .ToListAsync();

            var formula = await _db.formulaNutricional
                .AsNoTracking()
                .Where(x => x.analisisSueloCalculoId == id && x.activo)
                .Select(x => new
                {
                    x.formulaNutricionalId,
                    x.nombreFormula,
                    x.fechaCreacion,
                    x.totalLibras,
                    x.mezclaTotalQq,
                    x.totalPlantas,
                    x.totalAplicaciones,
                    x.totalOnzas,
                    x.precioTotalFormula,
                    x.precioPorAplicacion,
                    x.dosisPlantaAnualOz,
                    x.dosisPlantaPorAplicacionOz,
                    x.terrenoId
                })
                .FirstOrDefaultAsync();

            object? formulaCompleta = null;
            if (formula != null)
            {
                var detalles = await _db.formulaNutricionalDetalle
                    .AsNoTracking()
                    .Where(x => x.formulaNutricionalId == formula.formulaNutricionalId && x.activo)
                    .Select(x => new
                    {
                        x.formulaNutricionalDetalleId,
                        x.fuenteNutrientesId,
                        x.elementoQuimicosId,
                        x.libras,
                        x.qq,
                        x.requerimientoLibras,
                        x.precioPorQuintal,
                        x.subtotalFuente,
                        x.onzasAnuales,
                        x.onzasPorAplicacion
                    })
                    .ToListAsync();

                var detalleIds = detalles.Select(x => x.formulaNutricionalDetalleId).ToList();
                var aportes = await _db.formulaNutricionalAporte
                    .AsNoTracking()
                    .Where(x => detalleIds.Contains(x.formulaNutricionalDetalleId) && x.activo)
                    .Select(x => new
                    {
                        x.formulaNutricionalAporteId,
                        x.formulaNutricionalDetalleId,
                        x.elementoQuimicosId,
                        x.valor
                    })
                    .ToListAsync();

                formulaCompleta = new { formula, detalles, aportes };
            }

            var enmienda = await _db.enmiendaCalcarea
                .AsNoTracking()
                .Where(x => x.analisisSueloCalculoId == id && x.activo)
                .Select(x => new
                {
                    x.enmiendaCalcareaId,
                    x.nombreAnalisis,
                    x.fuenteNutrientesId,
                    x.terrenoId,
                    x.totalPlantas,
                    x.totalAplicaciones,
                    x.ph,
                    x.ca,
                    x.mg,
                    x.k,
                    x.acidezTotal,
                    x.saturacionDeseada,
                    x.prnt,
                    x.sumaBases,
                    x.cice,
                    x.saturacionActual,
                    x.necesidadEncaladoTonHa,
                    x.necesidadEncaladoKgHa,
                    x.necesidadEncaladoLbHa,
                    x.necesidadEncaladoLbMz,
                    x.necesidadEncaladoOzMz,
                    x.dosisPlantaAnualOz,
                    x.dosisPlantaPorAplicacionOz,
                    x.fechaCreacion
                })
                .FirstOrDefaultAsync();

            var mixta = await _db.fertilizacionMixta
                .AsNoTracking()
                .Where(x => x.analisisSueloCalculoId == id && x.activo)
                .Select(x => new
                {
                    x.fertilizacionMixtaId,
                    x.fechaCalculo,
                    x.observacion
                })
                .FirstOrDefaultAsync();

            object? mixtaCompleta = null;
            if (mixta != null)
            {
                var fuentes = await _db.fertilizacionMixtaFuente
                    .AsNoTracking()
                    .Where(x => x.fertilizacionMixtaId == mixta.fertilizacionMixtaId && x.activo)
                    .Select(x => new
                    {
                        x.fertilizacionMixtaFuenteId,
                        x.fuenteNutrientesId,
                        x.cantidadQq
                    })
                    .ToListAsync();

                var detalles = await _db.fertilizacionMixtaDetalle
                    .AsNoTracking()
                    .Where(x => x.fertilizacionMixtaId == mixta.fertilizacionMixtaId && x.activo)
                    .Select(x => new
                    {
                        x.fertilizacionMixtaDetalleId,
                        x.elementoQuimicosId,
                        x.requerimientoOriginal,
                        x.aporteOrganico,
                        x.diferencia,
                        x.deficit,
                        x.sobrante
                    })
                    .ToListAsync();

                mixtaCompleta = new { mixta, fuentes, detalles };
            }

            return Ok(new
            {
                success = true,
                data = new
                {
                    datosAnalisis = new
                    {
                        analisis.analisisSueloId,
                        analisis.fechaAnalisisSuelo,
                        analisis.fechaCreacionAnalisisSuelo,
                        analisis.laboratorioAnalasisSuelo,
                        analisis.identificadorAnalisisSuelo,
                        usuarioId = calculo.usuarioId,
                        elementosQuimicos = elementosOriginales
                    },
                    requerimientoAnual = new
                    {
                        calculo.analisisSueloCalculoId,
                        calculo.terrenoId,
                        calculo.tipoCultivoId,
                        calculo.tipoAnalisisSueloId,
                        calculo.cantidadQuintalesOro,
                        calculo.tamanoFinca,
                        ph = calculo.phAnalisisSuelo,
                        calculo.materiaOrganica,
                        calculo.acidezTotal,
                        calculo.unidadMedidaMateriaOrganicaId,
                        calculo.recomendacionGeneral,
                        observaciones = string.IsNullOrWhiteSpace(calculo.observacion)
                            ? Array.Empty<string>()
                            : calculo.observacion.Split(" | ", StringSplitOptions.RemoveEmptyEntries),
                        elementos = elementosCalculados
                    },
                    balanceNutricional = formulaCompleta,
                    enmiendaCalcarea = enmienda,
                    fertilizacionMixta = mixtaCompleta
                }
            });
        }
        // =========================================================
        // EDITAR REGISTRO COMPLETO
        // =========================================================
        [HttpPut("editar/{id:int}")]
        public async Task<IActionResult> Editar(
            int id,
            [FromBody] GuardarTodoDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

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
                var calculo = await _db.AnalisisSueloCalculos
                    .FirstOrDefaultAsync(x =>
                        x.analisisSueloCalculoId == id && x.activo);

                if (calculo == null)
                {
                    await transaccion.RollbackAsync();
                    return NotFound(new
                    {
                        success = false,
                        message = "No se encontró el análisis solicitado."
                    });
                }

                var analisis = await _db.AnalisisSuelos
                    .FirstOrDefaultAsync(x =>
                        x.analisisSueloId == calculo.analisisSueloId &&
                        x.activo);

                if (analisis == null)
                {
                    await transaccion.RollbackAsync();
                    return NotFound(new
                    {
                        success = false,
                        message = "No se encontró el análisis de suelo relacionado."
                    });
                }

                var datosAnalisis = dto.datosAnalisis;
                var resultadoAnual = dto.requerimientoAnual;

                string identificador =
                    datosAnalisis.identificadorAnalisisSuelo
                        .Trim()
                        .ToUpperInvariant();

                bool identificadorExiste =
                    await _db.AnalisisSuelos.AnyAsync(x =>
                        x.analisisSueloId != analisis.analisisSueloId &&
                        x.identificadorAnalisisSuelo == identificador &&
                        x.activo);

                if (identificadorExiste)
                {
                    await transaccion.RollbackAsync();
                    return Conflict(new
                    {
                        success = false,
                        message = "Ya existe otro análisis de suelo con ese identificador."
                    });
                }

                // 1. Actualizar análisis original.
                analisis.fechaAnalisisSuelo =
                    datosAnalisis.fechaAnalisisSuelo;
                analisis.laboratorioAnalasisSuelo =
                    datosAnalisis.laboratorioAnalasisSuelo
                        .Trim()
                        .ToUpperInvariant();
                analisis.identificadorAnalisisSuelo = identificador;
                analisis.activo = true;

                var elementosOriginalesAnteriores =
                    await _db.AnalisisSueloElementos
                        .Where(x =>
                            x.analisisSueloId == analisis.analisisSueloId &&
                            x.activo)
                        .ToListAsync();

                elementosOriginalesAnteriores.ForEach(x => x.activo = false);

                var nuevosElementosOriginales =
                    datosAnalisis.elementosQuimicos.Select(elemento =>
                        new AnalisisSueloElementoQuimico
                        {
                            analisisSueloId = analisis.analisisSueloId,
                            elementoQuimicosId = elemento.elementoQuimicosId,
                            unidadMedidaId = elemento.unidadMedidaId,
                            cantidadElemento = Math.Round(
                                elemento.cantidadElemento,
                                4),
                            activo = true
                        })
                        .ToList();

                _db.AnalisisSueloElementos.AddRange(
                    nuevosElementosOriginales);

                // 2. Actualizar requerimiento anual.
                calculo.cantidadQuintalesOro = Math.Round(
                    resultadoAnual.cantidadQuintalesOro,
                    4);
                calculo.tamanoFinca = Math.Round(
                    resultadoAnual.tamanoFinca,
                    4);
                calculo.phAnalisisSuelo = Math.Round(
                    resultadoAnual.ph,
                    4);
                calculo.materiaOrganica = Math.Round(
                    resultadoAnual.materiaOrganica,
                    4);
                calculo.acidezTotal = resultadoAnual.acidezTotal.HasValue
                    ? Math.Round(resultadoAnual.acidezTotal.Value, 4)
                    : 0;
                calculo.recomendacionGeneral =
                    resultadoAnual.recomendacionGeneral ?? string.Empty;
                calculo.observacion = string.Join(
                    " | ",
                    resultadoAnual.observaciones ?? new List<string>());
                calculo.fechaCalculo = DateTime.Now;
                calculo.terrenoId = resultadoAnual.terrenoId;
                calculo.tipoCultivoId = resultadoAnual.tipoCultivoId;
                calculo.tipoAnalisisSueloId =
                    resultadoAnual.tipoAnalisisSueloId;
                calculo.usuarioId = datosAnalisis.usuarioId;
                calculo.unidadMedidaMateriaOrganicaId =
                    resultadoAnual.unidadMedidaMateriaOrganicaId;
                calculo.activo = true;

                var elementosCalculadosAnteriores =
                    await _db.AnalisisSueloCalculoElementoQuimicos
                        .Where(x =>
                            x.analisisSueloCalculoId == id &&
                            x.activo)
                        .ToListAsync();

                elementosCalculadosAnteriores.ForEach(x => x.activo = false);

                var nuevosElementosCalculados =
                    resultadoAnual.elementos.Select(elemento =>
                        new AnalisisSueloCalculoElementoQuimico
                        {
                            analisisSueloCalculoId = id,
                            elementoQuimicosId = elemento.elementoQuimicosId,
                            unidadMedidaId = elemento.unidadMedidaResultadoId,
                            cantidadIngresada = Math.Round(
                                elemento.cantidadIngresada,
                                4),
                            cantidadConvertidaLbMz = RedondearNullable(
                                elemento.cantidadConvertidaLbMz),
                            requerimientoCalculado = RedondearNullable(
                                elemento.requerimientoCalculado),
                            clasificacion =
                                elemento.clasificacion ?? string.Empty,
                            observacion =
                                elemento.observacion ?? string.Empty,
                            activo = true
                        })
                        .ToList();

                _db.AnalisisSueloCalculoElementoQuimicos.AddRange(
                    nuevosElementosCalculados);

                // Desactivar módulos opcionales anteriores y sus detalles.
                var formulasAnteriores = await _db.formulaNutricional
                    .Where(x =>
                        x.analisisSueloCalculoId == id &&
                        x.activo)
                    .ToListAsync();
                formulasAnteriores.ForEach(x => x.activo = false);

                var formulaIds = formulasAnteriores
                    .Select(x => x.formulaNutricionalId)
                    .ToList();

                var detallesFormulaAnteriores =
                    await _db.formulaNutricionalDetalle
                        .Where(x =>
                            formulaIds.Contains(x.formulaNutricionalId) &&
                            x.activo)
                        .ToListAsync();
                detallesFormulaAnteriores.ForEach(x => x.activo = false);

                var detalleFormulaIds = detallesFormulaAnteriores
                    .Select(x => x.formulaNutricionalDetalleId)
                    .ToList();

                var aportesAnteriores =
                    await _db.formulaNutricionalAporte
                        .Where(x =>
                            detalleFormulaIds.Contains(
                                x.formulaNutricionalDetalleId) &&
                            x.activo)
                        .ToListAsync();
                aportesAnteriores.ForEach(x => x.activo = false);

                var enmiendasAnteriores = await _db.enmiendaCalcarea
                    .Where(x =>
                        x.analisisSueloCalculoId == id &&
                        x.activo)
                    .ToListAsync();
                enmiendasAnteriores.ForEach(x => x.activo = false);

                var mixtasAnteriores = await _db.fertilizacionMixta
                    .Where(x =>
                        x.analisisSueloCalculoId == id &&
                        x.activo)
                    .ToListAsync();
                mixtasAnteriores.ForEach(x => x.activo = false);

                var mixtaIds = mixtasAnteriores
                    .Select(x => x.fertilizacionMixtaId)
                    .ToList();

                var fuentesMixtasAnteriores =
                    await _db.fertilizacionMixtaFuente
                        .Where(x =>
                            mixtaIds.Contains(x.fertilizacionMixtaId) &&
                            x.activo)
                        .ToListAsync();
                fuentesMixtasAnteriores.ForEach(x => x.activo = false);

                var detallesMixtosAnteriores =
                    await _db.fertilizacionMixtaDetalle
                        .Where(x =>
                            mixtaIds.Contains(x.fertilizacionMixtaId) &&
                            x.activo)
                        .ToListAsync();
                detallesMixtosAnteriores.ForEach(x => x.activo = false);

                await _db.SaveChangesAsync();

                FormulaNutricional? formulaNueva = null;
                EnmiendaCalcarea? enmiendaNueva = null;
                FertilizacionMixta? mixtaNueva = null;

                // 3. Crear nueva versión de fórmula nutricional.
                if (dto.balanceNutricional != null)
                {
                    var resultadoFormula =
                        dto.balanceNutricional.resultado;

                    formulaNueva = new FormulaNutricional
                    {
                        analisisSueloCalculoId = id,
                        nombreFormula =
                            resultadoFormula.nombreFormula?.Trim() ??
                            string.Empty,
                        fechaCreacion = DateTime.Now,
                        totalLibras = Math.Round(
                            resultadoFormula.totalLibras,
                            4),
                        mezclaTotalQq = Math.Round(
                            resultadoFormula.mezclaTotalQq,
                            4),
                        totalPlantas = resultadoFormula.totalPlantas,
                        totalAplicaciones = resultadoFormula.totalAplicaciones,
                        totalOnzas = Math.Round(
                            resultadoFormula.totalOnzas,
                            4),
                        precioTotalFormula = Math.Round(
                            resultadoFormula.precioTotalFormula,
                            4),
                        precioPorAplicacion = Math.Round(
                            resultadoFormula.precioPorAplicacion,
                            4),
                        dosisPlantaAnualOz = Math.Round(
                            resultadoFormula.dosisPlantaAnualOz,
                            4),
                        dosisPlantaPorAplicacionOz = Math.Round(
                            resultadoFormula.dosisPlantaPorAplicacionOz,
                            4),
                        terrenoId = dto.balanceNutricional.terrenoId,
                        activo = true
                    };

                    _db.formulaNutricional.Add(formulaNueva);
                    await _db.SaveChangesAsync();

                    var elementosActivos = await _db.elementoQuimico
                        .Where(x => x.activo)
                        .ToListAsync();

                    var elementosPorSimbolo = elementosActivos
                        .Where(x => !string.IsNullOrWhiteSpace(
                            x.simboloElementoQuimico))
                        .GroupBy(x => NormalizarSimbolo(
                            x.simboloElementoQuimico))
                        .ToDictionary(
                            grupo => grupo.Key,
                            grupo => grupo.First().elementoQuimicosId);

                    for (int indice = 0;
                         indice < dto.balanceNutricional.items.Count;
                         indice++)
                    {
                        var item = dto.balanceNutricional.items[indice];
                        var detalleResultado =
                            resultadoFormula.detalle[indice];

                        var formulaDetalle =
                            new FormulaNutricionalDetalle
                            {
                                formulaNutricionalId =
                                    formulaNueva.formulaNutricionalId,
                                fuenteNutrientesId =
                                    item.fuenteNutrientesId,
                                elementoQuimicosId =
                                    item.elementoQuimicosId,
                                libras = Math.Round(
                                    detalleResultado.lb,
                                    4),
                                qq = Math.Round(
                                    detalleResultado.qq,
                                    4),
                                requerimientoLibras = Math.Round(
                                    detalleResultado.requerimientoLibras,
                                    4),
                                precioPorQuintal = Math.Round(
                                    detalleResultado.precioPorQuintal,
                                    4),
                                subtotalFuente = Math.Round(
                                    detalleResultado.subtotalFuente,
                                    4),
                                onzasAnuales = Math.Round(
                                    detalleResultado.onzasAnuales,
                                    4),
                                onzasPorAplicacion = Math.Round(
                                    detalleResultado.onzasPorAplicacion,
                                    4),
                                activo = true
                            };

                        _db.formulaNutricionalDetalle.Add(formulaDetalle);
                        await _db.SaveChangesAsync();

                        foreach (var aporte in detalleResultado.aportes)
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
                                    elementoQuimicosId = elementoAporteId,
                                    valor = Math.Round(aporte.Value, 4),
                                    activo = true
                                });
                        }

                        await _db.SaveChangesAsync();
                    }
                }

                // 4. Crear nueva versión de enmienda calcárea.
                if (dto.enmiendaCalcarea != null)
                {
                    var r = dto.enmiendaCalcarea.resultado;

                    enmiendaNueva = new EnmiendaCalcarea
                    {
                        analisisSueloCalculoId = id,
                        nombreAnalisis =
                            r.nombreAnalisis?.Trim() ?? string.Empty,
                        fuenteNutrientesId =
                            dto.enmiendaCalcarea.fuenteNutrientesId,
                        terrenoId = r.terrenoId,
                        totalPlantas = r.totalPlantas,
                        totalAplicaciones = r.totalAplicaciones,
                        ph = Math.Round(r.ph, 4),
                        ca = Math.Round(r.ca, 4),
                        mg = Math.Round(r.mg, 4),
                        k = Math.Round(r.k, 4),
                        acidezTotal = Math.Round(r.acidezTotal, 4),
                        saturacionDeseada = Math.Round(
                            r.saturacionDeseada,
                            4),
                        prnt = Math.Round(r.prnt, 4),
                        sumaBases = Math.Round(r.sumaBases, 4),
                        cice = Math.Round(r.cice, 4),
                        saturacionActual = Math.Round(
                            r.saturacionActual,
                            4),
                        necesidadEncaladoTonHa = Math.Round(
                            r.necesidadEncaladoTonHa,
                            4),
                        necesidadEncaladoKgHa = Math.Round(
                            r.necesidadEncaladoKgHa,
                            4),
                        necesidadEncaladoLbHa = Math.Round(
                            r.necesidadEncaladoLbHa,
                            4),
                        necesidadEncaladoLbMz = Math.Round(
                            r.necesidadEncaladoLbMz,
                            4),
                        necesidadEncaladoOzMz = Math.Round(
                            r.necesidadEncaladoOzMz,
                            4),
                        dosisPlantaAnualOz = Math.Round(
                            r.dosisPlantaAnualOz,
                            4),
                        dosisPlantaPorAplicacionOz = Math.Round(
                            r.dosisPlantaPorAplicacionOz,
                            4),
                        fechaCreacion = DateTime.Now,
                        activo = true
                    };

                    _db.enmiendaCalcarea.Add(enmiendaNueva);
                    await _db.SaveChangesAsync();
                }

                // 5. Crear nueva versión de fertilización mixta.
                if (dto.fertilizacionMixta != null)
                {
                    var resultadoMixta = dto.fertilizacionMixta;

                    mixtaNueva = new FertilizacionMixta
                    {
                        analisisSueloCalculoId = id,
                        fechaCalculo = DateTime.Now,
                        observacion =
                            resultadoMixta.observacion ?? string.Empty,
                        activo = true
                    };

                    _db.fertilizacionMixta.Add(mixtaNueva);
                    await _db.SaveChangesAsync();

                    var nuevasFuentes = resultadoMixta.fuentes
                        .Select(fuente =>
                            new FertilizacionMixtaFuente
                            {
                                fertilizacionMixtaId =
                                    mixtaNueva.fertilizacionMixtaId,
                                fuenteNutrientesId =
                                    fuente.fuenteNutrientesId,
                                cantidadQq = Math.Round(
                                    fuente.cantidadQq,
                                    4),
                                activo = true
                            })
                        .ToList();

                    var nuevosDetalles = resultadoMixta.detalles
                        .Select(detalle =>
                            new FertilizacionMixtaDetalle
                            {
                                fertilizacionMixtaId =
                                    mixtaNueva.fertilizacionMixtaId,
                                elementoQuimicosId =
                                    detalle.elementoQuimicosId,
                                requerimientoOriginal = Math.Round(
                                    detalle.exportable,
                                    4),
                                aporteOrganico = Math.Round(
                                    detalle.aporteOrganico,
                                    4),
                                diferencia = Math.Round(
                                    detalle.diferencia,
                                    4),
                                deficit = Math.Round(
                                    detalle.deficit,
                                    4),
                                sobrante = Math.Round(
                                    detalle.sobrante,
                                    4),
                                activo = true
                            })
                        .ToList();

                    _db.fertilizacionMixtaFuente.AddRange(nuevasFuentes);
                    _db.fertilizacionMixtaDetalle.AddRange(nuevosDetalles);
                    await _db.SaveChangesAsync();
                }

                await transaccion.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "El análisis fue actualizado correctamente.",
                    data = new GuardarTodoRespuestaDto
                    {
                        analisisSueloId = analisis.analisisSueloId,
                        analisisSueloCalculoId = id,
                        formulaNutricionalId =
                            formulaNueva?.formulaNutricionalId,
                        enmiendaCalcareaId =
                            enmiendaNueva?.enmiendaCalcareaId,
                        fertilizacionMixtaId =
                            mixtaNueva?.fertilizacionMixtaId
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
                        "Ocurrió un error al actualizar el análisis.",
                    detail = ex.Message,
                    inner = ex.InnerException?.Message ?? string.Empty
                });
            }
        }
        // =========================================================
        // ELIMINACIÓN LÓGICA DEL REGISTRO COMPLETO
        // =========================================================
        [HttpDelete("eliminar/{id:int}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            await using var transaccion = await _db.Database.BeginTransactionAsync();

            try
            {
                var calculo = await _db.AnalisisSueloCalculos
                    .FirstOrDefaultAsync(x =>
                        x.analisisSueloCalculoId == id && x.activo);

                if (calculo == null)
                {
                    await transaccion.RollbackAsync();
                    return NotFound(new
                    {
                        success = false,
                        message = "No se encontró el análisis solicitado."
                    });
                }

                calculo.activo = false;

                var elementosCalculados = await _db.AnalisisSueloCalculoElementoQuimicos
                    .Where(x => x.analisisSueloCalculoId == id && x.activo)
                    .ToListAsync();
                elementosCalculados.ForEach(x => x.activo = false);

                var analisis = await _db.AnalisisSuelos
                    .FirstOrDefaultAsync(x => x.analisisSueloId == calculo.analisisSueloId);
                if (analisis != null)
                    analisis.activo = false;

                var elementosOriginales = await _db.AnalisisSueloElementos
                    .Where(x => x.analisisSueloId == calculo.analisisSueloId && x.activo)
                    .ToListAsync();
                elementosOriginales.ForEach(x => x.activo = false);

                var formulas = await _db.formulaNutricional
                    .Where(x => x.analisisSueloCalculoId == id && x.activo)
                    .ToListAsync();
                formulas.ForEach(x => x.activo = false);

                var formulaIds = formulas.Select(x => x.formulaNutricionalId).ToList();
                var detallesFormula = await _db.formulaNutricionalDetalle
                    .Where(x => formulaIds.Contains(x.formulaNutricionalId) && x.activo)
                    .ToListAsync();
                detallesFormula.ForEach(x => x.activo = false);

                var detalleFormulaIds = detallesFormula
                    .Select(x => x.formulaNutricionalDetalleId).ToList();
                var aportes = await _db.formulaNutricionalAporte
                    .Where(x => detalleFormulaIds.Contains(x.formulaNutricionalDetalleId) && x.activo)
                    .ToListAsync();
                aportes.ForEach(x => x.activo = false);

                var enmiendas = await _db.enmiendaCalcarea
                    .Where(x => x.analisisSueloCalculoId == id && x.activo)
                    .ToListAsync();
                enmiendas.ForEach(x => x.activo = false);

                var mixtas = await _db.fertilizacionMixta
                    .Where(x => x.analisisSueloCalculoId == id && x.activo)
                    .ToListAsync();
                mixtas.ForEach(x => x.activo = false);

                var mixtaIds = mixtas.Select(x => x.fertilizacionMixtaId).ToList();
                var fuentesMixtas = await _db.fertilizacionMixtaFuente
                    .Where(x => mixtaIds.Contains(x.fertilizacionMixtaId) && x.activo)
                    .ToListAsync();
                fuentesMixtas.ForEach(x => x.activo = false);

                var detallesMixtos = await _db.fertilizacionMixtaDetalle
                    .Where(x => mixtaIds.Contains(x.fertilizacionMixtaId) && x.activo)
                    .ToListAsync();
                detallesMixtos.ForEach(x => x.activo = false);

                await _db.SaveChangesAsync();
                await transaccion.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "El análisis y todos sus registros relacionados fueron eliminados correctamente."
                });
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync();
                return StatusCode(500, new
                {
                    success = false,
                    message = "Ocurrió un error al eliminar el análisis.",
                    detail = ex.Message,
                    inner = ex.InnerException?.Message ?? string.Empty
                });
            }
        }

    }
}
