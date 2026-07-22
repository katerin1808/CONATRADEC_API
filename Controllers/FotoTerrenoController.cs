using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
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
        private readonly ImageService _imageService;

        public FotoTerrenoController(
            DBContext db,
            ImageService imageService)
        {
            _db = db;
            _imageService = imageService;
        }

        [HttpPost("subir")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(40 * 1024 * 1024)]
        public async Task<IActionResult> SubirFotos(
       [FromForm] FotoTerrenoCrearDto dto)
        {
            var terreno = await _db.Terreno
                .FirstOrDefaultAsync(x =>
                    x.terrenoId == dto.terrenoId &&
                    x.activo);

            if (terreno == null)
            {
                return BadRequest(new
                {
                    mensaje = "El terreno no existe o está inactivo."
                });
            }

            if (dto.fotos == null || !dto.fotos.Any())
            {
                return BadRequest(new
                {
                    mensaje = "Debe subir al menos una foto."
                });
            }

            var fotosRespuesta = new List<FotoTerrenoListarDto>();
            var rutasNuevas = new List<string>();

            try
            {
                foreach (var foto in dto.fotos)
                {
                    if (foto == null || foto.Length <= 0)
                        continue;

                    string rutaRelativa =
                        await _imageService.GuardarImagenWebpAsync(
                            foto,
                            "terrenos",
                            1280,
                            1280,
                            65);

                    rutasNuevas.Add(rutaRelativa);

                    string urlCompleta =
                        $"{Request.Scheme}://{Request.Host}{rutaRelativa}";

                    var fotoTerreno = new FotoTerreno
                    {
                        terrenoId = dto.terrenoId,
                        urlFotoTerreno = urlCompleta,
                        activo = true
                    };

                    _db.FotoTerreno.Add(fotoTerreno);

                    fotosRespuesta.Add(new FotoTerrenoListarDto
                    {
                        fotoTerrenoId = fotoTerreno.fotoTerrenoId,
                        terrenoId = fotoTerreno.terrenoId,
                        urlFotoTerreno = fotoTerreno.urlFotoTerreno
                    });
                }

                if (!fotosRespuesta.Any())
                {
                    return BadRequest(new
                    {
                        mensaje = "No se encontró ninguna fotografía válida."
                    });
                }

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    mensaje = "Fotos guardadas correctamente.",
                    fotos = fotosRespuesta
                });
            }
            catch
            {
                foreach (string ruta in rutasNuevas)
                {
                    _imageService.EliminarImagen(ruta);
                }

                throw;
            }
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

        [HttpPut("editar/{fotoTerrenoId:int}")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<IActionResult> EditarFoto(
       int fotoTerrenoId,
       [FromForm] FotoTerrenoEditarDto dto)
        {
            var fotoTerreno = await _db.FotoTerreno
                .FirstOrDefaultAsync(x =>
                    x.fotoTerrenoId == fotoTerrenoId &&
                    x.activo);

            if (fotoTerreno == null)
            {
                return NotFound(new
                {
                    mensaje = "La foto no existe o está inactiva."
                });
            }

            if (dto.foto == null || dto.foto.Length <= 0)
            {
                return BadRequest(new
                {
                    mensaje = "Debe subir una foto."
                });
            }

            string? rutaAnterior = fotoTerreno.urlFotoTerreno;
            string? rutaNueva = null;

            try
            {
                rutaNueva =
                    await _imageService.GuardarImagenWebpAsync(
                        dto.foto,
                        "terrenos",
                        1280,
                        1280,
                        65);

                string urlCompleta =
                    $"{Request.Scheme}://{Request.Host}{rutaNueva}";

                fotoTerreno.urlFotoTerreno = urlCompleta;

                await _db.SaveChangesAsync();

                if (!string.IsNullOrWhiteSpace(rutaAnterior))
                {
                    string rutaAnteriorRelativa = rutaAnterior;

                    if (Uri.TryCreate(
                            rutaAnterior,
                            UriKind.Absolute,
                            out Uri? uriAnterior))
                    {
                        rutaAnteriorRelativa = uriAnterior.LocalPath;
                    }

                    _imageService.EliminarImagen(
                        rutaAnteriorRelativa);
                }

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
            catch
            {
                if (!string.IsNullOrWhiteSpace(rutaNueva))
                {
                    _imageService.EliminarImagen(rutaNueva);
                }

                throw;
            }
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
