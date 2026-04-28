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
            await using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                var fuente = await _db.fuenteNutriente
                    .FirstOrDefaultAsync(x => x.fuenteNutrientesId == id && x.activo);

                if (fuente == null)
                    return NotFound(new { mensaje = "Fuente nutriente no encontrada o ya eliminada." });

                fuente.activo = false;

                var relaciones = await _db.fuenteNutrienteElementoQuimico
                    .Where(x => x.fuenteNutrientesId == id && x.activo)
                    .ToListAsync();

                foreach (var relacion in relaciones)
                {
                    relacion.activo = false;
                }

                await _db.SaveChangesAsync();
                await trans.CommitAsync();

                return Ok(new
                {
                    mensaje = "Fuente nutriente eliminada correctamente junto con su matriz.",
                    data = new
                    {
                        fuenteNutrientesId = fuente.fuenteNutrientesId,
                        nombreNutriente = fuente.nombreNutriente,
                        descripcionNutriente = fuente.descripcionNutriente,
                        precioNutriente = fuente.precioNutriente,
                        activo = fuente.activo,
                        relacionesDesactivadas = relaciones.Count
                    }
                });
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();

                return StatusCode(500, new
                {
                    mensaje = "Error al eliminar fuente nutriente.",
                    detalle = ex.Message
                });
            }
        }
    }
}