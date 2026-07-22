using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/album-botanico")]
    public class AlbumBotanicoCafeController : ControllerBase
    {
        private readonly DBContext _context;
        private readonly ILogger<AlbumBotanicoCafeController> _logger;
        private readonly ImageService _imageService;

        private static readonly string[] ExtensionesPermitidas =
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

        private const long TamanoMaximoArchivo =
            8 * 1024 * 1024;

        public AlbumBotanicoCafeController(
            DBContext context,
                ILogger<AlbumBotanicoCafeController> logger,
              ImageService imageService)

        {
            _context = context;
            _logger = logger;
            _imageService = imageService;

        }

        // GET: api/album-botanico/galeria
        // GET: api/album-botanico/galeria?incluirInactivos=true
        // GET: api/album-botanico/galeria?categoriaId=2&buscar=roya
        [HttpGet("galeria")]
        public async Task<ActionResult> Galeria(
            [FromQuery] int? categoriaId = null,
            [FromQuery] string? buscar = null,
            [FromQuery] bool incluirInactivos = false)
        {
            var query = _context.AlbumesBotanicosCafe
                .AsNoTracking()
                .AsQueryable();

            if (!incluirInactivos)
            {
                query = query.Where(x =>
                    x.activo &&
                    x.Categoria.activo);
            }

            if (categoriaId.HasValue)
            {
                query = query.Where(x =>
                    x.categoriaAlbumBotanicoId ==
                    categoriaId.Value);
            }

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();

                query = query.Where(x =>
                    x.titulo.Contains(texto) ||
                    (
                        x.nombreCientifico != null &&
                        x.nombreCientifico.Contains(texto)
                    ) ||
                    x.descripcion.Contains(texto));
            }

            var data = await query
                .OrderByDescending(x => x.activo)
                .ThenBy(x => x.Categoria.nombreCategoria)
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
                        x.Fotos.Count(f => f.activo),

                    x.activo,
                    categoriaActiva = x.Categoria.activo,
                    x.fechaCreacion
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
        // GET: api/album-botanico/detalle/1?incluirInactivos=true
        [HttpGet("detalle/{id:int}")]
        public async Task<ActionResult> Detalle(
            int id,
            [FromQuery] bool incluirInactivos = false)
        {
            var query = _context.AlbumesBotanicosCafe
                .AsNoTracking()
                .Where(x => x.albumBotanicoCafeId == id);

            if (!incluirInactivos)
            {
                query = query.Where(x =>
                    x.activo &&
                    x.Categoria.activo);
            }

            var data = await query
                .Select(x => new
                {
                    x.albumBotanicoCafeId,
                    x.categoriaAlbumBotanicoId,
                    categoria =
                        x.Categoria.nombreCategoria,
                    categoriaActiva = x.Categoria.activo,
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
                    message =
                        "El registro del álbum no fue encontrado."
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
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            bool categoriaExiste = await _context
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
                    message =
                        "La categoría no existe o está inactiva."
                });
            }

            string titulo = dto.titulo.Trim();
            string descripcion = dto.descripcion.Trim();

            if (string.IsNullOrWhiteSpace(titulo))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El título es obligatorio."
                });
            }

            if (string.IsNullOrWhiteSpace(descripcion))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La descripción es obligatoria."
                });
            }

            var registro = new AlbumBotanicoCafe
            {
                categoriaAlbumBotanicoId =
                    dto.categoriaAlbumBotanicoId,
                titulo = titulo,
                nombreCientifico =
                    dto.nombreCientifico?.Trim(),
                descripcion = descripcion,
                caracteristicas =
                    dto.caracteristicas?.Trim(),
                sintomas = dto.sintomas?.Trim(),
                causas = dto.causas?.Trim(),
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
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

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
                    message =
                        "El registro del álbum no fue encontrado."
                });
            }

            bool categoriaExiste = await _context
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
                    message =
                        "La categoría no existe o está inactiva."
                });
            }

            string titulo = dto.titulo.Trim();
            string descripcion = dto.descripcion.Trim();

            if (string.IsNullOrWhiteSpace(titulo) ||
                string.IsNullOrWhiteSpace(descripcion))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El título y la descripción son obligatorios."
                });
            }

            registro.categoriaAlbumBotanicoId =
                dto.categoriaAlbumBotanicoId;
            registro.titulo = titulo;
            registro.nombreCientifico =
                dto.nombreCientifico?.Trim();
            registro.descripcion = descripcion;
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
                    message =
                        "El registro del álbum no fue encontrado."
                });
            }

            if (registro.activo == activo)
            {
                return Ok(new
                {
                    success = true,
                    message = activo
                        ? "El registro ya se encuentra activo."
                        : "El registro ya se encuentra inactivo."
                });
            }

            if (activo)
            {
                bool categoriaActiva = await _context
                    .CategoriasAlbumBotanico
                    .AnyAsync(x =>
                        x.categoriaAlbumBotanicoId ==
                            registro.categoriaAlbumBotanicoId &&
                        x.activo);

                if (!categoriaActiva)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "No se puede activar el registro porque su categoría está inactiva."
                    });
                }
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
        // La eliminación es lógica.
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
            bool albumExiste = await _context
                .AlbumesBotanicosCafe
                .AnyAsync(x =>
                    x.albumBotanicoCafeId == id &&
                    x.activo);

            if (!albumExiste)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El registro del álbum no existe o está inactivo."
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

            string extension = Path
                .GetExtension(dto.archivo.FileName)
                .ToLowerInvariant();

            if (!ExtensionesPermitidas.Contains(extension))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Solo se permiten imágenes JPG, JPEG, PNG o WEBP."
                });
            }

            string rutaPublica;

            try
            {
                rutaPublica = await _imageService.GuardarImagenWebpAsync(
                    dto.archivo,
                    $"album-botanico/{id}",
                    anchoMaximo: 1600,
                    altoMaximo: 1600,
                    calidad: 80);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error al procesar la fotografía del álbum {AlbumId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message = "Ocurrió un error al procesar la imagen."
                });
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

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _imageService.EliminarImagen(rutaPublica);

                _logger.LogError(
                    ex,
                    "Error al guardar en base de datos la fotografía del álbum {AlbumId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message = "La imagen fue procesada, pero no se pudo guardar su información."
                });
            }

            return Ok(new
            {
                success = true,
                message = "Fotografía optimizada y guardada correctamente.",
                data = new
                {
                    foto.albumBotanicoCafeFotoId,
                    foto.rutaFoto,
                    foto.esPortada,
                    foto.orden
                }
            });
        }

        // PUT: api/album-botanico/fotos/1
        [HttpPut("fotos/{fotoId:int}")]
        public async Task<ActionResult> ActualizarFoto(
            int fotoId,
            [FromBody] ActualizarFotoAlbumBotanicoDto dto)
        {
            if (fotoId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El identificador de la fotografía no es válido."
                });
            }

            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

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

            if (!foto.activo)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La fotografía se encuentra inactiva."
                });
            }

            if (dto.orden <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El orden debe ser mayor que cero."
                });
            }

            string? descripcion = dto.descripcionFoto?.Trim();

            if (!string.IsNullOrEmpty(descripcion) &&
                descripcion.Length > 500)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La descripción no puede superar los 500 caracteres."
                });
            }

            /*
             * Evita que dos fotografías activas del mismo registro
             * tengan el mismo número de orden.
             */
            bool ordenDuplicado = await _context
                .AlbumesBotanicosCafeFotos
                .AnyAsync(x =>
                    x.albumBotanicoCafeFotoId != fotoId &&
                    x.albumBotanicoCafeId == foto.albumBotanicoCafeId &&
                    x.orden == dto.orden &&
                    x.activo);

            if (ordenDuplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message = "Ya existe otra fotografía con ese número de orden."
                });
            }

            foto.descripcionFoto = descripcion;
            foto.orden = dto.orden;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Fotografía actualizada correctamente.",
                data = new
                {
                    foto.albumBotanicoCafeFotoId,
                    foto.descripcionFoto,
                    foto.orden,
                    foto.esPortada,
                    foto.rutaFoto
                }
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

            if (foto.esPortada)
            {
                return Ok(new
                {
                    success = true,
                    message = "Esta fotografía ya es la portada."
                });
            }

            await using var transaccion = await _context
                .Database
                .BeginTransactionAsync();

            try
            {
                /*
                 * Se realiza en dos guardados para evitar una violación
                 * temporal del índice que garantiza una sola portada
                 * activa por registro. SQL Server no garantiza el orden
                 * de los UPDATE enviados dentro de un único SaveChanges.
                 */
                var portadasActuales = await _context
                    .AlbumesBotanicosCafeFotos
                    .Where(x =>
                        x.albumBotanicoCafeId ==
                            foto.albumBotanicoCafeId &&
                        x.albumBotanicoCafeFotoId != fotoId &&
                        x.activo &&
                        x.esPortada)
                    .ToListAsync();

                if (portadasActuales.Count > 0)
                {
                    foreach (var portadaActual in portadasActuales)
                    {
                        portadaActual.esPortada = false;
                    }

                    await _context.SaveChangesAsync();
                }

                foto.esPortada = true;
                await _context.SaveChangesAsync();

                await transaccion.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "Portada actualizada correctamente."
                });
            }
            catch (DbUpdateException ex)
            {
                await transaccion.RollbackAsync();

                _logger.LogError(
                    ex,
                    "Error de base de datos al establecer la foto {FotoId} " +
                    "como portada del registro {RegistroId}.",
                    fotoId,
                    foto.albumBotanicoCafeId);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "No fue posible actualizar la portada en la base de datos."
                });
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync();

                _logger.LogError(
                    ex,
                    "Error inesperado al establecer la foto {FotoId} " +
                    "como portada del registro {RegistroId}.",
                    fotoId,
                    foto.albumBotanicoCafeId);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "No fue posible establecer la fotografía como portada."
                });
            }
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
