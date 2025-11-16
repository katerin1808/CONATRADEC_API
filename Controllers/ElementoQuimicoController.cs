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

                    // 🔹 QUITAR ESPACIOS EN BLANCO
                    simboloElementoQuimico = x.simboloElementoQuimico.Trim(),

                    nombreElementoQuimico = x.nombreElementoQuimico.Trim(),

                    pesoEquivalentEelementoQuimico = x.pesoEquivalentEelementoQuimico
                })
                .ToListAsync();

            return Ok(lista);
        }

        // ============================================
        // CREAR (CON VALIDACIÓN DE DUPLICADOS)
        // ============================================
        [HttpPost("crear")]
        public async Task<IActionResult> Crear([FromBody] ElementoQuimicoCrearDto dto)
        {
            using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                string simbolo = dto.simboloElementoQuimico.Trim().ToUpper();

                // 🔹 VALIDAR DUPLICADOS POR SÍMBOLO
                bool existe = await _db.ElementoQuimicos
                    .AnyAsync(x => x.simboloElementoQuimico.Trim().ToUpper() == simbolo && x.activo);

                if (existe)
                    return BadRequest($"Ya existe un elemento con el símbolo '{simbolo}'.");

                var entidad = new ElementoQuimico
                {
                    simboloElementoQuimico = simbolo,
                    nombreElementoQuimico = dto.nombreElementoQuimico.Trim(),
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
        // EDITAR (VALIDANDO EXISTENCIA + DUPLICADO + ACTIVO)
        // ============================================
        [HttpPut("editar/{id}")]
        public async Task<IActionResult> Editar(int id, [FromBody] ElementoQuimicoEditarDto dto)
        {
            using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                // 🔹 Buscar solo elementos activos
                var entidad = await _db.ElementoQuimicos
                    .FirstOrDefaultAsync(x => x.elementoQuimicosId == id && x.activo);

                if (entidad is null)
                    return NotFound("Elemento químico no encontrado o ya está eliminado.");

                string simbolo = dto.simboloElementoQuimico.Trim().ToUpper();

                // 🔹 VALIDAR DUPLICADO (excluyendo el mismo ID)
                bool existe = await _db.ElementoQuimicos
                    .AnyAsync(x =>
                        x.elementoQuimicosId != id &&
                        x.activo &&
                        x.simboloElementoQuimico.Trim().ToUpper() == simbolo);

                if (existe)
                    return BadRequest($"Ya existe un elemento con el símbolo '{simbolo}'.");

                // 🔹 Actualizar campos
                entidad.simboloElementoQuimico = simbolo;
                entidad.nombreElementoQuimico = dto.nombreElementoQuimico.Trim();
                entidad.pesoEquivalentEelementoQuimico = dto.pesoEquivalentEelementoQuimico;

                await _db.SaveChangesAsync();
                await trans.CommitAsync();

                return Ok("Elemento químico editado correctamente.");
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
            var entidad = await _db.ElementoQuimicos.FindAsync(id);
            if (entidad is null)
                return NotFound("Elemento químico no encontrado.");

            // 🔹 EVITAR ELIMINAR MÁS DE UNA VEZ
            if (!entidad.activo)
                return BadRequest("El elemento ya se encuentra eliminado.");

            entidad.activo = false;
            await _db.SaveChangesAsync();

            return Ok("Elemento químico eliminado correctamente.");
        }
    }
}

