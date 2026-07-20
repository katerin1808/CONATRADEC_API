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
            var data = await _db.fuenteNutriente
                .Where(x => x.activo)
                .Select(x => new FuenteNutrienteConElementosRespuestaDto
                {
                    fuenteNutrientesId = x.fuenteNutrientesId,
                    nombreNutriente = x.nombreNutriente,
                    descripcionNutriente = x.descripcionNutriente,
                    precioNutriente = x.precioNutriente,
                    activo = x.activo,

                    habilitadaEnmiendaCalcarea = _db.ParametroEnmiendaCalcarea
                        .Any(e =>
                            e.fuenteNutrientesId == x.fuenteNutrientesId &&
                            e.activo == true),

                    habilitadaFertilizacionMixta = _db.fuenteFertilizacionMixta
                        .Any(f =>
                            f.fuenteNutrientesId == x.fuenteNutrientesId &&
                            f.activo == true),

                    parametrosEnmiendaCalcarea = _db.ParametroEnmiendaCalcarea
                      .Where(e =>
                    e.fuenteNutrientesId == x.fuenteNutrientesId &&
                    e.activo == true)
                   .Select(e => new ParametroEnmiendaCalcareaFuenteDto
    {
                    prnt = e.prnt,
                    descripcionParametro = e.descripcionParametro
                     })
                   .ToList(),

                    elementosQuimicos = x.fuenteNutrienteElementoQuimico
                        .Where(r => r.activo)
                        .Select(r => new ElementoFuenteRespuestaDto
                        {
                            fuenteNutrienteElementoQuimicoId = r.fuenteNutrienteElementoQuimicoId,
                            elementoQuimicosId = r.elementoQuimicosId,
                            nombreElementoQuimico = r.elementoQuimico != null ? r.elementoQuimico.nombreElementoQuimico : "",
                            simboloElementoQuimico = r.elementoQuimico != null ? r.elementoQuimico.simboloElementoQuimico : "",
                            cantidadAporte = r.cantidadAporte
                        }).ToList()
                })
                .ToListAsync();

            return Ok(data);
        }

        [HttpGet("obtener/{id:int}")]
        public async Task<IActionResult> ObtenerPorId(int id)
        {
            var data = await _db.fuenteNutriente
                .Where(x => x.fuenteNutrientesId == id && x.activo)
                .Select(x => new FuenteNutrienteConElementosRespuestaDto
                {
                    fuenteNutrientesId = x.fuenteNutrientesId,
                    nombreNutriente = x.nombreNutriente,
                    descripcionNutriente = x.descripcionNutriente,
                    precioNutriente = x.precioNutriente,
                    activo = x.activo,
                    elementosQuimicos = x.fuenteNutrienteElementoQuimico
                        .Where(r => r.activo)
                        .Select(r => new ElementoFuenteRespuestaDto
                        {
                            fuenteNutrienteElementoQuimicoId = r.fuenteNutrienteElementoQuimicoId,
                            elementoQuimicosId = r.elementoQuimicosId,
                            nombreElementoQuimico = r.elementoQuimico != null ? r.elementoQuimico.nombreElementoQuimico : "",
                            simboloElementoQuimico = r.elementoQuimico != null ? r.elementoQuimico.simboloElementoQuimico : "",
                            cantidadAporte = r.cantidadAporte
                        }).ToList()
                })
                .FirstOrDefaultAsync();

            if (data == null)
                return NotFound(new { mensaje = "Fuente nutriente no encontrada." });

            return Ok(data);
        }

    

        [HttpPost("crear-con-elementos")]
        public async Task<IActionResult> CrearConElementos([FromBody] FuenteNutrienteConElementosCrearDto dto)
        {
            await using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                if (string.IsNullOrWhiteSpace(dto.nombreNutriente))
                    return BadRequest(new { mensaje = "El nombre nutriente es obligatorio." });

                if (dto.elementosQuimicos == null || !dto.elementosQuimicos.Any())
                    return BadRequest(new { mensaje = "Debe agregar al menos un elemento químico." });

                string nombre = dto.nombreNutriente.Trim().ToUpper();

                bool existe = await _db.fuenteNutriente
                    .AnyAsync(x => x.nombreNutriente.Trim().ToUpper() == nombre && x.activo);

                if (existe)
                    return BadRequest(new { mensaje = "Ya existe una fuente nutriente con ese nombre." });

                bool elementosRepetidos = dto.elementosQuimicos
                    .GroupBy(x => x.elementoQuimicosId)
                    .Any(g => g.Count() > 1);

                if (elementosRepetidos)
                    return BadRequest(new { mensaje = "No puede repetir elementos químicos en la matriz." });

                var idsElementos = dto.elementosQuimicos
                    .Select(x => x.elementoQuimicosId)
                    .ToList();

                var idsExistentes = await _db.elementoQuimico
                    .Where(x => idsElementos.Contains(x.elementoQuimicosId) && x.activo)
                    .Select(x => x.elementoQuimicosId)
                    .ToListAsync();

                var faltantes = idsElementos.Except(idsExistentes).ToList();

                if (faltantes.Any())
                    return BadRequest(new
                    {
                        mensaje = "Hay elementos químicos inválidos o inactivos.",
                        faltantes
                    });

                var fuente = new FuenteNutriente
                {
                    nombreNutriente = nombre,
                    descripcionNutriente = dto.descripcionNutriente.Trim(),
                    precioNutriente = dto.precioNutriente,
                    activo = true
                };

                _db.fuenteNutriente.Add(fuente);
                await _db.SaveChangesAsync();

                var relaciones = dto.elementosQuimicos.Select(e => new FuenteNutrienteElementoQuimico
                {
                    fuenteNutrientesId = fuente.fuenteNutrientesId,
                    elementoQuimicosId = e.elementoQuimicosId,
                    cantidadAporte = e.cantidadAporte,
                    activo = true
                }).ToList();

                _db.fuenteNutrienteElementoQuimico.AddRange(relaciones);
                await _db.SaveChangesAsync();

                await trans.CommitAsync();

                var respuesta = await _db.fuenteNutriente
                    .Where(x => x.fuenteNutrientesId == fuente.fuenteNutrientesId)
                    .Select(x => new FuenteNutrienteConElementosRespuestaDto
                    {
                        fuenteNutrientesId = x.fuenteNutrientesId,
                        nombreNutriente = x.nombreNutriente,
                        descripcionNutriente = x.descripcionNutriente,
                        precioNutriente = x.precioNutriente,
                        activo = x.activo,
                        elementosQuimicos = x.fuenteNutrienteElementoQuimico
                            .Where(r => r.activo)
                            .Select(r => new ElementoFuenteRespuestaDto
                            {
                                fuenteNutrienteElementoQuimicoId = r.fuenteNutrienteElementoQuimicoId,
                                elementoQuimicosId = r.elementoQuimicosId,
                                nombreElementoQuimico = r.elementoQuimico != null ? r.elementoQuimico.nombreElementoQuimico : "",
                                simboloElementoQuimico = r.elementoQuimico != null ? r.elementoQuimico.simboloElementoQuimico : "",
                                cantidadAporte = r.cantidadAporte
                            })
                            .ToList()
                    })
                    .FirstAsync();

                return Ok(new
                {
                    mensaje = "Fuente nutriente creada correctamente con sus elementos químicos.",
                    data = respuesta
                });
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();

                return StatusCode(500, new
                {
                    mensaje = "Error al crear fuente nutriente.",
                    detalle = ex.Message
                });
            }
        }

        [HttpPost("{fuenteNutrientesId}/habilitar-enmienda-calcarea")]
        public async Task<IActionResult> HabilitarEnmiendaCalcarea(
    int fuenteNutrientesId,
    [FromBody] HabilitarEnmiendaCalcareaDto dto)
        {
            var fuente = await _db.fuenteNutriente
                .FirstOrDefaultAsync(x => x.fuenteNutrientesId == fuenteNutrientesId && x.activo);

            if (fuente == null)
                return NotFound(new { mensaje = "La fuente nutriente no existe o está inactiva." });

            if (dto.prnt <= 0)
                return BadRequest(new { mensaje = "El PRNT debe ser mayor a cero." });

            var yaExiste = await _db.ParametroEnmiendaCalcarea
                .AnyAsync(x => x.fuenteNutrientesId == fuenteNutrientesId && x.activo);

            if (yaExiste)
                return BadRequest(new { mensaje = "Esta fuente ya está habilitada para cálculo de enmienda calcárea." });

            var parametro = new ParametroEnmiendaCalcarea
            {
                fuenteNutrientesId = fuenteNutrientesId,
                saturacionBasesDeseada = 70,
                prnt = dto.prnt,
                factorTonHaALbHa = 2200,
                factorHaAMz = 0.7026m,
                factorTonHaAKgHa = 1000,
                descripcionParametro = dto.descripcionParametro ?? "Fuente utilizada para cálculo de enmienda calcárea",
                activo = true
            };

            _db.ParametroEnmiendaCalcarea.Add(parametro);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Fuente habilitada para cálculo de enmienda calcárea.",
                fuente.fuenteNutrientesId,
                fuente.nombreNutriente,
                parametro.parametroEnmiendaCalcareaId,
                parametro.prnt
            });
        }

        [HttpPut("deshabilitar-enmienda-calcarea/{fuenteNutrientesId:int}")]
        public async Task<IActionResult> DeshabilitarEnmiendaCalcarea(int fuenteNutrientesId)
        {
            var parametro = await _db.ParametroEnmiendaCalcarea
                .FirstOrDefaultAsync(x => x.fuenteNutrientesId == fuenteNutrientesId);

            if (parametro == null)
            {
                return NotFound(new
                {
                    mensaje = "La fuente no existe en la configuración de enmienda calcárea."
                });
            }

            parametro.activo = false;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Fuente deshabilitada para enmienda calcárea.",
                parametro.fuenteNutrientesId,
                parametro.activo
            });
        }
        [HttpPost("habilitar-fertilizacion-mixta/{fuenteNutrientesId:int}")]
        public async Task<IActionResult> HabilitarFertilizacionMixta(int fuenteNutrientesId)
        {
            var fuente = await _db.fuenteNutriente
                .FirstOrDefaultAsync(x =>
                    x.fuenteNutrientesId == fuenteNutrientesId &&
                    x.activo == true);

            if (fuente == null)
            {
                return NotFound(new
                {
                    mensaje = "La fuente nutriente no existe o está inactiva."
                });
            }

            var yaExiste = await _db.fuenteFertilizacionMixta
                .FirstOrDefaultAsync(x =>
                    x.fuenteNutrientesId == fuenteNutrientesId);

            if (yaExiste != null)
            {
                if (yaExiste.activo == true)
                {
                    return BadRequest(new
                    {
                        mensaje = "Esta fuente ya está habilitada para fertilización mixta."
                    });
                }

                yaExiste.activo = true;

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    mensaje = "Fuente habilitada nuevamente para fertilización mixta.",
                    fuente.fuenteNutrientesId,
                    fuente.nombreNutriente
                });
            }

            var fuenteMixta = new FuenteFertilizacionMixta
            {
                fuenteNutrientesId = fuenteNutrientesId,
                activo = true
            };

            _db.fuenteFertilizacionMixta.Add(fuenteMixta);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Fuente habilitada para fertilización mixta.",
                fuente.fuenteNutrientesId,
                fuente.nombreNutriente
            });
        }
        [HttpPut("deshabilitar-fertilizacion-mixta/{fuenteNutrientesId:int}")]
        public async Task<IActionResult> DeshabilitarFertilizacionMixta(int fuenteNutrientesId)
        {
            var fuenteMixta = await _db.fuenteFertilizacionMixta
                .FirstOrDefaultAsync(x => x.fuenteNutrientesId == fuenteNutrientesId);

            if (fuenteMixta == null)
            {
                return NotFound(new
                {
                    mensaje = "La fuente no existe en la configuración de fertilización mixta."
                });
            }

            fuenteMixta.activo = false;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Fuente deshabilitada para fertilización mixta.",
                fuenteMixta.fuenteNutrientesId,
                fuenteMixta.activo
            });
        }

        [HttpGet("listar-fertilizacion-mixta")]
        public async Task<IActionResult> ListarFertilizacionMixta()
        {
            var data = await _db.fuenteNutriente
                .Where(x =>
                    x.activo == true &&
                    _db.fuenteFertilizacionMixta.Any(f =>
                        f.fuenteNutrientesId == x.fuenteNutrientesId &&
                        f.activo == true))
                .Select(x => new FuenteFertilizacionMixtaListadoDto
                {
                    fuenteNutrientesId = x.fuenteNutrientesId,
                    nombreNutriente = x.nombreNutriente,
                    descripcionNutriente = x.descripcionNutriente,
                    precioNutriente = x.precioNutriente,
                    activo = x.activo,

                    elementosQuimicos = x.fuenteNutrienteElementoQuimico
                        .Where(r => r.activo == true)
                        .Select(r => new ElementoFuenteRespuestaDto
                        {
                            fuenteNutrienteElementoQuimicoId = r.fuenteNutrienteElementoQuimicoId,
                            elementoQuimicosId = r.elementoQuimicosId,
                            nombreElementoQuimico = r.elementoQuimico != null
                                ? r.elementoQuimico.nombreElementoQuimico
                                : "",
                            simboloElementoQuimico = r.elementoQuimico != null
                                ? r.elementoQuimico.simboloElementoQuimico
                                : "",
                            cantidadAporte = r.cantidadAporte
                        })
                        .ToList()
                })
                .ToListAsync();

            return Ok(data);
        }

        [HttpGet("enmiendas-calcareas")]
        public async Task<IActionResult> ObtenerEnmiendasCalcareas()
        {
            var data = await _db.ParametroEnmiendaCalcarea
                .Include(x => x.FuenteNutriente)
                .Where(x => x.activo && x.FuenteNutriente != null && x.FuenteNutriente.activo)
                .Select(x => new
                {
                    x.parametroEnmiendaCalcareaId,
                    x.fuenteNutrientesId,
                    nombreNutriente = x.FuenteNutriente!.nombreNutriente,
                    precioNutriente = x.FuenteNutriente.precioNutriente,
                    x.prnt,
                    x.descripcionParametro
                })
                .ToListAsync();

            return Ok(data);
        }

        [HttpPut("editar-con-elementos/{id:int}")]
        public async Task<IActionResult> EditarConElementos(
     int id,
     [FromBody] FuenteNutrienteConElementosCrearDto dto)
        {
            await using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                var fuente = await _db.fuenteNutriente
                    .FirstOrDefaultAsync(x => x.fuenteNutrientesId == id && x.activo);

                if (fuente == null)
                    return NotFound(new { mensaje = "Fuente nutriente no encontrada." });

                if (string.IsNullOrWhiteSpace(dto.nombreNutriente))
                    return BadRequest(new { mensaje = "El nombre nutriente es obligatorio." });

                if (dto.elementosQuimicos == null || !dto.elementosQuimicos.Any())
                    return BadRequest(new { mensaje = "Debe agregar al menos un elemento químico." });

                string nombre = dto.nombreNutriente.Trim().ToUpper();

                bool existeDuplicado = await _db.fuenteNutriente
                    .AnyAsync(x =>
                        x.fuenteNutrientesId != id &&
                        x.nombreNutriente.Trim().ToUpper() == nombre &&
                        x.activo);

                if (existeDuplicado)
                    return BadRequest(new { mensaje = "Ya existe otra fuente nutriente con ese nombre." });

                bool elementosRepetidos = dto.elementosQuimicos
                    .GroupBy(x => x.elementoQuimicosId)
                    .Any(g => g.Count() > 1);

                if (elementosRepetidos)
                    return BadRequest(new { mensaje = "No puede repetir elementos químicos en la matriz." });

                var idsElementos = dto.elementosQuimicos
                    .Select(x => x.elementoQuimicosId)
                    .ToList();

                var idsExistentes = await _db.elementoQuimico
                    .Where(x => idsElementos.Contains(x.elementoQuimicosId) && x.activo)
                    .Select(x => x.elementoQuimicosId)
                    .ToListAsync();

                var faltantes = idsElementos.Except(idsExistentes).ToList();

                if (faltantes.Any())
                    return BadRequest(new
                    {
                        mensaje = "Hay elementos químicos inválidos o inactivos.",
                        faltantes
                    });

                fuente.nombreNutriente = nombre;
                fuente.descripcionNutriente = dto.descripcionNutriente.Trim();
                fuente.precioNutriente = dto.precioNutriente;

                var relacionesActuales = await _db.fuenteNutrienteElementoQuimico
                    .Where(x => x.fuenteNutrientesId == id && x.activo)
                    .ToListAsync();

                foreach (var relacion in relacionesActuales)
                {
                    relacion.activo = false;
                }

                var nuevasRelaciones = dto.elementosQuimicos.Select(e => new FuenteNutrienteElementoQuimico
                {
                    fuenteNutrientesId = id,
                    elementoQuimicosId = e.elementoQuimicosId,
                    cantidadAporte = e.cantidadAporte,
                    activo = true
                }).ToList();

                _db.fuenteNutrienteElementoQuimico.AddRange(nuevasRelaciones);

                await _db.SaveChangesAsync();
                await trans.CommitAsync();

                var respuesta = await _db.fuenteNutriente
                    .Where(x => x.fuenteNutrientesId == id)
                    .Select(x => new FuenteNutrienteConElementosRespuestaDto
                    {
                        fuenteNutrientesId = x.fuenteNutrientesId,
                        nombreNutriente = x.nombreNutriente,
                        descripcionNutriente = x.descripcionNutriente,
                        precioNutriente = x.precioNutriente,
                        activo = x.activo,
                        elementosQuimicos = x.fuenteNutrienteElementoQuimico
                            .Where(r => r.activo)
                            .Select(r => new ElementoFuenteRespuestaDto
                            {
                                fuenteNutrienteElementoQuimicoId = r.fuenteNutrienteElementoQuimicoId,
                                elementoQuimicosId = r.elementoQuimicosId,
                                nombreElementoQuimico = r.elementoQuimico != null ? r.elementoQuimico.nombreElementoQuimico : "",
                                simboloElementoQuimico = r.elementoQuimico != null ? r.elementoQuimico.simboloElementoQuimico : "",
                                cantidadAporte = r.cantidadAporte
                            })
                            .ToList()
                    })
                    .FirstAsync();

                return Ok(new
                {
                    mensaje = "Fuente nutriente actualizada correctamente con su matriz.",
                    data = respuesta
                });
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();

                return StatusCode(500, new
                {
                    mensaje = "Error al editar fuente nutriente.",
                    detalle = ex.Message
                });
            }
        }

        [HttpDelete("eliminar/{id:int}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var fuente = await _db.fuenteNutriente
                .FirstOrDefaultAsync(x =>
                    x.fuenteNutrientesId == id &&
                    x.activo);

            if (fuente == null)
            {
                return NotFound(new
                {
                    mensaje = "Fuente nutriente no encontrada o ya está desactivada."
                });
            }

            var dependencias = new List<string>();

            /*
             * CONFIGURACIONES ACTIVAS
             */

            var usadaEnParametrosEnmienda = await _db.ParametroEnmiendaCalcarea
                .AnyAsync(x =>
                    x.fuenteNutrientesId == id &&
                    x.activo);

            if (usadaEnParametrosEnmienda)
            {
                dependencias.Add("parámetros de enmienda calcárea");
            }

            var usadaEnParametrosOrganicos = await _db.ParametroFuenteOrganicaAporte
                .AnyAsync(x =>
                    x.fuenteNutrientesId == id &&
                    x.activo);

            if (usadaEnParametrosOrganicos)
            {
                dependencias.Add("parámetros de fuentes orgánicas");
            }

            var habilitadaParaFertilizacionMixta =
                await _db.fuenteFertilizacionMixta
                    .AnyAsync(x =>
                        x.fuenteNutrientesId == id &&
                        x.activo);

            if (habilitadaParaFertilizacionMixta)
            {
                dependencias.Add("configuración de fertilización mixta");
            }

            /*
             * DATOS HISTÓRICOS
             *
             * No se filtran por activo porque deben conservar
             * la referencia a la fuente utilizada.
             */

            var usadaEnEnmiendas = await _db.enmiendaCalcarea
                .AnyAsync(x =>
                    x.fuenteNutrientesId == id);

            if (usadaEnEnmiendas)
            {
                dependencias.Add("enmiendas calcáreas guardadas");
            }

            var usadaEnFormulas = await _db.formulaNutricionalDetalle
                .AnyAsync(x =>
                    x.fuenteNutrientesId == id);

            if (usadaEnFormulas)
            {
                dependencias.Add("fórmulas nutricionales guardadas");
            }

            var usadaEnFertilizacionesMixtas =
                await _db.fertilizacionMixtaFuente
                    .AnyAsync(x =>
                        x.fuenteNutrientesId == id);

            if (usadaEnFertilizacionesMixtas)
            {
                dependencias.Add("fertilizaciones mixtas guardadas");
            }

            var usadaEnControlesAplicacion =
                await _db.FuenteNutrienteControlAplicaciones
                    .AnyAsync(x =>
                        x.fuenteNutrientesId == id);

            if (usadaEnControlesAplicacion)
            {
                dependencias.Add("controles de aplicación");
            }

            var usadaEnInterpretaciones =
                await _db.InterpretacionFuenteNutrientes
                    .AnyAsync(x =>
                        x.fuenteNutrientesId == id);

            if (usadaEnInterpretaciones)
            {
                dependencias.Add("interpretaciones guardadas");
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

                    usadoEn = dependencias
                });
            }

            await using var transaccion =
                await _db.Database.BeginTransactionAsync();

            try
            {
                fuente.activo = false;

                /*
                 * La matriz pertenece directamente a la fuente.
                 * Se desactiva solamente después de comprobar
                 * que la fuente no tiene otras dependencias.
                 */
                var relaciones = await _db.fuenteNutrienteElementoQuimico
                    .Where(x =>
                        x.fuenteNutrientesId == id &&
                        x.activo)
                    .ToListAsync();

                foreach (var relacion in relaciones)
                {
                    relacion.activo = false;
                }

                await _db.SaveChangesAsync();
                await transaccion.CommitAsync();

                return Ok(new
                {
                    mensaje = "Fuente nutriente desactivada correctamente.",
                    data = new
                    {
                        fuente.fuenteNutrientesId,
                        fuente.nombreNutriente,
                        fuente.activo,
                        relacionesDesactivadas = relaciones.Count
                    }
                });
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync();

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        mensaje = "Ocurrió un error al desactivar la fuente nutriente.",
                        detalle = ex.Message
                    });
            }
        }

        [HttpGet("aportes-tabla")]
        public async Task<IActionResult> AportesTabla()
        {
            var fuentes = await _db.fuenteNutriente
                .Where(f => f.activo)
                .ToListAsync();

            var aportes = await _db.fuenteNutrienteElementoQuimico
                .Include(a => a.elementoQuimico)
                .Where(a => a.activo)
                .ToListAsync();

            var data = fuentes.Select(f =>
            {
                var ap = aportes
                    .Where(a => a.fuenteNutrientesId == f.fuenteNutrientesId)
                    .ToList();

                decimal Valor(string simbolo)
                {
                    return ap
                        .Where(x => x.elementoQuimico!.simboloElementoQuimico.Trim().ToUpper() == simbolo)
                        .Select(x => x.cantidadAporte)
                        .FirstOrDefault();
                }

                return new FuenteNutrienteAporteTablaDto
                {
                    fuenteNutrientesId = f.fuenteNutrientesId,
                    fuente = f.nombreNutriente,

                    n = Valor("N"),
                    p = Valor("P"),
                    k = Valor("K"),
                    ca = Valor("CA"),
                    mg = Valor("MG"),
                    zn = Valor("ZN"),
                    s = Valor("S"),
                    b = Valor("B")
                };
            }).ToList();

            return Ok(data);
        }
    }
}