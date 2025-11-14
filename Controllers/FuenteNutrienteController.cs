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
        public FuenteNutrienteController(DBContext db) => _db = db;

        // ============================================
        // LISTAR (solo activos)
        // ============================================
        [HttpGet("listar")]
        public async Task<ActionResult<IEnumerable<FuenteNutrienteListarDto>>> Listar()
        {
            var lista = await _db.FuenteNutrientes
                .Where(x => x.activo)  // activo = 1
                .Select(x => new FuenteNutrienteListarDto
                {
                    fuenteNutrientesId = x.fuenteNutrientesId,
                    nombreNutriente = x.nombreNutriente,
                    descripcionNutriente = x.descripcionNutriente,
                    precioNutriente = x.precioNutriente
                })
                .ToListAsync();

            return Ok(lista);
        }

        // ============================================
        // CREAR (CON BEGIN TRANSACTION)
        // ============================================
        [HttpPost("crear")]
        public async Task<IActionResult> Crear([FromBody] FuenteNutrienteCrearDto dto)
        {
            using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                var entidad = new FuenteNutriente
                {
                    nombreNutriente = dto.nombreNutriente,
                    descripcionNutriente = dto.descripcionNutriente,
                    precioNutriente = dto.precioNutriente,
                    activo = true
                };

                _db.FuenteNutrientes.Add(entidad);
                await _db.SaveChangesAsync();

                await trans.CommitAsync();
                return Ok("Fuente de nutrientes creada correctamente.");
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        // ============================================
        // EDITAR
        // ============================================
        [HttpPut("editar/{id}")]
        public async Task<IActionResult> Editar(int id, [FromBody] FuenteNutrienteEditarDto dto)
        {
            var entidad = await _db.FuenteNutrientes.FindAsync(id);
            if (entidad is null)
                return NotFound("Fuente de nutrientes no encontrada.");

            entidad.nombreNutriente = dto.nombreNutriente;
            entidad.descripcionNutriente = dto.descripcionNutriente;
            entidad.precioNutriente = dto.precioNutriente;

            await _db.SaveChangesAsync();

            return Ok("Fuente de nutrientes editada correctamente.");
        }

        // ============================================
        // ELIMINAR (LÓGICO)
        // ============================================
        [HttpDelete("eliminar/{id}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            // Buscamos solo si está activo
            var entidad = await _db.FuenteNutrientes
                .FirstOrDefaultAsync(f => f.fuenteNutrientesId == id && f.activo);

            if (entidad is null)
                return NotFound("Fuente de nutrientes no encontrada o ya eliminada.");

            entidad.activo = false;

            await _db.SaveChangesAsync();

            return Ok("Fuente de nutrientes eliminada correctamente.");
        }
    }

}