using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Endpoints nuevos para la papelera de publicaciones. Los endpoints
    /// históricos de PublicacionController permanecen sin cambios.
    /// </summary>
    [ApiController]
    [Route("api/publicacion-eliminadas")]
    public sealed class PublicacionEliminadasController : ControllerBase
    {
        private const string InterfazNoticias = "noticiasPage";
        private const string EstadoBorrador = "BORRADOR";

        private readonly NoticiasDbContext noticiasDb;
        private readonly DBContext db;
        private readonly PermisoApiService permisoApiService;

        public PublicacionEliminadasController(
            NoticiasDbContext noticiasDb,
            DBContext db,
            PermisoApiService permisoApiService)
        {
            this.noticiasDb = noticiasDb;
            this.db = db;
            this.permisoApiService = permisoApiService;
        }

        [HttpGet]
        public async Task<ActionResult> Listar(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromQuery] string? buscar = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 16,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Administrar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 8, 50);

            IQueryable<Publicacion> query =
                noticiasDb.Publicaciones
                    .AsNoTracking()
                    .Where(x => !x.activo);

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.Trim();

                if (texto.Length > 120)
                    texto = texto[..120];

                query = query.Where(x =>
                    x.titulo.Contains(texto) ||
                    x.resumen.Contains(texto) ||
                    x.ubicacion.Contains(texto));
            }

            int total =
                await query.CountAsync(cancellationToken);

            int totalPaginas =
                total == 0
                    ? 1
                    : (int)Math.Ceiling(
                        total /
                        (double)tamanoPagina);

            int paginaNormalizada =
                Math.Min(
                    pagina,
                    Math.Max(1, totalPaginas));

            List<PublicacionListadoDto> items =
                await query
                    .OrderByDescending(x =>
                        x.fechaUltimaModificacionUtc)
                    .ThenByDescending(x =>
                        x.publicacionId)
                    .Skip(
                        (paginaNormalizada - 1) *
                        tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(x => new PublicacionListadoDto
                    {
                        PublicacionId = x.publicacionId,
                        CategoriaPublicacionId =
                            x.categoriaPublicacionId,
                        Categoria =
                            x.CategoriaPublicacion
                                .nombreCategoriaPublicacion,
                        ColorCategoria =
                            x.CategoriaPublicacion.colorHex,
                        Titulo = x.titulo,
                        Resumen = x.resumen,
                        RutaImagenPortada =
                            x.rutaImagenPortada,
                        Ubicacion = x.ubicacion,
                        FechaEventoInicioUtc =
                            x.fechaEventoInicioUtc,
                        FechaEventoFinUtc =
                            x.fechaEventoFinUtc,
                        FechaInicioPublicacionUtc =
                            x.fechaInicioPublicacionUtc,
                        FechaFinPublicacionUtc =
                            x.fechaFinPublicacionUtc,
                        EstadoPublicacion =
                            x.estadoPublicacion,
                        EstadoVisual = "ELIMINADA",
                        Destacada = false,
                        FechaCreacionUtc =
                            x.fechaCreacionUtc,
                        FechaUltimaModificacionUtc =
                            x.fechaUltimaModificacionUtc,
                        UsuarioCreacionId =
                            x.usuarioCreacionId,
                        UsuarioUltimaModificacionId =
                            x.usuarioUltimaModificacionId
                    })
                    .ToListAsync(cancellationToken);

            if (items.Count > 0)
            {
                int[] usuarioIds = items
                    .SelectMany(item => new[]
                    {
                        item.UsuarioCreacionId,
                        item.UsuarioUltimaModificacionId
                    })
                    .Where(id => id > 0)
                    .Distinct()
                    .ToArray();

                Dictionary<int, string> usuarios =
                    await ObtenerNombresUsuariosAsync(
                        usuarioIds,
                        cancellationToken);

                foreach (PublicacionListadoDto item in items)
                {
                    item.Autor =
                        ObtenerNombreUsuario(
                            usuarios,
                            item.UsuarioCreacionId);

                    item.UltimoEditor =
                        ObtenerNombreUsuario(
                            usuarios,
                            item.UsuarioUltimaModificacionId);
                }
            }

            var data = new PublicacionPaginadaDto
            {
                Items = items,
                Pagina = paginaNormalizada,
                TamanoPagina = tamanoPagina,
                TotalRegistros = total,
                TotalPaginas = totalPaginas
            };

            return Ok(new
            {
                success = true,
                message =
                    "Publicaciones eliminadas obtenidas correctamente.",
                data
            });
        }

        [HttpPut("{id:int}/reactivar")]
        public async Task<ActionResult> Reactivar(
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso = await ValidarAccesoAsync(
                usuarioSesionId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            Publicacion? publicacion =
                await noticiasDb.Publicaciones
                    .FirstOrDefaultAsync(
                        x =>
                            x.publicacionId == id &&
                            !x.activo,
                        cancellationToken);

            if (publicacion == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "La publicación eliminada no fue encontrada o ya fue restaurada."
                });
            }

            bool categoriaActiva =
                await noticiasDb.CategoriasPublicacion
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.categoriaPublicacionId ==
                                publicacion.categoriaPublicacionId &&
                            x.activo,
                        cancellationToken);

            if (!categoriaActiva)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No es posible restaurar la publicación porque su tipo de publicación está eliminado. Reactive primero el tipo relacionado."
                });
            }

            /*
             * Restaurar nunca vuelve a publicar automáticamente. El contenido,
             * la portada, el identificador y las relaciones se conservan, pero
             * el administrador debe revisar y publicar nuevamente el borrador.
             */
            publicacion.activo = true;
            publicacion.estadoPublicacion = EstadoBorrador;
            publicacion.destacada = false;
            publicacion.usuarioUltimaModificacionId =
                usuarioSesionId!.Value;
            publicacion.fechaUltimaModificacionUtc =
                DateTime.UtcNow;

            await noticiasDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Publicación restaurada correctamente como borrador."
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

        private async Task<Dictionary<int, string>>
            ObtenerNombresUsuariosAsync(
                IEnumerable<int> usuarioIds,
                CancellationToken cancellationToken)
        {
            int[] ids = usuarioIds
                .Where(id => id > 0)
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
                x => !string.IsNullOrWhiteSpace(
                        x.nombreCompletoUsuario)
                    ? x.nombreCompletoUsuario.Trim()
                    : x.nombreUsuario.Trim());
        }

        private static string ObtenerNombreUsuario(
            IReadOnlyDictionary<int, string> usuarios,
            int usuarioId) =>
            usuarios.TryGetValue(
                usuarioId,
                out string? nombre)
                ? nombre
                : $"Usuario #{usuarioId}";
    }
}
