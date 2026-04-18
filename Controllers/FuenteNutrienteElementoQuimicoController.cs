using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace CONATRADEC_API.Controllers
{


    [ApiController]
    [Route("api/[controller]")]
    public class FuenteNutrienteElementoQuimicoController : ControllerBase
    {
        private readonly DBContext _db; // Cambia DbContext por tu contexto real

        public FuenteNutrienteElementoQuimicoController(DBContext db) // Cambia DbContext por tu contexto real
        {
            _db = db;
        }   

        [HttpGet("listar")]
        public async Task<IActionResult> Listar()
        {
            var data = await _db.fuenteNutrienteElementoQuimico
                .Include(x => x.fuenteNutriente)
                .Include(x => x.elementoQuimico)
                .OrderByDescending(x => x.fuenteNutrienteElementoQuimicoId)
                .Select(x => new FuenteNutrienteElementoQuimicoListarDto
                {
                    fuenteNutrienteElementoQuimicoId = x.fuenteNutrienteElementoQuimicoId,
                    cantidadAporte = x.cantidadAporte,
                    activo = x.activo,
                    fuenteNutrientesId = x.fuenteNutrientesId,
                    nombreNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.nombreNutriente : string.Empty,
                    descripcionNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.descripcionNutriente : string.Empty,
                    precioNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.precioNutriente : 0,
                    elementoQuimicosId = x.elementoQuimicosId,
                    nombreElementoQuimico = x.elementoQuimico != null ? x.elementoQuimico.nombreElementoQuimico : string.Empty,
                    simboloElementoQuimico = x.elementoQuimico != null ? x.elementoQuimico.simboloElementoQuimico : string.Empty
                })
                .ToListAsync();

            return Ok(data);
        }

        [HttpGet("obtener/{id:int}")]
        public async Task<IActionResult> ObtenerPorId(int id)
        {
            var data = await _db.fuenteNutrienteElementoQuimico
                .Include(x => x.fuenteNutriente)
                .Include(x => x.elementoQuimico)
                .Where(x => x.fuenteNutrienteElementoQuimicoId == id)
                .Select(x => new FuenteNutrienteElementoQuimicoListarDto
                {
                    fuenteNutrienteElementoQuimicoId = x.fuenteNutrienteElementoQuimicoId,
                    cantidadAporte = x.cantidadAporte,
                    activo = x.activo,
                    fuenteNutrientesId = x.fuenteNutrientesId,
                    nombreNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.nombreNutriente : string.Empty,
                    descripcionNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.descripcionNutriente : string.Empty,
                    precioNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.precioNutriente : 0,
                    elementoQuimicosId = x.elementoQuimicosId,
                    nombreElementoQuimico = x.elementoQuimico != null ? x.elementoQuimico.nombreElementoQuimico : string.Empty,
                    simboloElementoQuimico = x.elementoQuimico != null ? x.elementoQuimico.simboloElementoQuimico : string.Empty
                })
                .FirstOrDefaultAsync();

            if (data == null)
                return NotFound(new { mensaje = "Registro no encontrado." });

            return Ok(data);
        }

        [HttpPost("crear")]
        public async Task<IActionResult> Crear([FromBody] FuenteNutrienteElementoQuimicoCrearDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            await using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                var valid = await ValidarLlavesForaneas(dto.fuenteNutrientesId, dto.elementoQuimicosId);
                if (!valid.ok)
                    return BadRequest(new { mensaje = valid.mensaje });

                var existeRelacion = await _db.fuenteNutrienteElementoQuimico.AnyAsync(x =>
                    x.fuenteNutrientesId == dto.fuenteNutrientesId &&
                    x.elementoQuimicosId == dto.elementoQuimicosId &&
                    x.activo);

                if (existeRelacion)
                    return BadRequest(new
                    {
                        mensaje = "Ya existe una relación activa entre esta fuente nutriente y este elemento químico."
                    });

                var entity = new FuenteNutrienteElementoQuimico
                {
                    cantidadAporte = dto.cantidadAporte,
                    fuenteNutrientesId = dto.fuenteNutrientesId,
                    elementoQuimicosId = dto.elementoQuimicosId,
                    activo = true
                };

                _db.fuenteNutrienteElementoQuimico.Add(entity);
                await _db.SaveChangesAsync();

                var respuesta = await _db.fuenteNutrienteElementoQuimico
                    .Include(x => x.fuenteNutriente)
                    .Include(x => x.elementoQuimico)
                    .Where(x => x.fuenteNutrienteElementoQuimicoId == entity.fuenteNutrienteElementoQuimicoId)
                    .Select(x => new FuenteNutrienteElementoQuimicoListarDto
                    {
                        fuenteNutrienteElementoQuimicoId = x.fuenteNutrienteElementoQuimicoId,
                        cantidadAporte = x.cantidadAporte,
                        activo = x.activo,
                        fuenteNutrientesId = x.fuenteNutrientesId,
                        nombreNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.nombreNutriente : string.Empty,
                        descripcionNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.descripcionNutriente : string.Empty,
                        precioNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.precioNutriente : 0,
                        elementoQuimicosId = x.elementoQuimicosId,
                        nombreElementoQuimico = x.elementoQuimico != null ? x.elementoQuimico.nombreElementoQuimico : string.Empty,
                        simboloElementoQuimico = x.elementoQuimico != null ? x.elementoQuimico.simboloElementoQuimico : string.Empty
                    })
                    .FirstAsync();

                await trans.CommitAsync();

                return Ok(new
                {
                    mensaje = "Creado correctamente",
                    data = respuesta
                });
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                return StatusCode(500, new
                {
                    mensaje = "Ocurrió un error al crear el registro.",
                    detalle = ex.Message
                });
            }
        }

        [HttpPut("editar/{id:int}")]
        public async Task<IActionResult> Editar(int id, [FromBody] FuenteNutrienteElementoQuimicoActualizarDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (id != dto.fuenteNutrienteElementoQuimicoId)
                return BadRequest(new { mensaje = "El id de la ruta no coincide con el id del objeto." });

            await using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                var entity = await _db.fuenteNutrienteElementoQuimico
                    .FirstOrDefaultAsync(x => x.fuenteNutrienteElementoQuimicoId == id);

                if (entity == null)
                    return NotFound(new { mensaje = "Registro no encontrado." });

                var valid = await ValidarLlavesForaneas(dto.fuenteNutrientesId, dto.elementoQuimicosId);
                if (!valid.ok)
                    return BadRequest(new { mensaje = valid.mensaje });

                var existeDuplicado = await _db.fuenteNutrienteElementoQuimico.AnyAsync(x =>
                    x.fuenteNutrienteElementoQuimicoId != id &&
                    x.fuenteNutrientesId == dto.fuenteNutrientesId &&
                    x.elementoQuimicosId == dto.elementoQuimicosId &&
                    x.activo);

                if (existeDuplicado)
                    return BadRequest(new
                    {
                        mensaje = "Ya existe otra relación activa con esa fuente nutriente y elemento químico."
                    });

                entity.cantidadAporte = dto.cantidadAporte;
                entity.fuenteNutrientesId = dto.fuenteNutrientesId;
                entity.elementoQuimicosId = dto.elementoQuimicosId;

                await _db.SaveChangesAsync();

                var respuesta = await _db.fuenteNutrienteElementoQuimico
                    .Include(x => x.fuenteNutriente)
                    .Include(x => x.elementoQuimico)
                    .Where(x => x.fuenteNutrienteElementoQuimicoId == entity.fuenteNutrienteElementoQuimicoId)
                    .Select(x => new FuenteNutrienteElementoQuimicoListarDto
                    {
                        fuenteNutrienteElementoQuimicoId = x.fuenteNutrienteElementoQuimicoId,
                        cantidadAporte = x.cantidadAporte,
                        activo = x.activo,
                        fuenteNutrientesId = x.fuenteNutrientesId,
                        nombreNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.nombreNutriente : string.Empty,
                        descripcionNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.descripcionNutriente : string.Empty,
                        precioNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.precioNutriente : 0,
                        elementoQuimicosId = x.elementoQuimicosId,
                        nombreElementoQuimico = x.elementoQuimico != null ? x.elementoQuimico.nombreElementoQuimico : string.Empty,
                        simboloElementoQuimico = x.elementoQuimico != null ? x.elementoQuimico.simboloElementoQuimico : string.Empty
                    })
                    .FirstAsync();

                await trans.CommitAsync();

                return Ok(new
                {
                    mensaje = "Actualizado correctamente",
                    data = respuesta
                });
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                return StatusCode(500, new
                {
                    mensaje = "Ocurrió un error al actualizar el registro.",
                    detalle = ex.Message
                });
            }
        }

        [HttpDelete("eliminar/{id:int}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            try
            {
                var entity = await _db.fuenteNutrienteElementoQuimico
                    .FirstOrDefaultAsync(x => x.fuenteNutrienteElementoQuimicoId == id);

                if (entity == null)
                    return NotFound(new { mensaje = "Registro no encontrado." });

                entity.activo = false;
                await _db.SaveChangesAsync();

                return Ok(new { mensaje = "Registro desactivado correctamente." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje = "Ocurrió un error al eliminar el registro.",
                    detalle = ex.Message
                });
            }
        }

        private async Task<(bool ok, string mensaje)> ValidarLlavesForaneas(int fuenteNutrientesId, int elementoQuimicosId)
        {
            var existeFuente = await _db.fuenteNutriente
                .AnyAsync(x => x.fuenteNutrientesId == fuenteNutrientesId && x.activo);

            if (!existeFuente)
                return (false, "La fuente nutriente no existe o está inactiva.");

            var existeElemento = await _db.elementoQuimico
                .AnyAsync(x => x.elementoQuimicosId == elementoQuimicosId && x.activo);

            if (!existeElemento)
                return (false, "El elemento químico no existe o está inactivo.");

            return (true, string.Empty);
        }
    }
}