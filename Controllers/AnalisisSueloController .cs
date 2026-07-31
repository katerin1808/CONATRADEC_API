using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/analisis-suelo")]
public class AnalisisSueloController :
    ControllerBase
{
    private readonly DBContext _db;

    private readonly
        AnalisisSueloCalculoService
        _calculoService;

    public AnalisisSueloController(
        DBContext db,
        AnalisisSueloCalculoService
            calculoService)
    {
        _db = db;
        _calculoService =
            calculoService;
    }

    // =============================================================
    // CALCULAR ANÁLISIS DE SUELO
    // =============================================================
    [HttpPost("calcular")]
    public async Task<IActionResult> Calcular(
        [FromBody]
        AnalisisSueloCalculoRequestDto dto)
    {
        try
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            AnalisisSueloCalculoResponseDto
                resultado =
                    await _calculoService
                        .CalcularAsync(dto);

            return Ok(new
            {
                success = true,
                message =
                    "Cálculo realizado correctamente.",
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

    // =============================================================
    // GUARDAR ÚNICAMENTE ANÁLISIS Y REQUERIMIENTO
    // Endpoint conservado para compatibilidad con el flujo anterior.
    // El flujo completo utiliza /api/guardar-todo.
    // =============================================================
    [HttpPost("guardar-calculo")]
    public async Task<IActionResult>
        GuardarCalculo(
            [FromBody]
            AnalisisSueloGuardarRequestDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        string identificador =
            dto.identificadorAnalisisSuelo
                .Trim()
                .ToUpperInvariant();

        bool existeIdentificador =
            await _db.AnalisisSuelos
                .AnyAsync(x =>
                    x
                        .identificadorAnalisisSuelo ==
                    identificador);

        if (existeIdentificador)
        {
            return Conflict(new
            {
                success = false,
                message =
                    "Ya existe un análisis de suelo con ese identificador."
            });
        }

        bool terrenoExiste =
            await _db.Terreno
                .AnyAsync(x =>
                    x.terrenoId ==
                        dto.terrenoId &&
                    x.activo);

        if (!terrenoExiste)
        {
            return NotFound(new
            {
                success = false,
                message =
                    "El terreno indicado no existe o está inactivo."
            });
        }

        await using var transaccion =
            await _db.Database
                .BeginTransactionAsync();

        try
        {
            AnalisisSueloCalculoResponseDto
                resultado =
                    await _calculoService
                        .CalcularAsync(dto);

            AnalisisSuelo analisis =
                new()
                {
                    fechaAnalisisSuelo =
                        dto.fechaAnalisisSuelo,
                    fechaCreacionAnalisisSuelo =
                        DateTime.Now,
                    laboratorioAnalasisSuelo =
                        dto
                            .laboratorioAnalasisSuelo
                            .Trim()
                            .ToUpperInvariant(),
                    identificadorAnalisisSuelo =
                        identificador,
                    activo = true
                };

            _db.AnalisisSuelos.Add(
                analisis);

            await _db.SaveChangesAsync();

            _db.AnalisisSueloElementos
                .AddRange(
                    dto.elementosQuimicos
                        .Select(elemento =>
                            new
                                AnalisisSueloElementoQuimico
                                {
                                    analisisSueloId =
                                        analisis
                                            .analisisSueloId,
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
                                }));

            AnalisisSueloCalculo calculo =
                new()
                {
                    cantidadQuintalesOro =
                        Math.Round(
                            dto
                                .cantidadQuintalesOro,
                            4),
                    tamanoFinca =
                        Math.Round(
                            dto.tamanoFinca,
                            4),
                    phAnalisisSuelo =
                        Math.Round(
                            dto.ph,
                            4),
                    materiaOrganica =
                        Math.Round(
                            dto.materiaOrganica,
                            4),
                    unidadMedidaMateriaOrganicaId =
                        dto
                            .unidadMedidaMateriaOrganicaId,
                    acidezTotal =
                        Math.Round(
                            dto.acidezTotal,
                            4),
                    recomendacionGeneral =
                        resultado
                            .recomendacionGeneral,
                    observacion =
                        string.Join(
                            " | ",
                            resultado
                                .observaciones),
                    fechaCalculo =
                        DateTime.Now,
                    activo = true,
                    analisisSueloId =
                        analisis
                            .analisisSueloId,
                    terrenoId =
                        dto.terrenoId,
                    tipoCultivoId =
                        dto.tipoCultivoId,
                    tipoAnalisisSueloId =
                        dto
                            .tipoAnalisisSueloId,
                    usuarioId =
                        dto.usuarioId
                };

            _db.AnalisisSueloCalculos.Add(
                calculo);

            await _db.SaveChangesAsync();

            UnidadMedida? unidadResultado =
                await _db.UnidadMedidas
                    .FirstOrDefaultAsync(x =>
                        x.activo &&
                        x.nombreUnidadMedida
                            .ToLower() ==
                            "lb/mz");

            if (unidadResultado == null)
            {
                await transaccion
                    .RollbackAsync();

                return BadRequest(new
                {
                    success = false,
                    message =
                        "No existe la unidad de medida lb/Mz en la base de datos."
                });
            }

            _db
                .AnalisisSueloCalculoElementoQuimicos
                .AddRange(
                    resultado.elementos
                        .Select(elemento =>
                            new
                                AnalisisSueloCalculoElementoQuimico
                                {
                                    analisisSueloCalculoId =
                                        calculo
                                            .analisisSueloCalculoId,
                                    elementoQuimicosId =
                                        elemento
                                            .elementoQuimicosId,
                                    unidadMedidaId =
                                        unidadResultado
                                            .unidadMedidaId,
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
                                            .clasificacion,
                                    observacion =
                                        elemento
                                            .observacion,
                                    incluirCalculosComplementarios =
                                        elemento
                                            .incluirCalculosComplementarios,
                                    activo = true
                                }));

            await _db.SaveChangesAsync();

            await transaccion.CommitAsync();

            return Ok(new
            {
                success = true,
                message =
                    "Análisis de suelo y requerimiento anual guardados correctamente.",
                data = new
                {
                    analisisSueloId =
                        analisis
                            .analisisSueloId,
                    analisisSueloCalculoId =
                        calculo
                            .analisisSueloCalculoId,
                    identificadorAnalisisSuelo =
                        analisis
                            .identificadorAnalisisSuelo,
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
                message =
                    "Error al guardar el análisis de suelo.",
                error = ex.Message,
                innerError =
                    ex.InnerException?
                        .Message,
                detalle =
                    ex
                        .GetBaseException()
                        .Message
            });
        }
    }

    // =============================================================
    // OBTENER ANÁLISIS COMPLETO
    // =============================================================
    [HttpGet("{id:int}")]
    public async Task<IActionResult>
        ObtenerPorId(int id)
    {
        AnalisisSuelo? analisis =
            await _db.AnalisisSuelos
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.analisisSueloId ==
                        id &&
                    x.activo);

        if (analisis == null)
        {
            return NotFound(new
            {
                success = false,
                message =
                    "Análisis de suelo no encontrado."
            });
        }

        AnalisisSueloCalculo? calculo =
            await _db.AnalisisSueloCalculos
                .AsNoTracking()
                .Where(x =>
                    x.analisisSueloId ==
                        id &&
                    x.activo)
                .OrderByDescending(x =>
                    x.fechaCalculo)
                .FirstOrDefaultAsync();

        Terreno? terreno =
            calculo == null
                ? null
                : await _db.Terreno
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.terrenoId ==
                            calculo
                                .terrenoId);

        var propietario =
            calculo == null
                ? null
                : await _db.PropietarioTerrenos
                    .AsNoTracking()
                    .Where(relacion =>
                        relacion.terrenoId ==
                            calculo.terrenoId &&
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

        TipoCultivo? tipoCultivo =
            calculo == null
                ? null
                : await _db.TipoCultivos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.tipoCultivoId ==
                            calculo
                                .tipoCultivoId);

        TipoAnalisisSuelo?
            tipoAnalisis =
                calculo == null
                    ? null
                    : await _db
                        .TipoAnalisisSuelos
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x =>
                            x
                                .tipoAnalisisSueloId ==
                            calculo
                                .tipoAnalisisSueloId);

        var elementosIngresados =
            await _db.AnalisisSueloElementos
                .AsNoTracking()
                .Where(x =>
                    x.analisisSueloId ==
                        id &&
                    x.activo)
                .Select(x => new
                {
                    x
                        .analisisSueloElementoQuimicoId,
                    x.elementoQuimicosId,
                    simboloElementoQuimico =
                        x.ElementoQuimico
                            .simboloElementoQuimico
                            .Trim(),
                    nombreElementoQuimico =
                        x.ElementoQuimico
                            .nombreElementoQuimico
                            .Trim(),
                    x.cantidadElemento,
                    x.unidadMedidaId,
                    nombreUnidadMedida =
                        x.UnidadMedida
                            .nombreUnidadMedida
                            .Trim(),
                    x.activo
                })
                .ToListAsync();

        var elementosCalculados =
            calculo == null
                ? new List<object>()
                : (
                    await _db
                        .AnalisisSueloCalculoElementoQuimicos
                        .AsNoTracking()
                        .Where(x =>
                            x
                                .analisisSueloCalculoId ==
                            calculo
                                .analisisSueloCalculoId &&
                            x.activo)
                        .Select(x => new
                        {
                            x
                                .analisisSueloCalculoElementoQuimicoId,
                            x.elementoQuimicosId,
                            simboloElementoQuimico =
                                x.ElementoQuimico
                                    .simboloElementoQuimico
                                    .Trim(),
                            nombreElementoQuimico =
                                x.ElementoQuimico
                                    .nombreElementoQuimico
                                    .Trim(),
                            x.cantidadIngresada,
                            x.cantidadConvertidaLbMz,
                            x.requerimientoCalculado,
                            x.unidadMedidaId,
                            nombreUnidadResultado =
                                x.UnidadMedida ==
                                    null
                                    ? null
                                    : x
                                        .UnidadMedida
                                        .nombreUnidadMedida
                                        .Trim(),
                            x.clasificacion,
                            x.observacion,
                            x
                                .incluirCalculosComplementarios,
                            x.activo
                        })
                        .ToListAsync())
                    .Select(x =>
                        (object)x)
                    .ToList();

        return Ok(new
        {
            success = true,
            message =
                "Análisis de suelo obtenido correctamente.",
            data = new
            {
                analisisSuelo = new
                {
                    analisis.analisisSueloId,
                    analisis.fechaAnalisisSuelo,
                    analisis
                        .fechaCreacionAnalisisSuelo,
                    analisis
                        .laboratorioAnalasisSuelo,
                    analisis
                        .identificadorAnalisisSuelo,
                    analisis.activo
                },

                terreno =
                    terreno == null
                        ? null
                        : new
                        {
                            terreno.terrenoId,
                            terreno
                                .codigoTerreno,
                            propietario,
                            terreno
                                .direccionTerreno,
                            terreno
                                .extensionManzanaTerreno,
                            terreno
                                .cantidadQuintalesOro,
                            terreno.latitud,
                            terreno.longitud
                        },

                tipoCultivo =
                    tipoCultivo == null
                        ? null
                        : new
                        {
                            tipoCultivo
                                .tipoCultivoId,
                            tipoCultivo
                                .nombreTipoCultivo,
                            tipoCultivo
                                .descripcionTipoCultivo
                        },

                tipoAnalisisSuelo =
                    tipoAnalisis == null
                        ? null
                        : new
                        {
                            tipoAnalisis
                                .tipoAnalisisSueloId,
                            tipoAnalisis
                                .nombreTipoAnalisisSuelo,
                            tipoAnalisis
                                .descripcionTipoAnalisisSuelo
                        },

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
                            calculo
                                .materiaOrganica,
                            calculo.acidezTotal,
                            calculo
                                .recomendacionGeneral,
                            calculo.observacion,
                            calculo.fechaCalculo,
                            calculo.usuarioId
                        },

                elementosIngresados,
                elementosCalculados
            }
        });
    }

    // =============================================================
    // LISTAR ANÁLISIS CON RESUMEN
    // =============================================================
    [HttpGet("listar")]
    public async Task<IActionResult> Listar()
    {
        List<AnalisisSuelo> analisisLista =
            await _db.AnalisisSuelos
                .AsNoTracking()
                .Where(x => x.activo)
                .OrderByDescending(x =>
                    x
                        .fechaCreacionAnalisisSuelo)
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
    // DESACTIVAR ANÁLISIS BÁSICO
    // =============================================================
    [HttpPut("desactivar/{id:int}")]
    public async Task<IActionResult>
        Desactivar(int id)
    {
        AnalisisSuelo? analisis =
            await _db.AnalisisSuelos
                .FirstOrDefaultAsync(x =>
                    x.analisisSueloId ==
                        id &&
                    x.activo);

        if (analisis == null)
        {
            return NotFound(new
            {
                success = false,
                message =
                    "Análisis de suelo no encontrado o ya se encuentra inactivo."
            });
        }

        await using var transaccion =
            await _db.Database
                .BeginTransactionAsync();

        try
        {
            analisis.activo = false;

            List<
                AnalisisSueloElementoQuimico>
                elementosIngresados =
                    await _db
                        .AnalisisSueloElementos
                        .Where(x =>
                            x.analisisSueloId ==
                                id &&
                            x.activo)
                        .ToListAsync();

            elementosIngresados
                .ForEach(x =>
                    x.activo = false);

            List<AnalisisSueloCalculo>
                calculos =
                    await _db
                        .AnalisisSueloCalculos
                        .Where(x =>
                            x.analisisSueloId ==
                                id &&
                            x.activo)
                        .ToListAsync();

            foreach (
                AnalisisSueloCalculo calculo
                in calculos)
            {
                calculo.activo = false;

                List<
                    AnalisisSueloCalculoElementoQuimico>
                    elementosCalculados =
                        await _db
                            .AnalisisSueloCalculoElementoQuimicos
                            .Where(x =>
                                x
                                    .analisisSueloCalculoId ==
                                calculo
                                    .analisisSueloCalculoId &&
                                x.activo)
                            .ToListAsync();

                elementosCalculados
                    .ForEach(x =>
                        x.activo = false);
            }

            await _db.SaveChangesAsync();

            await transaccion.CommitAsync();

            return Ok(new
            {
                success = true,
                message =
                    "Análisis de suelo desactivado correctamente.",
                data = new
                {
                    analisisSueloId = id,
                    elementosIngresadosDesactivados =
                        elementosIngresados.Count,
                    calculosDesactivados =
                        calculos.Count
                }
            });
        }
        catch (Exception ex)
        {
            await transaccion.RollbackAsync();

            return BadRequest(new
            {
                success = false,
                message =
                    "Error al desactivar el análisis de suelo.",
                error = ex.Message,
                innerError =
                    ex.InnerException?
                        .Message,
                detalle =
                    ex
                        .GetBaseException()
                        .Message
            });
        }
    }

    // =============================================================
    // CATÁLOGOS UTILIZADOS POR EL FORMULARIO
    // =============================================================
    [HttpGet("tipo-cultivo/listar")]
    public async Task<IActionResult>
        ListarTiposCultivo()
    {
        var lista =
            await _db.TipoCultivos
                .AsNoTracking()
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

    [HttpGet("tipo-analisis-suelo/listar")]
    public async Task<IActionResult>
        ListarTiposAnalisisSuelo()
    {
        var lista =
            await _db.TipoAnalisisSuelos
                .AsNoTracking()
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
}
