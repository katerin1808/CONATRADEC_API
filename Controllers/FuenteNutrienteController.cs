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

        // ============================================
        // CREAR (VALIDAR DUPLICADO)
        // ============================================
        [HttpPost("crear")]
        public async Task<IActionResult> Crear([FromBody] FuenteNutrienteCrearDto dto)
        {
            using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                string nombre = dto.nombreNutriente.Trim().ToUpper();

                // 🔹 Validar duplicados
                bool existe = await _db.FuenteNutrientes
                    .AnyAsync(f => f.nombreNutriente.Trim().ToUpper() == nombre && f.activo);

                if (existe)
                    return BadRequest($"Ya existe una fuente de nutriente con el nombre '{dto.nombreNutriente}'.");

                var entidad = new FuenteNutriente
                {
                    nombreNutriente = nombre,
                    descripcionNutriente = dto.descripcionNutriente.Trim(),
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
        // EDITAR (VALIDAR EXISTENCIA + DUPLICADOS)
        // ============================================
        [HttpPut("editar/{id}")]
        public async Task<IActionResult> Editar(int id, [FromBody] FuenteNutrienteEditarDto dto)
        {
            using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                // 🔹 Solo elementos activos
                var entidad = await _db.FuenteNutrientes
                    .FirstOrDefaultAsync(f => f.fuenteNutrientesId == id && f.activo);

                if (entidad is null)
                    return NotFound("Fuente de nutrientes no encontrada o ya eliminada.");

                string nombre = dto.nombreNutriente.Trim().ToUpper();

                // 🔹 Validar duplicados (excluyendo el mismo ID)
                bool existe = await _db.FuenteNutrientes
                    .AnyAsync(f =>
                        f.fuenteNutrientesId != id &&
                        f.activo &&
                        f.nombreNutriente.Trim().ToUpper() == nombre);

                if (existe)
                    return BadRequest($"Ya existe una fuente de nutriente con el nombre '{dto.nombreNutriente}'.");

                // 🔹 Actualizar
                entidad.nombreNutriente = nombre;
                entidad.descripcionNutriente = dto.descripcionNutriente.Trim();
                entidad.precioNutriente = dto.precioNutriente;

                await _db.SaveChangesAsync();
                await trans.CommitAsync();

                return Ok("Fuente de nutrientes editada correctamente.");
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        // ============================================
        // ELIMINAR (LÓGICO)
        // ============================================
        [HttpDelete("eliminar/{id}")]
        public async Task<IActionResult> Eliminar(int id)
        {
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