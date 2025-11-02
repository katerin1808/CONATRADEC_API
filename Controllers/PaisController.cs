using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using static CONATRADEC_API.DTOs.PaisDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/pais")]
    public class PaisController : ControllerBase
        {



        private readonly RolContext _ctx;
        public PaisController( RolContext ctx) => _ctx = ctx;
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PaisResponse>>> GetAll()
        {
            var data = await _ctx.Pais
                .AsNoTracking()
                .Where(p => p.Activo)                 // solo los activos
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

        [HttpPost("Crear")]
        public async Task<ActionResult> Create(PaisRequest req)
        {
            // Normaliza
            var iso = req.CodigoISOPais?.Trim().ToUpperInvariant();
            var nombre = req.NombrePais?.Trim();

            // Revalida por si vinieron espacios
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest("El nombre del país es requerido.");

            if (string.IsNullOrWhiteSpace(iso) || iso.Length != 2|| !iso.All(char.IsLetter))
                return BadRequest("El Código ISO debe tener exactamente 2 letras (A-Z).");

            // Duplicado por ISO
            bool isoDup = await _ctx.Pais.AnyAsync(p => p.CodigoISOPais == iso);
            if (isoDup) return Conflict("Ya existe un país con ese Código ISO.");

            var entity = new Pais
            {
                NombrePais = nombre,
                CodigoISOPais = iso,
                Activo = true
            };

            _ctx.Pais.Add(entity);
            await _ctx.SaveChangesAsync();

            return Ok(new
            {
                message = "País creado exitosamente",
                pais = new { entity.PaisId, entity.NombrePais, entity.CodigoISOPais }
            });
        }

        [HttpPut("actualizarPais{id:int}")]
        public async Task<ActionResult> Update(int id, PaisRequest req)
        {
            var entity = await _ctx.Pais.FindAsync(id);
            if (entity is null) return NotFound();

            string iso = req.CodigoISOPais.Trim().ToUpperInvariant();
            bool isoDup = await _ctx.Pais.AnyAsync(p => p.PaisId != id && p.CodigoISOPais == iso);
            if (isoDup) return Conflict("Ya existe un país con ese Código ISO.");

            entity.NombrePais = req.NombrePais.Trim();
            entity.CodigoISOPais = iso;
          

            await _ctx.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("eliminarPais/{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            var entity = await _ctx.Pais.FindAsync(id);
            if (entity is null) return NotFound();

            entity.Activo = false; // borrado lógico
            await _ctx.SaveChangesAsync();
            return NoContent();
        }
    }


}


