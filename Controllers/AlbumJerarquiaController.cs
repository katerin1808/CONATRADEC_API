using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using System.Text;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Administra la estructura limpia del Álbum Botánico:
    /// Categoría -> Subcategoría específica -> Fotografías.
    ///
    /// AlbumBotanicoCafe representa la subcategoría específica. Los nombres
    /// antiguos de algunos DTO y rutas se mantienen únicamente para
    /// compatibilidad con versiones anteriores de la aplicación.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/album-jerarquia")]
    public sealed class AlbumJerarquiaController : ControllerBase
    {
        private const string InterfazAlbum = "albumFotosPage";
        private const string InterfazSolicitud = "diagnosticoIASolicitudPage";
        private const string InterfazAnalizador = "diagnosticoIAAnalizadorPage";
        private const string InterfazAprobador = "diagnosticoIAAprobadorPage";

        private readonly AlbumJerarquiaDbContext db;
        private readonly PermisoApiService permisos;

        public AlbumJerarquiaController(
            AlbumJerarquiaDbContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;
        }

        [HttpGet("inicio")]
        public async Task<IActionResult> Inicio(
            [FromQuery] int tamanoPagina = 6,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazAlbum,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            tamanoPagina = Math.Clamp(tamanoPagina, 1, 30);

            var categorias = await db.Categorias
                .AsNoTracking()
                .Where(item => item.Activo)
                .OrderBy(item => item.NombreCategoria)
                .Select(item => new
                {
                    categoriaAlbumBotanicoId =
                        item.CategoriaAlbumBotanicoId,
                    nombreCategoria = item.NombreCategoria,
                    descripcion = item.Descripcion,
                    rutaImagenPortada = item.RutaImagenPortada,
                    activo = item.Activo,
                    totalRegistros = item.Subcategorias.Count(registro =>
                        registro.Activo),
                    totalRegistrosActivos = item.Subcategorias.Count(registro =>
                        registro.Activo)
                })
                .ToListAsync(cancellationToken);

            List<SubcategoriaAlbumDto> subcategorias =
                await ConstruirConsultaSubcategorias(incluirInactivas: false)
                    .OrderBy(item => item.Categoria.NombreCategoria)
                    .ThenBy(item => item.Titulo)
                    .Select(item => new SubcategoriaAlbumDto
                    {
                        SubcategoriaAlbumBotanicoId = item.AlbumBotanicoCafeId,
                        CategoriaAlbumBotanicoId = item.CategoriaAlbumBotanicoId,
                        Categoria = item.Categoria.NombreCategoria,
                        NombreSubcategoria = item.Titulo,
                        Descripcion = item.Descripcion,
                        Activo = item.Activo,
                        TotalRegistros = item.Fotos.Count(foto => foto.Activo)
                    })
                    .ToListAsync(cancellationToken);

            IQueryable<AlbumBotanicoCafeJerarquia> query = db.Subcategorias
                .AsNoTracking()
                .Where(item => item.Activo && item.Categoria.Activo);

            object galeria = await ConstruirPaginaGaleriaAsync(
                query,
                pagina: 1,
                tamanoPagina,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Álbum botánico cargado correctamente.",
                data = new
                {
                    categorias,
                    subcategorias,
                    galeria
                }
            });
        }

        [HttpGet("galeria-paginada")]
        public async Task<IActionResult> GaleriaPaginada(
            [FromQuery] int? categoriaId = null,
            [FromQuery] int? subcategoriaId = null,
            [FromQuery] string? buscar = null,
            [FromQuery] bool incluirInactivos = false,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 6,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazAlbum,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 1, 30);

            IQueryable<AlbumBotanicoCafeJerarquia> query = db.Subcategorias
                .AsNoTracking();

            if (!incluirInactivos)
            {
                query = query.Where(item =>
                    item.Activo && item.Categoria.Activo);
            }

            if (categoriaId is > 0)
            {
                query = query.Where(item =>
                    item.CategoriaAlbumBotanicoId == categoriaId.Value);
            }

            if (subcategoriaId is > 0)
            {
                query = query.Where(item =>
                    item.AlbumBotanicoCafeId == subcategoriaId.Value);
            }

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();

                query = query.Where(item =>
                    item.Titulo.Contains(texto) ||
                    (item.NombreCientifico != null &&
                     item.NombreCientifico.Contains(texto)) ||
                    item.Descripcion.Contains(texto) ||
                    item.Categoria.NombreCategoria.Contains(texto));
            }

            object data = await ConstruirPaginaGaleriaAsync(
                query,
                pagina,
                tamanoPagina,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Subcategorías del álbum obtenidas correctamente.",
                data
            });
        }

        /// <summary>
        /// Ruta conservada para compatibilidad. Devuelve directamente los
        /// registros AlbumBotanicoCafe como subcategorías específicas.
        /// </summary>
        [HttpGet("subcategorias")]
        public async Task<IActionResult> ListarSubcategorias(
            [FromQuery] int? categoriaId = null,
            [FromQuery] bool incluirInactivas = false,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazAlbum,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            IQueryable<AlbumBotanicoCafeJerarquia> query =
                ConstruirConsultaSubcategorias(incluirInactivas);

            if (categoriaId is > 0)
            {
                query = query.Where(item =>
                    item.CategoriaAlbumBotanicoId == categoriaId.Value);
            }

            List<SubcategoriaAlbumDto> data = await query
                .OrderBy(item => item.Categoria.NombreCategoria)
                .ThenBy(item => item.Titulo)
                .Select(item => new SubcategoriaAlbumDto
                {
                    SubcategoriaAlbumBotanicoId = item.AlbumBotanicoCafeId,
                    CategoriaAlbumBotanicoId = item.CategoriaAlbumBotanicoId,
                    Categoria = item.Categoria.NombreCategoria,
                    NombreSubcategoria = item.Titulo,
                    Descripcion = item.Descripcion,
                    Activo = item.Activo,
                    TotalRegistros = item.Fotos.Count(foto => foto.Activo)
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Subcategorías obtenidas correctamente.",
                data
            });
        }

        /// <summary>
        /// Creación básica para clientes anteriores. La administración normal
        /// usa el formulario completo de AlbumBotanicoCafe.
        /// </summary>
        [HttpPost("subcategorias")]
        public async Task<IActionResult> CrearSubcategoria(
            [FromBody] GuardarSubcategoriaAlbumRequest request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazAlbum,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            CategoriaAlbumJerarquia? categoria = await db.Categorias
                .FirstOrDefaultAsync(item =>
                    item.CategoriaAlbumBotanicoId ==
                        request.CategoriaAlbumBotanicoId &&
                    item.Activo,
                    cancellationToken);

            if (categoria == null)
                return ErrorValidacion("La categoría no existe o está inactiva.");

            string nombre = Limpiar(request.NombreSubcategoria, 200);
            string descripcion = Limpiar(request.Descripcion, 4000);

            if (nombre.Length < 3)
                return ErrorValidacion("Ingrese un nombre de subcategoría válido.");

            if (descripcion.Length < 3)
                descripcion = $"Subcategoría específica {nombre}.";

            bool duplicada = await db.Subcategorias.AnyAsync(item =>
                item.CategoriaAlbumBotanicoId ==
                    categoria.CategoriaAlbumBotanicoId &&
                item.Titulo == nombre,
                cancellationToken);

            if (duplicada)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe una subcategoría con ese nombre dentro de la categoría seleccionada."
                });
            }

            var entidad = new AlbumBotanicoCafeJerarquia
            {
                CategoriaAlbumBotanicoId = categoria.CategoriaAlbumBotanicoId,
                Titulo = nombre,
                Descripcion = descripcion,
                Activo = true,
                FechaCreacion = DateTime.Now
            };

            db.Subcategorias.Add(entidad);
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Subcategoría creada correctamente.",
                data = CrearSubcategoriaDto(entidad, categoria.NombreCategoria)
            });
        }

        [HttpPut("subcategorias/{id:int}")]
        public async Task<IActionResult> ActualizarSubcategoria(
            int id,
            [FromBody] GuardarSubcategoriaAlbumRequest request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazAlbum,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            AlbumBotanicoCafeJerarquia? entidad = await db.Subcategorias
                .FirstOrDefaultAsync(item =>
                    item.AlbumBotanicoCafeId == id,
                    cancellationToken);

            if (entidad == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La subcategoría no fue encontrada."
                });
            }

            bool categoriaValida = await db.Categorias.AnyAsync(item =>
                item.CategoriaAlbumBotanicoId ==
                    request.CategoriaAlbumBotanicoId &&
                item.Activo,
                cancellationToken);

            if (!categoriaValida)
                return ErrorValidacion("La categoría no existe o está inactiva.");

            string nombre = Limpiar(request.NombreSubcategoria, 200);
            if (nombre.Length < 3)
                return ErrorValidacion("Ingrese un nombre de subcategoría válido.");

            bool duplicada = await db.Subcategorias.AnyAsync(item =>
                item.AlbumBotanicoCafeId != id &&
                item.CategoriaAlbumBotanicoId ==
                    request.CategoriaAlbumBotanicoId &&
                item.Titulo == nombre,
                cancellationToken);

            if (duplicada)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe otra subcategoría con ese nombre dentro de la categoría seleccionada."
                });
            }

            entidad.CategoriaAlbumBotanicoId =
                request.CategoriaAlbumBotanicoId;
            entidad.Titulo = nombre;

            string descripcion = Limpiar(request.Descripcion, 4000);
            if (!string.IsNullOrWhiteSpace(descripcion))
                entidad.Descripcion = descripcion;

            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Subcategoría actualizada correctamente."
            });
        }

        [HttpPatch("subcategorias/{id:int}/estado")]
        public async Task<IActionResult> CambiarEstadoSubcategoria(
            int id,
            [FromQuery] bool activo,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazAlbum,
                TipoPermisoApi.Eliminar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            AlbumBotanicoCafeJerarquia? entidad = await db.Subcategorias
                .FirstOrDefaultAsync(item =>
                    item.AlbumBotanicoCafeId == id,
                    cancellationToken);

            if (entidad == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La subcategoría no fue encontrada."
                });
            }

            if (activo)
            {
                bool categoriaActiva = await db.Categorias.AnyAsync(item =>
                    item.CategoriaAlbumBotanicoId ==
                        entidad.CategoriaAlbumBotanicoId &&
                    item.Activo,
                    cancellationToken);

                if (!categoriaActiva)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "No puede activar la subcategoría mientras su categoría esté inactiva."
                    });
                }
            }

            entidad.Activo = activo;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = activo
                    ? "Subcategoría activada correctamente."
                    : "Subcategoría desactivada correctamente."
            });
        }

        [HttpGet("registros")]
        public async Task<IActionResult> ObtenerJerarquiaRegistros(
            [FromQuery] string? ids,
            [FromQuery] int? categoriaId = null,
            [FromQuery] int? subcategoriaId = null,
            [FromQuery] bool incluirInactivos = false,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazAlbum,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            HashSet<int> identificadores = ParsearIds(ids);
            IQueryable<AlbumBotanicoCafeJerarquia> query = db.Subcategorias
                .AsNoTracking();

            if (identificadores.Count > 0)
            {
                query = query.Where(item =>
                    identificadores.Contains(item.AlbumBotanicoCafeId));
            }

            if (categoriaId is > 0)
            {
                query = query.Where(item =>
                    item.CategoriaAlbumBotanicoId == categoriaId.Value);
            }

            if (subcategoriaId is > 0)
            {
                query = query.Where(item =>
                    item.AlbumBotanicoCafeId == subcategoriaId.Value);
            }

            if (!incluirInactivos)
            {
                query = query.Where(item =>
                    item.Activo && item.Categoria.Activo);
            }

            List<AlbumRegistroJerarquiaDto> data = await query
                .OrderBy(item => item.Categoria.NombreCategoria)
                .ThenBy(item => item.Titulo)
                .Select(item => new AlbumRegistroJerarquiaDto
                {
                    AlbumBotanicoCafeId = item.AlbumBotanicoCafeId,
                    CategoriaAlbumBotanicoId = item.CategoriaAlbumBotanicoId,
                    Categoria = item.Categoria.NombreCategoria,
                    SubcategoriaAlbumBotanicoId = item.AlbumBotanicoCafeId,
                    Subcategoria = item.Titulo,
                    Titulo = item.Titulo,
                    NombreCientifico = item.NombreCientifico,
                    Descripcion = item.Descripcion,
                    Activo = item.Activo
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Subcategorías específicas obtenidas correctamente.",
                data
            });
        }

        /// <summary>
        /// Compatibilidad con clientes que guardaban una ficha y luego la
        /// asignaban al nivel intermedio. Ahora la subcategoría es el propio
        /// registro, por lo que la operación solo valida la identidad.
        /// </summary>
        [HttpPut("registros/{id:int}/subcategoria")]
        public async Task<IActionResult> AsignarSubcategoriaRegistro(
            int id,
            [FromBody] AsignarSubcategoriaRegistroRequest request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                InterfazAlbum,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            bool existe = await db.Subcategorias.AnyAsync(item =>
                item.AlbumBotanicoCafeId == id,
                cancellationToken);

            if (!existe)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La subcategoría no fue encontrada."
                });
            }

            if (request.SubcategoriaAlbumBotanicoId > 0 &&
                request.SubcategoriaAlbumBotanicoId != id)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "El álbum ya no utiliza un nivel intermedio. El identificador debe corresponder a la misma subcategoría específica."
                });
            }

            return Ok(new
            {
                success = true,
                message = "La subcategoría ya se encuentra vinculada correctamente."
            });
        }

        [HttpGet("diagnosticos/{diagnosticoId:int}")]
        public async Task<IActionResult> ObtenerJerarquiaDiagnostico(
            int diagnosticoId,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarLecturaDiagnosticoAsync(
                cancellationToken);

            if (acceso != null)
                return acceso;

            bool existe = await db.Diagnosticos
                .AsNoTracking()
                .AnyAsync(item =>
                    item.DiagnosticoIAId == diagnosticoId &&
                    item.Activo,
                    cancellationToken);

            if (!existe)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La inspección no fue encontrada."
                });
            }

            List<CategoriaLigera> categorias = await db.Categorias
                .AsNoTracking()
                .Where(item => item.Activo)
                .Select(item => new CategoriaLigera
                {
                    CategoriaAlbumBotanicoId = item.CategoriaAlbumBotanicoId,
                    NombreCategoria = item.NombreCategoria
                })
                .ToListAsync(cancellationToken);

            List<AlbumRegistroJerarquiaDto> subcategorias = await db.Subcategorias
                .AsNoTracking()
                .Where(item => item.Activo && item.Categoria.Activo)
                .Select(item => new AlbumRegistroJerarquiaDto
                {
                    AlbumBotanicoCafeId = item.AlbumBotanicoCafeId,
                    CategoriaAlbumBotanicoId = item.CategoriaAlbumBotanicoId,
                    Categoria = item.Categoria.NombreCategoria,
                    SubcategoriaAlbumBotanicoId = item.AlbumBotanicoCafeId,
                    Subcategoria = item.Titulo,
                    Titulo = item.Titulo,
                    NombreCientifico = item.NombreCientifico,
                    Descripcion = item.Descripcion,
                    Activo = item.Activo
                })
                .ToListAsync(cancellationToken);

            var filas = await db.Fotografias
                .AsNoTracking()
                .Where(item => item.DiagnosticoIAId == diagnosticoId)
                .OrderBy(item => item.Orden)
                .Select(item => new
                {
                    Foto = item,
                    Resultado = item.ResultadoIA,
                    Jerarquia = db.ClasificacionesJerarquia
                        .AsNoTracking()
                        .FirstOrDefault(jerarquia =>
                            jerarquia.DiagnosticoIAImagenId ==
                                item.DiagnosticoIAImagenId)
                })
                .ToListAsync(cancellationToken);

            Dictionary<int, AlbumRegistroJerarquiaDto> porId =
                subcategorias.ToDictionary(
                    item => item.AlbumBotanicoCafeId);

            List<JerarquiaDiagnosticoFotoDto> data = filas
                .Select(item => ConstruirJerarquiaFoto(
                    item.Foto,
                    item.Resultado,
                    item.Jerarquia,
                    categorias,
                    subcategorias,
                    porId))
                .ToList();

            return Ok(new
            {
                success = true,
                message = "Clasificación del Álbum Botánico obtenida correctamente.",
                data
            });
        }

        [HttpPost("diagnosticos/{diagnosticoId:int}/fotografias/{fotoId:int}/resolver")]
        public async Task<IActionResult> ResolverJerarquia(
            int diagnosticoId,
            int fotoId,
            [FromBody] ResolverJerarquiaAlbumRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "No se encontró el usuario autenticado."
                });
            }

            string etapa = (request.Etapa ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            if (etapa is not ("ANALIZADOR" or "APROBADOR"))
                return ErrorValidacion("La etapa de clasificación no es válida.");

            bool esAprobador = etapa == "APROBADOR";
            string interfaz = esAprobador
                ? InterfazAprobador
                : InterfazAnalizador;

            IActionResult? acceso = await ValidarPermisoAsync(
                interfaz,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DiagnosticoIAJerarquiaReferencia? diagnostico = await db.Diagnosticos
                .Include(item => item.Fotografias)
                    .ThenInclude(item => item.ResultadoIA)
                .FirstOrDefaultAsync(item =>
                    item.DiagnosticoIAId == diagnosticoId &&
                    item.Activo,
                    cancellationToken);

            if (diagnostico == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La inspección no fue encontrada."
                });
            }

            DiagnosticoIAImagenJerarquiaReferencia? foto =
                diagnostico.Fotografias.FirstOrDefault(item =>
                    item.DiagnosticoIAImagenId == fotoId);

            if (foto?.ResultadoIA == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "La fotografía o su resultado individual no fueron encontrados."
                });
            }

            if (!foto.Activo || foto.Descartada)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "La fotografía ya no se encuentra disponible para modificar su clasificación."
                });
            }

            /*
             * El flujo fitosanitario actual avanza por fotografía. El estado
             * general de la inspección es un resumen y puede ser, por ejemplo,
             * PENDIENTE_REVISION mientras una evidencia concreta todavía está
             * PENDIENTE_ANALIZADOR. Por eso la autorización de esta operación
             * se realiza contra el estado individual de la fotografía.
             */
            string estadoFotografia = (foto.Estado ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            bool estadoPermitido = esAprobador
                ? estadoFotografia == "PENDIENTE_APROBACION"
                : estadoFotografia is
                    "PENDIENTE_ANALIZADOR" or
                    "EN_ANALISIS_HUMANO" or
                    "DEVUELTO_PARA_CORRECCION" or
                    "DEVUELTA_AL_ANALIZADOR";

            if (!estadoPermitido)
            {
                return Conflict(new
                {
                    success = false,
                    message = esAprobador
                        ? "La fotografía no está pendiente de aprobación y su clasificación no puede modificarse en esta etapa."
                        : "La fotografía no está disponible para revisión del analizador en su estado actual."
                });
            }

            string motivo = Limpiar(request.Motivo, 1200);
            if (!esAprobador && motivo.Length < 8)
            {
                return ErrorValidacion(
                    "Explique por qué la clasificación seleccionada representa la fotografía.");
            }

            CategoriaAlbumJerarquia? categoria = null;
            if (request.CategoriaAlbumBotanicoId is > 0)
            {
                categoria = await db.Categorias.FirstOrDefaultAsync(item =>
                    item.CategoriaAlbumBotanicoId ==
                        request.CategoriaAlbumBotanicoId.Value &&
                    item.Activo,
                    cancellationToken);

                if (categoria == null)
                    return ErrorValidacion("La categoría seleccionada no existe o está inactiva.");
            }

            string categoriaPropuesta = Limpiar(request.CategoriaPropuesta, 100);

            if (categoria == null && esAprobador)
            {
                if (!request.ProponerCategoria || categoriaPropuesta.Length < 3)
                {
                    return ErrorValidacion(
                        "Seleccione una categoría existente o proponga una nueva.");
                }

                IActionResult? permisoAlbum = await ValidarPermisoAsync(
                    InterfazAlbum,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

                if (permisoAlbum != null)
                    return permisoAlbum;

                categoria = await db.Categorias.FirstOrDefaultAsync(item =>
                    item.NombreCategoria == categoriaPropuesta,
                    cancellationToken);

                if (categoria == null)
                {
                    categoria = new CategoriaAlbumJerarquia
                    {
                        NombreCategoria = categoriaPropuesta,
                        Descripcion = LimpiarOpcional(motivo, 500),
                        Activo = true
                    };

                    db.Categorias.Add(categoria);
                    await db.SaveChangesAsync(cancellationToken);
                }
                else if (!categoria.Activo)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "Ya existe una categoría inactiva con ese nombre. Actívela desde el Álbum Botánico."
                    });
                }
            }

            int? categoriaIdFinal = categoria?.CategoriaAlbumBotanicoId;
            string categoriaNombreFinal = categoria?.NombreCategoria
                ?? categoriaPropuesta;

            int? subcategoriaIdSolicitada =
                request.AlbumBotanicoCafeId is > 0
                    ? request.AlbumBotanicoCafeId
                    : request.SubcategoriaAlbumBotanicoId;

            AlbumBotanicoCafeJerarquia? subcategoria = null;
            if (subcategoriaIdSolicitada is > 0)
            {
                subcategoria = await db.Subcategorias.FirstOrDefaultAsync(item =>
                    item.AlbumBotanicoCafeId ==
                        subcategoriaIdSolicitada.Value &&
                    item.Activo,
                    cancellationToken);

                if (subcategoria == null)
                    return ErrorValidacion("La subcategoría seleccionada no existe o está inactiva.");

                if (categoriaIdFinal.HasValue &&
                    subcategoria.CategoriaAlbumBotanicoId !=
                        categoriaIdFinal.Value)
                {
                    return ErrorValidacion(
                        "La subcategoría seleccionada no pertenece a la categoría indicada.");
                }

                categoria ??= await db.Categorias.FirstAsync(item =>
                    item.CategoriaAlbumBotanicoId ==
                        subcategoria.CategoriaAlbumBotanicoId,
                    cancellationToken);

                categoriaIdFinal = subcategoria.CategoriaAlbumBotanicoId;
                categoriaNombreFinal = categoria.NombreCategoria;
            }

            string subcategoriaPropuesta = PrimerTexto(
                request.SubcategoriaPropuesta,
                request.FichaPropuesta,
                foto.ResultadoIA.ClasificacionAlbumSugerida,
                foto.ResultadoIA.DiagnosticoProbable);
            subcategoriaPropuesta = Limpiar(
                LimpiarNombreDiagnostico(subcategoriaPropuesta),
                200);

            string nombreCientifico = Limpiar(
                request.NombreCientifico,
                200);
            string descripcion = Limpiar(request.Descripcion, 4000);

            if (subcategoria == null && esAprobador)
            {
                if (!request.ProponerSubcategoria ||
                    subcategoriaPropuesta.Length < 3)
                {
                    return ErrorValidacion(
                        "Seleccione una subcategoría existente o proponga una nueva.");
                }

                if (!categoriaIdFinal.HasValue)
                {
                    return ErrorValidacion(
                        "La nueva subcategoría necesita una categoría oficial.");
                }

                if (descripcion.Length < 8)
                {
                    descripcion = PrimerTexto(
                        foto.ResultadoIA.MotivoClasificacionAlbum,
                        motivo,
                        $"Subcategoría específica {subcategoriaPropuesta}.");
                }

                IActionResult? permisoAlbum = await ValidarPermisoAsync(
                    InterfazAlbum,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

                if (permisoAlbum != null)
                    return permisoAlbum;

                subcategoria = await db.Subcategorias.FirstOrDefaultAsync(item =>
                    item.CategoriaAlbumBotanicoId ==
                        categoriaIdFinal.Value &&
                    item.Titulo == subcategoriaPropuesta,
                    cancellationToken);

                if (subcategoria == null)
                {
                    subcategoria = new AlbumBotanicoCafeJerarquia
                    {
                        CategoriaAlbumBotanicoId = categoriaIdFinal.Value,
                        Titulo = subcategoriaPropuesta,
                        NombreCientifico =
                            LimpiarOpcional(nombreCientifico, 200),
                        Descripcion = descripcion,
                        Sintomas = LimpiarOpcional(request.Sintomas, 4000),
                        Observaciones =
                            "Subcategoría creada desde una clasificación fitosanitaria aprobada.",
                        Activo = true,
                        FechaCreacion = DateTime.Now
                    };

                    db.Subcategorias.Add(subcategoria);
                    await db.SaveChangesAsync(cancellationToken);
                }
                else if (!subcategoria.Activo)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "Ya existe una subcategoría inactiva con ese nombre. Actívela desde el Álbum Botánico."
                    });
                }
            }

            int? subcategoriaIdFinal = subcategoria?.AlbumBotanicoCafeId;
            string subcategoriaNombreFinal = subcategoria?.Titulo
                ?? subcategoriaPropuesta;
            string cientificoFinal = PrimerTexto(
                subcategoria?.NombreCientifico,
                nombreCientifico,
                foto.ResultadoIA.NombreCientificoSugerido,
                ExtraerNombreCientifico(foto.ResultadoIA.DiagnosticoProbable));

            bool nivelesExistentes =
                categoriaIdFinal.HasValue &&
                subcategoriaIdFinal.HasValue;

            DiagnosticoIAClasificacionJerarquia jerarquia =
                await db.ClasificacionesJerarquia
                    .FirstOrDefaultAsync(item =>
                        item.DiagnosticoIAImagenId == fotoId,
                        cancellationToken)
                ?? new DiagnosticoIAClasificacionJerarquia
                {
                    DiagnosticoIAImagenId = fotoId
                };

            if (jerarquia.DiagnosticoIAClasificacionJerarquiaId == 0)
                db.ClasificacionesJerarquia.Add(jerarquia);

            if (!esAprobador)
            {
                jerarquia.CategoriaAlbumBotanicoIdSugerida =
                    categoriaIdFinal;
                jerarquia.AlbumBotanicoCafeIdSugerido =
                    subcategoriaIdFinal;
                jerarquia.CategoriaSugerida = categoriaNombreFinal;
                jerarquia.SubcategoriaSugerida = subcategoriaNombreFinal;
                jerarquia.NombreCientificoSugerido = cientificoFinal;
                jerarquia.MotivoSugerencia = motivo;
                jerarquia.ProponeCategoria = !categoriaIdFinal.HasValue;
                jerarquia.ProponeSubcategoria = !subcategoriaIdFinal.HasValue;
                jerarquia.Estado = nivelesExistentes
                    ? "RESUELTA_ANALIZADOR"
                    : "PROPUESTA_ANALIZADOR";

                DiagnosticoIAImagenResultadoJerarquiaReferencia resultado =
                    foto.ResultadoIA;

                resultado.CategoriaAlbumBotanicoIdSugerida =
                    categoriaIdFinal;
                resultado.AlbumBotanicoCafeIdSugerido =
                    subcategoriaIdFinal;
                resultado.CategoriaAlbumSugerida = categoriaNombreFinal;
                resultado.ClasificacionAlbumSugerida =
                    subcategoriaNombreFinal;
                resultado.NombreCientificoSugerido = cientificoFinal;
                resultado.CoincideCatalogoAlbum = nivelesExistentes;
                resultado.RequiereDecisionClasificacion = !nivelesExistentes;
                resultado.MotivoClasificacionAlbum = motivo;
                resultado.EstadoClasificacionAlbum = nivelesExistentes
                    ? "RESUELTA_POR_ANALIZADOR"
                    : "PROPUESTA_ANALIZADOR";
            }
            else
            {
                if (!nivelesExistentes ||
                    categoria == null ||
                    subcategoria == null)
                {
                    return ErrorValidacion(
                        "El aprobador debe dejar una categoría y una subcategoría específica oficiales seleccionadas o creadas.");
                }

                jerarquia.CategoriaAlbumBotanicoIdSeleccionada =
                    categoria.CategoriaAlbumBotanicoId;
                jerarquia.AlbumBotanicoCafeIdSeleccionado =
                    subcategoria.AlbumBotanicoCafeId;
                jerarquia.CategoriaSeleccionada = categoria.NombreCategoria;
                jerarquia.SubcategoriaSeleccionada = subcategoria.Titulo;
                jerarquia.ProponeCategoria = false;
                jerarquia.ProponeSubcategoria = false;
                jerarquia.Estado = "RESUELTA_APROBADOR";

                DiagnosticoIAImagenResultadoJerarquiaReferencia resultado =
                    foto.ResultadoIA;

                resultado.CategoriaAlbumBotanicoIdSeleccionada =
                    categoria.CategoriaAlbumBotanicoId;
                resultado.AlbumBotanicoCafeIdSeleccionado =
                    subcategoria.AlbumBotanicoCafeId;
                resultado.CategoriaAlbumSeleccionada =
                    categoria.NombreCategoria;
                resultado.ClasificacionAlbumSeleccionada =
                    subcategoria.Titulo;
                resultado.RequiereDecisionClasificacion = false;
                resultado.EstadoClasificacionAlbum =
                    "RESUELTA_POR_APROBADOR";
            }

            jerarquia.UsuarioActualizacionId = usuarioId;
            jerarquia.FechaActualizacionUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = esAprobador
                    ? "La clasificación quedó vinculada con el Álbum Botánico."
                    : nivelesExistentes
                        ? "La fotografía quedó vinculada con una subcategoría existente del Álbum Botánico."
                        : "La propuesta fue guardada para decisión del aprobador."
            });
        }

        private JerarquiaDiagnosticoFotoDto ConstruirJerarquiaFoto(
            DiagnosticoIAImagenJerarquiaReferencia foto,
            DiagnosticoIAImagenResultadoJerarquiaReferencia? resultado,
            DiagnosticoIAClasificacionJerarquia? guardada,
            IReadOnlyCollection<CategoriaLigera> categorias,
            IReadOnlyCollection<AlbumRegistroJerarquiaDto> subcategorias,
            IReadOnlyDictionary<int, AlbumRegistroJerarquiaDto> porId)
        {
            if (resultado == null)
            {
                return new JerarquiaDiagnosticoFotoDto
                {
                    FotografiaId = foto.DiagnosticoIAImagenId,
                    Orden = foto.Orden,
                    TieneClasificacion = false
                };
            }

            int? subcategoriaId =
                guardada?.AlbumBotanicoCafeIdSeleccionado ??
                guardada?.AlbumBotanicoCafeIdSugerido ??
                resultado.AlbumBotanicoCafeIdSeleccionado ??
                resultado.AlbumBotanicoCafeIdSugerido;

            porId.TryGetValue(
                subcategoriaId ?? 0,
                out AlbumRegistroJerarquiaDto? existente);

            string categoria = PrimerTexto(
                guardada?.CategoriaSeleccionada,
                guardada?.CategoriaSugerida,
                existente?.Categoria,
                resultado.CategoriaAlbumSeleccionada,
                resultado.CategoriaAlbumSugerida,
                MapearCategoria(resultado));

            int? categoriaId =
                guardada?.CategoriaAlbumBotanicoIdSeleccionada ??
                guardada?.CategoriaAlbumBotanicoIdSugerida ??
                existente?.CategoriaAlbumBotanicoId ??
                resultado.CategoriaAlbumBotanicoIdSeleccionada ??
                resultado.CategoriaAlbumBotanicoIdSugerida ??
                BuscarCategoriaId(categoria, categorias);

            string subcategoria = PrimerTexto(
                guardada?.SubcategoriaSeleccionada,
                guardada?.SubcategoriaSugerida,
                existente?.Titulo,
                resultado.ClasificacionAlbumSeleccionada,
                resultado.ClasificacionAlbumSugerida,
                SugerirSubcategoriaEspecifica(resultado));

            if (!subcategoriaId.HasValue)
            {
                subcategoriaId = BuscarSubcategoriaId(
                    categoriaId,
                    subcategoria,
                    subcategorias);

                if (subcategoriaId.HasValue)
                    porId.TryGetValue(subcategoriaId.Value, out existente);
            }

            string cientifico = PrimerTexto(
                guardada?.NombreCientificoSugerido,
                existente?.NombreCientifico,
                resultado.NombreCientificoSugerido,
                ExtraerNombreCientifico(resultado.DiagnosticoProbable));

            bool categoriaExiste = categoriaId.HasValue;
            bool subcategoriaExiste = existente != null;

            string motivo = PrimerTexto(
                guardada?.MotivoSugerencia,
                resultado.MotivoClasificacionAlbum,
                ConstruirMotivo(
                    categoriaExiste,
                    subcategoriaExiste,
                    categoria,
                    subcategoria));

            return new JerarquiaDiagnosticoFotoDto
            {
                FotografiaId = foto.DiagnosticoIAImagenId,
                Orden = foto.Orden,
                TieneClasificacion =
                    resultado.ImagenValida && resultado.ParecePlantaCafe,
                CategoriaAlbumBotanicoId = categoriaId,
                SubcategoriaAlbumBotanicoId = existente?.AlbumBotanicoCafeId,
                AlbumBotanicoCafeId = existente?.AlbumBotanicoCafeId,
                Categoria = categoria,
                Subcategoria = subcategoria,
                Ficha = subcategoria,
                NombreCientifico = cientifico,
                Motivo = motivo,
                CategoriaEsPropuesta = !categoriaExiste,
                SubcategoriaEsPropuesta = !subcategoriaExiste,
                FichaEsPropuesta = !subcategoriaExiste,
                Estado = guardada?.Estado ??
                    (subcategoriaExiste
                        ? "COINCIDENCIA_CATALOGO"
                        : "SUGERIDA_IA")
            };
        }

        private IQueryable<AlbumBotanicoCafeJerarquia>
            ConstruirConsultaSubcategorias(bool incluirInactivas)
        {
            IQueryable<AlbumBotanicoCafeJerarquia> query = db.Subcategorias
                .AsNoTracking();

            if (!incluirInactivas)
            {
                query = query.Where(item =>
                    item.Activo && item.Categoria.Activo);
            }

            return query;
        }

        private static SubcategoriaAlbumDto CrearSubcategoriaDto(
            AlbumBotanicoCafeJerarquia item,
            string categoria) =>
            new()
            {
                SubcategoriaAlbumBotanicoId = item.AlbumBotanicoCafeId,
                CategoriaAlbumBotanicoId = item.CategoriaAlbumBotanicoId,
                Categoria = categoria,
                NombreSubcategoria = item.Titulo,
                Descripcion = item.Descripcion,
                Activo = item.Activo,
                TotalRegistros = 0
            };

        private static async Task<object> ConstruirPaginaGaleriaAsync(
            IQueryable<AlbumBotanicoCafeJerarquia> query,
            int pagina,
            int tamanoPagina,
            CancellationToken cancellationToken)
        {
            int totalRegistros = await query.CountAsync(cancellationToken);
            int omitir = (pagina - 1) * tamanoPagina;

            List<AlbumGaleriaJerarquiaFila> items = await query
                .OrderByDescending(item => item.Activo)
                .ThenBy(item => item.Categoria.NombreCategoria)
                .ThenBy(item => item.Titulo)
                .Skip(omitir)
                .Take(tamanoPagina)
                .Select(item => new AlbumGaleriaJerarquiaFila
                {
                    AlbumBotanicoCafeId = item.AlbumBotanicoCafeId,
                    CategoriaAlbumBotanicoId =
                        item.CategoriaAlbumBotanicoId,
                    Categoria = item.Categoria.NombreCategoria,
                    SubcategoriaAlbumBotanicoId = item.AlbumBotanicoCafeId,
                    Subcategoria = item.Titulo,
                    Titulo = item.Titulo,
                    NombreCientifico = item.NombreCientifico,
                    DescripcionCorta = item.Descripcion.Length > 180
                        ? item.Descripcion.Substring(0, 180) + "..."
                        : item.Descripcion,
                    FotoPortada = item.Fotos
                        .Where(foto => foto.Activo)
                        .OrderByDescending(foto => foto.EsPortada)
                        .ThenBy(foto => foto.Orden)
                        .Select(foto => foto.RutaFoto)
                        .FirstOrDefault(),
                    TotalFotos = item.Fotos.Count(foto => foto.Activo),
                    Activo = item.Activo,
                    CategoriaActiva = item.Categoria.Activo,
                    SubcategoriaActiva = item.Activo,
                    FechaCreacion = item.FechaCreacion
                })
                .ToListAsync(cancellationToken);

            int totalPaginas = totalRegistros == 0
                ? 0
                : (int)Math.Ceiling(
                    totalRegistros / (double)tamanoPagina);

            return new
            {
                items,
                paginaActual = pagina,
                tamanoPagina,
                totalRegistros,
                totalPaginas,
                tieneMas = pagina < totalPaginas
            };
        }

        private static string MapearCategoria(
            DiagnosticoIAImagenResultadoJerarquiaReferencia resultado)
        {
            string texto = NormalizarComparacion(
                $"{resultado.CategoriaPrincipal} {resultado.EstadoGeneral}");

            if (texto.Contains("PLAGA"))
                return "Plagas";
            if (texto.Contains("ENFERMED"))
                return "Enfermedades";
            if (texto.Contains("DEFICI") || texto.Contains("NUTRIC"))
                return "Alteraciones nutricionales";
            if (texto.Contains("ESTRES") || texto.Contains("DANO_NO_BIOTICO"))
                return "Estrés abiótico";
            if (texto.Contains("SANA"))
                return "Plantas sanas";

            return Limpiar(resultado.CategoriaPrincipal, 150)
                .Replace('_', ' ');
        }

        private static string SugerirSubcategoriaEspecifica(
            DiagnosticoIAImagenResultadoJerarquiaReferencia resultado) =>
            LimpiarNombreDiagnostico(PrimerTexto(
                resultado.ClasificacionAlbumSeleccionada,
                resultado.ClasificacionAlbumSugerida,
                resultado.DiagnosticoProbable,
                resultado.TipoDiagnostico.Replace('_', ' ')));

        private static string LimpiarNombreDiagnostico(string? valor)
        {
            string texto = (valor ?? string.Empty).Trim();
            int parentesis = texto.IndexOf('(');

            if (parentesis > 0)
                texto = texto[..parentesis].Trim();

            int separador = texto.IndexOf(" - ", StringComparison.Ordinal);
            if (separador > 0)
                texto = texto[..separador].Trim();

            return texto;
        }

        private static string ConstruirMotivo(
            bool categoriaExiste,
            bool subcategoriaExiste,
            string categoria,
            string subcategoria)
        {
            if (!categoriaExiste)
            {
                return $"No existe una categoría activa compatible. Se propone crear {categoria} y la subcategoría específica {subcategoria}.";
            }

            if (!subcategoriaExiste)
            {
                return $"La categoría {categoria} existe, pero no hay una subcategoría específica compatible. Se propone crear {subcategoria}.";
            }

            return $"La fotografía coincide con {categoria} → {subcategoria}.";
        }

        private static int? BuscarCategoriaId(
            string nombre,
            IEnumerable<CategoriaLigera> categorias)
        {
            string buscado = NormalizarComparacion(nombre);

            foreach (CategoriaLigera categoria in categorias)
            {
                if (NormalizarComparacion(categoria.NombreCategoria) == buscado)
                    return categoria.CategoriaAlbumBotanicoId;
            }

            return null;
        }

        private static int? BuscarSubcategoriaId(
            int? categoriaId,
            string nombre,
            IEnumerable<AlbumRegistroJerarquiaDto> subcategorias)
        {
            if (!categoriaId.HasValue)
                return null;

            string buscado = NormalizarComparacion(nombre);

            foreach (AlbumRegistroJerarquiaDto subcategoria in subcategorias)
            {
                if (subcategoria.CategoriaAlbumBotanicoId == categoriaId.Value &&
                    NormalizarComparacion(subcategoria.Titulo) == buscado)
                {
                    return subcategoria.AlbumBotanicoCafeId;
                }
            }

            return null;
        }

        private async Task<IActionResult?> ValidarLecturaDiagnosticoAsync(
            CancellationToken cancellationToken)
        {
            int? usuarioId = ObtenerUsuarioId();

            foreach (string interfaz in new[]
            {
                InterfazSolicitud,
                InterfazAnalizador,
                InterfazAprobador
            })
            {
                ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                    usuarioId,
                    interfaz,
                    TipoPermisoApi.Leer,
                    cancellationToken);

                if (permiso.Permitido)
                    return null;
            }

            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    success = false,
                    message =
                        "No tiene permiso para consultar esta clasificación."
                });
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            string interfaz,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                ObtenerUsuarioId(),
                interfaz,
                permiso,
                cancellationToken);

            return resultado.Permitido
                ? null
                : StatusCode(
                    resultado.CodigoEstado,
                    new
                    {
                        success = false,
                        message = resultado.Mensaje
                    });
        }

        private int? ObtenerUsuarioId()
        {
            string? valor = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("UsuarioId")
                ?? User.FindFirstValue("usuarioId")
                ?? User.FindFirstValue("sub");

            return int.TryParse(valor, out int id) && id > 0
                ? id
                : null;
        }

        private BadRequestObjectResult ErrorValidacion(string mensaje) =>
            BadRequest(new { success = false, message = mensaje });

        private static HashSet<int> ParsearIds(string? ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
                return [];

            return ids
                .Split(',', StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries)
                .Select(valor => int.TryParse(valor, out int id) ? id : 0)
                .Where(id => id > 0)
                .Take(200)
                .ToHashSet();
        }

        private static string PrimerTexto(params string?[] valores) =>
            valores.FirstOrDefault(valor =>
                !string.IsNullOrWhiteSpace(valor))?.Trim() ?? string.Empty;

        private static string Limpiar(string? valor, int longitudMaxima)
        {
            string resultado = (valor ?? string.Empty).Trim();
            return resultado.Length <= longitudMaxima
                ? resultado
                : resultado[..longitudMaxima];
        }

        private static string? LimpiarOpcional(
            string? valor,
            int longitudMaxima)
        {
            string resultado = Limpiar(valor, longitudMaxima);
            return string.IsNullOrWhiteSpace(resultado) ? null : resultado;
        }

        private static string NormalizarComparacion(string? valor)
        {
            string texto = (valor ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Normalize(NormalizationForm.FormD);

            var builder = new StringBuilder(texto.Length);

            foreach (char caracter in texto)
            {
                UnicodeCategory categoria = CharUnicodeInfo
                    .GetUnicodeCategory(caracter);

                if (categoria != UnicodeCategory.NonSpacingMark)
                    builder.Append(caracter);
            }

            return builder
                .ToString()
                .Normalize(NormalizationForm.FormC);
        }

        private static string ExtraerNombreCientifico(string? diagnostico)
        {
            string texto = diagnostico ?? string.Empty;
            int inicio = texto.IndexOf('(');
            int fin = texto.IndexOf(')', inicio + 1);

            if (inicio < 0 || fin <= inicio)
                return string.Empty;

            return Limpiar(texto[(inicio + 1)..fin], 200);
        }
    }

    public sealed class GuardarSubcategoriaAlbumRequest
    {
        public int CategoriaAlbumBotanicoId { get; set; }
        public string NombreSubcategoria { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
    }

    public sealed class AsignarSubcategoriaRegistroRequest
    {
        public int SubcategoriaAlbumBotanicoId { get; set; }
    }

    public sealed class ResolverJerarquiaAlbumRequest
    {
        public string Etapa { get; set; } = "ANALIZADOR";
        public int? CategoriaAlbumBotanicoId { get; set; }
        public int? SubcategoriaAlbumBotanicoId { get; set; }
        public int? AlbumBotanicoCafeId { get; set; }
        public bool ProponerCategoria { get; set; }
        public bool ProponerSubcategoria { get; set; }
        public bool ProponerFicha { get; set; }
        public string CategoriaPropuesta { get; set; } = string.Empty;
        public string SubcategoriaPropuesta { get; set; } = string.Empty;
        public string FichaPropuesta { get; set; } = string.Empty;
        public string NombreCientifico { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Sintomas { get; set; } = string.Empty;
        public string Motivo { get; set; } = string.Empty;
    }

    public sealed class SubcategoriaAlbumDto
    {
        public int SubcategoriaAlbumBotanicoId { get; set; }
        public int CategoriaAlbumBotanicoId { get; set; }
        public string Categoria { get; set; } = string.Empty;
        public string NombreSubcategoria { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public bool Activo { get; set; }
        public int TotalRegistros { get; set; }
    }

    public sealed class AlbumRegistroJerarquiaDto
    {
        public int AlbumBotanicoCafeId { get; set; }
        public int CategoriaAlbumBotanicoId { get; set; }
        public string Categoria { get; set; } = string.Empty;
        public int? SubcategoriaAlbumBotanicoId { get; set; }
        public string Subcategoria { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public string? NombreCientifico { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public bool Activo { get; set; }
    }

    public sealed class JerarquiaDiagnosticoFotoDto
    {
        public int FotografiaId { get; set; }
        public int Orden { get; set; }
        public bool TieneClasificacion { get; set; }
        public int? CategoriaAlbumBotanicoId { get; set; }
        public int? SubcategoriaAlbumBotanicoId { get; set; }
        public int? AlbumBotanicoCafeId { get; set; }
        public string Categoria { get; set; } = string.Empty;
        public string Subcategoria { get; set; } = string.Empty;
        public string Ficha { get; set; } = string.Empty;
        public string NombreCientifico { get; set; } = string.Empty;
        public string Motivo { get; set; } = string.Empty;
        public bool CategoriaEsPropuesta { get; set; }
        public bool SubcategoriaEsPropuesta { get; set; }
        public bool FichaEsPropuesta { get; set; }
        public string Estado { get; set; } = string.Empty;
    }

    public sealed class AlbumGaleriaJerarquiaFila
    {
        public int AlbumBotanicoCafeId { get; set; }
        public int CategoriaAlbumBotanicoId { get; set; }
        public string Categoria { get; set; } = string.Empty;
        public int? SubcategoriaAlbumBotanicoId { get; set; }
        public string Subcategoria { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public string? NombreCientifico { get; set; }
        public string DescripcionCorta { get; set; } = string.Empty;
        public string? FotoPortada { get; set; }
        public int TotalFotos { get; set; }
        public bool Activo { get; set; }
        public bool CategoriaActiva { get; set; }
        public bool SubcategoriaActiva { get; set; }
        public DateTime FechaCreacion { get; set; }
    }

    internal sealed class CategoriaLigera
    {
        public int CategoriaAlbumBotanicoId { get; set; }
        public string NombreCategoria { get; set; } = string.Empty;
    }
}
