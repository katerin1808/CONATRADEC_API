using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Administra el nivel intermedio del Álbum Botánico y la clasificación
    /// jerárquica de cada fotografía:
    /// Categoría -> Subcategoría -> Ficha específica.
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

        /// <summary>
        /// Carga inicial optimizada del álbum: capítulos, subcategorías y la
        /// primera página de fichas. Todo se resuelve en tres consultas SQL,
        /// sin consultas individuales por tarjeta.
        /// </summary>
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
                    totalRegistros = item.Registros.Count(registro =>
                        registro.Activo),
                    totalRegistrosActivos = item.Registros.Count(registro =>
                        registro.Activo)
                })
                .ToListAsync(cancellationToken);

            var subcategorias = await db.Subcategorias
                .AsNoTracking()
                .Where(item => item.Activo && item.Categoria.Activo)
                .OrderBy(item => item.Categoria.NombreCategoria)
                .ThenBy(item => item.NombreSubcategoria)
                .Select(item => new SubcategoriaAlbumDto
                {
                    SubcategoriaAlbumBotanicoId =
                        item.SubcategoriaAlbumBotanicoId,
                    CategoriaAlbumBotanicoId =
                        item.CategoriaAlbumBotanicoId,
                    Categoria = item.Categoria.NombreCategoria,
                    NombreSubcategoria = item.NombreSubcategoria,
                    Descripcion = item.Descripcion,
                    Activo = item.Activo,
                    TotalRegistros = item.Registros.Count(registro =>
                        registro.Activo)
                })
                .ToListAsync(cancellationToken);

            IQueryable<AlbumBotanicoCafeJerarquia> query = db.RegistrosAlbum
                .AsNoTracking()
                .Where(item =>
                    item.Activo &&
                    item.Categoria.Activo &&
                    (item.Subcategoria == null || item.Subcategoria.Activo));

            object galeria = await ConstruirPaginaGaleriaAsync(
                query,
                pagina: 1,
                tamanoPagina,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Álbum botánico jerárquico cargado correctamente.",
                data = new
                {
                    categorias,
                    subcategorias,
                    galeria
                }
            });
        }

        /// <summary>
        /// Página filtrada por categoría y subcategoría. La paginación se
        /// ejecuta en SQL Server y solo devuelve la fotografía de portada.
        /// </summary>
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

            IQueryable<AlbumBotanicoCafeJerarquia> query = db.RegistrosAlbum
                .AsNoTracking();

            if (!incluirInactivos)
            {
                query = query.Where(item =>
                    item.Activo &&
                    item.Categoria.Activo &&
                    (item.Subcategoria == null || item.Subcategoria.Activo));
            }

            if (categoriaId is > 0)
            {
                query = query.Where(item =>
                    item.CategoriaAlbumBotanicoId == categoriaId.Value);
            }

            if (subcategoriaId is > 0)
            {
                query = query.Where(item =>
                    item.SubcategoriaAlbumBotanicoId ==
                        subcategoriaId.Value);
            }

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();

                query = query.Where(item =>
                    item.Titulo.Contains(texto) ||
                    (item.NombreCientifico != null &&
                     item.NombreCientifico.Contains(texto)) ||
                    item.Descripcion.Contains(texto) ||
                    (item.Subcategoria != null &&
                     item.Subcategoria.NombreSubcategoria.Contains(texto)) ||
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
                message = "Página jerárquica del álbum obtenida correctamente.",
                data
            });
        }

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

            IQueryable<SubcategoriaAlbumBotanico> query = db.Subcategorias
                .AsNoTracking();

            if (categoriaId.HasValue && categoriaId.Value > 0)
            {
                query = query.Where(item =>
                    item.CategoriaAlbumBotanicoId == categoriaId.Value);
            }

            if (!incluirInactivas)
            {
                query = query.Where(item =>
                    item.Activo && item.Categoria.Activo);
            }

            var data = await query
                .OrderBy(item => item.Categoria.NombreCategoria)
                .ThenBy(item => item.NombreSubcategoria)
                .Select(item => new SubcategoriaAlbumDto
                {
                    SubcategoriaAlbumBotanicoId =
                        item.SubcategoriaAlbumBotanicoId,
                    CategoriaAlbumBotanicoId =
                        item.CategoriaAlbumBotanicoId,
                    Categoria = item.Categoria.NombreCategoria,
                    NombreSubcategoria = item.NombreSubcategoria,
                    Descripcion = item.Descripcion,
                    Activo = item.Activo,
                    TotalRegistros = item.Registros.Count(registro =>
                        incluirInactivas || registro.Activo)
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Subcategorías obtenidas correctamente.",
                data
            });
        }

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

            string nombre = Limpiar(request.NombreSubcategoria, 120);
            string? descripcion = LimpiarOpcional(request.Descripcion, 600);

            if (nombre.Length < 3)
                return ErrorValidacion("Ingrese un nombre de subcategoría válido.");

            bool duplicada = await db.Subcategorias.AnyAsync(item =>
                item.CategoriaAlbumBotanicoId ==
                    categoria.CategoriaAlbumBotanicoId &&
                item.NombreSubcategoria == nombre,
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

            var entidad = new SubcategoriaAlbumBotanico
            {
                CategoriaAlbumBotanicoId = categoria.CategoriaAlbumBotanicoId,
                NombreSubcategoria = nombre,
                Descripcion = descripcion,
                Activo = true,
                FechaCreacionUtc = DateTime.UtcNow
            };

            db.Subcategorias.Add(entidad);
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Subcategoría creada correctamente.",
                data = new SubcategoriaAlbumDto
                {
                    SubcategoriaAlbumBotanicoId =
                        entidad.SubcategoriaAlbumBotanicoId,
                    CategoriaAlbumBotanicoId =
                        entidad.CategoriaAlbumBotanicoId,
                    Categoria = categoria.NombreCategoria,
                    NombreSubcategoria = entidad.NombreSubcategoria,
                    Descripcion = entidad.Descripcion,
                    Activo = entidad.Activo,
                    TotalRegistros = 0
                }
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

            SubcategoriaAlbumBotanico? entidad = await db.Subcategorias
                .FirstOrDefaultAsync(item =>
                    item.SubcategoriaAlbumBotanicoId == id,
                    cancellationToken);

            if (entidad == null)
                return NotFound(new { success = false, message = "La subcategoría no fue encontrada." });

            bool categoriaValida = await db.Categorias.AnyAsync(item =>
                item.CategoriaAlbumBotanicoId ==
                    request.CategoriaAlbumBotanicoId &&
                item.Activo,
                cancellationToken);

            if (!categoriaValida)
                return ErrorValidacion("La categoría no existe o está inactiva.");

            string nombre = Limpiar(request.NombreSubcategoria, 120);

            if (nombre.Length < 3)
                return ErrorValidacion("Ingrese un nombre de subcategoría válido.");

            bool duplicada = await db.Subcategorias.AnyAsync(item =>
                item.SubcategoriaAlbumBotanicoId != id &&
                item.CategoriaAlbumBotanicoId ==
                    request.CategoriaAlbumBotanicoId &&
                item.NombreSubcategoria == nombre,
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
            entidad.NombreSubcategoria = nombre;
            entidad.Descripcion = LimpiarOpcional(request.Descripcion, 600);
            entidad.FechaActualizacionUtc = DateTime.UtcNow;

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

            SubcategoriaAlbumBotanico? entidad = await db.Subcategorias
                .FirstOrDefaultAsync(item =>
                    item.SubcategoriaAlbumBotanicoId == id,
                    cancellationToken);

            if (entidad == null)
                return NotFound(new { success = false, message = "La subcategoría no fue encontrada." });

            if (!activo)
            {
                bool tieneRegistrosActivos = await db.RegistrosAlbum.AnyAsync(
                    item =>
                        item.SubcategoriaAlbumBotanicoId == id &&
                        item.Activo,
                    cancellationToken);

                if (tieneRegistrosActivos)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "No puede desactivar la subcategoría mientras existan fichas activas asociadas."
                    });
                }
            }

            entidad.Activo = activo;
            entidad.FechaActualizacionUtc = DateTime.UtcNow;
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

            IQueryable<AlbumBotanicoCafeJerarquia> query = db.RegistrosAlbum
                .AsNoTracking();

            if (identificadores.Count > 0)
            {
                query = query.Where(item =>
                    identificadores.Contains(item.AlbumBotanicoCafeId));
            }

            if (categoriaId.HasValue && categoriaId.Value > 0)
            {
                query = query.Where(item =>
                    item.CategoriaAlbumBotanicoId == categoriaId.Value);
            }

            if (subcategoriaId.HasValue && subcategoriaId.Value > 0)
            {
                query = query.Where(item =>
                    item.SubcategoriaAlbumBotanicoId == subcategoriaId.Value);
            }

            if (!incluirInactivos)
            {
                query = query.Where(item =>
                    item.Activo &&
                    item.Categoria.Activo &&
                    (item.Subcategoria == null || item.Subcategoria.Activo));
            }

            var data = await query
                .OrderBy(item => item.Categoria.NombreCategoria)
                .ThenBy(item => item.Subcategoria != null
                    ? item.Subcategoria.NombreSubcategoria
                    : string.Empty)
                .ThenBy(item => item.Titulo)
                .Select(item => new AlbumRegistroJerarquiaDto
                {
                    AlbumBotanicoCafeId = item.AlbumBotanicoCafeId,
                    CategoriaAlbumBotanicoId = item.CategoriaAlbumBotanicoId,
                    Categoria = item.Categoria.NombreCategoria,
                    SubcategoriaAlbumBotanicoId =
                        item.SubcategoriaAlbumBotanicoId,
                    Subcategoria = item.Subcategoria != null
                        ? item.Subcategoria.NombreSubcategoria
                        : string.Empty,
                    Titulo = item.Titulo,
                    NombreCientifico = item.NombreCientifico,
                    Descripcion = item.Descripcion,
                    Activo = item.Activo
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Jerarquía de las fichas obtenida correctamente.",
                data
            });
        }

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

            AlbumBotanicoCafeJerarquia? registro = await db.RegistrosAlbum
                .FirstOrDefaultAsync(item =>
                    item.AlbumBotanicoCafeId == id,
                    cancellationToken);

            if (registro == null)
                return NotFound(new { success = false, message = "La ficha no fue encontrada." });

            SubcategoriaAlbumBotanico? subcategoria = await db.Subcategorias
                .AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.SubcategoriaAlbumBotanicoId ==
                        request.SubcategoriaAlbumBotanicoId &&
                    item.Activo,
                    cancellationToken);

            if (subcategoria == null)
                return ErrorValidacion("La subcategoría no existe o está inactiva.");

            if (subcategoria.CategoriaAlbumBotanicoId !=
                registro.CategoriaAlbumBotanicoId)
            {
                return ErrorValidacion(
                    "La subcategoría seleccionada no pertenece a la categoría de la ficha.");
            }

            registro.SubcategoriaAlbumBotanicoId =
                subcategoria.SubcategoriaAlbumBotanicoId;

            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Subcategoría asignada correctamente."
            });
        }

        [HttpGet("diagnosticos/{diagnosticoId:int}")]
        public async Task<IActionResult> ObtenerJerarquiaDiagnostico(
            int diagnosticoId,
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
                return NotFound(new { success = false, message = "La inspección no fue encontrada." });

            List<CategoriaLigera> categorias = await db.Categorias
                .AsNoTracking()
                .Where(item => item.Activo)
                .Select(item => new CategoriaLigera
                {
                    CategoriaAlbumBotanicoId = item.CategoriaAlbumBotanicoId,
                    NombreCategoria = item.NombreCategoria
                })
                .ToListAsync(cancellationToken);

            List<SubcategoriaLigera> subcategorias = await db.Subcategorias
                .AsNoTracking()
                .Where(item => item.Activo && item.Categoria.Activo)
                .Select(item => new SubcategoriaLigera
                {
                    SubcategoriaAlbumBotanicoId =
                        item.SubcategoriaAlbumBotanicoId,
                    CategoriaAlbumBotanicoId =
                        item.CategoriaAlbumBotanicoId,
                    NombreSubcategoria = item.NombreSubcategoria
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

            var idsFicha = filas
                .Where(item => item.Resultado != null)
                .SelectMany(item => new int?[]
                {
                    item.Jerarquia?.AlbumBotanicoCafeIdSeleccionado,
                    item.Jerarquia?.AlbumBotanicoCafeIdSugerido,
                    item.Resultado!.AlbumBotanicoCafeIdSeleccionado,
                    item.Resultado!.AlbumBotanicoCafeIdSugerido
                })
                .Where(item => item.HasValue && item.Value > 0)
                .Select(item => item!.Value)
                .Distinct()
                .ToList();

            Dictionary<int, AlbumRegistroJerarquiaDto> fichas =
                await db.RegistrosAlbum
                    .AsNoTracking()
                    .Where(item => idsFicha.Contains(item.AlbumBotanicoCafeId))
                    .Select(item => new AlbumRegistroJerarquiaDto
                    {
                        AlbumBotanicoCafeId = item.AlbumBotanicoCafeId,
                        CategoriaAlbumBotanicoId = item.CategoriaAlbumBotanicoId,
                        Categoria = item.Categoria.NombreCategoria,
                        SubcategoriaAlbumBotanicoId =
                            item.SubcategoriaAlbumBotanicoId,
                        Subcategoria = item.Subcategoria != null
                            ? item.Subcategoria.NombreSubcategoria
                            : string.Empty,
                        Titulo = item.Titulo,
                        NombreCientifico = item.NombreCientifico,
                        Descripcion = item.Descripcion,
                        Activo = item.Activo
                    })
                    .ToDictionaryAsync(
                        item => item.AlbumBotanicoCafeId,
                        cancellationToken);

            var data = filas.Select(item =>
                ConstruirJerarquiaFoto(
                    item.Foto,
                    item.Resultado,
                    item.Jerarquia,
                    categorias,
                    subcategorias,
                    fichas))
                .ToList();

            return Ok(new
            {
                success = true,
                message = "Clasificación jerárquica obtenida correctamente.",
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

            bool estadoPermitido = esAprobador
                ? string.Equals(
                    diagnostico.Estado,
                    "PENDIENTE_APROBACION",
                    StringComparison.OrdinalIgnoreCase)
                : diagnostico.Estado is
                    "PENDIENTE_ANALIZADOR" or
                    "EN_ANALISIS_HUMANO" or
                    "DEVUELTO_PARA_CORRECCION" or
                    "DEVUELTA_AL_ANALIZADOR";

            if (!estadoPermitido)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "La clasificación jerárquica no puede modificarse en el estado actual de la inspección."
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

            string motivo = Limpiar(request.Motivo, 1200);

            if (!esAprobador && motivo.Length < 8)
            {
                return ErrorValidacion(
                    "Explique por qué la jerarquía seleccionada representa la fotografía.");
            }

            CategoriaAlbumJerarquia? categoria = null;
            SubcategoriaAlbumBotanico? subcategoria = null;
            AlbumBotanicoCafeJerarquia? ficha = null;

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

            string categoriaPropuesta =
                Limpiar(request.CategoriaPropuesta, 100);

            if (categoria == null)
            {
                if (!request.ProponerCategoria || categoriaPropuesta.Length < 3)
                {
                    return ErrorValidacion(
                        "Seleccione una categoría existente o proponga una nueva.");
                }

                if (esAprobador)
                {
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
            }

            int? categoriaIdFinal = categoria?.CategoriaAlbumBotanicoId;
            string categoriaNombreFinal = categoria?.NombreCategoria
                ?? categoriaPropuesta;

            if (request.SubcategoriaAlbumBotanicoId is > 0)
            {
                if (!categoriaIdFinal.HasValue)
                {
                    return ErrorValidacion(
                        "Una subcategoría existente necesita una categoría existente.");
                }

                subcategoria = await db.Subcategorias.FirstOrDefaultAsync(item =>
                    item.SubcategoriaAlbumBotanicoId ==
                        request.SubcategoriaAlbumBotanicoId.Value &&
                    item.CategoriaAlbumBotanicoId == categoriaIdFinal.Value &&
                    item.Activo,
                    cancellationToken);

                if (subcategoria == null)
                {
                    return ErrorValidacion(
                        "La subcategoría seleccionada no pertenece a la categoría o está inactiva.");
                }
            }

            string subcategoriaPropuesta =
                Limpiar(request.SubcategoriaPropuesta, 120);

            if (subcategoria == null)
            {
                if (!request.ProponerSubcategoria ||
                    subcategoriaPropuesta.Length < 3)
                {
                    return ErrorValidacion(
                        "Seleccione una subcategoría existente o proponga una nueva.");
                }

                if (esAprobador)
                {
                    if (!categoriaIdFinal.HasValue)
                    {
                        return ErrorValidacion(
                            "La nueva subcategoría necesita una categoría oficial.");
                    }

                    IActionResult? permisoAlbum = await ValidarPermisoAsync(
                        InterfazAlbum,
                        TipoPermisoApi.Agregar,
                        cancellationToken);

                    if (permisoAlbum != null)
                        return permisoAlbum;

                    subcategoria = await db.Subcategorias.FirstOrDefaultAsync(item =>
                        item.CategoriaAlbumBotanicoId == categoriaIdFinal.Value &&
                        item.NombreSubcategoria == subcategoriaPropuesta,
                        cancellationToken);

                    if (subcategoria == null)
                    {
                        subcategoria = new SubcategoriaAlbumBotanico
                        {
                            CategoriaAlbumBotanicoId = categoriaIdFinal.Value,
                            NombreSubcategoria = subcategoriaPropuesta,
                            Descripcion = LimpiarOpcional(motivo, 600),
                            Activo = true,
                            FechaCreacionUtc = DateTime.UtcNow
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
            }

            int? subcategoriaIdFinal =
                subcategoria?.SubcategoriaAlbumBotanicoId;
            string subcategoriaNombreFinal =
                subcategoria?.NombreSubcategoria ?? subcategoriaPropuesta;

            if (request.AlbumBotanicoCafeId is > 0)
            {
                ficha = await db.RegistrosAlbum.FirstOrDefaultAsync(item =>
                    item.AlbumBotanicoCafeId ==
                        request.AlbumBotanicoCafeId.Value &&
                    item.Activo,
                    cancellationToken);

                if (ficha == null)
                    return ErrorValidacion("La ficha seleccionada no existe o está inactiva.");

                if (categoriaIdFinal.HasValue &&
                    ficha.CategoriaAlbumBotanicoId != categoriaIdFinal.Value)
                {
                    return ErrorValidacion(
                        "La ficha seleccionada no pertenece a la categoría indicada.");
                }

                if (subcategoriaIdFinal.HasValue &&
                    ficha.SubcategoriaAlbumBotanicoId != subcategoriaIdFinal.Value)
                {
                    return ErrorValidacion(
                        "La ficha seleccionada no pertenece a la subcategoría indicada.");
                }

                categoria ??= await db.Categorias.FirstAsync(item =>
                    item.CategoriaAlbumBotanicoId ==
                        ficha.CategoriaAlbumBotanicoId,
                    cancellationToken);

                categoriaIdFinal = ficha.CategoriaAlbumBotanicoId;
                categoriaNombreFinal = categoria.NombreCategoria;

                if (ficha.SubcategoriaAlbumBotanicoId is > 0)
                {
                    subcategoria ??= await db.Subcategorias.FirstAsync(item =>
                        item.SubcategoriaAlbumBotanicoId ==
                            ficha.SubcategoriaAlbumBotanicoId.Value,
                        cancellationToken);

                    subcategoriaIdFinal =
                        subcategoria.SubcategoriaAlbumBotanicoId;
                    subcategoriaNombreFinal =
                        subcategoria.NombreSubcategoria;
                }
            }

            string fichaPropuesta = Limpiar(request.FichaPropuesta, 200);
            string nombreCientifico =
                Limpiar(request.NombreCientifico, 200);
            string descripcion = Limpiar(request.Descripcion, 4000);

            if (ficha == null)
            {
                if (!request.ProponerFicha || fichaPropuesta.Length < 3)
                {
                    return ErrorValidacion(
                        "Seleccione una ficha existente o proponga una nueva.");
                }

                if (esAprobador)
                {
                    if (!categoriaIdFinal.HasValue ||
                        !subcategoriaIdFinal.HasValue)
                    {
                        return ErrorValidacion(
                            "La nueva ficha necesita una categoría y subcategoría oficiales.");
                    }

                    if (descripcion.Length < 8)
                    {
                        return ErrorValidacion(
                            "Ingrese una descripción válida para la nueva ficha.");
                    }

                    IActionResult? permisoAlbum = await ValidarPermisoAsync(
                        InterfazAlbum,
                        TipoPermisoApi.Agregar,
                        cancellationToken);

                    if (permisoAlbum != null)
                        return permisoAlbum;

                    ficha = await db.RegistrosAlbum.FirstOrDefaultAsync(item =>
                        item.CategoriaAlbumBotanicoId == categoriaIdFinal.Value &&
                        item.SubcategoriaAlbumBotanicoId ==
                            subcategoriaIdFinal.Value &&
                        item.Titulo == fichaPropuesta &&
                        item.Activo,
                        cancellationToken);

                    if (ficha == null)
                    {
                        ficha = new AlbumBotanicoCafeJerarquia
                        {
                            CategoriaAlbumBotanicoId = categoriaIdFinal.Value,
                            SubcategoriaAlbumBotanicoId =
                                subcategoriaIdFinal.Value,
                            Titulo = fichaPropuesta,
                            NombreCientifico =
                                LimpiarOpcional(nombreCientifico, 200),
                            Descripcion = descripcion,
                            Sintomas = LimpiarOpcional(request.Sintomas, 4000),
                            Observaciones =
                                "Ficha creada desde una clasificación jerárquica aprobada.",
                            Activo = true,
                            FechaCreacion = DateTime.Now
                        };

                        db.RegistrosAlbum.Add(ficha);
                        await db.SaveChangesAsync(cancellationToken);
                    }
                }
            }

            int? fichaIdFinal = ficha?.AlbumBotanicoCafeId;
            string fichaNombreFinal = ficha?.Titulo ?? fichaPropuesta;
            string cientificoFinal = PrimerTexto(
                ficha?.NombreCientifico,
                nombreCientifico,
                foto.ResultadoIA.NombreCientificoSugerido);

            bool nivelesExistentes =
                categoriaIdFinal.HasValue &&
                subcategoriaIdFinal.HasValue &&
                fichaIdFinal.HasValue;

            if (!esAprobador)
            {
                jerarquia.CategoriaAlbumBotanicoIdSugerida =
                    categoriaIdFinal;
                jerarquia.SubcategoriaAlbumBotanicoIdSugerida =
                    subcategoriaIdFinal;
                jerarquia.AlbumBotanicoCafeIdSugerido = fichaIdFinal;
                jerarquia.CategoriaSugerida = categoriaNombreFinal;
                jerarquia.SubcategoriaSugerida = subcategoriaNombreFinal;
                jerarquia.FichaSugerida = fichaNombreFinal;
                jerarquia.NombreCientificoSugerido = cientificoFinal;
                jerarquia.MotivoSugerencia = motivo;
                jerarquia.ProponeCategoria = !categoriaIdFinal.HasValue;
                jerarquia.ProponeSubcategoria = !subcategoriaIdFinal.HasValue;
                jerarquia.ProponeFicha = !fichaIdFinal.HasValue;
                jerarquia.Estado = nivelesExistentes
                    ? "RESUELTA_ANALIZADOR"
                    : "PROPUESTA_ANALIZADOR";

                DiagnosticoIAImagenResultadoJerarquiaReferencia resultado =
                    foto.ResultadoIA;

                resultado.CategoriaAlbumBotanicoIdSugerida =
                    categoriaIdFinal;
                resultado.AlbumBotanicoCafeIdSugerido = fichaIdFinal;
                resultado.CategoriaAlbumSugerida = categoriaNombreFinal;
                resultado.ClasificacionAlbumSugerida = fichaNombreFinal;
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
                    subcategoria == null ||
                    ficha == null)
                {
                    return ErrorValidacion(
                        "El aprobador debe dejar una categoría, subcategoría y ficha oficiales seleccionadas o creadas.");
                }

                jerarquia.CategoriaAlbumBotanicoIdSeleccionada =
                    categoria.CategoriaAlbumBotanicoId;
                jerarquia.SubcategoriaAlbumBotanicoIdSeleccionada =
                    subcategoria.SubcategoriaAlbumBotanicoId;
                jerarquia.AlbumBotanicoCafeIdSeleccionado =
                    ficha.AlbumBotanicoCafeId;
                jerarquia.CategoriaSeleccionada = categoria.NombreCategoria;
                jerarquia.SubcategoriaSeleccionada =
                    subcategoria.NombreSubcategoria;
                jerarquia.FichaSeleccionada = ficha.Titulo;
                jerarquia.ProponeCategoria = false;
                jerarquia.ProponeSubcategoria = false;
                jerarquia.ProponeFicha = false;
                jerarquia.Estado = "RESUELTA_APROBADOR";

                DiagnosticoIAImagenResultadoJerarquiaReferencia resultado =
                    foto.ResultadoIA;

                resultado.CategoriaAlbumBotanicoIdSeleccionada =
                    categoria.CategoriaAlbumBotanicoId;
                resultado.AlbumBotanicoCafeIdSeleccionado =
                    ficha.AlbumBotanicoCafeId;
                resultado.CategoriaAlbumSeleccionada =
                    categoria.NombreCategoria;
                resultado.ClasificacionAlbumSeleccionada = ficha.Titulo;
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
                    ? "La clasificación jerárquica quedó resuelta y vinculada con el Álbum Botánico."
                    : nivelesExistentes
                        ? "La fotografía quedó vinculada con una jerarquía existente del Álbum Botánico."
                        : "La propuesta jerárquica fue guardada para decisión del aprobador."
            });
        }

        private JerarquiaDiagnosticoFotoDto ConstruirJerarquiaFoto(
            DiagnosticoIAImagenJerarquiaReferencia foto,
            DiagnosticoIAImagenResultadoJerarquiaReferencia? resultado,
            DiagnosticoIAClasificacionJerarquia? guardada,
            IReadOnlyCollection<CategoriaLigera> categorias,
            IReadOnlyCollection<SubcategoriaLigera> subcategorias,
            IReadOnlyDictionary<int, AlbumRegistroJerarquiaDto> fichas)
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

            int? fichaId =
                guardada?.AlbumBotanicoCafeIdSeleccionado ??
                guardada?.AlbumBotanicoCafeIdSugerido ??
                resultado.AlbumBotanicoCafeIdSeleccionado ??
                resultado.AlbumBotanicoCafeIdSugerido;

            fichas.TryGetValue(
                fichaId ?? 0,
                out AlbumRegistroJerarquiaDto? fichaExistente);

            string categoria = PrimerTexto(
                guardada?.CategoriaSeleccionada,
                guardada?.CategoriaSugerida,
                fichaExistente?.Categoria,
                resultado.CategoriaAlbumSeleccionada,
                resultado.CategoriaAlbumSugerida,
                MapearCategoria(resultado));

            int? categoriaId =
                guardada?.CategoriaAlbumBotanicoIdSeleccionada ??
                guardada?.CategoriaAlbumBotanicoIdSugerida ??
                fichaExistente?.CategoriaAlbumBotanicoId ??
                resultado.CategoriaAlbumBotanicoIdSeleccionada ??
                resultado.CategoriaAlbumBotanicoIdSugerida ??
                BuscarCategoriaId(categoria, categorias);

            string subcategoria = PrimerTexto(
                guardada?.SubcategoriaSeleccionada,
                guardada?.SubcategoriaSugerida,
                fichaExistente?.Subcategoria,
                SugerirSubcategoria(resultado, foto.TipoFotografia));

            int? subcategoriaId =
                guardada?.SubcategoriaAlbumBotanicoIdSeleccionada ??
                guardada?.SubcategoriaAlbumBotanicoIdSugerida ??
                fichaExistente?.SubcategoriaAlbumBotanicoId ??
                BuscarSubcategoriaId(
                    categoriaId,
                    subcategoria,
                    subcategorias);

            string ficha = PrimerTexto(
                guardada?.FichaSeleccionada,
                guardada?.FichaSugerida,
                fichaExistente?.Titulo,
                resultado.ClasificacionAlbumSeleccionada,
                resultado.ClasificacionAlbumSugerida,
                resultado.DiagnosticoProbable);

            string cientifico = PrimerTexto(
                guardada?.NombreCientificoSugerido,
                fichaExistente?.NombreCientifico,
                resultado.NombreCientificoSugerido,
                ExtraerNombreCientifico(resultado.DiagnosticoProbable));

            bool categoriaExiste = categoriaId.HasValue;
            bool subcategoriaExiste = subcategoriaId.HasValue;
            bool fichaExiste = fichaExistente != null;

            string motivo = PrimerTexto(
                guardada?.MotivoSugerencia,
                resultado.MotivoClasificacionAlbum,
                ConstruirMotivo(
                    categoriaExiste,
                    subcategoriaExiste,
                    fichaExiste,
                    categoria,
                    subcategoria,
                    ficha));

            return new JerarquiaDiagnosticoFotoDto
            {
                FotografiaId = foto.DiagnosticoIAImagenId,
                Orden = foto.Orden,
                TieneClasificacion =
                    resultado.ImagenValida && resultado.ParecePlantaCafe,
                CategoriaAlbumBotanicoId = categoriaId,
                SubcategoriaAlbumBotanicoId = subcategoriaId,
                AlbumBotanicoCafeId = fichaExistente?.AlbumBotanicoCafeId,
                Categoria = categoria,
                Subcategoria = subcategoria,
                Ficha = ficha,
                NombreCientifico = cientifico,
                Motivo = motivo,
                CategoriaEsPropuesta = !categoriaExiste,
                SubcategoriaEsPropuesta = !subcategoriaExiste,
                FichaEsPropuesta = !fichaExiste,
                Estado = guardada?.Estado ??
                    (fichaExiste && subcategoriaExiste
                        ? "COINCIDENCIA_CATALOGO"
                        : "SUGERIDA_IA")
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

        private static string SugerirSubcategoria(
            DiagnosticoIAImagenResultadoJerarquiaReferencia resultado,
            string tipoFotografia)
        {
            string texto = NormalizarComparacion(
                $"{resultado.CategoriaPrincipal} " +
                $"{resultado.TipoDiagnostico} " +
                $"{resultado.DiagnosticoProbable} " +
                $"{resultado.CategoriasSecundariasJson}");

            string categoria = MapearCategoria(resultado);

            if (EsIgual(categoria, "Plagas"))
            {
                if (ContieneAlguno(texto, "ACARO", "ARANITA", "TARSONEM"))
                    return "Ácaros";

                if (ContieneAlguno(texto, "NEMATOD"))
                    return "Nematodos";

                if (ContieneAlguno(texto, "BABOSA", "CARACOL", "MOLUSC"))
                    return "Moluscos";

                if (ContieneAlguno(
                        texto,
                        "INSECT",
                        "MINADOR",
                        "BROCA",
                        "COCHINILLA",
                        "PULGON",
                        "TRIPS",
                        "MOSCA",
                        "GUSANO",
                        "LARVA",
                        "ESCARABAJO"))
                {
                    return "Insectos";
                }

                return "Otras plagas";
            }

            if (EsIgual(categoria, "Enfermedades"))
            {
                if (ContieneAlguno(
                        texto,
                        "HONGO",
                        "FUNG",
                        "ROYA",
                        "CERCOSPORA",
                        "MANCHA DE HIERRO",
                        "OJO DE GALLO",
                        "ANTRACNOSIS",
                        "PHOMA",
                        "MYCENA"))
                {
                    return "Enfermedades fúngicas";
                }

                if (ContieneAlguno(texto, "BACTER"))
                    return "Enfermedades bacterianas";

                if (ContieneAlguno(texto, "VIRUS", "VIRAL"))
                    return "Enfermedades virales";

                return "Otras enfermedades";
            }

            if (EsIgual(categoria, "Alteraciones nutricionales"))
            {
                if (ContieneAlguno(
                        texto,
                        "NITROGEN",
                        "FOSFOR",
                        "POTAS",
                        "CALCIO",
                        "MAGNES",
                        "AZUFRE"))
                {
                    return "Deficiencias de macronutrientes";
                }

                if (ContieneAlguno(
                        texto,
                        "HIERRO",
                        "ZINC",
                        "BORO",
                        "MANGAN",
                        "COBRE",
                        "MOLIBD"))
                {
                    return "Deficiencias de micronutrientes";
                }

                return "Otras alteraciones nutricionales";
            }

            if (EsIgual(categoria, "Estrés abiótico"))
            {
                if (ContieneAlguno(texto, "AGUA", "SEQUIA", "HIDRIC", "ENCHARCAM"))
                    return "Estrés hídrico";

                if (ContieneAlguno(texto, "CALOR", "FRIO", "TEMPERAT"))
                    return "Estrés térmico";

                if (ContieneAlguno(texto, "HERBIC", "QUIMIC", "FITOTOX"))
                    return "Daño químico";

                return "Otros daños no bióticos";
            }

            if (EsIgual(categoria, "Plantas sanas"))
            {
                string parte = NormalizarComparacion(
                    $"{resultado.PartePlanta} {tipoFotografia}");

                if (parte.Contains("HOJA"))
                    return "Hojas sanas";
                if (parte.Contains("FRUTO"))
                    return "Frutos sanos";
                if (parte.Contains("TALLO") || parte.Contains("RAMA"))
                    return "Tallos y ramas sanas";

                return "Planta completa";
            }

            return PrimerTexto(
                resultado.TipoDiagnostico.Replace('_', ' '),
                "Sin subcategoría definida");
        }

        private static string ConstruirMotivo(
            bool categoriaExiste,
            bool subcategoriaExiste,
            bool fichaExiste,
            string categoria,
            string subcategoria,
            string ficha)
        {
            if (!categoriaExiste)
            {
                return $"No existe una categoría activa compatible. Se propone crear {categoria}, luego {subcategoria} y la ficha {ficha}.";
            }

            if (!subcategoriaExiste)
            {
                return $"La categoría {categoria} existe, pero no hay una subcategoría activa compatible. Se propone crear {subcategoria} y después la ficha {ficha}.";
            }

            if (!fichaExiste)
            {
                return $"La categoría {categoria} y la subcategoría {subcategoria} existen, pero no hay una ficha compatible. Se propone crear {ficha}.";
            }

            return $"La fotografía coincide con {categoria} → {subcategoria} → {ficha}.";
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
            IEnumerable<SubcategoriaLigera> subcategorias)
        {
            if (!categoriaId.HasValue)
                return null;

            string buscado = NormalizarComparacion(nombre);

            foreach (SubcategoriaLigera subcategoria in subcategorias)
            {
                if (subcategoria.CategoriaAlbumBotanicoId == categoriaId.Value &&
                    NormalizarComparacion(subcategoria.NombreSubcategoria) == buscado)
                {
                    return subcategoria.SubcategoriaAlbumBotanicoId;
                }
            }

            return null;
        }

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
                .ThenBy(item => item.Subcategoria != null
                    ? item.Subcategoria.NombreSubcategoria
                    : string.Empty)
                .ThenBy(item => item.Titulo)
                .Skip(omitir)
                .Take(tamanoPagina)
                .Select(item => new AlbumGaleriaJerarquiaFila
                {
                    AlbumBotanicoCafeId = item.AlbumBotanicoCafeId,
                    CategoriaAlbumBotanicoId =
                        item.CategoriaAlbumBotanicoId,
                    Categoria = item.Categoria.NombreCategoria,
                    SubcategoriaAlbumBotanicoId =
                        item.SubcategoriaAlbumBotanicoId,
                    Subcategoria = item.Subcategoria != null
                        ? item.Subcategoria.NombreSubcategoria
                        : string.Empty,
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
                    SubcategoriaActiva = item.Subcategoria == null ||
                        item.Subcategoria.Activo,
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
                        "No tiene permiso para consultar esta clasificación jerárquica."
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

        private static bool ContieneAlguno(
            string texto,
            params string[] valores) =>
            valores.Any(valor => texto.Contains(
                NormalizarComparacion(valor),
                StringComparison.Ordinal));

        private static bool EsIgual(string izquierda, string derecha) =>
            string.Equals(
                NormalizarComparacion(izquierda),
                NormalizarComparacion(derecha),
                StringComparison.Ordinal);

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

    internal sealed class SubcategoriaLigera
    {
        public int SubcategoriaAlbumBotanicoId { get; set; }
        public int CategoriaAlbumBotanicoId { get; set; }
        public string NombreSubcategoria { get; set; } = string.Empty;
    }

}
