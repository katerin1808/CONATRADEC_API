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

                bool nombreDup = await _ctx.Pais.AnyAsync(p => p.Activo &&
                EF.Functions.Collate(p.NombrePais.ToUpper(), "Modern_Spanish_CI_AI") == nombre!.ToUpper());
                if (nombreDup) return Conflict("Ya existe un país ACTIVO con ese nombre.");

                var entity = new Pais
                {
                    NombrePais = nombre!.ToUpper(),
                    CodigoISOPais = iso.ToUpper(),
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

                if (!entity.Activo)
                    return Conflict("No se puede actualizar un pais que está inactivo.");

                // ✅ Solo contra activos (permite reutilizar valores que estén inactivos)
                bool isoDup = await _ctx.Pais.AnyAsync(p => p.PaisId != id && p.Activo && p.CodigoISOPais.ToUpper() == iso);
                if (isoDup) return Conflict("Ya existe un país ACTIVO con ese Código ISO.");

                bool nombreDup = await _ctx.Pais.AnyAsync(p => p.PaisId != id && p.Activo && EF.Functions.Collate(p.NombrePais.ToUpper(), "Modern_Spanish_CI_AI") == nombre!.ToUpper());
                if (nombreDup) return Conflict("Ya existe un país ACTIVO con ese nombre.");

                entity.NombrePais = nombre!.ToUpper();
                entity.CodigoISOPais = iso.ToUpper();

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
            var entity = await _ctx.Pais
                .FirstOrDefaultAsync(x =>
                    x.PaisId == id &&
                    x.Activo);

            if (entity == null)
            {
                return NotFound(new
                {
                    mensaje = "El país no existe o ya está desactivado."
                });
            }

            var dependencias = new List<string>();

            var tieneDepartamentos = await _ctx.Departamento
                .AnyAsync(x => x.PaisId == id);

            if (tieneDepartamentos)
            {
                dependencias.Add("departamentos");
            }

            if (dependencias.Count > 0)
            {
                return Conflict(new
                {
                    mensaje =
                        "No se puede eliminar el país porque está siendo utilizado.",

                    pais = new
                    {
                        entity.PaisId,
                        entity.NombrePais,
                        entity.CodigoISOPais
                    },

                    usadoEn = dependencias
                });
            }

            entity.Activo = false;

            await _ctx.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "País desactivado correctamente.",
                data = new
                {
                    entity.PaisId,
                    entity.NombrePais,
                    entity.CodigoISOPais,
                    entity.Activo
                }
            });
        }


    }
}


