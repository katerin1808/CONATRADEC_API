using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/analisis-suelo")]
public class AnalisisSueloController : ControllerBase
{

    private readonly DBContext _db;
    private readonly AnalisisSueloCalculoService _calculoService;

    public AnalisisSueloController(DBContext db, AnalisisSueloCalculoService calculoService)
    {
        _db = db;
        _calculoService = calculoService;
    }

    // ============================
    // CALCULAR ANÁLISIS DE SUELO
    // ============================
    [HttpPost("calcular")]
    public async Task<IActionResult> Calcular([FromBody] AnalisisSueloCalculoRequestDto dto)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var resultado = await _calculoService.CalcularAsync(dto);

            return Ok(new
            {
                success = true,
                message = "Cálculo realizado correctamente.",
                data = resultado
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    // ============================
    // GUARDAR ANÁLISIS CALCULADO
    // ============================
    [HttpPost("guardar-calculo")]
    public async Task<IActionResult> GuardarCalculo([FromBody] AnalisisSueloGuardarRequestDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var existeIdentificador = await _db.AnalisisSuelos
            .AnyAsync(x => x.identificadorAnalisisSuelo == dto.identificadorAnalisisSuelo.Trim().ToUpper());

        if (existeIdentificador)
        {
            return Conflict(new
            {
                success = false,
                message = "Ya existe un análisis de suelo con ese identificador."
            });
        }

        var terrenoExiste = await _db.Terreno
            .AnyAsync(x => x.terrenoId == dto.terrenoId && x.activo);

        if (!terrenoExiste)
        {
            return NotFound(new
            {
                success = false,
                message = "El terreno indicado no existe o está inactivo."
            });
        }

        using var transaccion = await _db.Database.BeginTransactionAsync();

        try
        {
            // 1. Calcular primero
            var resultado = await _calculoService.CalcularAsync(dto);

            // 2. Guardar encabezado del análisis
            var analisis = new AnalisisSuelo
            {
                // Fecha del análisis de laboratorio, enviada por el usuario
                fechaAnalisisSuelo = dto.fechaAnalisisSuelo,

                // Fecha en que el sistema registra/procesa el análisis
                fechaCreacionAnalisisSuelo = DateTime.Now,

                laboratorioAnalasisSuelo = dto.laboratorioAnalasisSuelo.Trim().ToUpper(),
                identificadorAnalisisSuelo = dto.identificadorAnalisisSuelo.Trim().ToUpper(),
                activo = true
            };

            _db.AnalisisSuelos.Add(analisis);
            await _db.SaveChangesAsync();

            // 3. Guardar elementos ingresados por el usuario
            foreach (var elemento in dto.elementosQuimicos)
            {
                var detalle = new AnalisisSueloElementoQuimico
                {
                    analisisSueloId = analisis.analisisSueloId,
                    elementoQuimicosId = elemento.elementoQuimicosId,
                    unidadMedidaId = elemento.unidadMedidaId,
                    cantidadElemento = elemento.cantidadElemento,
                    activo = true
                };

                _db.AnalisisSueloElementos.Add(detalle);
            }

            await _db.SaveChangesAsync();

            // 4. Guardar resumen del cálculo
            var calculo = new AnalisisSueloCalculo
            {
                cantidadQuintalesOro = dto.cantidadQuintalesOro,
                tamanoFinca = dto.tamanoFinca,
                phAnalisisSuelo = dto.ph,
                materiaOrganica = dto.materiaOrganica,
                unidadMedidaMateriaOrganicaId = dto.unidadMedidaMateriaOrganicaId,
                acidezTotal = dto.acidezTotal,

                recomendacionGeneral = resultado.recomendacionGeneral,
                observacion = string.Join(" | ", resultado.observaciones),

                fechaCalculo = DateTime.Now,
                activo = true,

                analisisSueloId = analisis.analisisSueloId,
                terrenoId = dto.terrenoId,
                tipoCultivoId = dto.tipoCultivoId,
                tipoAnalisisSueloId = dto.tipoAnalisisSueloId,
                usuarioId = dto.usuarioId
            };

            _db.AnalisisSueloCalculos.Add(calculo);
            await _db.SaveChangesAsync();

            // 5. Guardar resultado calculado por elemento químico

            var unidadResultado = await _db.UnidadMedidas
                .FirstOrDefaultAsync(x => x.nombreUnidadMedida == "lb/Mz" && x.activo);

            if (unidadResultado == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "No existe la unidad de medida lb/Mz en la base de datos."
                });
            }

            foreach (var elementoCalculado in resultado.elementos)
            {
                var detalleCalculo = new AnalisisSueloCalculoElementoQuimico
                {
                    analisisSueloCalculoId = calculo.analisisSueloCalculoId,
                    elementoQuimicosId = elementoCalculado.elementoQuimicosId,
                    unidadMedidaId = unidadResultado.unidadMedidaId,

                    cantidadIngresada = elementoCalculado.cantidadIngresada,
                    cantidadConvertidaLbMz = elementoCalculado.cantidadConvertidaLbMz,
                    requerimientoCalculado = elementoCalculado.requerimientoCalculado,

                    clasificacion = elementoCalculado.clasificacion,
                    observacion = elementoCalculado.observacion,
                    activo = true
                };

                _db.AnalisisSueloCalculoElementoQuimicos.Add(detalleCalculo);
            }

            await _db.SaveChangesAsync();

            await transaccion.CommitAsync();

            return Ok(new
            {
                success = true,
                message = "Análisis de suelo calculado y guardado correctamente.",
                data = new
                {
                    analisisSueloId = analisis.analisisSueloId,
                    analisisSueloCalculoId = calculo.analisisSueloCalculoId,
                    identificadorAnalisisSuelo = analisis.identificadorAnalisisSuelo,
                    resultado
                }
            });
        }
        catch (Exception ex)
        {
            await transaccion.RollbackAsync();

            return BadRequest(new
            {
                success = false,
                message = "Error al guardar el análisis de suelo.",
                error = ex.Message,
                innerError = ex.InnerException?.Message,
                detalle = ex.GetBaseException().Message
            });
        }
    }

    // ============================
    // OBTENER ANÁLISIS COMPLETO
    // ============================
    [HttpGet("{id:int}")]
    public async Task<IActionResult> ObtenerPorId(int id)
    {
        var analisis = await _db.AnalisisSuelos
            .FirstOrDefaultAsync(a => a.analisisSueloId == id && a.activo);

        if (analisis == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Análisis de suelo no encontrado."
            });
        }

        var calculo = await _db.AnalisisSueloCalculos
            .Where(c => c.analisisSueloId == id && c.activo)
            .OrderByDescending(c => c.fechaCalculo)
            .FirstOrDefaultAsync();

        var terreno = calculo == null
            ? null
            : await _db.Terreno.FirstOrDefaultAsync(t => t.terrenoId == calculo.terrenoId);

        var tipoCultivo = calculo == null
            ? null
            : await _db.TipoCultivos.FirstOrDefaultAsync(t => t.tipoCultivoId == calculo.tipoCultivoId);

        var tipoAnalisis = calculo == null
            ? null
            : await _db.TipoAnalisisSuelos.FirstOrDefaultAsync(t => t.tipoAnalisisSueloId == calculo.tipoAnalisisSueloId);

        var elementosIngresados = await _db.AnalisisSueloElementos
            .Where(e => e.analisisSueloId == id && e.activo)
            .Select(e => new
            {
                e.analisisSueloElementoQuimicoId,
                e.elementoQuimicosId,
                simboloElementoQuimico = e.ElementoQuimico.simboloElementoQuimico.Trim(),
                nombreElementoQuimico = e.ElementoQuimico.nombreElementoQuimico.Trim(),
                e.cantidadElemento,
                e.unidadMedidaId,
                nombreUnidadMedida = e.UnidadMedida.nombreUnidadMedida.Trim(),
                e.activo
            })
            .ToListAsync();

        var elementosCalculados = new List<object>();

        if (calculo != null)
        {
            var elementosCalculadosRaw = await _db.AnalisisSueloCalculoElementoQuimicos
             .Where(e => e.analisisSueloCalculoId == calculo.analisisSueloCalculoId && e.activo)
            .Select(e => new
            {
                e.analisisSueloCalculoElementoQuimicoId,
                e.elementoQuimicosId,
                simboloElementoQuimico = e.ElementoQuimico.simboloElementoQuimico.Trim(),
                nombreElementoQuimico = e.ElementoQuimico.nombreElementoQuimico.Trim(),
                e.cantidadIngresada,
                e.cantidadConvertidaLbMz,
                e.requerimientoCalculado,
                e.unidadMedidaId,
                nombreUnidadResultado = e.UnidadMedida == null ? null : e.UnidadMedida.nombreUnidadMedida.Trim(),
                e.clasificacion,
                e.observacion,
                e.activo
            })
            .ToListAsync();

            elementosCalculados = elementosCalculadosRaw
                .Select(e => (object)e)
                .ToList();
        }

        return Ok(new
        {
            success = true,
            message = "Análisis de suelo obtenido correctamente.",
            data = new
            {
                analisisSuelo = new
                {
                    analisis.analisisSueloId,
                    analisis.fechaAnalisisSuelo,
                    analisis.fechaCreacionAnalisisSuelo,
                    analisis.laboratorioAnalasisSuelo,
                    analisis.identificadorAnalisisSuelo,
                    analisis.activo
                },

                terreno = terreno == null ? null : new
                {
                    terreno.terrenoId,
                    terreno.codigoTerreno,
                    terreno.nombrePropietarioTerreno,
                    terreno.direccionTerreno,
                    terreno.extensionManzanaTerreno,
                    terreno.cantidadQuintalesOro,
                    terreno.latitud,
                    terreno.longitud
                },

                tipoCultivo = tipoCultivo == null ? null : new
                {
                    tipoCultivo.tipoCultivoId,
                    tipoCultivo.nombreTipoCultivo,
                    tipoCultivo.descripcionTipoCultivo
                },

                tipoAnalisisSuelo = tipoAnalisis == null ? null : new
                {
                    tipoAnalisis.tipoAnalisisSueloId,
                    tipoAnalisis.nombreTipoAnalisisSuelo,
                    tipoAnalisis.descripcionTipoAnalisisSuelo
                },

                calculo = calculo == null ? null : new
                {
                    calculo.analisisSueloCalculoId,
                    calculo.cantidadQuintalesOro,
                    calculo.tamanoFinca,
                    calculo.phAnalisisSuelo,
                    calculo.materiaOrganica,
                    calculo.acidezTotal,
                    calculo.recomendacionGeneral,
                    calculo.observacion,
                    calculo.fechaCalculo,
                    calculo.usuarioId
                },

                elementosIngresados,
                elementosCalculados
            }
        });
    }

    // ============================
    // LISTAR ANÁLISIS CON RESUMEN
    // ============================
    [HttpGet("listar")]
    public async Task<IActionResult> Listar()
    {
        var analisisLista = await _db.AnalisisSuelos
            .Where(a => a.activo)
            .OrderByDescending(a => a.analisisSueloId)
            .ToListAsync();

        var respuesta = new List<object>();

        foreach (var analisis in analisisLista)
        {
            var calculo = await _db.AnalisisSueloCalculos
                .Where(c => c.analisisSueloId == analisis.analisisSueloId && c.activo)
                .OrderByDescending(c => c.fechaCalculo)
                .FirstOrDefaultAsync();

            object? terrenoResumen = null;
            object? tipoCultivoResumen = null;
            object? tipoAnalisisResumen = null;

            if (calculo != null)
            {
                var terreno = await _db.Terreno
                    .FirstOrDefaultAsync(t => t.terrenoId == calculo.terrenoId);

                if (terreno != null)
                {
                    terrenoResumen = new
                    {
                        terreno.terrenoId,
                        terreno.codigoTerreno,
                        terreno.nombrePropietarioTerreno,
                        terreno.extensionManzanaTerreno,
                        terreno.cantidadQuintalesOro
                    };
                }

                var tipoCultivo = await _db.TipoCultivos
                    .FirstOrDefaultAsync(t => t.tipoCultivoId == calculo.tipoCultivoId);

                if (tipoCultivo != null)
                {
                    tipoCultivoResumen = new
                    {
                        tipoCultivo.tipoCultivoId,
                        tipoCultivo.nombreTipoCultivo
                    };
                }

                var tipoAnalisis = await _db.TipoAnalisisSuelos
                    .FirstOrDefaultAsync(t => t.tipoAnalisisSueloId == calculo.tipoAnalisisSueloId);

                if (tipoAnalisis != null)
                {
                    tipoAnalisisResumen = new
                    {
                        tipoAnalisis.tipoAnalisisSueloId,
                        tipoAnalisis.nombreTipoAnalisisSuelo
                    };
                }
            }

            var totalElementosIngresados = await _db.AnalisisSueloElementos
                .CountAsync(e => e.analisisSueloId == analisis.analisisSueloId && e.activo);

            var totalElementosCalculados = calculo == null
                ? 0
                : await _db.AnalisisSueloCalculoElementoQuimicos
                    .CountAsync(e => e.analisisSueloCalculoId == calculo.analisisSueloCalculoId && e.activo);

            respuesta.Add(new
            {
                analisis.analisisSueloId,
                analisis.fechaAnalisisSuelo,
                analisis.laboratorioAnalasisSuelo,
                analisis.identificadorAnalisisSuelo,
                analisis.activo,

                terreno = terrenoResumen,
                tipoCultivo = tipoCultivoResumen,
                tipoAnalisisSuelo = tipoAnalisisResumen,

                calculo = calculo == null ? null : new
                {
                    calculo.analisisSueloCalculoId,
                    calculo.cantidadQuintalesOro,
                    calculo.tamanoFinca,
                    calculo.phAnalisisSuelo,
                    calculo.acidezTotal,
                    calculo.recomendacionGeneral,
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
            message = "Listado de análisis de suelo obtenido correctamente.",
            data = respuesta
        });
    }

    // ============================
    // DESACTIVAR ANÁLISIS COMPLETO
    // ============================
    [HttpPut("desactivar/{id:int}")]
    public async Task<IActionResult> Desactivar(int id)
    {
        var analisis = await _db.AnalisisSuelos
            .FirstOrDefaultAsync(x => x.analisisSueloId == id && x.activo);

        if (analisis == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Análisis de suelo no encontrado o ya se encuentra inactivo."
            });
        }

        using var transaccion = await _db.Database.BeginTransactionAsync();

        try
        {
            // 1. Desactivar encabezado del análisis
            analisis.activo = false;

            // 2. Desactivar elementos ingresados
            var elementosIngresados = await _db.AnalisisSueloElementos
                .Where(x => x.analisisSueloId == id && x.activo)
                .ToListAsync();

            foreach (var elemento in elementosIngresados)
            {
                elemento.activo = false;
            }

            // 3. Desactivar cálculos asociados
            var calculos = await _db.AnalisisSueloCalculos
                .Where(x => x.analisisSueloId == id && x.activo)
                .ToListAsync();

            foreach (var calculo in calculos)
            {
                calculo.activo = false;

                // 4. Desactivar detalle calculado por elemento
                var elementosCalculados = await _db.AnalisisSueloCalculoElementoQuimicos
                    .Where(x => x.analisisSueloCalculoId == calculo.analisisSueloCalculoId && x.activo)
                    .ToListAsync();

                foreach (var detalle in elementosCalculados)
                {
                    detalle.activo = false;
                }
            }

            await _db.SaveChangesAsync();
            await transaccion.CommitAsync();

            return Ok(new
            {
                success = true,
                message = "Análisis de suelo desactivado correctamente.",
                data = new
                {
                    analisisSueloId = id,
                    elementosIngresadosDesactivados = elementosIngresados.Count,
                    calculosDesactivados = calculos.Count
                }
            });
        }
        catch (Exception ex)
        {
            await transaccion.RollbackAsync();

            return BadRequest(new
            {
                success = false,
                message = "Error al desactivar el análisis de suelo.",
                error = ex.Message,
                innerError = ex.InnerException?.Message,
                detalle = ex.GetBaseException().Message
            });
        }
    }

    // ============================
    // LISTAR TIPOS DE CULTIVO
    // ============================
    [HttpGet("tipo-cultivo/listar")]
    public async Task<IActionResult> ListarTiposCultivo()
    {
        var lista = await _db.TipoCultivos
            .Where(x => x.activo)
            .Select(x => new
            {
                x.tipoCultivoId,
                x.nombreTipoCultivo,
                x.descripcionTipoCultivo,
                x.activo
            })
            .ToListAsync();

        return Ok(lista);
    }

    // ============================
    // LISTAR TIPOS DE ANÁLISIS DE SUELO
    // ============================
    [HttpGet("tipo-analisis-suelo/listar")]
    public async Task<IActionResult> ListarTiposAnalisisSuelo()
    {
        var lista = await _db.TipoAnalisisSuelos
            .Where(x => x.activo)
            .Select(x => new
            {
                x.tipoAnalisisSueloId,
                x.nombreTipoAnalisisSuelo,
                x.descripcionTipoAnalisisSuelo,
                x.activo
            })
            .ToListAsync();

        return Ok(lista);
    }



    // ============================================================
    // MÉTODOS LEGACY / FLUJO ANTERIOR
    // Estos métodos se usaban antes de implementar guardar-calculo.
    // Actualmente el flujo principal es:
    // POST /api/analisis-suelo/guardar-calculo
    // ============================================================
    // ============================
    // 1️⃣ CREAR ANÁLISIS DE SUELO
    // ============================
    /*
     *[HttpPost("crear")]
    public async Task<IActionResult> Crear([FromBody] AnalisisSueloCrearDto req)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        bool existe = await _db.AnalisisSuelos
            .AnyAsync(a => a.identificadorAnalisisSuelo == req.identificadorAnalisisSuelo);
        if (existe)
            return Conflict("El identificador ya existe.");

        var analisis = new AnalisisSuelo
        {
            fechaAnalisisSuelo = req.fechaAnalisisSuelo,
            laboratorioAnalasisSuelo = req.laboratorioAnalasisSuelo.Trim().ToUpper(),
            identificadorAnalisisSuelo = req.identificadorAnalisisSuelo.Trim().ToUpper(),
            activo = true
        };

        _db.AnalisisSuelos.Add(analisis);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(ObtenerPorId), new { id = analisis.analisisSueloId }, analisis);
    }

    // ============================
    // 2️⃣ AGREGAR ELEMENTO QUÍMICO
    // ============================
    [HttpPost("{analisisId:int}/agregar-elemento")]
    public async Task<IActionResult> AgregarElemento(int analisisId, [FromBody] AnalisisSueloElementoCrearDto dto)
    {
        var analisis = await _db.AnalisisSuelos.FindAsync(analisisId);
        if (analisis == null || !analisis.activo)
            return NotFound("Análisis no encontrado o inactivo.");

        var elemento = new AnalisisSueloElementoQuimico
        {
            cantidadElemento = dto.cantidadElemento,
            analisisSueloId = analisisId,
            elementoQuimicosId = dto.elementoQuimicosId,
            unidadMedidaId = dto.unidadMedidaId,
            activo = true
        };

        _db.AnalisisSueloElementos.Add(elemento);
        await _db.SaveChangesAsync();

        return Ok(new { mensaje = "Elemento agregado correctamente." });
    }*/

}
