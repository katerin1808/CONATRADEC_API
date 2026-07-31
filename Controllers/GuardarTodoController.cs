using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.FertilizacionMixtaDto;
using static CONATRADEC_API.DTOs.FormulaNutricionalDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/guardar-todo")]
    public class GuardarTodoController :
        ControllerBase
    {
        private readonly DBContext _db;

        public GuardarTodoController(
            DBContext db)
        {
            _db = db;
        }

        // =============================================================
        // CREAR ANÁLISIS COMPLETO
        // =============================================================
        [HttpPost]
        public async Task<IActionResult>
            GuardarTodo(
                [FromBody] GuardarTodoDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            NormalizarBalance(dto);

            string? errorValidacion =
                ValidarSolicitud(dto);

            if (errorValidacion != null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = errorValidacion
                });
            }

            await using var transaccion =
                await _db.Database
                    .BeginTransactionAsync();

            try
            {
                AnalisisSueloGuardarRequestDto
                    datosAnalisis =
                        dto.datosAnalisis;

                AnalisisSueloCalculoResponseDto
                    resultadoAnual =
                        dto.requerimientoAnual;

                string identificador =
                    NormalizarTexto(
                        datosAnalisis
                            .identificadorAnalisisSuelo);

                bool identificadorExiste =
                    await _db.AnalisisSuelos
                        .AnyAsync(x =>
                            x.identificadorAnalisisSuelo ==
                                identificador);

                if (identificadorExiste)
                {
                    await transaccion
                        .RollbackAsync();

                    return Conflict(new
                    {
                        success = false,
                        message =
                            "Ya existe un análisis de suelo con ese identificador."
                    });
                }

                AnalisisSuelo analisisSuelo =
                    CrearAnalisisOriginal(
                        datosAnalisis,
                        identificador);

                _db.AnalisisSuelos.Add(
                    analisisSuelo);

                await _db.SaveChangesAsync();

                _db.AnalisisSueloElementos
                    .AddRange(
                        CrearElementosOriginales(
                            analisisSuelo
                                .analisisSueloId,
                            datosAnalisis
                                .elementosQuimicos));

                AnalisisSueloCalculo
                    analisisSueloCalculo =
                        CrearRequerimientoAnual(
                            analisisSuelo
                                .analisisSueloId,
                            datosAnalisis,
                            resultadoAnual);

                _db.AnalisisSueloCalculos.Add(
                    analisisSueloCalculo);

                await _db.SaveChangesAsync();

                _db
                    .AnalisisSueloCalculoElementoQuimicos
                    .AddRange(
                        CrearElementosCalculados(
                            analisisSueloCalculo
                                .analisisSueloCalculoId,
                            resultadoAnual
                                .elementos));

                await _db.SaveChangesAsync();

                ResultadoModulosOpcionales
                    modulos =
                        await GuardarModulosOpcionalesAsync(
                            analisisSueloCalculo
                                .analisisSueloCalculoId,
                            dto);

                await transaccion.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message =
                        CrearMensajeGuardado(dto),
                    data =
                        new GuardarTodoRespuestaDto
                        {
                            analisisSueloId =
                                analisisSuelo
                                    .analisisSueloId,
                            analisisSueloCalculoId =
                                analisisSueloCalculo
                                    .analisisSueloCalculoId,
                            formulaNutricionalId =
                                modulos
                                    .FormulaNutricionalId,
                            enmiendaCalcareaId =
                                modulos
                                    .EnmiendaCalcareaId,
                            fertilizacionMixtaId =
                                modulos
                                    .FertilizacionMixtaId
                        }
                });
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync();

                return StatusCode(
                    StatusCodes
                        .Status500InternalServerError,
                    new
                    {
                        success = false,
                        message =
                            "Ocurrió un error al guardar el análisis.",
                        detail = ex.Message,
                        inner =
                            ex.InnerException?
                                .Message ??
                            string.Empty
                    });
            }
        }

        // =============================================================
        // LISTADO PRINCIPAL
        // =============================================================
        [HttpGet]
        public async Task<IActionResult> Listar()
        {
            var registros =
                await (
                    from calculo
                    in _db.AnalisisSueloCalculos
                        .AsNoTracking()

                    join analisis
                    in _db.AnalisisSuelos
                        .AsNoTracking()
                    on calculo.analisisSueloId
                    equals analisis.analisisSueloId

                    where
                        calculo.activo &&
                        analisis.activo

                    orderby
                        analisis
                            .fechaCreacionAnalisisSuelo
                            descending,
                        calculo.fechaCalculo
                            descending

                    select new
                    {
                        calculo
                            .analisisSueloCalculoId,
                        analisis.analisisSueloId,
                        analisis
                            .identificadorAnalisisSuelo,
                        analisis
                            .laboratorioAnalasisSuelo,
                        analisis.fechaAnalisisSuelo,
                        analisis
                            .fechaCreacionAnalisisSuelo,
                        calculo.fechaCalculo,
                        calculo.terrenoId,
                        calculo.tipoCultivoId,
                        calculo.tipoAnalisisSueloId,
                        calculo
                            .cantidadQuintalesOro,
                        calculo.tamanoFinca,
                        calculo.phAnalisisSuelo,
                        calculo.usuarioId,

                        tieneFormulaNutricional =
                            _db.formulaNutricional
                                .Any(x =>
                                    x
                                        .analisisSueloCalculoId ==
                                    calculo
                                        .analisisSueloCalculoId &&
                                    x.activo),

                        tieneEnmiendaCalcarea =
                            _db.enmiendaCalcarea
                                .Any(x =>
                                    x
                                        .analisisSueloCalculoId ==
                                    calculo
                                        .analisisSueloCalculoId &&
                                    x.activo),

                        tieneFertilizacionMixta =
                            _db.fertilizacionMixta
                                .Any(x =>
                                    x
                                        .analisisSueloCalculoId ==
                                    calculo
                                        .analisisSueloCalculoId &&
                                    x.activo)
                    })
                    .ToListAsync();

            return Ok(new
            {
                success = true,
                total = registros.Count,
                data = registros
            });
        }

        // Se conserva la ruta anterior y se agrega una variante sin espacio.
        [HttpGet("listar usuario")]
        [HttpGet("listar-usuario")]
        public async Task<IActionResult>
            ListarPorUsuario(
                [FromQuery] int? usuarioId =
                    null)
        {
            IQueryable<AnalisisSuelo> query =
                _db.AnalisisSuelos
                    .AsNoTracking()
                    .Where(x => x.activo);

            if (usuarioId.HasValue)
            {
                query = query.Where(analisis =>
                    _db.AnalisisSueloCalculos
                        .Any(calculo =>
                            calculo.analisisSueloId ==
                                analisis
                                    .analisisSueloId &&
                            calculo.usuarioId ==
                                usuarioId.Value &&
                            calculo.activo));
            }

            List<AnalisisSuelo> analisisLista =
                await query
                    .OrderByDescending(x =>
                        x.fechaCreacionAnalisisSuelo)
                    .ThenByDescending(x =>
                        x.analisisSueloId)
                    .ToListAsync();

            List<object> respuesta = new();

            foreach (
                AnalisisSuelo analisis
                in analisisLista)
            {
                AnalisisSueloCalculo? calculo =
                    await _db.AnalisisSueloCalculos
                        .AsNoTracking()
                        .Where(x =>
                            x.analisisSueloId ==
                                analisis
                                    .analisisSueloId &&
                            x.activo)
                        .OrderByDescending(x =>
                            x.fechaCalculo)
                        .FirstOrDefaultAsync();

                object? terrenoResumen = null;
                object? tipoCultivoResumen = null;
                object? tipoAnalisisResumen = null;

                if (calculo != null)
                {
                    Terreno? terreno =
                        await _db.Terreno
                            .AsNoTracking()
                            .FirstOrDefaultAsync(x =>
                                x.terrenoId ==
                                    calculo
                                        .terrenoId);

                    if (terreno != null)
                    {
                        var propietario =
                            await _db.PropietarioTerrenos
                                .AsNoTracking()
                                .Where(relacion =>
                                    relacion.terrenoId ==
                                        terreno.terrenoId &&
                                    relacion.activo &&
                                    relacion.Propietario.activo)
                                .OrderByDescending(relacion =>
                                    relacion.fechaAsignacionUtc)
                                .Select(relacion => new
                                {
                                    relacion.Propietario.propietarioId,
                                    relacion.Propietario.identificacion,
                                    relacion.Propietario.nombreCompleto,
                                    relacion.Propietario.telefono,
                                    relacion.Propietario.correo,
                                    relacion.Propietario.direccion
                                })
                                .FirstOrDefaultAsync();

                        terrenoResumen = new
                        {
                            terreno.terrenoId,
                            terreno.codigoTerreno,
                            propietario,
                            terreno
                                .extensionManzanaTerreno,
                            terreno
                                .cantidadQuintalesOro
                        };
                    }

                    TipoCultivo? cultivo =
                        await _db.TipoCultivos
                            .AsNoTracking()
                            .FirstOrDefaultAsync(x =>
                                x.tipoCultivoId ==
                                    calculo
                                        .tipoCultivoId);

                    if (cultivo != null)
                    {
                        tipoCultivoResumen = new
                        {
                            cultivo.tipoCultivoId,
                            cultivo.nombreTipoCultivo
                        };
                    }

                    TipoAnalisisSuelo?
                        tipoAnalisis =
                            await _db
                                .TipoAnalisisSuelos
                                .AsNoTracking()
                                .FirstOrDefaultAsync(x =>
                                    x
                                        .tipoAnalisisSueloId ==
                                    calculo
                                        .tipoAnalisisSueloId);

                    if (tipoAnalisis != null)
                    {
                        tipoAnalisisResumen = new
                        {
                            tipoAnalisis
                                .tipoAnalisisSueloId,
                            tipoAnalisis
                                .nombreTipoAnalisisSuelo
                        };
                    }
                }

                int totalElementosIngresados =
                    await _db
                        .AnalisisSueloElementos
                        .AsNoTracking()
                        .CountAsync(x =>
                            x.analisisSueloId ==
                                analisis
                                    .analisisSueloId &&
                            x.activo);

                int totalElementosCalculados =
                    calculo == null
                        ? 0
                        : await _db
                            .AnalisisSueloCalculoElementoQuimicos
                            .AsNoTracking()
                            .CountAsync(x =>
                                x
                                    .analisisSueloCalculoId ==
                                calculo
                                    .analisisSueloCalculoId &&
                                x.activo);

                respuesta.Add(new
                {
                    analisis.analisisSueloId,
                    analisis.fechaAnalisisSuelo,
                    analisis
                        .fechaCreacionAnalisisSuelo,
                    analisis
                        .laboratorioAnalasisSuelo,
                    analisis
                        .identificadorAnalisisSuelo,
                    analisis.activo,

                    terreno =
                        terrenoResumen,

                    tipoCultivo =
                        tipoCultivoResumen,

                    tipoAnalisisSuelo =
                        tipoAnalisisResumen,

                    calculo =
                        calculo == null
                            ? null
                            : new
                            {
                                calculo
                                    .analisisSueloCalculoId,
                                calculo
                                    .cantidadQuintalesOro,
                                calculo.tamanoFinca,
                                calculo
                                    .phAnalisisSuelo,
                                calculo.acidezTotal,
                                calculo
                                    .recomendacionGeneral,
                                calculo.fechaCalculo,
                                calculo.usuarioId
                            },

                    totalElementosIngresados,
                    totalElementosCalculados
                });
            }

            return Ok(new
            {
                success = true,
                message =
                    "Listado de análisis de suelo obtenido correctamente.",
                data = respuesta
            });
        }

        // =============================================================
        // DETALLE COMPLETO
        // =============================================================
        [HttpGet("listardetalle/{id:int}")]
        public async Task<IActionResult>
            ObtenerPorId(int id)
        {
            AnalisisSueloCalculo? calculo =
                await _db.AnalisisSueloCalculos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.analisisSueloCalculoId ==
                            id &&
                        x.activo);

            if (calculo == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontró el análisis solicitado."
                });
            }

            AnalisisSuelo? analisis =
                await _db.AnalisisSuelos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.analisisSueloId ==
                            calculo.analisisSueloId &&
                        x.activo);

            if (analisis == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontraron los datos originales del análisis."
                });
            }

            var elementosOriginales =
                await _db.AnalisisSueloElementos
                    .AsNoTracking()
                    .Where(x =>
                        x.analisisSueloId ==
                            analisis.analisisSueloId &&
                        x.activo)
                    .Select(x => new
                    {
                        x
                            .analisisSueloElementoQuimicoId,
                        x.elementoQuimicosId,
                        x.unidadMedidaId,
                        x.cantidadElemento
                    })
                    .ToListAsync();

            var elementosCalculados =
                await _db
                    .AnalisisSueloCalculoElementoQuimicos
                    .AsNoTracking()
                    .Where(x =>
                        x.analisisSueloCalculoId ==
                            id &&
                        x.activo)
                    .Select(x => new
                    {
                        x
                            .analisisSueloCalculoElementoQuimicoId,
                        x.elementoQuimicosId,
                        x.unidadMedidaId,
                        x.cantidadIngresada,
                        x.cantidadConvertidaLbMz,
                        x.requerimientoCalculado,
                        x.clasificacion,
                        x.observacion,
                        x
                            .incluirCalculosComplementarios
                    })
                    .ToListAsync();

            object? formulaCompleta =
                await ObtenerFormulaCompletaAsync(
                    id);

            object? enmienda =
                await ObtenerEnmiendaAsync(id);

            object? mixtaCompleta =
                await ObtenerMixtaCompletaAsync(
                    id);

            return Ok(new
            {
                success = true,
                data = new
                {
                    datosAnalisis = new
                    {
                        analisis.analisisSueloId,
                        analisis
                            .fechaAnalisisSuelo,
                        analisis
                            .fechaCreacionAnalisisSuelo,
                        analisis
                            .laboratorioAnalasisSuelo,
                        analisis
                            .identificadorAnalisisSuelo,
                        usuarioId =
                            calculo.usuarioId,
                        elementosQuimicos =
                            elementosOriginales
                    },

                    requerimientoAnual = new
                    {
                        calculo
                            .analisisSueloCalculoId,
                        calculo.terrenoId,
                        calculo.tipoCultivoId,
                        calculo
                            .tipoAnalisisSueloId,
                        calculo
                            .cantidadQuintalesOro,
                        calculo.tamanoFinca,
                        ph =
                            calculo
                                .phAnalisisSuelo,
                        calculo.materiaOrganica,
                        calculo.acidezTotal,
                        calculo
                            .unidadMedidaMateriaOrganicaId,
                        calculo
                            .recomendacionGeneral,

                        observaciones =
                            string.IsNullOrWhiteSpace(
                                calculo.observacion)
                                ? Array.Empty<string>()
                                : calculo.observacion
                                    .Split(
                                        " | ",
                                        StringSplitOptions
                                            .RemoveEmptyEntries),

                        elementos =
                            elementosCalculados
                    },

                    balanceNutricional =
                        formulaCompleta,

                    enmiendaCalcarea =
                        enmienda,

                    fertilizacionMixta =
                        mixtaCompleta
                }
            });
        }

        // =============================================================
        // EDITAR ANÁLISIS COMPLETO
        // =============================================================
        [HttpPut("editar/{id:int}")]
        public async Task<IActionResult> Editar(
            int id,
            [FromBody] GuardarTodoDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            NormalizarBalance(dto);

            string? errorValidacion =
                ValidarSolicitud(dto);

            if (errorValidacion != null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = errorValidacion
                });
            }

            await using var transaccion =
                await _db.Database
                    .BeginTransactionAsync();

            try
            {
                AnalisisSueloCalculo? calculo =
                    await _db.AnalisisSueloCalculos
                        .FirstOrDefaultAsync(x =>
                            x
                                .analisisSueloCalculoId ==
                            id &&
                            x.activo);

                if (calculo == null)
                {
                    await transaccion
                        .RollbackAsync();

                    return NotFound(new
                    {
                        success = false,
                        message =
                            "No se encontró el análisis solicitado."
                    });
                }

                AnalisisSuelo? analisis =
                    await _db.AnalisisSuelos
                        .FirstOrDefaultAsync(x =>
                            x.analisisSueloId ==
                                calculo
                                    .analisisSueloId &&
                            x.activo);

                if (analisis == null)
                {
                    await transaccion
                        .RollbackAsync();

                    return NotFound(new
                    {
                        success = false,
                        message =
                            "No se encontró el análisis de suelo relacionado."
                    });
                }

                AnalisisSueloGuardarRequestDto
                    datosAnalisis =
                        dto.datosAnalisis;

                AnalisisSueloCalculoResponseDto
                    resultadoAnual =
                        dto.requerimientoAnual;

                string identificador =
                    NormalizarTexto(
                        datosAnalisis
                            .identificadorAnalisisSuelo);

                bool identificadorExiste =
                    await _db.AnalisisSuelos
                        .AnyAsync(x =>
                            x.analisisSueloId !=
                                analisis
                                    .analisisSueloId &&
                            x
                                .identificadorAnalisisSuelo ==
                                identificador &&
                            x.activo);

                if (identificadorExiste)
                {
                    await transaccion
                        .RollbackAsync();

                    return Conflict(new
                    {
                        success = false,
                        message =
                            "Ya existe otro análisis de suelo con ese identificador."
                    });
                }

                ActualizarAnalisisOriginal(
                    analisis,
                    datosAnalisis,
                    identificador);

                await ReemplazarElementosOriginalesAsync(
                    analisis
                        .analisisSueloId,
                    datosAnalisis
                        .elementosQuimicos);

                ActualizarRequerimientoAnual(
                    calculo,
                    datosAnalisis,
                    resultadoAnual);

                await ReemplazarElementosCalculadosAsync(
                    id,
                    resultadoAnual
                        .elementos);

                /*
                 * Primero se desactivan los cálculos complementarios
                 * anteriores. Si todos llegan null, el análisis queda
                 * actualizado únicamente con requerimiento anual.
                 */
                await DesactivarModulosOpcionalesAsync(
                    id);

                ResultadoModulosOpcionales
                    modulos =
                        await GuardarModulosOpcionalesAsync(
                            id,
                            dto);

                await _db.SaveChangesAsync();

                await transaccion.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message =
                        CrearMensajeActualizado(dto),
                    data =
                        new GuardarTodoRespuestaDto
                        {
                            analisisSueloId =
                                analisis
                                    .analisisSueloId,
                            analisisSueloCalculoId =
                                id,
                            formulaNutricionalId =
                                modulos
                                    .FormulaNutricionalId,
                            enmiendaCalcareaId =
                                modulos
                                    .EnmiendaCalcareaId,
                            fertilizacionMixtaId =
                                modulos
                                    .FertilizacionMixtaId
                        }
                });
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync();

                return StatusCode(
                    StatusCodes
                        .Status500InternalServerError,
                    new
                    {
                        success = false,
                        message =
                            "Ocurrió un error al actualizar el análisis.",
                        detail = ex.Message,
                        inner =
                            ex.InnerException?
                                .Message ??
                            string.Empty
                    });
            }
        }

        // =============================================================
        // ELIMINAR LÓGICAMENTE
        // =============================================================
        [HttpDelete("{analisisSueloId:int}")]
        public async Task<IActionResult> Eliminar(
            int analisisSueloId)
        {
            await using var transaccion =
                await _db.Database
                    .BeginTransactionAsync();

            try
            {
                AnalisisSuelo? analisis =
                    await _db.AnalisisSuelos
                        .FirstOrDefaultAsync(x =>
                            x.analisisSueloId ==
                                analisisSueloId);

                if (analisis == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message =
                            "No se encontró el análisis de suelo."
                    });
                }

                if (!analisis.activo)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "El análisis ya se encuentra eliminado."
                    });
                }

                analisis.activo = false;

                List<
                    AnalisisSueloElementoQuimico>
                    elementosOriginales =
                        await _db
                            .AnalisisSueloElementos
                            .Where(x =>
                                x.analisisSueloId ==
                                    analisisSueloId)
                            .ToListAsync();

                elementosOriginales
                    .ForEach(x =>
                        x.activo = false);

                List<AnalisisSueloCalculo>
                    calculos =
                        await _db
                            .AnalisisSueloCalculos
                            .Where(x =>
                                x.analisisSueloId ==
                                    analisisSueloId)
                            .ToListAsync();

                calculos.ForEach(x =>
                    x.activo = false);

                foreach (
                    AnalisisSueloCalculo calculo
                    in calculos)
                {
                    await DesactivarElementosCalculadosAsync(
                        calculo
                            .analisisSueloCalculoId,
                        solamenteActivos: false);

                    await DesactivarModulosOpcionalesAsync(
                        calculo
                            .analisisSueloCalculoId,
                        solamenteActivos: false);
                }

                await _db.SaveChangesAsync();

                await transaccion.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message =
                        "El análisis de suelo y todos sus cálculos relacionados fueron eliminados correctamente."
                });
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync();

                return StatusCode(
                    StatusCodes
                        .Status500InternalServerError,
                    new
                    {
                        success = false,
                        message =
                            "Ocurrió un error al eliminar el análisis de suelo.",
                        detail = ex.Message
                    });
            }
        }

        // =============================================================
        // CREACIÓN DE ENTIDADES PRINCIPALES
        // =============================================================
        private static AnalisisSuelo
            CrearAnalisisOriginal(
                AnalisisSueloGuardarRequestDto dto,
                string identificador)
        {
            return new AnalisisSuelo
            {
                fechaAnalisisSuelo =
                    dto.fechaAnalisisSuelo,
                fechaCreacionAnalisisSuelo =
                    DateTime.Now,
                laboratorioAnalasisSuelo =
                    NormalizarTexto(
                        dto
                            .laboratorioAnalasisSuelo),
                identificadorAnalisisSuelo =
                    identificador,
                activo = true
            };
        }

        private static List<
            AnalisisSueloElementoQuimico>
            CrearElementosOriginales(
                int analisisSueloId,
                IEnumerable<
                    AnalisisSueloElementoEntradaDto>
                        elementos)
        {
            return elementos
                .Select(elemento =>
                    new
                        AnalisisSueloElementoQuimico
                        {
                            analisisSueloId =
                                analisisSueloId,
                            elementoQuimicosId =
                                elemento
                                    .elementoQuimicosId,
                            unidadMedidaId =
                                elemento
                                    .unidadMedidaId,
                            cantidadElemento =
                                Math.Round(
                                    elemento
                                        .cantidadElemento,
                                    4),
                            activo = true
                        })
                .ToList();
        }

        private static AnalisisSueloCalculo
            CrearRequerimientoAnual(
                int analisisSueloId,
                AnalisisSueloGuardarRequestDto
                    datos,
                AnalisisSueloCalculoResponseDto
                    resultado)
        {
            return new AnalisisSueloCalculo
            {
                cantidadQuintalesOro =
                    Math.Round(
                        resultado
                            .cantidadQuintalesOro,
                        4),
                tamanoFinca =
                    Math.Round(
                        resultado.tamanoFinca,
                        4),
                phAnalisisSuelo =
                    Math.Round(
                        resultado.ph,
                        4),
                materiaOrganica =
                    Math.Round(
                        resultado.materiaOrganica,
                        4),
                acidezTotal =
                    Math.Round(
                        resultado.acidezTotal,
                        4),
                recomendacionGeneral =
                    resultado
                        .recomendacionGeneral ??
                    string.Empty,
                observacion =
                    string.Join(
                        " | ",
                        resultado
                            .observaciones ??
                        new List<string>()),
                fechaCalculo =
                    DateTime.Now,
                activo = true,
                analisisSueloId =
                    analisisSueloId,
                terrenoId =
                    resultado.terrenoId,
                tipoCultivoId =
                    resultado.tipoCultivoId,
                tipoAnalisisSueloId =
                    resultado
                        .tipoAnalisisSueloId,
                usuarioId =
                    datos.usuarioId,
                unidadMedidaMateriaOrganicaId =
                    resultado
                        .unidadMedidaMateriaOrganicaId
            };
        }

        private static List<
            AnalisisSueloCalculoElementoQuimico>
            CrearElementosCalculados(
                int analisisSueloCalculoId,
                IEnumerable<
                    ResultadoElementoCalculoDto>
                        elementos)
        {
            return elementos
                .Select(elemento =>
                    new
                        AnalisisSueloCalculoElementoQuimico
                        {
                            analisisSueloCalculoId =
                                analisisSueloCalculoId,
                            elementoQuimicosId =
                                elemento
                                    .elementoQuimicosId,
                            unidadMedidaId =
                                elemento
                                    .unidadMedidaResultadoId,
                            cantidadIngresada =
                                Math.Round(
                                    elemento
                                        .cantidadIngresada,
                                    4),
                            cantidadConvertidaLbMz =
                                RedondearNullable(
                                    elemento
                                        .cantidadConvertidaLbMz),
                            requerimientoCalculado =
                                RedondearNullable(
                                    elemento
                                        .requerimientoCalculado),
                            clasificacion =
                                elemento
                                    .clasificacion ??
                                string.Empty,
                            observacion =
                                elemento
                                    .observacion ??
                                string.Empty,
                            incluirCalculosComplementarios =
                                elemento
                                    .incluirCalculosComplementarios,
                            activo = true
                        })
                .ToList();
        }

        // =============================================================
        // ACTUALIZACIÓN DE ENTIDADES PRINCIPALES
        // =============================================================
        private static void
            ActualizarAnalisisOriginal(
                AnalisisSuelo analisis,
                AnalisisSueloGuardarRequestDto dto,
                string identificador)
        {
            analisis.fechaAnalisisSuelo =
                dto.fechaAnalisisSuelo;

            analisis.laboratorioAnalasisSuelo =
                NormalizarTexto(
                    dto
                        .laboratorioAnalasisSuelo);

            analisis.identificadorAnalisisSuelo =
                identificador;

            analisis.activo = true;
        }

        private static void
            ActualizarRequerimientoAnual(
                AnalisisSueloCalculo calculo,
                AnalisisSueloGuardarRequestDto
                    datos,
                AnalisisSueloCalculoResponseDto
                    resultado)
        {
            calculo.cantidadQuintalesOro =
                Math.Round(
                    resultado
                        .cantidadQuintalesOro,
                    4);

            calculo.tamanoFinca =
                Math.Round(
                    resultado.tamanoFinca,
                    4);

            calculo.phAnalisisSuelo =
                Math.Round(
                    resultado.ph,
                    4);

            calculo.materiaOrganica =
                Math.Round(
                    resultado.materiaOrganica,
                    4);

            calculo.acidezTotal =
                Math.Round(
                    resultado.acidezTotal,
                    4);

            calculo.recomendacionGeneral =
                resultado
                    .recomendacionGeneral ??
                string.Empty;

            calculo.observacion =
                string.Join(
                    " | ",
                    resultado.observaciones ??
                    new List<string>());

            calculo.fechaCalculo =
                DateTime.Now;

            calculo.terrenoId =
                resultado.terrenoId;

            calculo.tipoCultivoId =
                resultado.tipoCultivoId;

            calculo.tipoAnalisisSueloId =
                resultado.tipoAnalisisSueloId;

            calculo.usuarioId =
                datos.usuarioId;

            calculo
                .unidadMedidaMateriaOrganicaId =
                    resultado
                        .unidadMedidaMateriaOrganicaId;

            calculo.activo = true;
        }

        private async Task
            ReemplazarElementosOriginalesAsync(
                int analisisSueloId,
                IEnumerable<
                    AnalisisSueloElementoEntradaDto>
                        nuevos)
        {
            List<
                AnalisisSueloElementoQuimico>
                anteriores =
                    await _db
                        .AnalisisSueloElementos
                        .Where(x =>
                            x.analisisSueloId ==
                                analisisSueloId &&
                            x.activo)
                        .ToListAsync();

            anteriores.ForEach(x =>
                x.activo = false);

            _db.AnalisisSueloElementos
                .AddRange(
                    CrearElementosOriginales(
                        analisisSueloId,
                        nuevos));
        }

        private async Task
            ReemplazarElementosCalculadosAsync(
                int analisisSueloCalculoId,
                IEnumerable<
                    ResultadoElementoCalculoDto>
                        nuevos)
        {
            await DesactivarElementosCalculadosAsync(
                analisisSueloCalculoId,
                solamenteActivos: true);

            _db
                .AnalisisSueloCalculoElementoQuimicos
                .AddRange(
                    CrearElementosCalculados(
                        analisisSueloCalculoId,
                        nuevos));
        }

        private async Task
            DesactivarElementosCalculadosAsync(
                int analisisSueloCalculoId,
                bool solamenteActivos)
        {
            IQueryable<
                AnalisisSueloCalculoElementoQuimico>
                query =
                    _db
                        .AnalisisSueloCalculoElementoQuimicos
                        .Where(x =>
                            x.analisisSueloCalculoId ==
                                analisisSueloCalculoId);

            if (solamenteActivos)
            {
                query = query.Where(x =>
                    x.activo);
            }

            List<
                AnalisisSueloCalculoElementoQuimico>
                elementos =
                    await query.ToListAsync();

            elementos.ForEach(x =>
                x.activo = false);
        }

        // =============================================================
        // MÓDULOS OPCIONALES
        // =============================================================
        private async Task<
            ResultadoModulosOpcionales>
            GuardarModulosOpcionalesAsync(
                int analisisSueloCalculoId,
                GuardarTodoDto dto)
        {
            ResultadoModulosOpcionales
                resultado = new();

            if (dto.balanceNutricional != null)
            {
                FormulaNutricional formula =
                    await GuardarFormulaAsync(
                        analisisSueloCalculoId,
                        dto
                            .balanceNutricional);

                resultado.FormulaNutricionalId =
                    formula
                        .formulaNutricionalId;
            }

            if (dto.enmiendaCalcarea != null)
            {
                EnmiendaCalcarea enmienda =
                    await GuardarEnmiendaAsync(
                        analisisSueloCalculoId,
                        dto.enmiendaCalcarea);

                resultado.EnmiendaCalcareaId =
                    enmienda
                        .enmiendaCalcareaId;
            }

            if (dto.fertilizacionMixta != null)
            {
                FertilizacionMixta mixta =
                    await GuardarMixtaAsync(
                        analisisSueloCalculoId,
                        dto
                            .fertilizacionMixta);

                resultado.FertilizacionMixtaId =
                    mixta
                        .fertilizacionMixtaId;
            }

            return resultado;
        }

        private async Task<FormulaNutricional>
            GuardarFormulaAsync(
                int analisisSueloCalculoId,
                FormulaNutricionalGuardarDto dto)
        {
            FormulaNutricionalRespuestaDto r =
                dto.resultado;

            FormulaNutricional formula =
                new()
                {
                    analisisSueloCalculoId =
                        analisisSueloCalculoId,
                    nombreFormula =
                        r.nombreFormula?
                            .Trim() ??
                        string.Empty,
                    fechaCreacion =
                        DateTime.Now,
                    totalLibras =
                        Math.Round(
                            r.totalLibras,
                            4),
                    mezclaTotalQq =
                        Math.Round(
                            r.mezclaTotalQq,
                            4),
                    totalPlantas =
                        r.totalPlantas,
                    totalAplicaciones =
                        r.totalAplicaciones,
                    esComplementoFertilizacionMixta =
                        dto
                            .esComplementoFertilizacionMixta,
                    totalOnzas =
                        Math.Round(
                            r.totalOnzas,
                            4),
                    precioTotalFormula =
                        Math.Round(
                            r.precioTotalFormula,
                            4),
                    precioPorAplicacion =
                        Math.Round(
                            r.precioPorAplicacion,
                            4),
                    dosisPlantaAnualOz =
                        Math.Round(
                            r.dosisPlantaAnualOz,
                            4),
                    dosisPlantaPorAplicacionOz =
                        Math.Round(
                            r
                                .dosisPlantaPorAplicacionOz,
                            4),
                    terrenoId =
                        dto.terrenoId,
                    activo = true
                };

            _db.formulaNutricional.Add(
                formula);

            await _db.SaveChangesAsync();

            Dictionary<string, int>
                elementosPorSimbolo =
                    await ObtenerElementosPorSimboloAsync();

            for (
                int indice = 0;
                indice < dto.items.Count;
                indice++)
            {
                FormulaNutricionalGuardarItemDto
                    item = dto.items[indice];

                FormulaNutricionalDetalleRespuestaDto
                    detalleResultado =
                        r.detalle[indice];

                FormulaNutricionalDetalle detalle =
                    new()
                    {
                        formulaNutricionalId =
                            formula
                                .formulaNutricionalId,
                        fuenteNutrientesId =
                            item
                                .fuenteNutrientesId,
                        elementoQuimicosId =
                            item
                                .elementoQuimicosId,
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

                _db.formulaNutricionalDetalle
                    .Add(detalle);

                await _db.SaveChangesAsync();

                foreach (
                    KeyValuePair<string, decimal>
                        aporte
                    in detalleResultado.aportes)
                {
                    string simbolo =
                        NormalizarSimbolo(
                            aporte.Key);

                    if (!elementosPorSimbolo
                        .TryGetValue(
                            simbolo,
                            out int elementoAporteId))
                    {
                        throw new
                            InvalidOperationException(
                                $"No se encontró el elemento químico del aporte '{aporte.Key}'.");
                    }

                    _db
                        .formulaNutricionalAporte
                        .Add(
                            new
                                FormulaNutricionalAporte
                                {
                                    formulaNutricionalDetalleId =
                                        detalle
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

            return formula;
        }

        private async Task<EnmiendaCalcarea>
            GuardarEnmiendaAsync(
                int analisisSueloCalculoId,
                EnmiendaCalcareaGuardarDto dto)
        {
            EnmiendaCalcareaRespuestaDto r =
                dto.resultado;

            EnmiendaCalcarea enmienda =
                new()
                {
                    analisisSueloCalculoId =
                        analisisSueloCalculoId,
                    nombreAnalisis =
                        r.nombreAnalisis?
                            .Trim() ??
                        string.Empty,
                    fuenteNutrientesId =
                        dto.fuenteNutrientesId,
                    terrenoId =
                        r.terrenoId,
                    totalPlantas =
                        r.totalPlantas,
                    totalAplicaciones =
                        r.totalAplicaciones,
                    ph =
                        Math.Round(r.ph, 4),
                    ca =
                        Math.Round(r.ca, 4),
                    mg =
                        Math.Round(r.mg, 4),
                    k =
                        Math.Round(r.k, 4),
                    acidezTotal =
                        Math.Round(
                            r.acidezTotal,
                            4),
                    saturacionDeseada =
                        Math.Round(
                            r.saturacionDeseada,
                            4),
                    prnt =
                        Math.Round(r.prnt, 4),
                    sumaBases =
                        Math.Round(
                            r.sumaBases,
                            4),
                    cice =
                        Math.Round(r.cice, 4),
                    saturacionActual =
                        Math.Round(
                            r.saturacionActual,
                            4),
                    necesidadEncaladoTonHa =
                        Math.Round(
                            r.necesidadEncaladoTonHa,
                            4),
                    necesidadEncaladoKgHa =
                        Math.Round(
                            r.necesidadEncaladoKgHa,
                            4),
                    necesidadEncaladoLbHa =
                        Math.Round(
                            r.necesidadEncaladoLbHa,
                            4),
                    necesidadEncaladoLbMz =
                        Math.Round(
                            r.necesidadEncaladoLbMz,
                            4),
                    necesidadEncaladoOzMz =
                        Math.Round(
                            r.necesidadEncaladoOzMz,
                            4),
                    dosisPlantaAnualOz =
                        Math.Round(
                            r.dosisPlantaAnualOz,
                            4),
                    dosisPlantaPorAplicacionOz =
                        Math.Round(
                            r
                                .dosisPlantaPorAplicacionOz,
                            4),
                    fechaCreacion =
                        DateTime.Now,
                    activo = true
                };

            _db.enmiendaCalcarea.Add(
                enmienda);

            await _db.SaveChangesAsync();

            return enmienda;
        }

        private async Task<FertilizacionMixta>
            GuardarMixtaAsync(
                int analisisSueloCalculoId,
                FertilizacionMixtaRespuestaDto dto)
        {
            FertilizacionMixta mixta =
                new()
                {
                    analisisSueloCalculoId =
                        analisisSueloCalculoId,
                    fechaCalculo =
                        DateTime.Now,
                    observacion =
                        dto.observacion ??
                        string.Empty,
                    esComplementoBalance =
                        dto.esComplementoBalance,
                    activo = true
                };

            _db.fertilizacionMixta.Add(
                mixta);

            await _db.SaveChangesAsync();

            _db.fertilizacionMixtaFuente
                .AddRange(
                    dto.fuentes.Select(fuente =>
                        new
                            FertilizacionMixtaFuente
                            {
                                fertilizacionMixtaId =
                                    mixta
                                        .fertilizacionMixtaId,
                                fuenteNutrientesId =
                                    fuente
                                        .fuenteNutrientesId,
                                cantidadQq =
                                    Math.Round(
                                        fuente
                                            .cantidadQq,
                                        4),
                                activo = true
                            }));

            _db.fertilizacionMixtaDetalle
                .AddRange(
                    dto.detalles.Select(detalle =>
                        new
                            FertilizacionMixtaDetalle
                            {
                                fertilizacionMixtaId =
                                    mixta
                                        .fertilizacionMixtaId,
                                elementoQuimicosId =
                                    detalle
                                        .elementoQuimicosId,
                                requerimientoOriginal =
                                    Math.Round(
                                        detalle
                                            .exportable,
                                        4),
                                aporteOrganico =
                                    Math.Round(
                                        detalle
                                            .aporteOrganico,
                                        4),
                                diferencia =
                                    Math.Round(
                                        detalle
                                            .diferencia,
                                        4),
                                deficit =
                                    Math.Round(
                                        detalle
                                            .deficit,
                                        4),
                                sobrante =
                                    Math.Round(
                                        detalle
                                            .sobrante,
                                        4),
                                activo = true
                            }));

            await _db.SaveChangesAsync();

            return mixta;
        }

        private async Task
            DesactivarModulosOpcionalesAsync(
                int analisisSueloCalculoId,
                bool solamenteActivos = true)
        {
            IQueryable<FormulaNutricional>
                formulasQuery =
                    _db.formulaNutricional
                        .Where(x =>
                            x.analisisSueloCalculoId ==
                                analisisSueloCalculoId);

            if (solamenteActivos)
            {
                formulasQuery =
                    formulasQuery.Where(x =>
                        x.activo);
            }

            List<FormulaNutricional> formulas =
                await formulasQuery
                    .ToListAsync();

            formulas.ForEach(x =>
                x.activo = false);

            List<int> formulaIds =
                formulas
                    .Select(x =>
                        x.formulaNutricionalId)
                    .ToList();

            if (formulaIds.Count > 0)
            {
                List<FormulaNutricionalDetalle>
                    detalles =
                        await _db
                            .formulaNutricionalDetalle
                            .Where(x =>
                                formulaIds.Contains(
                                    x
                                        .formulaNutricionalId))
                            .ToListAsync();

                detalles.ForEach(x =>
                    x.activo = false);

                List<int> detalleIds =
                    detalles
                        .Select(x =>
                            x
                                .formulaNutricionalDetalleId)
                        .ToList();

                if (detalleIds.Count > 0)
                {
                    List<
                        FormulaNutricionalAporte>
                        aportes =
                            await _db
                                .formulaNutricionalAporte
                                .Where(x =>
                                    detalleIds.Contains(
                                        x
                                            .formulaNutricionalDetalleId))
                                .ToListAsync();

                    aportes.ForEach(x =>
                        x.activo = false);
                }
            }

            IQueryable<EnmiendaCalcarea>
                enmiendasQuery =
                    _db.enmiendaCalcarea
                        .Where(x =>
                            x.analisisSueloCalculoId ==
                                analisisSueloCalculoId);

            if (solamenteActivos)
            {
                enmiendasQuery =
                    enmiendasQuery.Where(x =>
                        x.activo);
            }

            List<EnmiendaCalcarea>
                enmiendas =
                    await enmiendasQuery
                        .ToListAsync();

            enmiendas.ForEach(x =>
                x.activo = false);

            IQueryable<FertilizacionMixta>
                mixtasQuery =
                    _db.fertilizacionMixta
                        .Where(x =>
                            x.analisisSueloCalculoId ==
                                analisisSueloCalculoId);

            if (solamenteActivos)
            {
                mixtasQuery =
                    mixtasQuery.Where(x =>
                        x.activo);
            }

            List<FertilizacionMixta> mixtas =
                await mixtasQuery
                    .ToListAsync();

            mixtas.ForEach(x =>
                x.activo = false);

            List<int> mixtaIds =
                mixtas
                    .Select(x =>
                        x.fertilizacionMixtaId)
                    .ToList();

            if (mixtaIds.Count > 0)
            {
                List<FertilizacionMixtaFuente>
                    fuentes =
                        await _db
                            .fertilizacionMixtaFuente
                            .Where(x =>
                                mixtaIds.Contains(
                                    x
                                        .fertilizacionMixtaId))
                            .ToListAsync();

                fuentes.ForEach(x =>
                    x.activo = false);

                List<FertilizacionMixtaDetalle>
                    detalles =
                        await _db
                            .fertilizacionMixtaDetalle
                            .Where(x =>
                                mixtaIds.Contains(
                                    x
                                        .fertilizacionMixtaId))
                            .ToListAsync();

                detalles.ForEach(x =>
                    x.activo = false);
            }

            await _db.SaveChangesAsync();
        }

        // =============================================================
        // CONSULTAS DE DETALLE
        // =============================================================
        private async Task<object?>
            ObtenerFormulaCompletaAsync(
                int analisisSueloCalculoId)
        {
            var formula =
                await _db.formulaNutricional
                    .AsNoTracking()
                    .Where(x =>
                        x.analisisSueloCalculoId ==
                            analisisSueloCalculoId &&
                        x.activo)
                    .Select(x => new
                    {
                        x.formulaNutricionalId,
                        x.nombreFormula,
                        x.fechaCreacion,
                        x.totalLibras,
                        x.mezclaTotalQq,
                        x.totalPlantas,
                        x.totalAplicaciones,
                        x
                            .esComplementoFertilizacionMixta,
                        x.totalOnzas,
                        x.precioTotalFormula,
                        x.precioPorAplicacion,
                        x.dosisPlantaAnualOz,
                        x
                            .dosisPlantaPorAplicacionOz,
                        x.terrenoId
                    })
                    .FirstOrDefaultAsync();

            if (formula == null)
                return null;

            var detalles =
                await _db
                    .formulaNutricionalDetalle
                    .AsNoTracking()
                    .Where(x =>
                        x.formulaNutricionalId ==
                            formula
                                .formulaNutricionalId &&
                        x.activo)
                    .Select(x => new
                    {
                        x
                            .formulaNutricionalDetalleId,
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

            List<int> detalleIds =
                detalles
                    .Select(x =>
                        x
                            .formulaNutricionalDetalleId)
                    .ToList();

            var aportes =
                await _db
                    .formulaNutricionalAporte
                    .AsNoTracking()
                    .Where(x =>
                        detalleIds.Contains(
                            x
                                .formulaNutricionalDetalleId) &&
                        x.activo)
                    .Select(x => new
                    {
                        x
                            .formulaNutricionalAporteId,
                        x
                            .formulaNutricionalDetalleId,
                        x.elementoQuimicosId,
                        x.valor
                    })
                    .ToListAsync();

            return new
            {
                formula,
                detalles,
                aportes
            };
        }

        private async Task<object?>
            ObtenerEnmiendaAsync(
                int analisisSueloCalculoId)
        {
            return await _db.enmiendaCalcarea
                .AsNoTracking()
                .Where(x =>
                    x.analisisSueloCalculoId ==
                        analisisSueloCalculoId &&
                    x.activo)
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
                    x
                        .necesidadEncaladoTonHa,
                    x
                        .necesidadEncaladoKgHa,
                    x
                        .necesidadEncaladoLbHa,
                    x
                        .necesidadEncaladoLbMz,
                    x
                        .necesidadEncaladoOzMz,
                    x.dosisPlantaAnualOz,
                    x
                        .dosisPlantaPorAplicacionOz,
                    x.fechaCreacion
                })
                .FirstOrDefaultAsync();
        }

        private async Task<object?>
            ObtenerMixtaCompletaAsync(
                int analisisSueloCalculoId)
        {
            var mixta =
                await _db.fertilizacionMixta
                    .AsNoTracking()
                    .Where(x =>
                        x.analisisSueloCalculoId ==
                            analisisSueloCalculoId &&
                        x.activo)
                    .Select(x => new
                    {
                        x.fertilizacionMixtaId,
                        x.fechaCalculo,
                        x.observacion,
                        x.esComplementoBalance
                    })
                    .FirstOrDefaultAsync();

            if (mixta == null)
                return null;

            var fuentes =
                await _db
                    .fertilizacionMixtaFuente
                    .AsNoTracking()
                    .Where(x =>
                        x.fertilizacionMixtaId ==
                            mixta
                                .fertilizacionMixtaId &&
                        x.activo)
                    .Select(x => new
                    {
                        x
                            .fertilizacionMixtaFuenteId,
                        x.fuenteNutrientesId,
                        x.cantidadQq
                    })
                    .ToListAsync();

            var detalles =
                await _db
                    .fertilizacionMixtaDetalle
                    .AsNoTracking()
                    .Where(x =>
                        x.fertilizacionMixtaId ==
                            mixta
                                .fertilizacionMixtaId &&
                        x.activo)
                    .Select(x => new
                    {
                        x
                            .fertilizacionMixtaDetalleId,
                        x.elementoQuimicosId,
                        x.requerimientoOriginal,
                        x.aporteOrganico,
                        x.diferencia,
                        x.deficit,
                        x.sobrante
                    })
                    .ToListAsync();

            return new
            {
                mixta,
                fuentes,
                detalles
            };
        }

        // =============================================================
        // VALIDACIONES
        // =============================================================
        private static string? ValidarSolicitud(
            GuardarTodoDto dto)
        {
            if (dto.datosAnalisis == null)
            {
                return
                    "Debe enviar los datos del análisis.";
            }

            if (string.IsNullOrWhiteSpace(
                    dto
                        .datosAnalisis
                        .identificadorAnalisisSuelo))
            {
                return
                    "El identificador del análisis es obligatorio.";
            }

            if (string.IsNullOrWhiteSpace(
                    dto
                        .datosAnalisis
                        .laboratorioAnalasisSuelo))
            {
                return
                    "El laboratorio del análisis es obligatorio.";
            }

            if (dto.datosAnalisis
                    .elementosQuimicos == null ||
                !dto.datosAnalisis
                    .elementosQuimicos.Any())
            {
                return
                    "Debe enviar los elementos originales del análisis.";
            }

            if (dto.requerimientoAnual == null)
            {
                return
                    "Debe enviar el resultado del requerimiento anual.";
            }

            if (dto.requerimientoAnual
                    .elementos == null ||
                !dto.requerimientoAnual
                    .elementos.Any())
            {
                return
                    "El requerimiento anual no contiene elementos calculados.";
            }

            HashSet<int> elementosIncluidos =
                dto.requerimientoAnual
                    .elementos
                    .Where(x =>
                        x
                            .incluirCalculosComplementarios)
                    .Select(x =>
                        x.elementoQuimicosId)
                    .ToHashSet();

            if (dto.balanceNutricional != null)
            {
                FormulaNutricionalGuardarDto
                    balance =
                        dto.balanceNutricional;

                if (balance.resultado == null)
                {
                    return
                        "El balance nutricional no contiene resultado.";
                }

                if (balance.items == null ||
                    !balance.items.Any())
                {
                    return
                        "Debe enviar los IDs de los detalles de la fórmula nutricional.";
                }

                if (balance.resultado.detalle == null ||
                    !balance.resultado.detalle.Any())
                {
                    return
                        "El balance nutricional no contiene detalles.";
                }

                if (balance.items.Count !=
                    balance.resultado
                        .detalle.Count)
                {
                    return
                        "La cantidad de items no coincide con los detalles calculados de la fórmula.";
                }

                if (dto.requerimientoAnual
                        .terrenoId !=
                    balance.terrenoId)
                {
                    return
                        "El terreno del requerimiento anual no coincide con el de la fórmula nutricional.";
                }

                if (balance.resultado
                        .mezclaTotalQq <= 0)
                {
                    return
                        "La mezcla total del balance debe ser mayor que cero. Recalcule el balance antes de guardar.";
                }

                if (balance.items.Any(x =>
                        !elementosIncluidos
                            .Contains(
                                x
                                    .elementoQuimicosId)))
                {
                    return
                        "El balance contiene un elemento que el usuario excluyó de los cálculos complementarios.";
                }

                if (balance
                        .esComplementoFertilizacionMixta &&
                    dto.fertilizacionMixta ==
                        null)
                {
                    return
                        "El balance marcado como complemento requiere una fertilización mixta calculada.";
                }
            }

            if (dto.enmiendaCalcarea != null)
            {
                if (dto.enmiendaCalcarea
                        .resultado == null)
                {
                    return
                        "La enmienda calcárea no contiene resultado.";
                }

                if (dto.enmiendaCalcarea
                        .fuenteNutrientesId <= 0)
                {
                    return
                        "La fuente de la enmienda calcárea no es válida.";
                }

                if (dto.enmiendaCalcarea
                        .resultado
                        .terrenoId
                        .HasValue &&
                    dto.enmiendaCalcarea
                        .resultado
                        .terrenoId
                        .Value !=
                    dto.requerimientoAnual
                        .terrenoId)
                {
                    return
                        "El terreno de la enmienda no coincide con el requerimiento anual.";
                }
            }

            if (dto.fertilizacionMixta != null)
            {
                FertilizacionMixtaRespuestaDto
                    mixta =
                        dto.fertilizacionMixta;

                if (mixta.fuentes == null ||
                    !mixta.fuentes.Any())
                {
                    return
                        "La fertilización mixta no contiene fuentes.";
                }

                if (mixta.detalles == null ||
                    !mixta.detalles.Any())
                {
                    return
                        "La fertilización mixta no contiene detalles.";
                }

                if (mixta.detalles.Any(x =>
                        !elementosIncluidos
                            .Contains(
                                x
                                    .elementoQuimicosId)))
                {
                    return
                        "La fertilización mixta contiene un elemento que el usuario excluyó de los cálculos complementarios.";
                }

                if (mixta.esComplementoBalance &&
                    dto.balanceNutricional ==
                        null)
                {
                    return
                        "La fertilización mixta marcada como complemento necesita un balance calculado.";
                }
            }

            return null;
        }

        private static void NormalizarBalance(
            GuardarTodoDto dto)
        {
            FormulaNutricionalRespuestaDto?
                resultado =
                    dto.balanceNutricional?
                        .resultado;

            if (resultado == null)
                return;

            resultado.detalle ??=
                new List<
                    FormulaNutricionalDetalleRespuestaDto>();

            if (resultado.totalLibras <= 0)
            {
                resultado.totalLibras =
                    resultado.detalle
                        .Sum(x => x.lb);
            }

            if (resultado.mezclaTotalQq <= 0)
            {
                resultado.mezclaTotalQq =
                    resultado.detalle
                        .Sum(x => x.qq);
            }

            if (resultado.mezclaTotalQq <= 0 &&
                resultado.totalLibras > 0)
            {
                resultado.mezclaTotalQq =
                    resultado.totalLibras /
                    100m;
            }

            resultado.totalLibras =
                Math.Round(
                    resultado.totalLibras,
                    4);

            resultado.mezclaTotalQq =
                Math.Round(
                    resultado.mezclaTotalQq,
                    4);
        }

        private async Task<
            Dictionary<string, int>>
            ObtenerElementosPorSimboloAsync()
        {
            List<ElementoQuimico> activos =
                await _db.elementoQuimico
                    .AsNoTracking()
                    .Where(x => x.activo)
                    .ToListAsync();

            return activos
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x
                            .simboloElementoQuimico))
                .GroupBy(x =>
                    NormalizarSimbolo(
                        x
                            .simboloElementoQuimico))
                .ToDictionary(
                    grupo => grupo.Key,
                    grupo =>
                        grupo
                            .First()
                            .elementoQuimicosId);
        }

        private static decimal?
            RedondearNullable(
                decimal? valor)
        {
            return valor.HasValue
                ? Math.Round(
                    valor.Value,
                    4)
                : null;
        }

        private static string
            NormalizarSimbolo(
                string? simbolo)
        {
            return (
                simbolo ??
                string.Empty)
                .Trim()
                .ToLowerInvariant();
        }

        private static string
            NormalizarTexto(
                string? texto)
        {
            return (
                texto ??
                string.Empty)
                .Trim()
                .ToUpperInvariant();
        }

        private static string
            CrearMensajeGuardado(
                GuardarTodoDto dto)
        {
            if (NoTieneModulosOpcionales(dto))
            {
                return
                    "El análisis y su requerimiento anual fueron guardados correctamente.";
            }

            return
                "El análisis completo fue guardado correctamente.";
        }

        private static string
            CrearMensajeActualizado(
                GuardarTodoDto dto)
        {
            if (NoTieneModulosOpcionales(dto))
            {
                return
                    "El análisis fue actualizado únicamente con su requerimiento anual.";
            }

            return
                "El análisis completo fue actualizado correctamente.";
        }

        private static bool
            NoTieneModulosOpcionales(
                GuardarTodoDto dto)
        {
            return
                dto.balanceNutricional ==
                    null &&
                dto.enmiendaCalcarea ==
                    null &&
                dto.fertilizacionMixta ==
                    null;
        }

        private sealed class
            ResultadoModulosOpcionales
        {
            public int?
                FormulaNutricionalId
            {
                get;
                set;
            }

            public int?
                EnmiendaCalcareaId
            {
                get;
                set;
            }

            public int?
                FertilizacionMixtaId
            {
                get;
                set;
            }
        }
    }
}
