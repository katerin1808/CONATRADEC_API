using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Endpoints administrativos del Álbum Botánico utilizados por las
    /// versiones nuevas de Android/Windows. Los endpoints históricos se
    /// conservan intactos para no afectar sincronización offline ni clientes
    /// anteriores.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/album-administracion")]
    public sealed class AlbumBotanicoAdministracionController : ControllerBase
    {
        private const string InterfazAlbum = "albumFotosPage";

        private readonly AlbumJerarquiaDbContext db;
        private readonly PermisoApiService permisos;

        public AlbumBotanicoAdministracionController(
            AlbumJerarquiaDbContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;
        }

        /// <summary>
        /// Devuelve en una sola solicitud los catálogos activos y la página
        /// solicitada. Se utiliza al iniciar una visita, al refrescar y después
        /// de una mutación real para reconciliar la interfaz con el servidor.
        /// </summary>
        [HttpGet("contexto")]
        public async Task<IActionResult> Contexto(
            [FromQuery] int? categoriaId = null,
            [FromQuery] int? subcategoriaId = null,
            [FromQuery] string? buscar = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 8,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            tamanoPagina = Math.Clamp(tamanoPagina, 1, 30);
            pagina = Math.Max(1, pagina);

            var categorias = await db.Categorias
                .AsNoTracking()
                .Where(item => item.Activo)
                .OrderBy(item => item.NombreCategoria)
                .ThenBy(item => item.CategoriaAlbumBotanicoId)
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

            List<SubcategoriaAdministracionDto> subcategorias =
                await db.Subcategorias
                    .AsNoTracking()
                    .Where(item => item.Activo && item.Categoria.Activo)
                    .OrderBy(item => item.Categoria.NombreCategoria)
                    .ThenBy(item => item.Titulo)
                    .ThenBy(item => item.AlbumBotanicoCafeId)
                    .Select(item => new SubcategoriaAdministracionDto
                    {
                        SubcategoriaAlbumBotanicoId =
                            item.AlbumBotanicoCafeId,
                        CategoriaAlbumBotanicoId =
                            item.CategoriaAlbumBotanicoId,
                        Categoria = item.Categoria.NombreCategoria,
                        NombreSubcategoria = item.Titulo,
                        Descripcion = item.Descripcion,
                        Activo = item.Activo,
                        TotalRegistros = item.Fotos.Count(foto => foto.Activo)
                    })
                    .ToListAsync(cancellationToken);

            HashSet<int> categoriasValidas = categorias
                .Select(item => item.categoriaAlbumBotanicoId)
                .ToHashSet();

            if (categoriaId is > 0 &&
                !categoriasValidas.Contains(categoriaId.Value))
            {
                categoriaId = null;
                subcategoriaId = null;
            }

            if (subcategoriaId is > 0)
            {
                SubcategoriaAdministracionDto? subcategoria =
                    subcategorias.FirstOrDefault(item =>
                        item.SubcategoriaAlbumBotanicoId ==
                            subcategoriaId.Value);

                if (subcategoria == null ||
                    (categoriaId is > 0 &&
                     subcategoria.CategoriaAlbumBotanicoId !=
                        categoriaId.Value))
                {
                    subcategoriaId = null;
                }
                else if (categoriaId is not > 0)
                {
                    categoriaId =
                        subcategoria.CategoriaAlbumBotanicoId;
                }
            }

            object galeria = await ConstruirPaginaActivaAsync(
                categoriaId,
                subcategoriaId,
                buscar,
                pagina,
                tamanoPagina,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Álbum botánico administrativo cargado correctamente.",
                data = new
                {
                    categorias,
                    subcategorias,
                    galeria
                }
            });
        }

        /// <summary>
        /// Devuelve exclusivamente una página activa. Las categorías y
        /// subcategorías ya permanecen en memoria durante la visita.
        /// </summary>
        [HttpGet("pagina")]
        public async Task<IActionResult> Pagina(
            [FromQuery] int? categoriaId = null,
            [FromQuery] int? subcategoriaId = null,
            [FromQuery] string? buscar = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 8,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            object data = await ConstruirPaginaActivaAsync(
                categoriaId,
                subcategoriaId,
                buscar,
                Math.Max(1, pagina),
                Math.Clamp(tamanoPagina, 1, 30),
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Página del álbum obtenida correctamente.",
                data
            });
        }

        /// <summary>
        /// Lista únicamente subcategorías/fichas desactivadas. Las categorías
        /// eliminadas continúan utilizando el flujo común de catálogos.
        /// </summary>
        [HttpGet("eliminados")]
        public async Task<IActionResult> Eliminados(
            [FromQuery] string? buscar = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 8,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Administrar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 1, 30);

            IQueryable<AlbumBotanicoCafeJerarquia> query = db.Subcategorias
                .AsNoTracking()
                .Where(item => !item.Activo);

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

            object data = await ConstruirPaginaAsync(
                query,
                pagina,
                tamanoPagina,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Subcategorías eliminadas obtenidas correctamente.",
                data
            });
        }

        /// <summary>
        /// Reactiva la misma fila persistida y conserva su identificador,
        /// información técnica, fotografías y relaciones históricas.
        /// </summary>
        [HttpPut("eliminados/{id:int}/reactivar")]
        public async Task<IActionResult> Reactivar(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso = await ValidarPermisoAsync(
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La subcategoría seleccionada no es válida."
                });
            }

            AlbumBotanicoCafeJerarquia? registro = await db.Subcategorias
                .Include(item => item.Categoria)
                .FirstOrDefaultAsync(item =>
                    item.AlbumBotanicoCafeId == id,
                    cancellationToken);

            if (registro == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "La subcategoría no fue encontrada."
                });
            }

            if (registro.Activo)
            {
                return Ok(new
                {
                    success = true,
                    message = "La subcategoría ya se encuentra activa."
                });
            }

            if (!registro.Categoria.Activo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Reactive primero la categoría del álbum a la que pertenece esta subcategoría."
                });
            }

            await using var transaccion = await db.Database
                .BeginTransactionAsync(cancellationToken);

            try
            {
                registro.Activo = true;
                await db.SaveChangesAsync(cancellationToken);
                await GarantizarPortadaActivaAsync(
                    registro.AlbumBotanicoCafeId,
                    cancellationToken);
                await transaccion.CommitAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Subcategoría reactivada correctamente."
                });
            }
            catch
            {
                await transaccion.RollbackAsync(cancellationToken);
                throw;
            }
        }

        private async Task<object> ConstruirPaginaActivaAsync(
            int? categoriaId,
            int? subcategoriaId,
            string? buscar,
            int pagina,
            int tamanoPagina,
            CancellationToken cancellationToken)
        {
            IQueryable<AlbumBotanicoCafeJerarquia> query = db.Subcategorias
                .AsNoTracking()
                .Where(item => item.Activo && item.Categoria.Activo);

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

            return await ConstruirPaginaAsync(
                query,
                pagina,
                tamanoPagina,
                cancellationToken);
        }

        private static async Task<object> ConstruirPaginaAsync(
            IQueryable<AlbumBotanicoCafeJerarquia> query,
            int pagina,
            int tamanoPagina,
            CancellationToken cancellationToken)
        {
            int totalRegistros = await query.CountAsync(cancellationToken);
            int totalPaginas = totalRegistros == 0
                ? 0
                : (int)Math.Ceiling(
                    totalRegistros / (double)tamanoPagina);

            int paginaNormalizada = totalPaginas == 0
                ? 1
                : Math.Clamp(pagina, 1, totalPaginas);

            int omitir = (paginaNormalizada - 1) * tamanoPagina;

            List<AlbumGaleriaAdministracionDto> items = await query
                .OrderBy(item => item.Categoria.NombreCategoria)
                .ThenBy(item => item.Titulo)
                .ThenBy(item => item.AlbumBotanicoCafeId)
                .Skip(omitir)
                .Take(tamanoPagina)
                .Select(item => new AlbumGaleriaAdministracionDto
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
                        .ThenBy(foto => foto.AlbumBotanicoCafeFotoId)
                        .Select(foto => foto.RutaFoto)
                        .FirstOrDefault(),
                    TotalFotos = item.Fotos.Count(foto => foto.Activo),
                    Activo = item.Activo,
                    CategoriaActiva = item.Categoria.Activo,
                    SubcategoriaActiva = item.Activo,
                    FechaCreacion = item.FechaCreacion
                })
                .ToListAsync(cancellationToken);

            return new
            {
                items,
                paginaActual = paginaNormalizada,
                tamanoPagina,
                totalRegistros,
                totalPaginas,
                tieneMas =
                    totalPaginas > 0 &&
                    paginaNormalizada < totalPaginas
            };
        }

        private async Task GarantizarPortadaActivaAsync(
            int albumBotanicoCafeId,
            CancellationToken cancellationToken)
        {
            List<AlbumBotanicoCafeFotoJerarquia> fotos = await db.FotosAlbum
                .Where(item =>
                    item.AlbumBotanicoCafeId == albumBotanicoCafeId &&
                    item.Activo)
                .OrderBy(item => item.Orden)
                .ThenBy(item => item.AlbumBotanicoCafeFotoId)
                .ToListAsync(cancellationToken);

            if (fotos.Count == 0)
                return;

            AlbumBotanicoCafeFotoJerarquia portada =
                fotos.FirstOrDefault(item => item.EsPortada) ??
                fotos[0];

            bool cambio = false;

            foreach (AlbumBotanicoCafeFotoJerarquia foto in fotos)
            {
                bool debeSerPortada =
                    foto.AlbumBotanicoCafeFotoId ==
                    portada.AlbumBotanicoCafeFotoId;

                if (foto.EsPortada == debeSerPortada)
                    continue;

                foto.EsPortada = debeSerPortada;
                cambio = true;
            }

            if (cambio)
                await db.SaveChangesAsync(cancellationToken);
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            int? usuarioId = ObtenerUsuarioId();

            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                InterfazAlbum,
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
            string texto = Request.Headers["X-Usuario-Id"].ToString();

            return int.TryParse(texto, out int usuarioId) && usuarioId > 0
                ? usuarioId
                : null;
        }

        private sealed class SubcategoriaAdministracionDto
        {
            public int SubcategoriaAlbumBotanicoId { get; init; }
            public int CategoriaAlbumBotanicoId { get; init; }
            public string Categoria { get; init; } = string.Empty;
            public string NombreSubcategoria { get; init; } = string.Empty;
            public string? Descripcion { get; init; }
            public bool Activo { get; init; }
            public int TotalRegistros { get; init; }
        }

        private sealed class AlbumGaleriaAdministracionDto
        {
            public int AlbumBotanicoCafeId { get; init; }
            public int CategoriaAlbumBotanicoId { get; init; }
            public string Categoria { get; init; } = string.Empty;
            public int? SubcategoriaAlbumBotanicoId { get; init; }
            public string Subcategoria { get; init; } = string.Empty;
            public string Titulo { get; init; } = string.Empty;
            public string? NombreCientifico { get; init; }
            public string DescripcionCorta { get; init; } = string.Empty;
            public string? FotoPortada { get; init; }
            public int TotalFotos { get; init; }
            public bool Activo { get; init; }
            public bool CategoriaActiva { get; init; }
            public bool SubcategoriaActiva { get; init; }
            public DateTime FechaCreacion { get; init; }
        }
    }
}
