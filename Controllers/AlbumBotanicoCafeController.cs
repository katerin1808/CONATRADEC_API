using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/album-botanico")]
    public class AlbumBotanicoCafeController : ControllerBase
    {
        private readonly DBContext _context;
        private readonly IWebHostEnvironment _env;

        private static readonly string[] ExtensionesPermitidas =
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

        private const long TamanoMaximoArchivo = 8 * 1024 * 1024;

        public AlbumBotanicoCafeController(
            DBContext context,
            IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // GET: api/album-botanico/galeria
        [HttpGet("galeria")]
        public async Task<ActionResult> Galeria(
            [FromQuery] int? categoriaId = null,
            [FromQuery] string? buscar = null)
        {
            var query = _context.AlbumesBotanicosCafe
                .AsNoTracking()
                .Where(x =>
                    x.activo &&
                    x.Categoria.activo);

            if (categoriaId.HasValue)
            {
                query = query.Where(x =>
                    x.categoriaAlbumBotanicoId == categoriaId.Value);
            }

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                var texto = buscar.Trim();

                query = query.Where(x =>
                    x.titulo.Contains(texto) ||
                    (
                        x.nombreCientifico != null &&
                        x.nombreCientifico.Contains(texto)
                    ) ||
                    x.descripcion.Contains(texto));
            }

            var data = await query
                .OrderBy(x => x.Categoria.nombreCategoria)
                .ThenBy(x => x.titulo)
                .Select(x => new
                {
                    x.albumBotanicoCafeId,
                    x.categoriaAlbumBotanicoId,

                    categoria =
                        x.Categoria.nombreCategoria,

                    x.titulo,
                    x.nombreCientifico,

                    descripcionCorta =
                        x.descripcion.Length > 180
                            ? x.descripcion.Substring(0, 180) + "..."
                            : x.descripcion,

                    fotoPortada = x.Fotos
                        .Where(f => f.activo)
                        .OrderByDescending(f => f.esPortada)
                        .ThenBy(f => f.orden)
                        .Select(f => f.rutaFoto)
                        .FirstOrDefault(),

                    totalFotos =
                        x.Fotos.Count(f => f.activo)
                })
                .ToListAsync();

            return Ok(new
            {
                success = true,
                message = "Galería obtenida correctamente.",
                data
            });
        }

        // GET: api/album-botanico/detalle/1
        [HttpGet("detalle/{id:int}")]
        public async Task<ActionResult> Detalle(int id)
        {
            var data = await _context.AlbumesBotanicosCafe
                .AsNoTracking()
                .Where(x =>
                    x.albumBotanicoCafeId == id &&
                    x.activo)
                .Select(x => new
                {
                    x.albumBotanicoCafeId,
                    x.categoriaAlbumBotanicoId,

                    categoria =
                        x.Categoria.nombreCategoria,

                    x.titulo,
                    x.nombreCientifico,
                    x.descripcion,
                    x.caracteristicas,
                    x.sintomas,
                    x.causas,
                    x.recomendaciones,
                    x.observaciones,
                    x.activo,
                    x.fechaCreacion,

                    fotos = x.Fotos
                        .Where(f => f.activo)
                        .OrderByDescending(f => f.esPortada)
                        .ThenBy(f => f.orden)
                        .Select(f => new
                        {
                            f.albumBotanicoCafeFotoId,
                            f.rutaFoto,
                            f.descripcionFoto,
                            f.esPortada,
                            f.orden
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync();

            if (data == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El registro del álbum no fue encontrado."
                });
            }

            return Ok(new
            {
                success = true,
                message = "Detalle obtenido correctamente.",
                data
            });
        }

        // POST: api/album-botanico/crear
        [HttpPost("crear")]
        public async Task<ActionResult> Crear(
            [FromBody] CrearAlbumBotanicoCafeDto dto)
        {
            var categoriaExiste = await _context
                .CategoriasAlbumBotanico
                .AnyAsync(x =>
                    x.categoriaAlbumBotanicoId ==
                        dto.categoriaAlbumBotanicoId &&
                    x.activo);

            if (!categoriaExiste)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La categoría no existe o está inactiva."
                });
            }

            var registro = new AlbumBotanicoCafe
            {
                categoriaAlbumBotanicoId =
                    dto.categoriaAlbumBotanicoId,

                titulo =
                    dto.titulo.Trim(),

                nombreCientifico =
                    dto.nombreCientifico?.Trim(),

                descripcion =
                    dto.descripcion.Trim(),

                caracteristicas =
                    dto.caracteristicas?.Trim(),

                sintomas =
                    dto.sintomas?.Trim(),

                causas =
                    dto.causas?.Trim(),

                recomendaciones =
                    dto.recomendaciones?.Trim(),

                observaciones =
                    dto.observaciones?.Trim(),

                activo = true,
                fechaCreacion = DateTime.Now
            };

            _context.AlbumesBotanicosCafe.Add(registro);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Registro creado correctamente.",
                data = new
                {
                    registro.albumBotanicoCafeId
                }
            });
        }

        // PUT: api/album-botanico/actualizar/1
        [HttpPut("actualizar/{id:int}")]
        public async Task<ActionResult> Actualizar(
            int id,
            [FromBody] ActualizarAlbumBotanicoCafeDto dto)
        {
            if (id != dto.albumBotanicoCafeId)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El ID de la ruta no coincide con el ID enviado."
                });
            }

            var registro = await _context
                .AlbumesBotanicosCafe
                .FirstOrDefaultAsync(x =>
                    x.albumBotanicoCafeId == id);

            if (registro == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El registro del álbum no fue encontrado."
                });
            }

            var categoriaExiste = await _context
                .CategoriasAlbumBotanico
                .AnyAsync(x =>
                    x.categoriaAlbumBotanicoId ==
                        dto.categoriaAlbumBotanicoId &&
                    x.activo);

            if (!categoriaExiste)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La categoría no existe o está inactiva."
                });
            }

            registro.categoriaAlbumBotanicoId =
                dto.categoriaAlbumBotanicoId;

            registro.titulo =
                dto.titulo.Trim();

            registro.nombreCientifico =
                dto.nombreCientifico?.Trim();

            registro.descripcion =
                dto.descripcion.Trim();

            registro.caracteristicas =
                dto.caracteristicas?.Trim();

            registro.sintomas =
                dto.sintomas?.Trim();

            registro.causas =
                dto.causas?.Trim();

            registro.recomendaciones =
                dto.recomendaciones?.Trim();

            registro.observaciones =
                dto.observaciones?.Trim();

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Registro actualizado correctamente."
            });
        }

        // PATCH: api/album-botanico/cambiar-estado/1?activo=true
        [HttpPatch("cambiar-estado/{id:int}")]
        public async Task<ActionResult> CambiarEstado(
            int id,
            [FromQuery] bool activo)
        {
            var registro = await _context
                .AlbumesBotanicosCafe
                .FirstOrDefaultAsync(x =>
                    x.albumBotanicoCafeId == id);

            if (registro == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El registro del álbum no fue encontrado."
                });
            }

            registro.activo = activo;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = activo
                    ? "Registro activado correctamente."
                    : "Registro desactivado correctamente."
            });
        }

        // DELETE: api/album-botanico/eliminar/1
        [HttpDelete("eliminar/{id:int}")]
        public async Task<ActionResult> Eliminar(int id)
        {
            var registro = await _context
                .AlbumesBotanicosCafe
                .FirstOrDefaultAsync(x =>
                    x.albumBotanicoCafeId == id &&
                    x.activo);

            if (registro == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El registro no existe o ya está inactivo."
                });
            }

            registro.activo = false;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Registro desactivado correctamente."
            });
        }

        // POST: api/album-botanico/1/fotos
        [HttpPost("{id:int}/fotos")]
        [RequestSizeLimit(TamanoMaximoArchivo)]
        public async Task<ActionResult> SubirFoto(
            int id,
            [FromForm] SubirFotoAlbumBotanicoDto dto)
        {
            var albumExiste = await _context
                .AlbumesBotanicosCafe
                .AnyAsync(x =>
                    x.albumBotanicoCafeId == id &&
                    x.activo);

            if (!albumExiste)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El registro del álbum no existe o está inactivo."
                });
            }

            if (dto.archivo == null || dto.archivo.Length == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Debe seleccionar una imagen."
                });
            }

            if (dto.archivo.Length > TamanoMaximoArchivo)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La imagen no puede superar los 8 MB."
                });
            }

            var extension = Path
                .GetExtension(dto.archivo.FileName)
                .ToLowerInvariant();

            if (!ExtensionesPermitidas.Contains(extension))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Solo se permiten imágenes JPG, JPEG, PNG o WEBP."
                });
            }

            var carpetaBase = Path.Combine(
      Directory.GetCurrentDirectory(),
      "resources",
      "uploads",
      "album-botanico"
  );

            var carpetaFisica = Path.Combine(
                carpetaBase,
                id.ToString());

            Directory.CreateDirectory(carpetaFisica);

            var nombreArchivo =
                $"{Guid.NewGuid():N}{extension}";

            var rutaFisica = Path.Combine(
                carpetaFisica,
                nombreArchivo);

            await using (var stream = new FileStream(
                rutaFisica,
                FileMode.Create))
            {
                await dto.archivo.CopyToAsync(stream);
            }

            if (dto.esPortada)
            {
                var portadas = await _context
                    .AlbumesBotanicosCafeFotos
                    .Where(x =>
                        x.albumBotanicoCafeId == id &&
                        x.activo &&
                        x.esPortada)
                    .ToListAsync();

                foreach (var portada in portadas)
                {
                    portada.esPortada = false;
                }
            }

            var rutaPublica =
                $"/resources/uploads/album-botanico/{id}/{nombreArchivo}";

            var foto = new AlbumBotanicoCafeFoto
            {
                albumBotanicoCafeId = id,
                rutaFoto = rutaPublica,
                descripcionFoto = dto.descripcionFoto?.Trim(),
                esPortada = dto.esPortada,
                orden = dto.orden,
                activo = true
            };

            _context.AlbumesBotanicosCafeFotos.Add(foto);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Fotografía guardada correctamente.",
                data = new
                {
                    foto.albumBotanicoCafeFotoId,
                    foto.rutaFoto
                }
            });
        }

        // PUT: api/album-botanico/fotos/1
        [HttpPut("fotos/{fotoId:int}")]
        public async Task<ActionResult> ActualizarFoto(
            int fotoId,
            [FromBody] ActualizarFotoAlbumBotanicoDto dto)
        {
            var foto = await _context
                .AlbumesBotanicosCafeFotos
                .FirstOrDefaultAsync(x =>
                    x.albumBotanicoCafeFotoId == fotoId);

            if (foto == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La fotografía no fue encontrada."
                });
            }

            foto.descripcionFoto =
                dto.descripcionFoto?.Trim();

            foto.orden =
                dto.orden;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Fotografía actualizada correctamente."
            });
        }

        // PATCH: api/album-botanico/fotos/1/portada
        [HttpPatch("fotos/{fotoId:int}/portada")]
        public async Task<ActionResult> Portada(int fotoId)
        {
            var foto = await _context
                .AlbumesBotanicosCafeFotos
                .FirstOrDefaultAsync(x =>
                    x.albumBotanicoCafeFotoId == fotoId &&
                    x.activo);

            if (foto == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La fotografía no fue encontrada."
                });
            }

            var portadas = await _context
                .AlbumesBotanicosCafeFotos
                .Where(x =>
                    x.albumBotanicoCafeId ==
                        foto.albumBotanicoCafeId &&
                    x.activo &&
                    x.esPortada)
                .ToListAsync();

            foreach (var portada in portadas)
            {
                portada.esPortada = false;
            }

            foto.esPortada = true;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Portada actualizada correctamente."
            });
        }

        // DELETE: api/album-botanico/fotos/1
        [HttpDelete("fotos/{fotoId:int}")]
        public async Task<ActionResult> EliminarFoto(int fotoId)
        {
            var foto = await _context
                .AlbumesBotanicosCafeFotos
                .FirstOrDefaultAsync(x =>
                    x.albumBotanicoCafeFotoId == fotoId &&
                    x.activo);

            if (foto == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "La fotografía no existe o ya está inactiva."
                });
            }

            foto.activo = false;
            foto.esPortada = false;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Fotografía desactivada correctamente."
            });
        }
    }
}