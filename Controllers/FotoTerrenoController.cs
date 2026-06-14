using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.FotoTerrenoDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/fotoTerreno")]
    public class FotoTerrenoController : ControllerBase
    {
        private readonly DBContext _db;

        public FotoTerrenoController(DBContext db)
        {
            _db = db;
        }

        [HttpGet("listar/{terrenoId}")]
        public async Task<ActionResult<IEnumerable<FotoTerrenoListarDto>>> Listar(int terrenoId)
        {
            var existeTerreno = await _db.Terreno
                .AnyAsync(x => x.terrenoId == terrenoId && x.activo);

            if (!existeTerreno)
                return NotFound("Terreno no encontrado.");

            var fotos = await _db.FotoTerreno
                .Where(x => x.terrenoId == terrenoId && x.activo)
                .Select(x => new FotoTerrenoListarDto
                {
                    fotoTerrenoId = x.fotoTerrenoId,
                    urlFotoTerreno = x.urlFotoTerreno,
                    terrenoId = x.terrenoId
                })
                .ToListAsync();

            return Ok(fotos);
        }

        [HttpGet("detalle/{id}")]
        public async Task<ActionResult<FotoTerrenoDetalleDto>> Detalle(int id)
        {
            var foto = await _db.FotoTerreno
                .Include(x => x.Terreno)
                .Where(x => x.fotoTerrenoId == id && x.activo)
                .Select(x => new FotoTerrenoDetalleDto
                {
                    fotoTerrenoId = x.fotoTerrenoId,
                    urlFotoTerreno = x.urlFotoTerreno,
                    activo = x.activo,
                    terrenoId = x.terrenoId,
                    codigoTerreno = x.Terreno.codigoTerreno,
                    nombrePropietarioTerreno = x.Terreno.nombrePropietarioTerreno
                })
                .FirstOrDefaultAsync();

            if (foto == null)
                return NotFound("Foto no encontrada.");

            return Ok(foto);
        }

        [HttpPost("crear")]
        public async Task<IActionResult> Crear([FromBody] FotoTerrenoCrearDto dto)
        {
            var terreno = await _db.Terreno
                .FirstOrDefaultAsync(x => x.terrenoId == dto.terrenoId && x.activo);

            if (terreno == null)
                return BadRequest("El terreno no existe.");

            if (dto.urlsFotoTerreno == null || !dto.urlsFotoTerreno.Any())
                return BadRequest("Debe enviar al menos una foto.");

            var fotos = dto.urlsFotoTerreno
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => new FotoTerreno
                {
                    urlFotoTerreno = url.Trim(),
                    terrenoId = dto.terrenoId,
                    activo = true
                })
                .ToList();

            if (!fotos.Any())
                return BadRequest("Las URLs de las fotos no pueden estar vacías.");

            _db.FotoTerreno.AddRange(fotos);
            await _db.SaveChangesAsync();

            return Ok("Fotos agregadas correctamente.");
        }

        [HttpPut("editar/{id}")]
        public async Task<IActionResult> Editar(int id, [FromBody] FotoTerrenoEditarDto dto)
        {
            var foto = await _db.FotoTerreno
                .FirstOrDefaultAsync(x => x.fotoTerrenoId == id && x.activo);

            if (foto == null)
                return NotFound("Foto no encontrada.");

            if (string.IsNullOrWhiteSpace(dto.urlFotoTerreno))
                return BadRequest("La URL de la foto es obligatoria.");

            foto.urlFotoTerreno = dto.urlFotoTerreno.Trim();

            await _db.SaveChangesAsync();

            return Ok("Foto editada correctamente.");
        }

        [HttpDelete("eliminar/{id}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var foto = await _db.FotoTerreno
                .FirstOrDefaultAsync(x => x.fotoTerrenoId == id && x.activo);

            if (foto == null)
                return NotFound("Foto no encontrada.");

            foto.activo = false;

            await _db.SaveChangesAsync();

            return Ok("Foto eliminada correctamente.");
        }
    }
}
