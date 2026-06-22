using CONATRADEC_API.DTOs;
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

        [HttpPost("subir")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> SubirFotos([FromForm] FotoTerrenoCrearDto dto)
        {
            var terreno = await _db.Terreno
                .FirstOrDefaultAsync(x => x.terrenoId == dto.terrenoId && x.activo);

            if (terreno == null)
                return BadRequest(new { mensaje = "El terreno no existe o está inactivo." });

            if (dto.fotos == null || !dto.fotos.Any())
                return BadRequest(new { mensaje = "Debe subir al menos una foto." });

            string carpeta = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "terrenos");

            if (!Directory.Exists(carpeta))
                Directory.CreateDirectory(carpeta);

            var fotosRespuesta = new List<FotoTerrenoListarDto>();

            foreach (var foto in dto.fotos)
            {
                if (foto.Length <= 0)
                    continue;

                string extension = Path.GetExtension(foto.FileName).ToLower();
                string nombreArchivo = $"{Guid.NewGuid()}{extension}";
                string rutaArchivo = Path.Combine(carpeta, nombreArchivo);

                using (var stream = new FileStream(rutaArchivo, FileMode.Create))
                {
                    await foto.CopyToAsync(stream);
                }

                string url = $"/uploads/terrenos/{nombreArchivo}";

                var fotoTerreno = new FotoTerreno
                {
                    terrenoId = dto.terrenoId,
                    urlFotoTerreno = url,
                    activo = true
                };

                _db.FotoTerreno.Add(fotoTerreno);
                await _db.SaveChangesAsync();

                fotosRespuesta.Add(new FotoTerrenoListarDto
                {
                    fotoTerrenoId = fotoTerreno.fotoTerrenoId,
                    terrenoId = fotoTerreno.terrenoId,
                    urlFotoTerreno = fotoTerreno.urlFotoTerreno
                });
            }

            return Ok(new
            {
                mensaje = "Fotos guardadas correctamente.",
                fotos = fotosRespuesta
            });
        }

        [HttpGet("por-terreno/{terrenoId}")]
        public async Task<IActionResult> ObtenerPorTerreno(int terrenoId)
        {
            var fotos = await _db.FotoTerreno
                .Where(x => x.terrenoId == terrenoId && x.activo)
                .Select(x => new FotoTerrenoListarDto
                {
                    fotoTerrenoId = x.fotoTerrenoId,
                    terrenoId = x.terrenoId,
                    urlFotoTerreno = x.urlFotoTerreno
                })
                .ToListAsync();

            return Ok(fotos);
        }

        [HttpPut("editar/{fotoTerrenoId}")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> EditarFoto(int fotoTerrenoId, [FromForm] FotoTerrenoEditarDto dto)
        {
            var fotoTerreno = await _db.FotoTerreno
                .FirstOrDefaultAsync(x => x.fotoTerrenoId == fotoTerrenoId && x.activo);

            if (fotoTerreno == null)
                return NotFound(new { mensaje = "La foto no existe o está inactiva." });

            if (dto.foto == null || dto.foto.Length <= 0)
                return BadRequest(new { mensaje = "Debe subir una foto." });

            string carpeta = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "terrenos");

            if (!Directory.Exists(carpeta))
                Directory.CreateDirectory(carpeta);

            string extension = Path.GetExtension(dto.foto.FileName).ToLower();
            string nombreArchivo = $"{Guid.NewGuid()}{extension}";
            string rutaArchivo = Path.Combine(carpeta, nombreArchivo);

            using (var stream = new FileStream(rutaArchivo, FileMode.Create))
            {
                await dto.foto.CopyToAsync(stream);
            }

            fotoTerreno.urlFotoTerreno = $"/uploads/terrenos/{nombreArchivo}";

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Foto actualizada correctamente.",
                foto = new FotoTerrenoListarDto
                {
                    fotoTerrenoId = fotoTerreno.fotoTerrenoId,
                    terrenoId = fotoTerreno.terrenoId,
                    urlFotoTerreno = fotoTerreno.urlFotoTerreno
                }
            });
        }

        [HttpDelete("eliminar/{fotoTerrenoId}")]
        public async Task<IActionResult> EliminarFoto(int fotoTerrenoId)
        {
            var fotoTerreno = await _db.FotoTerreno
                .FirstOrDefaultAsync(x => x.fotoTerrenoId == fotoTerrenoId && x.activo);

            if (fotoTerreno == null)
                return NotFound(new { mensaje = "La foto no existe o ya está eliminada." });

            fotoTerreno.activo = false;

            await _db.SaveChangesAsync();

            return Ok(new { mensaje = "Foto eliminada correctamente." });
        }


    }
}
