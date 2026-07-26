using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Expone una huella liviana del contenido público de Noticias y Álbum.
    /// La aplicación compara esta versión con la almacenada localmente antes
    /// de volver a descargar listados, detalles o imágenes.
    /// </summary>
    [ApiController]
    [Route("api/contenido-sincronizacion")]
    public sealed class ContenidoSincronizacionController : ControllerBase
    {
        private const string ModuloNoticias = "noticias";
        private const string ModuloAlbum = "album";
        private const string InterfazNoticias = "noticiasPage";
        private const string InterfazAlbum = "albumFotosPage";
        private const string EstadoPublicada = "PUBLICADA";

        private readonly NoticiasDbContext noticiasDb;
        private readonly DBContext db;
        private readonly PermisoApiService permisoApiService;

        public ContenidoSincronizacionController(
            NoticiasDbContext noticiasDb,
            DBContext db,
            PermisoApiService permisoApiService)
        {
            this.noticiasDb = noticiasDb;
            this.db = db;
            this.permisoApiService = permisoApiService;
        }

        [HttpGet("estado")]
        public async Task<ActionResult> Estado(
            [FromQuery] string modulo,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            string moduloNormalizado = (modulo ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            if (moduloNormalizado is not (ModuloNoticias or ModuloAlbum))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El módulo solicitado no es válido."
                });
            }

            string interfaz = moduloNormalizado == ModuloNoticias
                ? InterfazNoticias
                : InterfazAlbum;

            ResultadoPermisoApi permiso =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    interfaz,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    new
                    {
                        success = false,
                        message = permiso.Mensaje
                    });
            }

            DateTime fechaServidorUtc = DateTime.UtcNow;

            string version = moduloNormalizado == ModuloNoticias
                ? await CalcularVersionNoticiasAsync(
                    fechaServidorUtc,
                    cancellationToken)
                : await CalcularVersionAlbumAsync(
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Estado de sincronización obtenido correctamente.",
                data = new
                {
                    modulo = moduloNormalizado,
                    version,
                    fechaServidorUtc
                }
            });
        }

        private async Task<string> CalcularVersionNoticiasAsync(
            DateTime fechaServidorUtc,
            CancellationToken cancellationToken)
        {
            var categorias = await noticiasDb
                .CategoriasPublicacion
                .AsNoTracking()
                .Where(x => x.activo)
                .OrderBy(x => x.categoriaPublicacionId)
                .Select(x => new
                {
                    x.categoriaPublicacionId,
                    x.nombreCategoriaPublicacion,
                    x.descripcionCategoriaPublicacion,
                    x.colorHex,
                    x.orden,
                    x.activo
                })
                .ToListAsync(cancellationToken);

            var publicaciones = await noticiasDb
                .Publicaciones
                .AsNoTracking()
                .Where(x =>
                    x.activo &&
                    x.CategoriaPublicacion.activo &&
                    x.estadoPublicacion == EstadoPublicada &&
                    x.fechaInicioPublicacionUtc <= fechaServidorUtc &&
                    (!x.fechaFinPublicacionUtc.HasValue ||
                     x.fechaFinPublicacionUtc.Value >= fechaServidorUtc))
                .OrderBy(x => x.publicacionId)
                .Select(x => new
                {
                    x.publicacionId,
                    x.categoriaPublicacionId,
                    x.titulo,
                    x.resumen,
                    x.contenido,
                    x.rutaImagenPortada,
                    x.enlaceExterno,
                    x.textoEnlace,
                    x.ubicacion,
                    x.fechaEventoInicioUtc,
                    x.fechaEventoFinUtc,
                    x.fechaInicioPublicacionUtc,
                    x.fechaFinPublicacionUtc,
                    x.estadoPublicacion,
                    x.destacada,
                    x.fechaCreacionUtc,
                    x.fechaUltimaModificacionUtc,
                    x.activo
                })
                .ToListAsync(cancellationToken);

            return CalcularHash(new
            {
                categorias,
                publicaciones
            });
        }

        private async Task<string> CalcularVersionAlbumAsync(
            CancellationToken cancellationToken)
        {
            var categorias = await db
                .CategoriasAlbumBotanico
                .AsNoTracking()
                .Where(x => x.activo)
                .OrderBy(x => x.categoriaAlbumBotanicoId)
                .Select(x => new
                {
                    x.categoriaAlbumBotanicoId,
                    x.nombreCategoria,
                    x.descripcion,
                    x.rutaImagenPortada,
                    x.activo
                })
                .ToListAsync(cancellationToken);

            var registros = await db
                .AlbumesBotanicosCafe
                .AsNoTracking()
                .Where(x => x.activo && x.Categoria.activo)
                .OrderBy(x => x.albumBotanicoCafeId)
                .Select(x => new
                {
                    x.albumBotanicoCafeId,
                    x.categoriaAlbumBotanicoId,
                    x.titulo,
                    x.nombreCientifico,
                    x.descripcion,
                    x.caracteristicas,
                    x.sintomas,
                    x.causas,
                    x.recomendaciones,
                    x.observaciones,
                    x.activo,
                    x.fechaCreacion
                })
                .ToListAsync(cancellationToken);

            var fotos = await db
                .AlbumesBotanicosCafeFotos
                .AsNoTracking()
                .Where(x =>
                    x.activo &&
                    x.AlbumBotanicoCafe.activo &&
                    x.AlbumBotanicoCafe.Categoria.activo)
                .OrderBy(x => x.albumBotanicoCafeFotoId)
                .Select(x => new
                {
                    x.albumBotanicoCafeFotoId,
                    x.albumBotanicoCafeId,
                    x.rutaFoto,
                    x.descripcionFoto,
                    x.esPortada,
                    x.orden,
                    x.activo
                })
                .ToListAsync(cancellationToken);

            return CalcularHash(new
            {
                categorias,
                registros,
                fotos
            });
        }

        private static string CalcularHash<T>(T value)
        {
            string json = JsonSerializer.Serialize(value);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            byte[] hash = SHA256.HashData(bytes);

            return Convert
                .ToHexString(hash)
                .ToLowerInvariant();
        }
    }
}
