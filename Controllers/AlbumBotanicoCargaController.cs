using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Endpoints livianos para la pantalla principal del álbum botánico.
    /// Mantiene el controlador anterior sin cambios y agrega carga inicial
    /// mínima y paginación real desde SQL Server.
    /// </summary>
    [ApiController]
    [Route("api/album-botanico")]
    public sealed class AlbumBotanicoCargaController : ControllerBase
    {
        private readonly DBContext context;

        public AlbumBotanicoCargaController(DBContext context)
        {
            this.context = context;
        }

        /// <summary>
        /// Devuelve en una sola solicitud las categorías activas y la primera
        /// página de registros activos. No carga inactivos, detalles ni todas
        /// las fotografías del registro.
        /// </summary>
        [HttpGet("inicio")]
        public async Task<ActionResult> Inicio(
            [FromQuery] int tamanoPagina = 6,
            CancellationToken cancellationToken = default)
        {
            tamanoPagina = NormalizarTamanoPagina(tamanoPagina);

            var categorias = await context.CategoriasAlbumBotanico
                .AsNoTracking()
                .Where(x => x.activo)
                .OrderBy(x => x.nombreCategoria)
                .Select(x => new
                {
                    x.categoriaAlbumBotanicoId,
                    x.nombreCategoria,
                    x.descripcion,
                    x.rutaImagenPortada,
                    x.activo,
                    totalRegistros = x.Registros.Count(r => r.activo),
                    totalRegistrosActivos = x.Registros.Count(r => r.activo)
                })
                .ToListAsync(cancellationToken);

            IQueryable<AlbumBotanicoCafe> query = context
                .AlbumesBotanicosCafe
                .AsNoTracking()
                .Where(x => x.activo && x.Categoria.activo);

            int totalRegistros = await query.CountAsync(cancellationToken);

            var items = await ProyectarGaleria(query)
                .Take(tamanoPagina)
                .ToListAsync(cancellationToken);

            object galeria = CrearPagina(
                items,
                paginaActual: 1,
                tamanoPagina: tamanoPagina,
                totalRegistros: totalRegistros);

            return Ok(new
            {
                success = true,
                message = "Álbum botánico cargado correctamente.",
                data = new
                {
                    categorias,
                    galeria
                }
            });
        }

        /// <summary>
        /// Devuelve solamente una página. Se usa al desplazarse, buscar,
        /// seleccionar una categoría o solicitar registros inactivos.
        /// </summary>
        [HttpGet("galeria-paginada")]
        public async Task<ActionResult> GaleriaPaginada(
            [FromQuery] int? categoriaId = null,
            [FromQuery] string? buscar = null,
            [FromQuery] bool incluirInactivos = false,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 6,
            CancellationToken cancellationToken = default)
        {
            pagina = Math.Max(1, pagina);
            tamanoPagina = NormalizarTamanoPagina(tamanoPagina);

            IQueryable<AlbumBotanicoCafe> query = context
                .AlbumesBotanicosCafe
                .AsNoTracking();

            if (!incluirInactivos)
            {
                query = query.Where(x =>
                    x.activo &&
                    x.Categoria.activo);
            }

            if (categoriaId.HasValue && categoriaId.Value > 0)
            {
                query = query.Where(x =>
                    x.categoriaAlbumBotanicoId == categoriaId.Value);
            }

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();

                query = query.Where(x =>
                    x.titulo.Contains(texto) ||
                    (x.nombreCientifico != null &&
                     x.nombreCientifico.Contains(texto)) ||
                    x.descripcion.Contains(texto));
            }

            int totalRegistros = await query.CountAsync(cancellationToken);

            int omitir = (pagina - 1) * tamanoPagina;

            var items = await ProyectarGaleria(query)
                .Skip(omitir)
                .Take(tamanoPagina)
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Página de la galería obtenida correctamente.",
                data = CrearPagina(
                    items,
                    paginaActual: pagina,
                    tamanoPagina: tamanoPagina,
                    totalRegistros: totalRegistros)
            });
        }

        private static IQueryable<AlbumGaleriaFila> ProyectarGaleria(
            IQueryable<AlbumBotanicoCafe> query)
        {
            return query
                .OrderByDescending(x => x.activo)
                .ThenBy(x => x.Categoria.nombreCategoria)
                .ThenBy(x => x.titulo)
                .Select(x => new AlbumGaleriaFila
                {
                    AlbumBotanicoCafeId = x.albumBotanicoCafeId,
                    CategoriaAlbumBotanicoId =
                        x.categoriaAlbumBotanicoId,
                    Categoria = x.Categoria.nombreCategoria,
                    Titulo = x.titulo,
                    NombreCientifico = x.nombreCientifico,
                    DescripcionCorta = x.descripcion.Length > 180
                        ? x.descripcion.Substring(0, 180) + "..."
                        : x.descripcion,
                    FotoPortada = x.Fotos
                        .Where(f => f.activo)
                        .OrderByDescending(f => f.esPortada)
                        .ThenBy(f => f.orden)
                        .Select(f => f.rutaFoto)
                        .FirstOrDefault(),
                    TotalFotos = x.Fotos.Count(f => f.activo),
                    Activo = x.activo,
                    CategoriaActiva = x.Categoria.activo,
                    FechaCreacion = x.fechaCreacion
                });
        }

        private static object CrearPagina(
            IReadOnlyCollection<AlbumGaleriaFila> items,
            int paginaActual,
            int tamanoPagina,
            int totalRegistros)
        {
            int totalPaginas = totalRegistros == 0
                ? 0
                : (int)Math.Ceiling(
                    totalRegistros / (double)tamanoPagina);

            return new
            {
                items,
                paginaActual,
                tamanoPagina,
                totalRegistros,
                totalPaginas,
                tieneMas = paginaActual < totalPaginas
            };
        }

        private sealed class AlbumGaleriaFila
        {
            public int AlbumBotanicoCafeId { get; set; }
            public int CategoriaAlbumBotanicoId { get; set; }
            public string Categoria { get; set; } = string.Empty;
            public string Titulo { get; set; } = string.Empty;
            public string? NombreCientifico { get; set; }
            public string DescripcionCorta { get; set; } = string.Empty;
            public string? FotoPortada { get; set; }
            public int TotalFotos { get; set; }
            public bool Activo { get; set; }
            public bool CategoriaActiva { get; set; }
            public DateTime FechaCreacion { get; set; }
        }

        private static int NormalizarTamanoPagina(int valor) =>
            Math.Clamp(valor, 1, 30);
    }
}
