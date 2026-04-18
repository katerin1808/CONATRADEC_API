using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.FuenteNutrienteDto;

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
        public async Task<ActionResult<IEnumerable<FuenteNutrienteListarDto>>> Listar()
        {
            var lista = await _db.fuenteNutriente
                .Where(x => x.activo)
                .Select(x => new FuenteNutrienteListarDto
                {
                    fuenteNutrientesId = x.fuenteNutrientesId,
                    nombreNutriente = x.nombreNutriente.Trim(),
                    descripcionNutriente = x.descripcionNutriente.Trim(),
                    precioNutriente = x.precioNutriente
                })
                .ToListAsync();

            return Ok(lista);
        }

        [HttpGet("obtener/{id:int}")]
        public async Task<ActionResult<FuenteNutrienteListarDto>> ObtenerPorId(int id)
        {
            var entidad = await _db.fuenteNutriente
                .Where(x => x.fuenteNutrientesId == id && x.activo)
                .Select(x => new FuenteNutrienteListarDto
                {
                    fuenteNutrientesId = x.fuenteNutrientesId,
                    nombreNutriente = x.nombreNutriente.Trim(),
                    descripcionNutriente = x.descripcionNutriente.Trim(),
                    precioNutriente = x.precioNutriente
                })
                .FirstOrDefaultAsync();

            if (entidad == null)
                return NotFound(new { mensaje = "Fuente de nutrientes no encontrada o inactiva." });

            return Ok(entidad);
        }

        [HttpPost("crear")]
        public async Task<IActionResult> Crear([FromBody] FuenteNutrienteCrearDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            await using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                string nombre = dto.nombreNutriente.Trim().ToUpper();

                bool existe = await _db.fuenteNutriente
                    .AnyAsync(f => f.nombreNutriente.Trim().ToUpper() == nombre && f.activo);

                if (existe)
                    return BadRequest(new
                    {
                        mensaje = $"Ya existe una fuente de nutriente con el nombre '{dto.nombreNutriente}'."
                    });

                var entidad = new FuenteNutriente
                {
                    nombreNutriente = nombre,
                    descripcionNutriente = dto.descripcionNutriente.Trim(),
                    precioNutriente = dto.precioNutriente,
                    activo = true
                };

                _db.fuenteNutriente.Add(entidad);
                await _db.SaveChangesAsync();

                await trans.CommitAsync();

                return Ok(new
                {
                    mensaje = "Fuente de nutrientes creada correctamente.",
                    fuenteNutrientesId = entidad.fuenteNutrientesId,
                    nombreNutriente = entidad.nombreNutriente,
                    descripcionNutriente = entidad.descripcionNutriente,
                    precioNutriente = entidad.precioNutriente,
                    activo = entidad.activo
                });
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                return StatusCode(500, new
                {
                    mensaje = "Ocurrió un error al crear la fuente de nutrientes.",
                    detalle = ex.Message
                });
            }
        }

        [HttpPut("editar/{id:int}")]
        public async Task<IActionResult> Editar(int id, [FromBody] FuenteNutrienteEditarDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            await using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                var entidad = await _db.fuenteNutriente
                    .FirstOrDefaultAsync(f => f.fuenteNutrientesId == id && f.activo);

                if (entidad == null)
                    return NotFound(new { mensaje = "Fuente de nutrientes no encontrada o ya eliminada." });

                string nombre = dto.nombreNutriente.Trim().ToUpper();

                bool existe = await _db.fuenteNutriente
                    .AnyAsync(f =>
                        f.fuenteNutrientesId != id &&
                        f.activo &&
                        f.nombreNutriente.Trim().ToUpper() == nombre);

                if (existe)
                    return BadRequest(new
                    {
                        mensaje = $"Ya existe una fuente de nutriente con el nombre '{dto.nombreNutriente}'."
                    });

                entidad.nombreNutriente = nombre;
                entidad.descripcionNutriente = dto.descripcionNutriente.Trim();
                entidad.precioNutriente = dto.precioNutriente;

                await _db.SaveChangesAsync();
                await trans.CommitAsync();

                return Ok(new
                {
                    mensaje = "Fuente de nutrientes editada correctamente.",
                    fuenteNutrientesId = entidad.fuenteNutrientesId,
                    nombreNutriente = entidad.nombreNutriente,
                    descripcionNutriente = entidad.descripcionNutriente,
                    precioNutriente = entidad.precioNutriente,
                    activo = entidad.activo
                });
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                return StatusCode(500, new
                {
                    mensaje = "Ocurrió un error al editar la fuente de nutrientes.",
                    detalle = ex.Message
                });
            }
        }

        [HttpDelete("eliminar/{id:int}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            try
            {
                var entidad = await _db.fuenteNutriente
                    .FirstOrDefaultAsync(f => f.fuenteNutrientesId == id && f.activo);

                if (entidad == null)
                    return NotFound(new { mensaje = "Fuente de nutrientes no encontrada o ya eliminada." });

                entidad.activo = false;
                await _db.SaveChangesAsync();

                return Ok(new { mensaje = "Fuente de nutrientes eliminada correctamente." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje = "Ocurrió un error al eliminar la fuente de nutrientes.",
                    detalle = ex.Message
                });
            }
        }
    }
}