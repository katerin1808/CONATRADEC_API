using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/publicacion")]
    public sealed class PublicacionController : ControllerBase
    {
        private const string InterfazNoticias = "noticiasPage";
        private const string EstadoBorrador = "BORRADOR";
        private const string EstadoPublicada = "PUBLICADA";
        private const string EstadoArchivada = "ARCHIVADA";

        private readonly NoticiasDbContext noticiasDb;
        private readonly DBContext db;
        private readonly PermisoApiService permisoApiService;
        private readonly ImageService imageService;
        private readonly ILogger<PublicacionController> logger;

        public PublicacionController(
            NoticiasDbContext noticiasDb,
            DBContext db,
            PermisoApiService permisoApiService,
            ImageService imageService,
            ILogger<PublicacionController> logger)
        {
            this.noticiasDb = noticiasDb;
            this.db = db;
            this.permisoApiService = permisoApiService;
            this.imageService = imageService;
            this.logger = logger;
        }

        [HttpGet("categorias")]
        public async Task<ActionResult> Categorias(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            List<CategoriaPublicacionDto> data = await noticiasDb
                .CategoriasPublicacion
                .AsNoTracking()
                .Where(x => x.activo)
                .OrderBy(x => x.orden)
                .ThenBy(x => x.nombreCategoriaPublicacion)
                .Select(x => new CategoriaPublicacionDto
                {
                    CategoriaPublicacionId = x.categoriaPublicacionId,
                    Nombre = x.nombreCategoriaPublicacion,
                    Descripcion = x.descripcionCategoriaPublicacion,
                    ColorHex = x.colorHex,
                    Orden = x.orden
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Categorías obtenidas correctamente.",
                data
            });
        }

        [HttpGet("feed")]
        public async Task<ActionResult> Feed(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromQuery] int? categoriaId = null,
            [FromQuery] string? buscar = null,
            [FromQuery] bool soloDestacadas = false,
            [FromQuery] bool soloEventos = false,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 12,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 6, 30);

            DateTime ahoraUtc = DateTime.UtcNow;

            IQueryable<Publicacion> query = noticiasDb.Publicaciones
                .AsNoTracking()
                .Where(x =>
                    x.activo &&
                    x.CategoriaPublicacion.activo &&
                    x.estadoPublicacion == EstadoPublicada &&
                    x.fechaInicioPublicacionUtc <= ahoraUtc &&
                    (!x.fechaFinPublicacionUtc.HasValue ||
                     x.fechaFinPublicacionUtc.Value >= ahoraUtc));

            if (categoriaId.HasValue && categoriaId.Value > 0)
            {
                query = query.Where(x =>
                    x.categoriaPublicacionId == categoriaId.Value);
            }

            if (soloDestacadas)
                query = query.Where(x => x.destacada);

            if (soloEventos)
            {
                query = query.Where(x =>
                    x.CategoriaPublicacion.nombreCategoriaPublicacion ==
                        "Evento" ||
                    x.fechaEventoInicioUtc.HasValue);
            }

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();
                query = query.Where(x =>
                    x.titulo.Contains(texto) ||
                    x.resumen.Contains(texto) ||
                    x.contenido.Contains(texto) ||
                    x.ubicacion.Contains(texto));
            }

            PublicacionPaginadaDto data = await ConstruirPaginaAsync(
                query,
                pagina,
                tamanoPagina,
                incluirAutores: false,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Publicaciones obtenidas correctamente.",
                data
            });
        }

        [HttpGet("detalle/{id:int}")]
        public async Task<ActionResult> Detalle(
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DateTime ahoraUtc = DateTime.UtcNow;

            Publicacion? publicacion = await noticiasDb.Publicaciones
                .AsNoTracking()
                .Include(x => x.CategoriaPublicacion)
                .FirstOrDefaultAsync(
                    x =>
                        x.publicacionId == id &&
                        x.activo &&
                        x.CategoriaPublicacion.activo &&
                        x.estadoPublicacion == EstadoPublicada &&
                        x.fechaInicioPublicacionUtc <= ahoraUtc &&
                        (!x.fechaFinPublicacionUtc.HasValue ||
                         x.fechaFinPublicacionUtc.Value >= ahoraUtc),
                    cancellationToken);

            if (publicacion == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "La publicación no existe, aún no está disponible o ya venció."
                });
            }

            PublicacionDetalleDto data = MapearDetalle(publicacion, ahoraUtc);
            data.Autor = "CONATRADEC";
            data.UltimoEditor = string.Empty;

            return Ok(new
            {
                success = true,
                message = "Detalle obtenido correctamente.",
                data
            });
        }

        [HttpGet("administrar")]
        public async Task<ActionResult> Administrar(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromQuery] int? categoriaId = null,
            [FromQuery] string? estado = null,
            [FromQuery] string? buscar = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 20,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Administrar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 10, 50);

            IQueryable<Publicacion> query = noticiasDb.Publicaciones
                .AsNoTracking()
                .Where(x => x.activo);

            if (categoriaId.HasValue && categoriaId.Value > 0)
            {
                query = query.Where(x =>
                    x.categoriaPublicacionId == categoriaId.Value);
            }

            if (!string.IsNullOrWhiteSpace(estado))
            {
                string estadoNormalizado = estado.Trim().ToUpperInvariant();

                if (estadoNormalizado == "PROGRAMADA")
                {
                    DateTime ahoraUtc = DateTime.UtcNow;
                    query = query.Where(x =>
                        x.estadoPublicacion == EstadoPublicada &&
                        x.fechaInicioPublicacionUtc > ahoraUtc);
                }
                else if (estadoNormalizado == "VENCIDA")
                {
                    DateTime ahoraUtc = DateTime.UtcNow;
                    query = query.Where(x =>
                        x.estadoPublicacion == EstadoPublicada &&
                        x.fechaFinPublicacionUtc.HasValue &&
                        x.fechaFinPublicacionUtc.Value < ahoraUtc);
                }
                else if (estadoNormalizado == EstadoPublicada)
                {
                    DateTime ahoraUtc = DateTime.UtcNow;
                    query = query.Where(x =>
                        x.estadoPublicacion == EstadoPublicada &&
                        x.fechaInicioPublicacionUtc <= ahoraUtc &&
                        (!x.fechaFinPublicacionUtc.HasValue ||
                         x.fechaFinPublicacionUtc.Value >= ahoraUtc));
                }
                else if (EsEstadoValido(estadoNormalizado))
                {
                    query = query.Where(x =>
                        x.estadoPublicacion == estadoNormalizado);
                }
            }

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();
                query = query.Where(x =>
                    x.titulo.Contains(texto) ||
                    x.resumen.Contains(texto) ||
                    x.contenido.Contains(texto) ||
                    x.ubicacion.Contains(texto));
            }

            PublicacionPaginadaDto data = await ConstruirPaginaAsync(
                query,
                pagina,
                tamanoPagina,
                incluirAutores: true,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Publicaciones administrativas obtenidas correctamente.",
                data
            });
        }

        [HttpGet("administrar/{id:int}")]
        public async Task<ActionResult> ObtenerParaAdministrar(
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Administrar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            Publicacion? publicacion = await noticiasDb.Publicaciones
                .AsNoTracking()
                .Include(x => x.CategoriaPublicacion)
                .FirstOrDefaultAsync(
                    x => x.publicacionId == id && x.activo,
                    cancellationToken);

            if (publicacion == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La publicación no fue encontrada."
                });
            }

            PublicacionDetalleDto data = MapearDetalle(
                publicacion,
                DateTime.UtcNow);

            Dictionary<int, string> autores = await ObtenerNombresUsuariosAsync(
                new[]
                {
                    publicacion.usuarioCreacionId,
                    publicacion.usuarioUltimaModificacionId
                },
                cancellationToken);

            data.Autor = ObtenerNombreUsuario(
                autores,
                publicacion.usuarioCreacionId);

            data.UltimoEditor = ObtenerNombreUsuario(
                autores,
                publicacion.usuarioUltimaModificacionId);

            return Ok(new
            {
                success = true,
                message = "Publicación obtenida correctamente.",
                data
            });
        }

        [HttpPost("crear")]
        public async Task<ActionResult> Crear(
            [FromBody] PublicacionGuardarDto dto,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            ActionResult? validacion = await ValidarDtoAsync(
                dto,
                cancellationToken);

            if (validacion != null)
                return validacion;

            DateTime ahoraUtc = DateTime.UtcNow;
            string estado = NormalizarEstado(dto.EstadoPublicacion);

            var publicacion = new Publicacion
            {
                categoriaPublicacionId = dto.CategoriaPublicacionId,
                titulo = dto.Titulo.Trim(),
                resumen = dto.Resumen.Trim(),
                contenido = dto.Contenido.Trim(),
                enlaceExterno = dto.EnlaceExterno?.Trim() ?? string.Empty,
                textoEnlace = dto.TextoEnlace?.Trim() ?? string.Empty,
                ubicacion = dto.Ubicacion?.Trim() ?? string.Empty,
                fechaEventoInicioUtc = dto.FechaEventoInicio?.UtcDateTime,
                fechaEventoFinUtc = dto.FechaEventoFin?.UtcDateTime,
                fechaInicioPublicacionUtc =
                    dto.FechaInicioPublicacion.UtcDateTime,
                fechaFinPublicacionUtc =
                    dto.FechaFinPublicacion?.UtcDateTime,
                estadoPublicacion = estado,
                destacada = dto.Destacada,
                usuarioCreacionId = usuarioSesionId!.Value,
                usuarioUltimaModificacionId = usuarioSesionId.Value,
                fechaCreacionUtc = ahoraUtc,
                fechaUltimaModificacionUtc = ahoraUtc,
                activo = true
            };

            noticiasDb.Publicaciones.Add(publicacion);
            await noticiasDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = estado == EstadoPublicada
                    ? "Publicación creada y publicada correctamente."
                    : "Borrador guardado correctamente.",
                data = new PublicacionCreadaDto
                {
                    PublicacionId = publicacion.publicacionId
                }
            });
        }

        [HttpPut("actualizar/{id:int}")]
        public async Task<ActionResult> Actualizar(
            int id,
            [FromBody] PublicacionGuardarDto dto,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (dto.PublicacionId > 0 && dto.PublicacionId != id)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El identificador de la ruta no coincide con la publicación enviada."
                });
            }

            ActionResult? validacion = await ValidarDtoAsync(
                dto,
                cancellationToken);

            if (validacion != null)
                return validacion;

            Publicacion? publicacion = await noticiasDb.Publicaciones
                .FirstOrDefaultAsync(
                    x => x.publicacionId == id && x.activo,
                    cancellationToken);

            if (publicacion == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La publicación no fue encontrada."
                });
            }

            publicacion.categoriaPublicacionId = dto.CategoriaPublicacionId;
            publicacion.titulo = dto.Titulo.Trim();
            publicacion.resumen = dto.Resumen.Trim();
            publicacion.contenido = dto.Contenido.Trim();
            publicacion.enlaceExterno =
                dto.EnlaceExterno?.Trim() ?? string.Empty;
            publicacion.textoEnlace =
                dto.TextoEnlace?.Trim() ?? string.Empty;
            publicacion.ubicacion = dto.Ubicacion?.Trim() ?? string.Empty;
            publicacion.fechaEventoInicioUtc =
                dto.FechaEventoInicio?.UtcDateTime;
            publicacion.fechaEventoFinUtc =
                dto.FechaEventoFin?.UtcDateTime;
            publicacion.fechaInicioPublicacionUtc =
                dto.FechaInicioPublicacion.UtcDateTime;
            publicacion.fechaFinPublicacionUtc =
                dto.FechaFinPublicacion?.UtcDateTime;
            publicacion.estadoPublicacion =
                NormalizarEstado(dto.EstadoPublicacion);
            publicacion.destacada = dto.Destacada;
            publicacion.usuarioUltimaModificacionId = usuarioSesionId!.Value;
            publicacion.fechaUltimaModificacionUtc = DateTime.UtcNow;

            await noticiasDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Publicación actualizada correctamente."
            });
        }

        [HttpPatch("cambiar-estado/{id:int}")]
        public async Task<ActionResult> CambiarEstado(
            int id,
            [FromBody] CambiarEstadoPublicacionDto dto,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            string estado = NormalizarEstado(dto.EstadoPublicacion);

            if (!EsEstadoValido(estado))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El estado debe ser BORRADOR, PUBLICADA o ARCHIVADA."
                });
            }

            Publicacion? publicacion = await noticiasDb.Publicaciones
                .FirstOrDefaultAsync(
                    x => x.publicacionId == id && x.activo,
                    cancellationToken);

            if (publicacion == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La publicación no fue encontrada."
                });
            }

            publicacion.estadoPublicacion = estado;
            publicacion.usuarioUltimaModificacionId = usuarioSesionId!.Value;
            publicacion.fechaUltimaModificacionUtc = DateTime.UtcNow;

            await noticiasDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = estado switch
                {
                    EstadoPublicada => "Publicación habilitada correctamente.",
                    EstadoArchivada => "Publicación archivada correctamente.",
                    _ => "Publicación convertida en borrador correctamente."
                }
            });
        }

        [HttpPatch("cambiar-destacada/{id:int}")]
        public async Task<ActionResult> CambiarDestacada(
            int id,
            [FromBody] CambiarDestacadaPublicacionDto dto,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            Publicacion? publicacion = await noticiasDb.Publicaciones
                .FirstOrDefaultAsync(
                    x => x.publicacionId == id && x.activo,
                    cancellationToken);

            if (publicacion == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La publicación no fue encontrada."
                });
            }

            publicacion.destacada = dto.Destacada;
            publicacion.usuarioUltimaModificacionId = usuarioSesionId!.Value;
            publicacion.fechaUltimaModificacionUtc = DateTime.UtcNow;

            await noticiasDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = dto.Destacada
                    ? "Publicación marcada como destacada."
                    : "Publicación retirada de destacados."
            });
        }

        [HttpDelete("eliminar/{id:int}")]
        public async Task<ActionResult> Eliminar(
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Eliminar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            Publicacion? publicacion = await noticiasDb.Publicaciones
                .FirstOrDefaultAsync(
                    x => x.publicacionId == id && x.activo,
                    cancellationToken);

            if (publicacion == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La publicación no existe o ya fue eliminada."
                });
            }

            publicacion.activo = false;
            publicacion.estadoPublicacion = EstadoArchivada;
            publicacion.destacada = false;
            publicacion.usuarioUltimaModificacionId = usuarioSesionId!.Value;
            publicacion.fechaUltimaModificacionUtc = DateTime.UtcNow;

            await noticiasDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Publicación eliminada correctamente."
            });
        }

        [HttpPost("{id:int}/portada")]
        [RequestSizeLimit(8 * 1024 * 1024)]
        public async Task<ActionResult> SubirPortada(
            int id,
            [FromForm] SubirPortadaPublicacionDto dto,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.AgregarOActualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            Publicacion? publicacion = await noticiasDb.Publicaciones
                .FirstOrDefaultAsync(
                    x => x.publicacionId == id && x.activo,
                    cancellationToken);

            if (publicacion == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La publicación no fue encontrada."
                });
            }

            string rutaNueva;

            try
            {
                rutaNueva = await imageService.GuardarImagenWebpAsync(
                    dto.Archivo,
                    $"noticias/{id}",
                    anchoMaximo: 1600,
                    altoMaximo: 1200,
                    calidad: 82);
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
                logger.LogError(
                    ex,
                    "No fue posible procesar la portada de la publicación {PublicacionId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message = "No fue posible procesar la imagen seleccionada."
                });
            }

            string rutaAnterior = publicacion.rutaImagenPortada;
            publicacion.rutaImagenPortada = rutaNueva;
            publicacion.usuarioUltimaModificacionId = usuarioSesionId!.Value;
            publicacion.fechaUltimaModificacionUtc = DateTime.UtcNow;

            try
            {
                await noticiasDb.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                EliminarArchivoNoticias(rutaNueva);
                throw;
            }

            EliminarArchivoNoticias(rutaAnterior);

            return Ok(new
            {
                success = true,
                message = "Portada actualizada correctamente.",
                data = new PortadaPublicacionDto
                {
                    PublicacionId = id,
                    RutaImagenPortada = rutaNueva
                }
            });
        }

        [HttpDelete("{id:int}/portada")]
        public async Task<ActionResult> EliminarPortada(
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.AgregarOActualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            Publicacion? publicacion = await noticiasDb.Publicaciones
                .FirstOrDefaultAsync(
                    x => x.publicacionId == id && x.activo,
                    cancellationToken);

            if (publicacion == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La publicación no fue encontrada."
                });
            }

            string rutaAnterior = publicacion.rutaImagenPortada;

            if (string.IsNullOrWhiteSpace(rutaAnterior))
            {
                return Ok(new
                {
                    success = true,
                    message = "La publicación no tiene una portada asignada."
                });
            }

            publicacion.rutaImagenPortada = string.Empty;
            publicacion.usuarioUltimaModificacionId = usuarioSesionId!.Value;
            publicacion.fechaUltimaModificacionUtc = DateTime.UtcNow;

            await noticiasDb.SaveChangesAsync(cancellationToken);
            EliminarArchivoNoticias(rutaAnterior);

            return Ok(new
            {
                success = true,
                message = "Portada eliminada correctamente."
            });
        }

        private async Task<ActionResult?> ValidarAccesoAsync(
            int? usuarioId,
            TipoPermisoApi tipoPermiso,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado =
                await permisoApiService.ValidarAsync(
                    usuarioId,
                    InterfazNoticias,
                    tipoPermiso,
                    cancellationToken);

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                new
                {
                    success = false,
                    message = resultado.Mensaje
                });
        }

        private async Task<ActionResult?> ValidarDtoAsync(
            PublicacionGuardarDto dto,
            CancellationToken cancellationToken)
        {
            string estado = NormalizarEstado(dto.EstadoPublicacion);

            if (!EsEstadoValido(estado))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El estado debe ser BORRADOR, PUBLICADA o ARCHIVADA."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.Titulo) ||
                string.IsNullOrWhiteSpace(dto.Resumen) ||
                string.IsNullOrWhiteSpace(dto.Contenido))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El título, el resumen y el contenido son obligatorios."
                });
            }

            bool categoriaExiste = await noticiasDb.CategoriasPublicacion
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.categoriaPublicacionId ==
                            dto.CategoriaPublicacionId &&
                        x.activo,
                    cancellationToken);

            if (!categoriaExiste)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La categoría seleccionada no existe o está inactiva."
                });
            }

            if (dto.FechaInicioPublicacion.Year < 2000)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La fecha de inicio de publicación no es válida."
                });
            }

            if (dto.FechaFinPublicacion.HasValue &&
                dto.FechaFinPublicacion.Value < dto.FechaInicioPublicacion)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La fecha final de publicación no puede ser anterior a la fecha inicial."
                });
            }

            if (dto.FechaEventoFin.HasValue &&
                !dto.FechaEventoInicio.HasValue)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Debe indicar la fecha de inicio del evento antes de indicar su fecha final."
                });
            }

            if (dto.FechaEventoInicio.HasValue &&
                dto.FechaEventoFin.HasValue &&
                dto.FechaEventoFin.Value < dto.FechaEventoInicio.Value)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La fecha final del evento no puede ser anterior a la fecha inicial."
                });
            }

            if (!string.IsNullOrWhiteSpace(dto.EnlaceExterno) &&
                (!Uri.TryCreate(
                    dto.EnlaceExterno.Trim(),
                    UriKind.Absolute,
                    out Uri? enlace) ||
                 (enlace.Scheme != Uri.UriSchemeHttp &&
                  enlace.Scheme != Uri.UriSchemeHttps)))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El enlace externo debe ser una dirección HTTP o HTTPS válida."
                });
            }

            return null;
        }

        private async Task<PublicacionPaginadaDto> ConstruirPaginaAsync(
            IQueryable<Publicacion> query,
            int pagina,
            int tamanoPagina,
            bool incluirAutores,
            CancellationToken cancellationToken)
        {
            int total = await query.CountAsync(cancellationToken);
            DateTime ahoraUtc = DateTime.UtcNow;

            List<PublicacionListadoDto> items = await query
                .OrderByDescending(x => x.destacada)
                .ThenByDescending(x => x.fechaInicioPublicacionUtc)
                .ThenByDescending(x => x.publicacionId)
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .Select(x => new PublicacionListadoDto
                {
                    PublicacionId = x.publicacionId,
                    CategoriaPublicacionId = x.categoriaPublicacionId,
                    Categoria =
                        x.CategoriaPublicacion.nombreCategoriaPublicacion,
                    ColorCategoria = x.CategoriaPublicacion.colorHex,
                    Titulo = x.titulo,
                    Resumen = x.resumen,
                    RutaImagenPortada = x.rutaImagenPortada,
                    Ubicacion = x.ubicacion,
                    FechaEventoInicioUtc = x.fechaEventoInicioUtc,
                    FechaEventoFinUtc = x.fechaEventoFinUtc,
                    FechaInicioPublicacionUtc =
                        x.fechaInicioPublicacionUtc,
                    FechaFinPublicacionUtc = x.fechaFinPublicacionUtc,
                    EstadoPublicacion = x.estadoPublicacion,
                    Destacada = x.destacada,
                    FechaCreacionUtc = x.fechaCreacionUtc,
                    FechaUltimaModificacionUtc =
                        x.fechaUltimaModificacionUtc,
                    UsuarioCreacionId = x.usuarioCreacionId,
                    UsuarioUltimaModificacionId =
                        x.usuarioUltimaModificacionId
                })
                .ToListAsync(cancellationToken);

            foreach (PublicacionListadoDto item in items)
            {
                item.EstadoVisual = ObtenerEstadoVisual(
                    item.EstadoPublicacion,
                    item.FechaInicioPublicacionUtc,
                    item.FechaFinPublicacionUtc,
                    ahoraUtc);
            }

            if (incluirAutores && items.Count > 0)
            {
                int[] usuarioIds = items
                    .SelectMany(x => new[]
                    {
                        x.UsuarioCreacionId,
                        x.UsuarioUltimaModificacionId
                    })
                    .Where(x => x > 0)
                    .Distinct()
                    .ToArray();

                Dictionary<int, string> autores =
                    await ObtenerNombresUsuariosAsync(
                        usuarioIds,
                        cancellationToken);

                foreach (PublicacionListadoDto item in items)
                {
                    item.Autor = ObtenerNombreUsuario(
                        autores,
                        item.UsuarioCreacionId);

                    item.UltimoEditor = ObtenerNombreUsuario(
                        autores,
                        item.UsuarioUltimaModificacionId);
                }
            }
            else
            {
                foreach (PublicacionListadoDto item in items)
                    item.Autor = "CONATRADEC";
            }

            return new PublicacionPaginadaDto
            {
                Items = items,
                Pagina = pagina,
                TamanoPagina = tamanoPagina,
                TotalRegistros = total,
                TotalPaginas = total == 0
                    ? 1
                    : (int)Math.Ceiling(total / (double)tamanoPagina)
            };
        }

        private async Task<Dictionary<int, string>>
            ObtenerNombresUsuariosAsync(
                IEnumerable<int> usuarioIds,
                CancellationToken cancellationToken)
        {
            int[] ids = usuarioIds
                .Where(x => x > 0)
                .Distinct()
                .ToArray();

            if (ids.Length == 0)
                return new Dictionary<int, string>();

            var usuarios = await db.Usuarios
                .AsNoTracking()
                .Where(x => ids.Contains(x.UsuarioId))
                .Select(x => new
                {
                    x.UsuarioId,
                    x.nombreCompletoUsuario,
                    x.nombreUsuario
                })
                .ToListAsync(cancellationToken);

            return usuarios.ToDictionary(
                x => x.UsuarioId,
                x => !string.IsNullOrWhiteSpace(x.nombreCompletoUsuario)
                    ? x.nombreCompletoUsuario.Trim()
                    : x.nombreUsuario.Trim());
        }

        private static string ObtenerNombreUsuario(
            IReadOnlyDictionary<int, string> usuarios,
            int usuarioId)
        {
            return usuarios.TryGetValue(usuarioId, out string? nombre)
                ? nombre
                : $"Usuario #{usuarioId}";
        }

        private static PublicacionDetalleDto MapearDetalle(
            Publicacion publicacion,
            DateTime ahoraUtc)
        {
            return new PublicacionDetalleDto
            {
                PublicacionId = publicacion.publicacionId,
                CategoriaPublicacionId =
                    publicacion.categoriaPublicacionId,
                Categoria = publicacion.CategoriaPublicacion
                    .nombreCategoriaPublicacion,
                ColorCategoria = publicacion.CategoriaPublicacion.colorHex,
                Titulo = publicacion.titulo,
                Resumen = publicacion.resumen,
                Contenido = publicacion.contenido,
                RutaImagenPortada = publicacion.rutaImagenPortada,
                EnlaceExterno = publicacion.enlaceExterno,
                TextoEnlace = publicacion.textoEnlace,
                Ubicacion = publicacion.ubicacion,
                FechaEventoInicioUtc = publicacion.fechaEventoInicioUtc,
                FechaEventoFinUtc = publicacion.fechaEventoFinUtc,
                FechaInicioPublicacionUtc =
                    publicacion.fechaInicioPublicacionUtc,
                FechaFinPublicacionUtc =
                    publicacion.fechaFinPublicacionUtc,
                EstadoPublicacion = publicacion.estadoPublicacion,
                EstadoVisual = ObtenerEstadoVisual(
                    publicacion.estadoPublicacion,
                    publicacion.fechaInicioPublicacionUtc,
                    publicacion.fechaFinPublicacionUtc,
                    ahoraUtc),
                Destacada = publicacion.destacada,
                FechaCreacionUtc = publicacion.fechaCreacionUtc,
                FechaUltimaModificacionUtc =
                    publicacion.fechaUltimaModificacionUtc,
                UsuarioCreacionId = publicacion.usuarioCreacionId,
                UsuarioUltimaModificacionId =
                    publicacion.usuarioUltimaModificacionId,
                Activo = publicacion.activo
            };
        }

        private static string ObtenerEstadoVisual(
            string estado,
            DateTime fechaInicioUtc,
            DateTime? fechaFinUtc,
            DateTime ahoraUtc)
        {
            string estadoNormalizado = NormalizarEstado(estado);

            if (estadoNormalizado == EstadoBorrador)
                return "BORRADOR";

            if (estadoNormalizado == EstadoArchivada)
                return "ARCHIVADA";

            if (fechaInicioUtc > ahoraUtc)
                return "PROGRAMADA";

            if (fechaFinUtc.HasValue && fechaFinUtc.Value < ahoraUtc)
                return "VENCIDA";

            return "PUBLICADA";
        }

        private static bool EsEstadoValido(string estado)
        {
            return estado == EstadoBorrador ||
                   estado == EstadoPublicada ||
                   estado == EstadoArchivada;
        }

        private static string NormalizarEstado(string? estado)
        {
            return string.IsNullOrWhiteSpace(estado)
                ? EstadoBorrador
                : estado.Trim().ToUpperInvariant();
        }

        private static void EliminarArchivoNoticias(string? rutaRelativa)
        {
            if (string.IsNullOrWhiteSpace(rutaRelativa))
                return;

            string rutaNormalizada = rutaRelativa
                .Replace('\\', '/')
                .Trim();

            const string prefijo = "/resources/uploads/noticias/";

            if (!rutaNormalizada.StartsWith(
                    prefijo,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string rutaFisica = Path.Combine(
                Directory.GetCurrentDirectory(),
                rutaNormalizada.TrimStart('/')
                    .Replace('/', Path.DirectorySeparatorChar));

            if (System.IO.File.Exists(rutaFisica))
                System.IO.File.Delete(rutaFisica);
        }
    }
}
