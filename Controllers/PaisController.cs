using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using static CONATRADEC_API.DTOs.DepartamentoDto;
using static CONATRADEC_API.DTOs.PaisDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/pais")]
    public class PaisController : ControllerBase
    {
        private readonly DBContext _ctx;
        public PaisController(DBContext ctx) => _ctx = ctx;

        // ==========================================================
        // LISTAR SOLO ACTIVOS
        // ==========================================================
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PaisResponse>>> GetAll()
        {
            var data = await _ctx.Pais
                .AsNoTracking()
                .Where(p => p.Activo)
                .OrderBy(p => p.NombrePais)
                .Select(p => new PaisResponse
                {
                    PaisId = p.PaisId,
                    NombrePais = p.NombrePais,
                    CodigoISOPais = p.CodigoISOPais,
                    Activo = p.Activo
                })
                .ToListAsync();

            return Ok(data);
        }

        // POST /api/departamento/crear
        // POST /api/pais/crearPais
        [HttpPost("crearPais")]
        [Consumes("application/json")]
        public async Task<ActionResult> Create([FromBody] PaisRequest? req)
        {
            if (req is null) return BadRequest("Body vacío o JSON mal formado.");

            var nombre = req.NombrePais?.ReplaceLineEndings(" ").Trim();
            var iso = req.CodigoISOPais?.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest("El nombre del país es requerido.");

            if (string.IsNullOrWhiteSpace(iso) || iso.Length != 3 || !iso.All(char.IsLetter))
                return BadRequest("El Código ISO debe tener exactamente 3 letras (A-Z).");

            await using var tx = await _ctx.Database.BeginTransactionAsync();
            try
            {
                // ✅ Unicidad SOLO contra activos
                bool isoDup = await _ctx.Pais.AnyAsync(p => p.Activo && p.CodigoISOPais.ToUpper() == iso);
                if (isoDup) return Conflict("Ya existe un país ACTIVO con ese Código ISO.");

                bool nombreDup = await _ctx.Pais.AnyAsync(p => p.Activo && p.NombrePais.ToLower() == nombre.ToLower());
                if (nombreDup) return Conflict("Ya existe un país ACTIVO con ese nombre.");

                var entity = new Pais
                {
                    NombrePais = nombre!,
                    CodigoISOPais = iso,
                    Activo = true
                };

                _ctx.Pais.Add(entity);
                await _ctx.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new
                {
                    message = "País creado correctamente",
                    pais = new { entity.PaisId, entity.NombrePais, entity.CodigoISOPais, entity.Activo }
                });
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // PUT /api/pais/actualizarPais/{id}
        [HttpPut("actualizarPais/{id:int}")]
        [Consumes("application/json")]
        public async Task<ActionResult> Update(int id, [FromBody] PaisRequest req)
        {
            if (req is null) return BadRequest("Body vacío o JSON mal formado.");

            var nombre = req.NombrePais?.ReplaceLineEndings(" ").Trim();
            var iso = req.CodigoISOPais?.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest("El nombre del país es requerido.");
            if (string.IsNullOrWhiteSpace(iso) || iso.Length != 3 || !iso.All(char.IsLetter))
                return BadRequest("El Código ISO debe tener exactamente 3 letras (A-Z).");

            await using var tx = await _ctx.Database.BeginTransactionAsync();
            try
            {
                var entity = await _ctx.Pais.FindAsync(id);
                if (entity is null) return NotFound("El país indicado no existe.");

                // ✅ Solo contra activos (permite reutilizar valores que estén inactivos)
                bool isoDup = await _ctx.Pais.AnyAsync(p => p.PaisId != id && p.Activo && p.CodigoISOPais.ToUpper() == iso);
                if (isoDup) return Conflict("Ya existe un país ACTIVO con ese Código ISO.");

                bool nombreDup = await _ctx.Pais.AnyAsync(p => p.PaisId != id && p.Activo && p.NombrePais.ToLower() == nombre!.ToLower());
                if (nombreDup) return Conflict("Ya existe un país ACTIVO con ese nombre.");

                entity.NombrePais = nombre!;
                entity.CodigoISOPais = iso;

                await _ctx.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new
                {
                    message = "País actualizado correctamente",
                    pais = new { entity.PaisId, entity.NombrePais, entity.CodigoISOPais, entity.Activo }
                });
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // ==========================================================
        // ELIMINAR (borrado lógico, con transacción)
        // ==========================================================
        [HttpDelete("eliminarPais/{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            await using var tx = await _ctx.Database.BeginTransactionAsync();
            try
            {
                var entity = await _ctx.Pais.FindAsync(id);
                if (entity is null) return NotFound("El país indicado no existe.");

                // (Opcional) Bloquear si tiene departamentos activos:
                // bool tieneDptos = await _ctx.Departamento.AnyAsync(d => d.PaisId == id && d.Activo);
                // if (tieneDptos) return Conflict("No se puede eliminar: el país tiene departamentos activos.");

                entity.Activo = false; // borrado lógico
                await _ctx.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new
                {
                    message = $"El país '{entity.NombrePais}' fue eliminado correctamente (borrado lógico).",
                    pais = new
                    {
                        entity.PaisId,
                        entity.NombrePais,
                        entity.CodigoISOPais,
                        entity.Activo
                    }
                });
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();
                _ctx.ChangeTracker.Clear();
                return BadRequest($"Error al eliminar el país. Detalle: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _ctx.ChangeTracker.Clear();
                return StatusCode(500, $"Error interno al eliminar el país: {ex.Message}");
            }
        }


}
}


