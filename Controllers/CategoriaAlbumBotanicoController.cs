using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/categoria-album-botanico")]
    public class CategoriaAlbumBotanicoController : ControllerBase
    {
        private readonly DBContext _context;

        public CategoriaAlbumBotanicoController(DBContext context)
        {
            _context = context;
        }

        // GET: api/categoria-album-botanico/listar
        // GET: api/categoria-album-botanico/listar?incluirInactivos=true
        [HttpGet("listar")]
        public async Task<ActionResult> Listar()
        {
            var data = await _context.CategoriasAlbumBotanico
                .AsNoTracking()
                .Where(x => x.activo)
                .OrderBy(x => x.nombreCategoria)
                .Select(x => new
                {
                    x.categoriaAlbumBotanicoId,
                    x.nombreCategoria,
                    x.descripcion,
                    x.rutaImagenPortada,
                    totalRegistros =
                        x.Registros.Count(r => r.activo)
                })
                .ToListAsync();

            return Ok(new
            {
                success = true,
                message = "Categorías obtenidas correctamente.",
                data
            });
        }

        // GET: api/categoria-album-botanico/obtener/1
        [HttpGet("obtener/{id:int}")]
        public async Task<ActionResult> Obtener(int id)
        {
            var data = await _context.CategoriasAlbumBotanico
                .AsNoTracking()
                .Where(x => x.categoriaAlbumBotanicoId == id)
                .Select(x => new
                {
                    x.categoriaAlbumBotanicoId,
                    x.nombreCategoria,
                    x.descripcion,
                    x.activo,

                    totalRegistros = x.Registros
                        .Count(r => r.activo)
                })
                .FirstOrDefaultAsync();

            if (data == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La categoría no fue encontrada."
                });
            }

            return Ok(new
            {
                success = true,
                message = "Categoría obtenida correctamente.",
                data
            });
        }

        // POST: api/categoria-album-botanico/crear
        [HttpPost("crear")]
        public async Task<ActionResult> Crear(
            [FromBody] CrearCategoriaAlbumBotanicoDto dto)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var nombre = dto.nombreCategoria.Trim();

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El nombre de la categoría es obligatorio."
                });
            }

            var existe = await _context.CategoriasAlbumBotanico
                .AnyAsync(x => x.nombreCategoria == nombre);

            if (existe)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La categoría ya existe."
                });
            }

            var registro = new CategoriaAlbumBotanico
            {
                nombreCategoria = nombre,
                descripcion = dto.descripcion?.Trim(),
                activo = true
            };

            _context.CategoriasAlbumBotanico.Add(registro);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Categoría creada correctamente.",
                data = new
                {
                    registro.categoriaAlbumBotanicoId
                }
            });
        }

        // PUT: api/categoria-album-botanico/actualizar/1
        [HttpPut("actualizar/{id:int}")]
        public async Task<ActionResult> Actualizar(
            int id,
            [FromBody] ActualizarCategoriaAlbumBotanicoDto dto)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            if (id != dto.categoriaAlbumBotanicoId)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El ID de la ruta no coincide con el ID enviado."
                });
            }

            var registro = await _context.CategoriasAlbumBotanico
                .FirstOrDefaultAsync(x =>
                    x.categoriaAlbumBotanicoId == id);

            if (registro == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La categoría no fue encontrada."
                });
            }

            var nombre = dto.nombreCategoria.Trim();

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El nombre de la categoría es obligatorio."
                });
            }

            var nombreDuplicado = await _context
                .CategoriasAlbumBotanico
                .AnyAsync(x =>
                    x.categoriaAlbumBotanicoId != id &&
                    x.nombreCategoria == nombre);

            if (nombreDuplicado)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Ya existe otra categoría con ese nombre."
                });
            }

            registro.nombreCategoria = nombre;
            registro.descripcion = dto.descripcion?.Trim();

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Categoría actualizada correctamente."
            });
        }

        // PATCH:
        // api/categoria-album-botanico/cambiar-estado/1?activo=true
        [HttpPatch("cambiar-estado/{id:int}")]
        public async Task<ActionResult> CambiarEstado(
            int id,
            [FromQuery] bool activo)
        {
            var registro = await _context.CategoriasAlbumBotanico
                .FirstOrDefaultAsync(x =>
                    x.categoriaAlbumBotanicoId == id);

            if (registro == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La categoría no fue encontrada."
                });
            }

            if (registro.activo == activo)
            {
                return Ok(new
                {
                    success = true,
                    message = activo
                        ? "La categoría ya se encuentra activa."
                        : "La categoría ya se encuentra inactiva."
                });
            }

            if (!activo)
            {
                var tieneRegistrosActivos = await _context
                    .AlbumesBotanicosCafe
                    .AnyAsync(x =>
                        x.categoriaAlbumBotanicoId == id &&
                        x.activo);

                if (tieneRegistrosActivos)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "La categoría tiene registros activos y no puede desactivarse."
                    });
                }
            }

            registro.activo = activo;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = activo
                    ? "Categoría activada correctamente."
                    : "Categoría desactivada correctamente."
            });
        }

        // DELETE: api/categoria-album-botanico/eliminar/1
        [HttpDelete("eliminar/{id:int}")]
        public async Task<ActionResult> Eliminar(int id)
        {
            var registro = await _context.CategoriasAlbumBotanico
                .FirstOrDefaultAsync(x =>
                    x.categoriaAlbumBotanicoId == id);

            if (registro == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La categoría no fue encontrada."
                });
            }

            if (!registro.activo)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La categoría ya se encuentra inactiva."
                });
            }

            var tieneRegistrosActivos = await _context
                .AlbumesBotanicosCafe
                .AnyAsync(x =>
                    x.categoriaAlbumBotanicoId == id &&
                    x.activo);

            if (tieneRegistrosActivos)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La categoría tiene registros activos y no puede eliminarse."
                });
            }

            /*
             * Eliminación lógica:
             * No se borra físicamente porque la categoría puede estar
             * relacionada con registros históricos del álbum.
             */
            registro.activo = false;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Categoría desactivada correctamente."
            });
        }

        [HttpPost("{id:int}/portada")]
        public async Task<ActionResult> SubirPortada(
    int id,
    [FromForm] SubirPortadaCategoriaAlbumDto dto)
        {
            var categoria = await _context.CategoriasAlbumBotanico
                .FirstOrDefaultAsync(x =>
                    x.categoriaAlbumBotanicoId == id);

            if (categoria == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La categoría no existe."
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

            var extensionesPermitidas = new[]
            {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

            var extension = Path
                .GetExtension(dto.archivo.FileName)
                .ToLowerInvariant();

            if (!extensionesPermitidas.Contains(extension))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Solo se permiten imágenes JPG, JPEG, PNG o WEBP."
                });
            }

            const long tamanioMaximo = 8 * 1024 * 1024;

            if (dto.archivo.Length > tamanioMaximo)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La imagen no puede superar los 8 MB."
                });
            }

            var carpetaBase = Path.Combine(
                Directory.GetCurrentDirectory(),
                "resources",
                "uploads",
                "categorias-album"
            );

            Directory.CreateDirectory(carpetaBase);

            if (!string.IsNullOrWhiteSpace(categoria.rutaImagenPortada))
            {
                var nombreAnterior = Path.GetFileName(
                    categoria.rutaImagenPortada);

                var rutaAnterior = Path.Combine(
                    carpetaBase,
                    nombreAnterior);

                if (System.IO.File.Exists(rutaAnterior))
                {
                    System.IO.File.Delete(rutaAnterior);
                }
            }

            var nombreArchivo =
                $"{id}_{Guid.NewGuid():N}{extension}";

            var rutaFisica = Path.Combine(
                carpetaBase,
                nombreArchivo);

            await using (var stream = new FileStream(
                rutaFisica,
                FileMode.Create))
            {
                await dto.archivo.CopyToAsync(stream);
            }

            var rutaPublica =
                $"/resources/uploads/categorias-album/{nombreArchivo}";

            categoria.rutaImagenPortada = rutaPublica;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Portada de la categoría guardada correctamente.",
                data = new
                {
                    categoria.categoriaAlbumBotanicoId,
                    categoria.rutaImagenPortada
                }
            });
        }
    }
}