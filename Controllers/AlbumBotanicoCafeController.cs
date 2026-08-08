using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;

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
                    categoria = x.Categoria.nombreCategoria,
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
                    totalFotos = x.Fotos.Count(f => f.activo),
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
                    categoria = x.Categoria.nombreCategoria,
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
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

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
                    message = "La categoría no existe o está inactiva."
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
                categoriaAlbumBotanicoId = dto.categoriaAlbumBotanicoId,
                titulo = titulo,
                nombreCientifico = dto.nombreCientifico?.Trim(),
                descripcion = descripcion,
                caracteristicas = dto.caracteristicas?.Trim(),
                sintomas = dto.sintomas?.Trim(),
                causas = dto.causas?.Trim(),
                recomendaciones = dto.recomendaciones?.Trim(),
                observaciones = dto.observaciones?.Trim(),
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
                return ValidationProblem(ModelState);

            if (id != dto.albumBotanicoCafeId)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El ID de la ruta no coincide con el ID enviado."
                });
            }

            var registro = await _context
                .AlbumesBotanicosCafe
                .FirstOrDefaultAsync(x => x.albumBotanicoCafeId == id);

            if (registro == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El registro del álbum no fue encontrado."
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
                    message = "La categoría no existe o está inactiva."
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
                    message = "El título y la descripción son obligatorios."
                });
            }

            registro.categoriaAlbumBotanicoId = dto.categoriaAlbumBotanicoId;
            registro.titulo = titulo;
            registro.nombreCientifico = dto.nombreCientifico?.Trim();
            registro.descripcion = descripcion;
            registro.caracteristicas = dto.caracteristicas?.Trim();
            registro.sintomas = dto.sintomas?.Trim();
            registro.causas = dto.causas?.Trim();
            registro.recomendaciones = dto.recomendaciones?.Trim();
            registro.observaciones = dto.observaciones?.Trim();

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
                .FirstOrDefaultAsync(x => x.albumBotanicoCafeId == id);

            if (registro == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El registro del álbum no fue encontrado."
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
                        message = "No se puede activar el registro porque su categoría está inactiva."
                    });
                }
            }

            await using var transaccion = await _context.Database
                .BeginTransactionAsync();

            try
            {
                /*
                 * Desactivar la ficha controla únicamente su visibilidad. Las
                 * publicaciones provenientes de inspecciones conservan su estado
                 * y vuelven a ser visibles si la ficha se reactiva.
                 */
                registro.activo = activo;

                await _context.SaveChangesAsync();

                if (activo)
                    await GarantizarPortadaActivaAsync(id);

                await transaccion.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = activo
                        ? "Registro activado correctamente."
                        : "Registro desactivado correctamente."
                });
            }
            catch
            {
                await transaccion.RollbackAsync();
                throw;
            }
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
                    message = "El registro no existe o ya está inactivo."
                });
            }

            await using var transaccion = await _context.Database
                .BeginTransactionAsync();

            try
            {
                /*
                 * La eliminación lógica de la ficha no revoca publicaciones de
                 * inspecciones; solo las oculta mientras la ficha esté inactiva.
                 */
                registro.activo = false;
                await _context.SaveChangesAsync();
                await transaccion.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = "Registro desactivado correctamente."
                });
            }
            catch
            {
                await transaccion.RollbackAsync();
                throw;
            }
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

            int siguienteOrden =
                (await _context.AlbumesBotanicosCafeFotos
                    .Where(x =>
                        x.albumBotanicoCafeId == id &&
                        x.activo)
                    .Select(x => (int?)x.orden)
                    .MaxAsync() ?? 0) + 1;

            bool existenFotosActivas = await _context
                .AlbumesBotanicosCafeFotos
                .AnyAsync(x =>
                    x.albumBotanicoCafeId == id &&
                    x.activo);

            bool existePortada = await _context
                .AlbumesBotanicosCafeFotos
                .AnyAsync(x =>
                    x.albumBotanicoCafeId == id &&
                    x.activo &&
                    x.esPortada);

            /*
             * Solo la primera fotografía activa de la ficha se convierte en
             * portada automáticamente. Si ya hay fotografías, la nueva se
             * agrega sin reemplazar la portada. La administración puede pedir
             * expresamente que la nueva fotografía sea portada.
             */
            bool seraPortada = dto.esPortada || !existenFotosActivas;

            if (seraPortada && existePortada)
            {
                var portadas = await _context
                    .AlbumesBotanicosCafeFotos
                    .Where(x =>
                        x.albumBotanicoCafeId == id &&
                        x.activo &&
                        x.esPortada)
                    .ToListAsync();

                foreach (var portada in portadas)
                    portada.esPortada = false;
            }

            var foto = new AlbumBotanicoCafeFoto
            {
                albumBotanicoCafeId = id,
                rutaFoto = rutaPublica,
                descripcionFoto = dto.descripcionFoto?.Trim(),
                esPortada = seraPortada,
                orden = dto.orden > 0 ? dto.orden : siguienteOrden,
                activo = true
            };

            _context.AlbumesBotanicosCafeFotos.Add(foto);

            try
            {
                await _context.SaveChangesAsync();
                await GarantizarPortadaActivaAsync(id);
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
                return ValidationProblem(ModelState);

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
                        portadaActual.esPortada = false;

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
                    message = "No fue posible actualizar la portada en la base de datos."
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
                    message = "No fue posible establecer la fotografía como portada."
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
                    message = "La fotografía no existe o ya está inactiva."
                });
            }

            /*
             * Una fotografía publicada desde una inspección fitosanitaria no
             * puede retirarse desde la administración general del Álbum. La
             * inspección es el origen auditado de esa decisión y debe conservar
             * el control de la publicación.
             */
            OrigenInspeccionFitosanitaria? origen =
                await ObtenerOrigenInspeccionFitosanitariaAsync(fotoId);

            if (origen != null)
            {
                string marcador =
                    $"[[INSPECCION_FITOSANITARIA:{origen.InspeccionId}]]";

                string mensaje =
                    $"{marcador}\n" +
                    "Esta fotografía fue publicada desde una inspección fitosanitaria y no puede desactivarse directamente desde el Álbum Botánico.\n\n" +
                    $"Inspección: #{origen.InspeccionId} · {origen.NombreInspeccion}\n" +
                    $"Terreno: {ValorOAlternativa(origen.CodigoTerreno, "No disponible")}\n" +
                    $"Fecha de inspección: {origen.FechaInspeccion:dd/MM/yyyy HH:mm}\n" +
                    $"Fotografía: {origen.OrdenFotografia} · {ValorOAlternativa(origen.TipoFotografia, "EVIDENCIA").Replace('_', ' ')}\n" +
                    $"Técnico: {ValorOAlternativa(origen.Tecnico, "No disponible")}\n" +
                    $"Publicada por: {ValorOAlternativa(origen.PublicadaPor, "No disponible")}\n" +
                    $"Fecha de publicación: {origen.FechaPublicacionUtc.ToLocalTime():dd/MM/yyyy HH:mm}\n\n" +
                    "Para retirarla, abra la inspección y utilice la acción ‘Retirar del Álbum’.";

                return Conflict(new
                {
                    success = false,
                    code = "FOTO_VINCULADA_INSPECCION_FITOSANITARIA",
                    message = mensaje,
                    data = new
                    {
                        inspeccionId = origen.InspeccionId,
                        origen.NombreInspeccion,
                        origen.CodigoTerreno,
                        origen.FechaInspeccion,
                        origen.OrdenFotografia,
                        origen.TipoFotografia,
                        origen.Tecnico,
                        origen.PublicadaPor,
                        origen.FechaPublicacionUtc
                    }
                });
            }

            int albumBotanicoCafeId = foto.albumBotanicoCafeId;
            bool eraPortada = foto.esPortada;

            await using var transaccion = await _context.Database
                .BeginTransactionAsync();

            try
            {
                foto.activo = false;
                foto.esPortada = false;
                await _context.SaveChangesAsync();

                if (eraPortada)
                    await GarantizarPortadaActivaAsync(albumBotanicoCafeId);

                await transaccion.CommitAsync();

                return Ok(new
                {
                    success = true,
                    message = eraPortada
                        ? "Fotografía desactivada correctamente. La portada fue reasignada automáticamente cuando existía otra fotografía activa."
                        : "Fotografía desactivada correctamente."
                });
            }
            catch
            {
                await transaccion.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Localiza el expediente que originó una fotografía del Álbum. Se
        /// consulta incluso una publicación histórica inactiva para impedir
        /// que una evidencia auditada se retire por una vía administrativa
        /// diferente a la inspección que la publicó.
        /// </summary>
        private async Task<OrigenInspeccionFitosanitaria?>
            ObtenerOrigenInspeccionFitosanitariaAsync(int albumFotoId)
        {
            DbConnection conexion = _context.Database.GetDbConnection();
            bool cerrarConexion = conexion.State != ConnectionState.Open;

            if (cerrarConexion)
                await conexion.OpenAsync();

            try
            {
                await using DbCommand comando = conexion.CreateCommand();
                comando.CommandText = @"
SELECT TOP (1)
    p.DiagnosticoIAId,
    COALESCE(
        NULLIF(LTRIM(RTRIM(d.NombreInspeccion)), N''),
        N'Inspección #' + CONVERT(NVARCHAR(20), d.DiagnosticoIAId)
    ) AS NombreInspeccion,
    ISNULL(d.CodigoTerreno, N'') AS CodigoTerreno,
    d.FechaSolicitudUtc,
    ISNULL(i.Orden, 0) AS OrdenFotografia,
    ISNULL(i.TipoFotografia, N'EVIDENCIA') AS TipoFotografia,
    COALESCE(
        NULLIF(LTRIM(RTRIM(ut.nombreCompletoUsuario)), N''),
        ut.nombreUsuario,
        N''
    ) AS Tecnico,
    COALESCE(
        NULLIF(LTRIM(RTRIM(up.nombreCompletoUsuario)), N''),
        up.nombreUsuario,
        N''
    ) AS PublicadaPor,
    p.FechaPublicacionUtc
FROM dbo.diagnosticoIAAlbumPublicacion p
INNER JOIN dbo.diagnosticoIA d
    ON d.DiagnosticoIAId = p.DiagnosticoIAId
INNER JOIN dbo.diagnosticoIAImagen i
    ON i.DiagnosticoIAImagenId = p.DiagnosticoIAImagenId
LEFT JOIN dbo.usuario ut
    ON ut.UsuarioId = COALESCE(
        d.UsuarioFinEtapaTecnicaId,
        d.UsuarioSolicitanteId
    )
LEFT JOIN dbo.usuario up
    ON up.UsuarioId = p.UsuarioPublicacionId
WHERE p.AlbumBotanicoCafeFotoId = @fotoId
ORDER BY
    p.Activo DESC,
    p.FechaPublicacionUtc DESC,
    p.DiagnosticoIAAlbumPublicacionId DESC;";

                DbParameter parametro = comando.CreateParameter();
                parametro.ParameterName = "@fotoId";
                parametro.Value = albumFotoId;
                comando.Parameters.Add(parametro);

                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync();

                if (!await reader.ReadAsync())
                    return null;

                return new OrigenInspeccionFitosanitaria(
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.GetDateTime(3),
                    reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    reader.GetDateTime(8));
            }
            finally
            {
                if (cerrarConexion)
                    await conexion.CloseAsync();
            }
        }

        private static string ValorOAlternativa(
            string? valor,
            string alternativa) =>
            string.IsNullOrWhiteSpace(valor) ? alternativa : valor.Trim();

        /// <summary>
        /// Garantiza que una ficha con fotografías activas tenga exactamente
        /// una portada. Si no existe portada, se selecciona la fotografía de
        /// menor orden y luego el menor identificador.
        /// </summary>
        private async Task GarantizarPortadaActivaAsync(int albumBotanicoCafeId)
        {
            List<AlbumBotanicoCafeFoto> fotosActivas = await _context
                .AlbumesBotanicosCafeFotos
                .Where(x =>
                    x.albumBotanicoCafeId == albumBotanicoCafeId &&
                    x.activo)
                .OrderBy(x => x.orden)
                .ThenBy(x => x.albumBotanicoCafeFotoId)
                .ToListAsync();

            if (fotosActivas.Count == 0)
                return;

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

        private async Task DesactivarPublicacionFitosanitariaPorFotoAsync(
            int albumBotanicoCafeFotoId)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
UPDATE dbo.diagnosticoIAAlbumPublicacion
SET Activo = 0
WHERE AlbumBotanicoCafeFotoId = {albumBotanicoCafeFotoId}
  AND Activo = 1;
""");
        }
        private sealed record OrigenInspeccionFitosanitaria(
            int InspeccionId,
            string NombreInspeccion,
            string CodigoTerreno,
            DateTime FechaInspeccion,
            int OrdenFotografia,
            string TipoFotografia,
            string Tecnico,
            string PublicadaPor,
            DateTime FechaPublicacionUtc);

    }
}
