using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.ElementoQuimicoDto;

namespace CONATRADEC_API.Controllers
{

    [ApiController]
    [Route("api/elemento-quimico")]
    public class ElementoQuimicoController : Controller
    {
        private readonly DBContext _db;
        public ElementoQuimicoController(DBContext db) => _db = db;

        // ============================================
        // LISTAR (solo activos)
        // ============================================
        [HttpGet("listar")]
        public async Task<ActionResult<IEnumerable<ElementoQuimicoListarDto>>> Listar()
        {
            var lista = await _db.ElementoQuimicos
                .Where(x => x.activo)
                .Select(x => new ElementoQuimicoListarDto
                {
                    elementoQuimicosId = x.elementoQuimicosId,
                    simboloElementoQuimico = x.simboloElementoQuimico,
                    nombreElementoQuimico = x.nombreElementoQuimico,
                    pesoEquivalentEelementoQuimico = x.pesoEquivalentEelementoQuimico
                })
                .ToListAsync();

            return Ok(lista);
        }

        // ============================================
        // CREAR (CON BEGIN TRANSACTION)
        // ============================================
        [HttpPost("crear")]
        public async Task<IActionResult> Crear([FromBody] ElementoQuimicoCrearDto dto)
        {
            using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                var entidad = new ElementoQuimico
                {
                    simboloElementoQuimico = dto.simboloElementoQuimico,
                    nombreElementoQuimico = dto.nombreElementoQuimico,
                    pesoEquivalentEelementoQuimico = dto.pesoEquivalentEelementoQuimico,
                    activo = true
                };

                _db.ElementoQuimicos.Add(entidad);
                await _db.SaveChangesAsync();

                await trans.CommitAsync();
                return Ok("Elemento químico creado correctamente.");
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
        public async Task<IActionResult> Editar(int id, [FromBody] ElementoQuimicoEditarDto dto)
        {
            var entidad = await _db.ElementoQuimicos.FindAsync(id);
            if (entidad is null)
                return NotFound("Elemento químico no encontrado.");

            entidad.simboloElementoQuimico = dto.simboloElementoQuimico;
            entidad.nombreElementoQuimico = dto.nombreElementoQuimico;
            entidad.pesoEquivalentEelementoQuimico = dto.pesoEquivalentEelementoQuimico;

            await _db.SaveChangesAsync();

            return Ok("Elemento químico editado correctamente.");
        }

        // ============================================
        // ELIMINAR (LÓGICO)
        // ============================================
        [HttpDelete("eliminar/{id}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var entidad = await _db.ElementoQuimicos.FindAsync(id);
            if (entidad is null)
                return NotFound("Elemento químico no encontrado.");

            entidad.activo = false;
            await _db.SaveChangesAsync();

            return Ok("Elemento químico eliminado correctamente.");
        }
    }
}
