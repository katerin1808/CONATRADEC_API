using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/fuente-nutriente")]
    public class FuenteNutrienteController : ControllerBase
    {
        private readonly DBContext _db;

        public FuenteNutrienteController(DBContext db)
        {
            _db = db;
        }

        [HttpGet("listar")]
        public async Task<IActionResult> Listar()
        {
            var data = await ConstruirRespuestaFuente()
                .OrderBy(x => x.nombreNutriente)
                .ToListAsync();

            return Ok(data);
        }

        [HttpGet("obtener/{id:int}")]
        public async Task<IActionResult> ObtenerPorId(int id)
        {
            var data = await ConstruirRespuestaFuente()
                .FirstOrDefaultAsync(x =>
                    x.fuenteNutrientesId == id);

            if (data == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "Fuente nutriente no encontrada."
                });
            }

            return Ok(data);
        }

        [HttpPost("crear-con-elementos")]
        public async Task<IActionResult> CrearConElementos(
            [FromBody]
            FuenteNutrienteConElementosCrearDto dto)
        {
            await using var trans =
                await _db.Database.BeginTransactionAsync();

            try
            {
                IActionResult? validacion =
                    await ValidarDatosFuenteAsync(
                        dto,
                        null);

                if (validacion != null)
                    return validacion;

                var elementos =
                    dto.elementosQuimicos ??
                    new List<ElementoFuenteCrearDto>();

                string nombre =
                    dto.nombreNutriente
                        .Trim()
                        .ToUpper();

                var fuente =
                    new FuenteNutriente
                    {
                        nombreNutriente =
                            nombre,

                        descripcionNutriente =
                            dto.descripcionNutriente?
                                .Trim() ??
                            string.Empty,

                        precioNutriente =
                            dto.precioNutriente,

                        activo =
                            true
                    };

                _db.fuenteNutriente.Add(
                    fuente);

                await _db.SaveChangesAsync();

                if (elementos.Count > 0)
                {
                    var relaciones =
                        elementos.Select(e =>
                            new FuenteNutrienteElementoQuimico
                            {
                                fuenteNutrientesId =
                                    fuente.fuenteNutrientesId,

                                elementoQuimicosId =
                                    e.elementoQuimicosId,

                                cantidadAporte =
                                    e.cantidadAporte,

                                activo =
                                    true
                            })
                        .ToList();

                    _db.fuenteNutrienteElementoQuimico
                        .AddRange(relaciones);

                    await _db.SaveChangesAsync();
                }

                await trans.CommitAsync();

                var respuesta =
                    await ConstruirRespuestaFuente(
                            incluirInactivas: true)
                        .FirstAsync(x =>
                            x.fuenteNutrientesId ==
                            fuente.fuenteNutrientesId);

                return Ok(new
                {
                    mensaje =
                        "Fuente nutriente creada correctamente.",

                    data =
                        respuesta
                });
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        mensaje =
                            "Error al crear fuente nutriente.",

                        detalle =
                            ex.Message
                    });
            }
        }

        [HttpPost(
            "{fuenteNutrientesId}/habilitar-enmienda-calcarea")]
        public async Task<IActionResult>
            HabilitarEnmiendaCalcarea(
                int fuenteNutrientesId,
                [FromBody]
                HabilitarEnmiendaCalcareaDto dto)
        {
            var fuente =
                await _db.fuenteNutriente
                    .FirstOrDefaultAsync(x =>
                        x.fuenteNutrientesId ==
                            fuenteNutrientesId &&
                        x.activo);

            if (fuente == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "La fuente nutriente no existe o está inactiva."
                });
            }

            if (dto.prnt <= 0)
            {
                return BadRequest(new
                {
                    mensaje =
                        "El PRNT debe ser mayor a cero."
                });
            }

            string descripcion =
                string.IsNullOrWhiteSpace(
                    dto.descripcionParametro)
                    ? "Fuente utilizada para cálculo de enmienda calcárea"
                    : dto.descripcionParametro.Trim();

            var parametro =
                await _db.ParametroEnmiendaCalcarea
                    .FirstOrDefaultAsync(x =>
                        x.fuenteNutrientesId ==
                        fuenteNutrientesId);

            bool fueCreado =
                parametro == null;

            if (parametro == null)
            {
                parametro =
                    new ParametroEnmiendaCalcarea
                    {
                        fuenteNutrientesId =
                            fuenteNutrientesId,

                        saturacionBasesDeseada =
                            70,

                        prnt =
                            dto.prnt,

                        factorTonHaALbHa =
                            2200,

                        factorHaAMz =
                            0.7026m,

                        factorTonHaAKgHa =
                            1000,

                        descripcionParametro =
                            descripcion,

                        activo =
                            true
                    };

                _db.ParametroEnmiendaCalcarea.Add(
                    parametro);
            }
            else
            {
                parametro.prnt =
                    dto.prnt;

                parametro.descripcionParametro =
                    descripcion;

                parametro.activo =
                    true;
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje =
                    fueCreado
                        ? "Fuente habilitada para cálculo de enmienda calcárea."
                        : "Datos de enmienda calcárea actualizados correctamente.",

                fuente.fuenteNutrientesId,
                fuente.nombreNutriente,
                parametro.parametroEnmiendaCalcareaId,
                parametro.prnt
            });
        }

        [HttpPut(
            "deshabilitar-enmienda-calcarea/{fuenteNutrientesId:int}")]
        public async Task<IActionResult>
            DeshabilitarEnmiendaCalcarea(
                int fuenteNutrientesId)
        {
            var parametro =
                await _db.ParametroEnmiendaCalcarea
                    .FirstOrDefaultAsync(x =>
                        x.fuenteNutrientesId ==
                        fuenteNutrientesId);

            if (parametro == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "La fuente no existe en la configuración de enmienda calcárea."
                });
            }

            parametro.activo =
                false;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje =
                    "Fuente deshabilitada para enmienda calcárea.",

                parametro.fuenteNutrientesId,
                parametro.activo
            });
        }

        [HttpPost(
            "habilitar-fertilizacion-mixta/{fuenteNutrientesId:int}")]
        public async Task<IActionResult>
            HabilitarFertilizacionMixta(
                int fuenteNutrientesId)
        {
            var fuente =
                await _db.fuenteNutriente
                    .FirstOrDefaultAsync(x =>
                        x.fuenteNutrientesId ==
                            fuenteNutrientesId &&
                        x.activo);

            if (fuente == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "La fuente nutriente no existe o está inactiva."
                });
            }

            var configuracion =
                await _db.fuenteFertilizacionMixta
                    .FirstOrDefaultAsync(x =>
                        x.fuenteNutrientesId ==
                        fuenteNutrientesId);

            if (configuracion != null)
            {
                if (configuracion.activo)
                {
                    return BadRequest(new
                    {
                        mensaje =
                            "Esta fuente ya está habilitada para fertilización mixta."
                    });
                }

                configuracion.activo =
                    true;

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    mensaje =
                        "Fuente habilitada nuevamente para fertilización mixta.",

                    fuente.fuenteNutrientesId,
                    fuente.nombreNutriente
                });
            }

            configuracion =
                new FuenteFertilizacionMixta
                {
                    fuenteNutrientesId =
                        fuenteNutrientesId,

                    activo =
                        true
                };

            _db.fuenteFertilizacionMixta.Add(
                configuracion);

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje =
                    "Fuente habilitada para fertilización mixta.",

                fuente.fuenteNutrientesId,
                fuente.nombreNutriente
            });
        }

        [HttpPut(
            "deshabilitar-fertilizacion-mixta/{fuenteNutrientesId:int}")]
        public async Task<IActionResult>
            DeshabilitarFertilizacionMixta(
                int fuenteNutrientesId)
        {
            var configuracion =
                await _db.fuenteFertilizacionMixta
                    .FirstOrDefaultAsync(x =>
                        x.fuenteNutrientesId ==
                        fuenteNutrientesId);

            if (configuracion == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "La fuente no existe en la configuración de fertilización mixta."
                });
            }

            configuracion.activo =
                false;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje =
                    "Fuente deshabilitada para fertilización mixta.",

                configuracion.fuenteNutrientesId,
                configuracion.activo
            });
        }

        [HttpGet("listar-fertilizacion-mixta")]
        public async Task<IActionResult>
            ListarFertilizacionMixta()
        {
            var data =
                await _db.fuenteNutriente
                    .Where(x =>
                        x.activo &&
                        _db.fuenteFertilizacionMixta
                            .Any(f =>
                                f.fuenteNutrientesId ==
                                    x.fuenteNutrientesId &&
                                f.activo))
                    .Select(x =>
                        new FuenteFertilizacionMixtaListadoDto
                        {
                            fuenteNutrientesId =
                                x.fuenteNutrientesId,

                            nombreNutriente =
                                x.nombreNutriente,

                            descripcionNutriente =
                                x.descripcionNutriente,

                            precioNutriente =
                                x.precioNutriente,

                            activo =
                                x.activo,

                            elementosQuimicos =
                                x.fuenteNutrienteElementoQuimico
                                    .Where(r =>
                                        r.activo)
                                    .Select(r =>
                                        new ElementoFuenteRespuestaDto
                                        {
                                            fuenteNutrienteElementoQuimicoId =
                                                r.fuenteNutrienteElementoQuimicoId,

                                            elementoQuimicosId =
                                                r.elementoQuimicosId,

                                            nombreElementoQuimico =
                                                r.elementoQuimico != null
                                                    ? r.elementoQuimico
                                                        .nombreElementoQuimico
                                                    : string.Empty,

                                            simboloElementoQuimico =
                                                r.elementoQuimico != null
                                                    ? r.elementoQuimico
                                                        .simboloElementoQuimico
                                                    : string.Empty,

                                            cantidadAporte =
                                                r.cantidadAporte
                                        })
                                    .ToList()
                        })
                    .ToListAsync();

            return Ok(data);
        }

        [HttpGet("enmiendas-calcareas")]
        public async Task<IActionResult>
            ObtenerEnmiendasCalcareas()
        {
            var data =
                await _db.ParametroEnmiendaCalcarea
                    .Include(x =>
                        x.FuenteNutriente)
                    .Where(x =>
                        x.activo &&
                        x.FuenteNutriente != null &&
                        x.FuenteNutriente.activo)
                    .Select(x => new
                    {
                        x.parametroEnmiendaCalcareaId,
                        x.fuenteNutrientesId,

                        nombreNutriente =
                            x.FuenteNutriente!
                                .nombreNutriente,

                        precioNutriente =
                            x.FuenteNutriente
                                .precioNutriente,

                        x.prnt,
                        x.descripcionParametro
                    })
                    .ToListAsync();

            return Ok(data);
        }

        [HttpPut("editar-con-elementos/{id:int}")]
        public async Task<IActionResult>
            EditarConElementos(
                int id,
                [FromBody]
                FuenteNutrienteConElementosCrearDto dto)
        {
            await using var trans =
                await _db.Database.BeginTransactionAsync();

            try
            {
                var fuente =
                    await _db.fuenteNutriente
                        .FirstOrDefaultAsync(x =>
                            x.fuenteNutrientesId == id &&
                            x.activo);

                if (fuente == null)
                {
                    return NotFound(new
                    {
                        mensaje =
                            "Fuente nutriente no encontrada."
                    });
                }

                IActionResult? validacion =
                    await ValidarDatosFuenteAsync(
                        dto,
                        id);

                if (validacion != null)
                    return validacion;

                var elementos =
                    dto.elementosQuimicos ??
                    new List<ElementoFuenteCrearDto>();

                fuente.nombreNutriente =
                    dto.nombreNutriente
                        .Trim()
                        .ToUpper();

                fuente.descripcionNutriente =
                    dto.descripcionNutriente?
                        .Trim() ??
                    string.Empty;

                fuente.precioNutriente =
                    dto.precioNutriente;

                var relacionesActuales =
                    await _db
                        .fuenteNutrienteElementoQuimico
                        .Where(x =>
                            x.fuenteNutrientesId == id &&
                            x.activo)
                        .ToListAsync();

                foreach (var relacion
                         in relacionesActuales)
                {
                    relacion.activo =
                        false;
                }

                if (elementos.Count > 0)
                {
                    var nuevasRelaciones =
                        elementos.Select(e =>
                            new FuenteNutrienteElementoQuimico
                            {
                                fuenteNutrientesId =
                                    id,

                                elementoQuimicosId =
                                    e.elementoQuimicosId,

                                cantidadAporte =
                                    e.cantidadAporte,

                                activo =
                                    true
                            })
                        .ToList();

                    _db.fuenteNutrienteElementoQuimico
                        .AddRange(
                            nuevasRelaciones);
                }

                await _db.SaveChangesAsync();
                await trans.CommitAsync();

                var respuesta =
                    await ConstruirRespuestaFuente(
                            incluirInactivas: true)
                        .FirstAsync(x =>
                            x.fuenteNutrientesId == id);

                return Ok(new
                {
                    mensaje =
                        "Fuente nutriente actualizada correctamente.",

                    data =
                        respuesta
                });
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        mensaje =
                            "Error al editar fuente nutriente.",

                        detalle =
                            ex.Message
                    });
            }
        }

        [HttpDelete("eliminar/{id:int}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var fuente =
                await _db.fuenteNutriente
                    .FirstOrDefaultAsync(x =>
                        x.fuenteNutrientesId == id &&
                        x.activo);

            if (fuente == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "Fuente nutriente no encontrada o ya está desactivada."
                });
            }

            /*
             * Solamente los datos históricos impiden eliminar.
             *
             * Las configuraciones de enmienda, fuente orgánica,
             * fertilización mixta y matriz química se desactivan
             * junto con la fuente.
             */
            var dependencias =
                new List<string>();

            bool usadaEnEnmiendas =
                await _db.enmiendaCalcarea
                    .AnyAsync(x =>
                        x.fuenteNutrientesId == id);

            if (usadaEnEnmiendas)
            {
                dependencias.Add(
                    "enmiendas calcáreas guardadas");
            }

            bool usadaEnFormulas =
                await _db.formulaNutricionalDetalle
                    .AnyAsync(x =>
                        x.fuenteNutrientesId == id);

            if (usadaEnFormulas)
            {
                dependencias.Add(
                    "fórmulas nutricionales guardadas");
            }

            bool usadaEnFertilizacionesMixtas =
                await _db.fertilizacionMixtaFuente
                    .AnyAsync(x =>
                        x.fuenteNutrientesId == id);

            if (usadaEnFertilizacionesMixtas)
            {
                dependencias.Add(
                    "fertilizaciones mixtas guardadas");
            }

            bool usadaEnControlesAplicacion =
                await _db
                    .FuenteNutrienteControlAplicaciones
                    .AnyAsync(x =>
                        x.fuenteNutrientesId == id);

            if (usadaEnControlesAplicacion)
            {
                dependencias.Add(
                    "controles de aplicación");
            }

            bool usadaEnInterpretaciones =
                await _db.InterpretacionFuenteNutrientes
                    .AnyAsync(x =>
                        x.fuenteNutrientesId == id);

            if (usadaEnInterpretaciones)
            {
                dependencias.Add(
                    "interpretaciones guardadas");
            }

            if (dependencias.Count > 0)
            {
                return Conflict(new
                {
                    mensaje =
                        "No se puede eliminar la fuente nutriente porque está siendo utilizada.",

                    fuente = new
                    {
                        fuente.fuenteNutrientesId,
                        fuente.nombreNutriente
                    },

                    usadoEn =
                        dependencias
                });
            }

            await using var transaccion =
                await _db.Database.BeginTransactionAsync();

            try
            {
                fuente.activo =
                    false;

                var parametrosEnmienda =
                    await _db.ParametroEnmiendaCalcarea
                        .Where(x =>
                            x.fuenteNutrientesId == id &&
                            x.activo)
                        .ToListAsync();

                foreach (var parametro
                         in parametrosEnmienda)
                {
                    parametro.activo =
                        false;
                }

                var parametrosOrganicos =
                    await _db.ParametroFuenteOrganicaAporte
                        .Where(x =>
                            x.fuenteNutrientesId == id &&
                            x.activo)
                        .ToListAsync();

                foreach (var parametro
                         in parametrosOrganicos)
                {
                    parametro.activo =
                        false;
                }

                var configuracionesMixtas =
                    await _db.fuenteFertilizacionMixta
                        .Where(x =>
                            x.fuenteNutrientesId == id &&
                            x.activo)
                        .ToListAsync();

                foreach (var configuracion
                         in configuracionesMixtas)
                {
                    configuracion.activo =
                        false;
                }

                var relaciones =
                    await _db
                        .fuenteNutrienteElementoQuimico
                        .Where(x =>
                            x.fuenteNutrientesId == id &&
                            x.activo)
                        .ToListAsync();

                foreach (var relacion
                         in relaciones)
                {
                    relacion.activo =
                        false;
                }

                await _db.SaveChangesAsync();
                await transaccion.CommitAsync();

                return Ok(new
                {
                    mensaje =
                        "Fuente nutriente desactivada correctamente.",

                    data = new
                    {
                        fuente.fuenteNutrientesId,
                        fuente.nombreNutriente,
                        fuente.activo,

                        relacionesDesactivadas =
                            relaciones.Count,

                        parametrosEnmiendaDesactivados =
                            parametrosEnmienda.Count,

                        parametrosOrganicosDesactivados =
                            parametrosOrganicos.Count,

                        configuracionesMixtasDesactivadas =
                            configuracionesMixtas.Count
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
                        mensaje =
                            "Ocurrió un error al desactivar la fuente nutriente.",

                        detalle =
                            ex.Message
                    });
            }
        }

        [HttpGet("aportes-tabla")]
        public async Task<IActionResult> AportesTabla()
        {
            var fuentes =
                await _db.fuenteNutriente
                    .Where(f =>
                        f.activo)
                    .ToListAsync();

            var aportes =
                await _db
                    .fuenteNutrienteElementoQuimico
                    .Include(a =>
                        a.elementoQuimico)
                    .Where(a =>
                        a.activo)
                    .ToListAsync();

            var data =
                fuentes.Select(f =>
                {
                    var ap =
                        aportes
                            .Where(a =>
                                a.fuenteNutrientesId ==
                                f.fuenteNutrientesId)
                            .ToList();

                    decimal Valor(string simbolo)
                    {
                        return ap
                            .Where(x =>
                                x.elementoQuimico!
                                    .simboloElementoQuimico
                                    .Trim()
                                    .ToUpper() ==
                                simbolo)
                            .Select(x =>
                                x.cantidadAporte)
                            .FirstOrDefault();
                    }

                    return
                        new FuenteNutrienteAporteTablaDto
                        {
                            fuenteNutrientesId =
                                f.fuenteNutrientesId,

                            fuente =
                                f.nombreNutriente,

                            n = Valor("N"),
                            p = Valor("P"),
                            k = Valor("K"),
                            ca = Valor("CA"),
                            mg = Valor("MG"),
                            zn = Valor("ZN"),
                            s = Valor("S"),
                            b = Valor("B")
                        };
                })
                .ToList();

            return Ok(data);
        }

        private async Task<IActionResult?>
            ValidarDatosFuenteAsync(
                FuenteNutrienteConElementosCrearDto dto,
                int? idExcluir)
        {
            if (string.IsNullOrWhiteSpace(
                    dto.nombreNutriente))
            {
                return BadRequest(new
                {
                    mensaje =
                        "El nombre nutriente es obligatorio."
                });
            }

            if (dto.precioNutriente <= 0)
            {
                return BadRequest(new
                {
                    mensaje =
                        "El precio por quintal debe ser mayor a cero."
                });
            }

            string nombre =
                dto.nombreNutriente
                    .Trim()
                    .ToUpper();

            bool existe =
                await _db.fuenteNutriente
                    .AnyAsync(x =>
                        (!idExcluir.HasValue ||
                         x.fuenteNutrientesId !=
                         idExcluir.Value) &&
                        x.nombreNutriente
                            .Trim()
                            .ToUpper() ==
                        nombre &&
                        x.activo);

            if (existe)
            {
                return BadRequest(new
                {
                    mensaje =
                        idExcluir.HasValue
                            ? "Ya existe otra fuente nutriente con ese nombre."
                            : "Ya existe una fuente nutriente con ese nombre."
                });
            }

            var elementos =
                dto.elementosQuimicos ??
                new List<ElementoFuenteCrearDto>();

            bool elementosRepetidos =
                elementos
                    .GroupBy(x =>
                        x.elementoQuimicosId)
                    .Any(g =>
                        g.Count() > 1);

            if (elementosRepetidos)
            {
                return BadRequest(new
                {
                    mensaje =
                        "No puede repetir elementos químicos en la matriz."
                });
            }

            bool aporteInvalido =
                elementos.Any(x =>
                    x.elementoQuimicosId <= 0 ||
                    x.cantidadAporte <= 0 ||
                    x.cantidadAporte > 100);

            if (aporteInvalido)
            {
                return BadRequest(new
                {
                    mensaje =
                        "Todos los aportes deben tener un elemento válido y un porcentaje mayor a 0 y menor o igual a 100."
                });
            }

            decimal total =
                elementos.Sum(x =>
                    x.cantidadAporte);

            if (total > 100)
            {
                return BadRequest(new
                {
                    mensaje =
                        "La suma de los aportes químicos no puede superar el 100%."
                });
            }

            var idsElementos =
                elementos
                    .Select(x =>
                        x.elementoQuimicosId)
                    .ToList();

            if (idsElementos.Count == 0)
                return null;

            var idsExistentes =
                await _db.elementoQuimico
                    .Where(x =>
                        idsElementos.Contains(
                            x.elementoQuimicosId) &&
                        x.activo)
                    .Select(x =>
                        x.elementoQuimicosId)
                    .ToListAsync();

            var faltantes =
                idsElementos
                    .Except(idsExistentes)
                    .ToList();

            if (faltantes.Any())
            {
                return BadRequest(new
                {
                    mensaje =
                        "Hay elementos químicos inválidos o inactivos.",

                    faltantes
                });
            }

            return null;
        }

        private IQueryable<
            FuenteNutrienteConElementosRespuestaDto>
            ConstruirRespuestaFuente(
                bool incluirInactivas = false)
        {
            var consulta =
                _db.fuenteNutriente.AsQueryable();

            if (!incluirInactivas)
            {
                consulta =
                    consulta.Where(x =>
                        x.activo);
            }

            return consulta.Select(x =>
                new FuenteNutrienteConElementosRespuestaDto
                {
                    fuenteNutrientesId =
                        x.fuenteNutrientesId,

                    nombreNutriente =
                        x.nombreNutriente,

                    descripcionNutriente =
                        x.descripcionNutriente,

                    precioNutriente =
                        x.precioNutriente,

                    activo =
                        x.activo,

                    habilitadaEnmiendaCalcarea =
                        _db.ParametroEnmiendaCalcarea
                            .Any(e =>
                                e.fuenteNutrientesId ==
                                    x.fuenteNutrientesId &&
                                e.activo),

                    habilitadaFertilizacionMixta =
                        _db.fuenteFertilizacionMixta
                            .Any(f =>
                                f.fuenteNutrientesId ==
                                    x.fuenteNutrientesId &&
                                f.activo),

                    prnt =
                        _db.ParametroEnmiendaCalcarea
                            .Where(e =>
                                e.fuenteNutrientesId ==
                                    x.fuenteNutrientesId &&
                                e.activo)
                            .Select(e =>
                                (decimal?)e.prnt)
                            .FirstOrDefault(),

                    descripcionParametro =
                        _db.ParametroEnmiendaCalcarea
                            .Where(e =>
                                e.fuenteNutrientesId ==
                                    x.fuenteNutrientesId &&
                                e.activo)
                            .Select(e =>
                                e.descripcionParametro)
                            .FirstOrDefault(),

                    parametrosEnmiendaCalcarea =
                        _db.ParametroEnmiendaCalcarea
                            .Where(e =>
                                e.fuenteNutrientesId ==
                                    x.fuenteNutrientesId &&
                                e.activo)
                            .Select(e =>
                                new ParametroEnmiendaCalcareaFuenteDto
                                {
                                    prnt =
                                        e.prnt,

                                    descripcionParametro =
                                        e.descripcionParametro
                                })
                            .ToList(),

                    elementosQuimicos =
                        x.fuenteNutrienteElementoQuimico
                            .Where(r =>
                                r.activo)
                            .Select(r =>
                                new ElementoFuenteRespuestaDto
                                {
                                    fuenteNutrienteElementoQuimicoId =
                                        r.fuenteNutrienteElementoQuimicoId,

                                    elementoQuimicosId =
                                        r.elementoQuimicosId,

                                    nombreElementoQuimico =
                                        r.elementoQuimico != null
                                            ? r.elementoQuimico
                                                .nombreElementoQuimico
                                            : string.Empty,

                                    simboloElementoQuimico =
                                        r.elementoQuimico != null
                                            ? r.elementoQuimico
                                                .simboloElementoQuimico
                                            : string.Empty,

                                    cantidadAporte =
                                        r.cantidadAporte
                                })
                            .ToList()
                });
        }
    }
}
