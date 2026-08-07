using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Expone una huella liviana de cada módulo sincronizable. La versión del
    /// álbum incluye categorías, subcategorías específicas y fotografías.
    /// </summary>
    [ApiController]
    [Route("api/contenido-sincronizacion")]
    public sealed class ContenidoSincronizacionController : ControllerBase
    {
        private const string ModuloNoticias = "noticias";
        private const string ModuloAlbum = "album";
        private const string ModuloCatalogos = "catalogos";
        private const string InterfazNoticias = "noticiasPage";
        private const string InterfazAlbum = "albumFotosPage";
        private const string InterfazDatosSinConexion =
            OfflinePermissionProvisioner.CodigoInterfaz;
        private const string EstadoPublicada = "PUBLICADA";

        private static readonly SemaphoreSlim CatalogosVersionLock =
            new(1, 1);
        private static readonly TimeSpan DuracionVersionCatalogos =
            TimeSpan.FromSeconds(20);
        private static string versionCatalogosCache = string.Empty;
        private static DateTime versionCatalogosExpiraUtc;

        private readonly NoticiasDbContext noticiasDb;
        private readonly DBContext db;
        private readonly AlbumJerarquiaDbContext albumJerarquiaDb;
        private readonly PermisoApiService permisoApiService;

        public ContenidoSincronizacionController(
            NoticiasDbContext noticiasDb,
            DBContext db,
            AlbumJerarquiaDbContext albumJerarquiaDb,
            PermisoApiService permisoApiService)
        {
            this.noticiasDb = noticiasDb;
            this.db = db;
            this.albumJerarquiaDb = albumJerarquiaDb;
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

            if (moduloNormalizado is not (
                    ModuloNoticias or ModuloAlbum or ModuloCatalogos))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El módulo solicitado no es válido."
                });
            }

            ActionResult? acceso = await ValidarAccesoAsync(
                moduloNormalizado,
                usuarioSesionId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DateTime fechaServidorUtc = DateTime.UtcNow;
            string version = moduloNormalizado switch
            {
                ModuloNoticias => await CalcularVersionNoticiasAsync(
                    fechaServidorUtc,
                    cancellationToken),
                ModuloAlbum => await CalcularVersionAlbumAsync(
                    cancellationToken),
                _ => await CalcularVersionCatalogosConCacheAsync(
                    cancellationToken)
            };

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

        private async Task<ActionResult?> ValidarAccesoAsync(
            string modulo,
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (modulo == ModuloCatalogos)
            {
                await OfflinePermissionProvisioner.AsegurarAsync(
                    db,
                    cancellationToken);

                ResultadoPermisoApi permisoOffline =
                    await permisoApiService.ValidarAsync(
                        usuarioId,
                        InterfazDatosSinConexion,
                        TipoPermisoApi.Leer,
                        cancellationToken);

                return permisoOffline.Permitido
                    ? null
                    : StatusCode(
                        permisoOffline.CodigoEstado,
                        new
                        {
                            success = false,
                            message = permisoOffline.Mensaje
                        });
            }

            string interfaz = modulo == ModuloNoticias
                ? InterfazNoticias
                : InterfazAlbum;

            ResultadoPermisoApi permiso = await permisoApiService.ValidarAsync(
                usuarioId,
                interfaz,
                TipoPermisoApi.Leer,
                cancellationToken);

            return permiso.Permitido
                ? null
                : StatusCode(
                    permiso.CodigoEstado,
                    new
                    {
                        success = false,
                        message = permiso.Mensaje
                    });
        }

        private async Task<string> CalcularVersionNoticiasAsync(
            DateTime fechaServidorUtc,
            CancellationToken cancellationToken)
        {
            var categorias = await noticiasDb.CategoriasPublicacion
                .AsNoTracking()
                .Where(item => item.activo)
                .OrderBy(item => item.categoriaPublicacionId)
                .Select(item => new
                {
                    item.categoriaPublicacionId,
                    item.nombreCategoriaPublicacion,
                    item.descripcionCategoriaPublicacion,
                    item.colorHex,
                    item.orden,
                    item.activo
                })
                .ToListAsync(cancellationToken);

            var publicaciones = await noticiasDb.Publicaciones
                .AsNoTracking()
                .Where(item =>
                    item.activo &&
                    item.CategoriaPublicacion.activo &&
                    item.estadoPublicacion == EstadoPublicada &&
                    item.fechaInicioPublicacionUtc <= fechaServidorUtc &&
                    (!item.fechaFinPublicacionUtc.HasValue ||
                     item.fechaFinPublicacionUtc.Value >= fechaServidorUtc))
                .OrderBy(item => item.publicacionId)
                .Select(item => new
                {
                    item.publicacionId,
                    item.categoriaPublicacionId,
                    item.titulo,
                    item.resumen,
                    item.contenido,
                    item.rutaImagenPortada,
                    item.enlaceExterno,
                    item.textoEnlace,
                    item.ubicacion,
                    item.fechaEventoInicioUtc,
                    item.fechaEventoFinUtc,
                    item.fechaInicioPublicacionUtc,
                    item.fechaFinPublicacionUtc,
                    item.estadoPublicacion,
                    item.destacada,
                    item.fechaCreacionUtc,
                    item.fechaUltimaModificacionUtc,
                    item.activo
                })
                .ToListAsync(cancellationToken);

            return CalcularHash(new { categorias, publicaciones });
        }

        private async Task<string> CalcularVersionAlbumAsync(
            CancellationToken cancellationToken)
        {
            var categorias = await albumJerarquiaDb.Categorias
                .AsNoTracking()
                .Where(item => item.Activo)
                .OrderBy(item => item.CategoriaAlbumBotanicoId)
                .Select(item => new
                {
                    item.CategoriaAlbumBotanicoId,
                    item.NombreCategoria,
                    item.Descripcion,
                    item.RutaImagenPortada,
                    item.Activo
                })
                .ToListAsync(cancellationToken);

            var subcategorias = await albumJerarquiaDb.Subcategorias
                .AsNoTracking()
                .Where(item => item.Activo && item.Categoria.Activo)
                .OrderBy(item => item.AlbumBotanicoCafeId)
                .Select(item => new
                {
                    subcategoriaAlbumBotanicoId = item.AlbumBotanicoCafeId,
                    item.CategoriaAlbumBotanicoId,
                    nombreSubcategoria = item.Titulo,
                    item.NombreCientifico,
                    item.Descripcion,
                    item.Caracteristicas,
                    item.Sintomas,
                    item.Causas,
                    item.Recomendaciones,
                    item.Observaciones,
                    item.Activo,
                    item.FechaCreacion
                })
                .ToListAsync(cancellationToken);

            var fotos = await albumJerarquiaDb.FotosAlbum
                .AsNoTracking()
                .Where(item =>
                    item.Activo &&
                    item.Subcategoria.Activo &&
                    item.Subcategoria.Categoria.Activo)
                .OrderBy(item => item.AlbumBotanicoCafeFotoId)
                .Select(item => new
                {
                    item.AlbumBotanicoCafeFotoId,
                    subcategoriaAlbumBotanicoId = item.AlbumBotanicoCafeId,
                    item.RutaFoto,
                    item.DescripcionFoto,
                    item.EsPortada,
                    item.Orden,
                    item.Activo
                })
                .ToListAsync(cancellationToken);

            return CalcularHash(new
            {
                categorias,
                subcategorias,
                fotos
            });
        }

        private async Task<string> CalcularVersionCatalogosConCacheAsync(
            CancellationToken cancellationToken)
        {
            DateTime ahora = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(versionCatalogosCache) &&
                ahora < versionCatalogosExpiraUtc)
            {
                return versionCatalogosCache;
            }

            await CatalogosVersionLock.WaitAsync(cancellationToken);
            try
            {
                ahora = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(versionCatalogosCache) &&
                    ahora < versionCatalogosExpiraUtc)
                {
                    return versionCatalogosCache;
                }

                versionCatalogosCache = await CalcularVersionCatalogosAsync(
                    cancellationToken);
                versionCatalogosExpiraUtc =
                    ahora + DuracionVersionCatalogos;
                return versionCatalogosCache;
            }
            finally
            {
                CatalogosVersionLock.Release();
            }
        }

        private async Task<string> CalcularVersionCatalogosAsync(
            CancellationToken cancellationToken)
        {
            var tablas = new SortedDictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["pais"] = await CapturarTablaAsync(db.Pais, cancellationToken),
                ["departamento"] = await CapturarTablaAsync(db.Departamento, cancellationToken),
                ["municipio"] = await CapturarTablaAsync(db.Municipios, cancellationToken),
                ["terreno"] = await CapturarTablaAsync(db.Terreno, cancellationToken),
                ["tipoCultivo"] = await CapturarTablaAsync(db.TipoCultivos, cancellationToken),
                ["tipoAnalisisSuelo"] = await CapturarTablaAsync(db.TipoAnalisisSuelos, cancellationToken),
                ["elementoQuimico"] = await CapturarTablaAsync(db.elementoQuimico, cancellationToken),
                ["unidadMedida"] = await CapturarTablaAsync(db.UnidadMedidas, cancellationToken),
                ["elementoQuimicoUnidadMedida"] = await CapturarTablaAsync(db.Set<ElementoQuimicoUnidadMedida>(), cancellationToken),
                ["materiaOrganicaUnidadMedida"] = await CapturarTablaAsync(db.Set<MateriaOrganicaUnidadMedida>(), cancellationToken),
                ["fuenteNutriente"] = await CapturarTablaAsync(db.fuenteNutriente, cancellationToken),
                ["fuenteNutrienteElementoQuimico"] = await CapturarTablaAsync(db.fuenteNutrienteElementoQuimico, cancellationToken),
                ["fuenteFertilizacionMixta"] = await CapturarTablaAsync(db.fuenteFertilizacionMixta, cancellationToken),
                ["rangoNutrimental"] = await CapturarTablaAsync(db.RangoNutrimentales, cancellationToken),
                ["parametroRangoNutrienteCultivo"] = await CapturarTablaAsync(db.ParametroRangoNutrienteCultivo, cancellationToken),
                ["parametroExtraccionNutrienteCafe"] = await CapturarTablaAsync(db.ParametroExtraccionNutrienteCafe, cancellationToken),
                ["parametroEnmiendaCalcarea"] = await CapturarTablaAsync(db.ParametroEnmiendaCalcarea, cancellationToken),
                ["parametroFuenteOrganicaAporte"] = await CapturarTablaAsync(db.ParametroFuenteOrganicaAporte, cancellationToken)
            };

            return CalcularHash(tablas);
        }

        private async Task<List<SortedDictionary<string, object?>>>
            CapturarTablaAsync<TEntity>(
                IQueryable<TEntity> query,
                CancellationToken cancellationToken)
            where TEntity : class
        {
            var entityType = db.Model.FindEntityType(typeof(TEntity))
                ?? throw new InvalidOperationException(
                    $"La entidad {typeof(TEntity).Name} no está registrada en el modelo.");

            var propiedades = entityType.GetProperties()
                .Where(item => item.PropertyInfo != null)
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .ToList();

            string[] llaves = entityType.FindPrimaryKey()?.Properties
                .Select(item => item.Name)
                .ToArray() ?? [];

            List<TEntity> registros = await query
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var filas = new List<SortedDictionary<string, object?>>(
                registros.Count);

            foreach (TEntity registro in registros)
            {
                var fila = new SortedDictionary<string, object?>(
                    StringComparer.Ordinal);

                foreach (var propiedad in propiedades)
                {
                    fila[propiedad.Name] = propiedad.PropertyInfo!
                        .GetValue(registro);
                }

                filas.Add(fila);
            }

            return filas
                .OrderBy(
                    fila => ConstruirClaveOrden(fila, llaves),
                    StringComparer.Ordinal)
                .ToList();
        }

        private static string ConstruirClaveOrden(
            IReadOnlyDictionary<string, object?> fila,
            IEnumerable<string> llaves) =>
            string.Join(
                "|",
                llaves.Select(nombre =>
                    fila.TryGetValue(nombre, out object? valor)
                        ? Convert.ToString(
                            valor,
                            CultureInfo.InvariantCulture) ?? string.Empty
                        : string.Empty));

        private static string CalcularHash<T>(T valor)
        {
            string json = JsonSerializer.Serialize(valor);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
