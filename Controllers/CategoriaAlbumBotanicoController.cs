using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/categoria-album-botanico")]
    public class CategoriaAlbumBotanicoController : ControllerBase
    {
        private readonly DBContext _context;
        private readonly ImageService _imageService;

        public CategoriaAlbumBotanicoController(
            DBContext context,
            ImageService imageService)
        {
            _context = context;
            _imageService = imageService;
        }

        // GET: api/categoria-album-botanico/listar
        // GET: api/categoria-album-botanico/listar?incluirInactivos=true
        [HttpGet("listar")]
        public async Task<ActionResult> Listar(
            [FromQuery] bool incluirInactivos = false)
        {
            var query = _context.CategoriasAlbumBotanico
                .AsNoTracking()
                .AsQueryable();

            if (!incluirInactivos)
                query = query.Where(x => x.activo);

            var data = await query
                .OrderByDescending(x => x.activo)
                .ThenBy(x => x.nombreCategoria)
                .Select(x => new
                {
                    x.categoriaAlbumBotanicoId,
                    x.nombreCategoria,
                    x.descripcion,
                    x.rutaImagenPortada,
                    x.activo,
                    totalRegistros = incluirInactivos
                        ? x.Registros.Count()
                        : x.Registros.Count(r => r.activo),
                    totalRegistrosActivos =
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
                    x.rutaImagenPortada,
                    x.activo,
                    totalRegistros = x.Registros.Count(),
                    totalRegistrosActivos =
                        x.Registros.Count(r => r.activo)
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
                return ValidationProblem(ModelState);

            string nombre = dto.nombreCategoria.Trim();

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El nombre de la categoría es obligatorio."
                });
            }

            bool existe = await _context.CategoriasAlbumBotanico
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
                return ValidationProblem(ModelState);

            if (id != dto.categoriaAlbumBotanicoId)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El ID de la ruta no coincide con el ID enviado."
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

            string nombre = dto.nombreCategoria.Trim();

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El nombre de la categoría es obligatorio."
                });
            }

            bool nombreDuplicado = await _context
                .CategoriasAlbumBotanico
                .AnyAsync(x =>
                    x.categoriaAlbumBotanicoId != id &&
                    x.nombreCategoria == nombre);

            if (nombreDuplicado)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Ya existe otra categoría con ese nombre."
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

            await using var transaccion = await _context.Database
                .BeginTransactionAsync();

            try
            {
                /*
                 * Los registros y las fotografías administradas manualmente
                 * conservan su estado, igual que en la lógica existente. Solo
                 * las copias creadas desde inspecciones fitosanitarias se
                 * retiran cuando la categoría se desactiva, evitando que una
                 * publicación reviva automáticamente al reactivar la categoría.
                 */
                registro.activo = activo;

                if (!activo)
                    await DesactivarPublicacionesFitosanitariasDeCategoriaAsync(id);

                await _context.SaveChangesAsync();

                if (activo)
                    await GarantizarPortadasDeCategoriaAsync(id);

                await transaccion.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = activo
                        ? "Categoría activada correctamente."
                        : "Categoría desactivada correctamente."
                });
            }
            catch
            {
                await transaccion.RollbackAsync();
                throw;
            }
        }

        // DELETE: api/categoria-album-botanico/eliminar/1
        // La eliminación es lógica.
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

            bool tieneRegistrosActivos = await _context
                .AlbumesBotanicosCafe
                .AnyAsync(x =>
                    x.categoriaAlbumBotanicoId == id &&
                    x.activo);

            if (tieneRegistrosActivos)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La categoría tiene registros activos y no puede eliminarse."
                });
            }

            await using var transaccion = await _context.Database
                .BeginTransactionAsync();

            try
            {
                registro.activo = false;
                await DesactivarPublicacionesFitosanitariasDeCategoriaAsync(id);
                await _context.SaveChangesAsync();
                await transaccion.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "Categoría desactivada correctamente."
                });
            }
            catch
            {
                await transaccion.RollbackAsync();
                throw;
            }
        }

        // POST: api/categoria-album-botanico/1/portada
        [HttpPost("{id:int}/portada")]
        [RequestSizeLimit(8 * 1024 * 1024)]
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

            string[] extensionesPermitidas =
            {
                ".jpg",
                ".jpeg",
                ".png",
                ".webp"
            };

            string extension = Path
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

            // Guarda la ruta anterior para eliminar el archivo luego.
            string? rutaAnterior = categoria.rutaImagenPortada;

            // Procesa la imagen (redimensiona, comprime y guarda en WebP).
            string rutaNueva = await _imageService.GuardarImagenWebpAsync(
                dto.archivo,
                "categorias-album",
                1280,
                1280,
                65);

            categoria.rutaImagenPortada = rutaNueva;

            await _context.SaveChangesAsync();

            // Elimina el archivo anterior solo cuando la nueva imagen
            // ya fue guardada correctamente.
            _imageService.EliminarImagen(rutaAnterior);
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

        private async Task DesactivarPublicacionesFitosanitariasDeCategoriaAsync(
            int categoriaAlbumBotanicoId)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
UPDATE f
SET f.activo = 0,
    f.esPortada = 0
FROM dbo.AlbumBotanicoCafeFoto f
INNER JOIN dbo.diagnosticoIAAlbumPublicacion p
    ON p.AlbumBotanicoCafeFotoId = f.albumBotanicoCafeFotoId
WHERE p.CategoriaAlbumBotanicoId = {categoriaAlbumBotanicoId}
  AND p.Activo = 1;

UPDATE dbo.diagnosticoIAAlbumPublicacion
SET Activo = 0
WHERE CategoriaAlbumBotanicoId = {categoriaAlbumBotanicoId}
  AND Activo = 1;
""");
        }

        /// <summary>
        /// Al reactivar una categoría se revisan sus fichas activas. Las fotos
        /// de publicaciones fitosanitarias retiradas no se reactivan, pero las
        /// fotografías activas restantes conservan una portada válida.
        /// </summary>
        private async Task GarantizarPortadasDeCategoriaAsync(
            int categoriaAlbumBotanicoId)
        {
            int[] fichas = await _context.AlbumesBotanicosCafe
                .AsNoTracking()
                .Where(x =>
                    x.categoriaAlbumBotanicoId == categoriaAlbumBotanicoId &&
                    x.activo)
                .Select(x => x.albumBotanicoCafeId)
                .ToArrayAsync();

            foreach (int fichaId in fichas)
            {
                List<AlbumBotanicoCafeFoto> fotosActivas = await _context
                    .AlbumesBotanicosCafeFotos
                    .Where(x =>
                        x.albumBotanicoCafeId == fichaId &&
                        x.activo)
                    .OrderBy(x => x.orden)
                    .ThenBy(x => x.albumBotanicoCafeFotoId)
                    .ToListAsync();

                if (fotosActivas.Count == 0)
                    continue;

                AlbumBotanicoCafeFoto portada =
                    fotosActivas.FirstOrDefault(x => x.esPortada) ??
                    fotosActivas[0];

                bool cambio = false;
                foreach (AlbumBotanicoCafeFoto foto in fotosActivas)
                {
                    bool debeSerPortada =
                        foto.albumBotanicoCafeFotoId ==
                        portada.albumBotanicoCafeFotoId;

                    if (foto.esPortada == debeSerPortada)
                        continue;

                    foto.esPortada = debeSerPortada;
                    cambio = true;
                }

                if (cambio)
                    await _context.SaveChangesAsync();
            }
        }
    }
}
