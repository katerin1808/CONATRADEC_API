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
    /// Expone una huella liviana del contenido sincronizable.
    ///
    /// Noticias y Álbum conservan sus validaciones de permisos.
    /// Catálogos devuelve únicamente una versión; los datos continúan
    /// protegidos por sus endpoints originales.
    /// </summary>
    [ApiController]
    [Route("api/contenido-sincronizacion")]
    public sealed class ContenidoSincronizacionController :
        ControllerBase
    {
        private const string ModuloNoticias = "noticias";
        private const string ModuloAlbum = "album";
        private const string ModuloCatalogos = "catalogos";

        private const string InterfazNoticias = "noticiasPage";
        private const string InterfazAlbum = "albumFotosPage";
        private const string InterfazDatosSinConexion =
            OfflinePermissionProvisioner.CodigoInterfaz;
        private const string EstadoPublicada = "PUBLICADA";

        private static readonly SemaphoreSlim
            CatalogosVersionLock = new(1, 1);

        private static readonly TimeSpan
            DuracionVersionCatalogos = TimeSpan.FromSeconds(20);

        private static string versionCatalogosCache =
            string.Empty;

        private static DateTime versionCatalogosExpiraUtc;

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
            [FromHeader(Name = "X-Usuario-Id")]
                int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            string moduloNormalizado =
                (modulo ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant();

            if (moduloNormalizado is not (
                    ModuloNoticias or
                    ModuloAlbum or
                    ModuloCatalogos))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El módulo solicitado no es válido."
                });
            }

            if (moduloNormalizado == ModuloCatalogos)
            {
                await OfflinePermissionProvisioner
                    .AsegurarAsync(
                        db,
                        cancellationToken);

                ResultadoPermisoApi permisoOffline =
                    await permisoApiService.ValidarAsync(
                        usuarioSesionId,
                        InterfazDatosSinConexion,
                        TipoPermisoApi.Leer,
                        cancellationToken);

                if (!permisoOffline.Permitido)
                {
                    return StatusCode(
                        permisoOffline.CodigoEstado,
                        new
                        {
                            success = false,
                            message =
                                permisoOffline.Mensaje
                        });
                }
            }
            else
            {
                string interfaz =
                    moduloNormalizado == ModuloNoticias
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
            }

            DateTime fechaServidorUtc = DateTime.UtcNow;

            string version =
                moduloNormalizado switch
                {
                    ModuloNoticias =>
                        await CalcularVersionNoticiasAsync(
                            fechaServidorUtc,
                            cancellationToken),

                    ModuloAlbum =>
                        await CalcularVersionAlbumAsync(
                            cancellationToken),

                    _ =>
                        await CalcularVersionCatalogosConCacheAsync(
                            cancellationToken)
                };

            return Ok(new
            {
                success = true,
                message =
                    "Estado de sincronización obtenido correctamente.",
                data = new
                {
                    modulo = moduloNormalizado,
                    version,
                    fechaServidorUtc
                }
            });
        }

        private async Task<string>
            CalcularVersionNoticiasAsync(
                DateTime fechaServidorUtc,
                CancellationToken cancellationToken)
        {
            var categorias =
                await noticiasDb
                    .CategoriasPublicacion
                    .AsNoTracking()
                    .Where(x => x.activo)
                    .OrderBy(x =>
                        x.categoriaPublicacionId)
                    .Select(x => new
                    {
                        x.categoriaPublicacionId,
                        x.nombreCategoriaPublicacion,
                        x.descripcionCategoriaPublicacion,
                        x.colorHex,
                        x.orden,
                        x.activo
                    })
                    .ToListAsync(
                        cancellationToken);

            var publicaciones =
                await noticiasDb
                    .Publicaciones
                    .AsNoTracking()
                    .Where(x =>
                        x.activo &&
                        x.CategoriaPublicacion.activo &&
                        x.estadoPublicacion ==
                            EstadoPublicada &&
                        x.fechaInicioPublicacionUtc <=
                            fechaServidorUtc &&
                        (!x.fechaFinPublicacionUtc.HasValue ||
                         x.fechaFinPublicacionUtc.Value >=
                            fechaServidorUtc))
                    .OrderBy(x =>
                        x.publicacionId)
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
                    .ToListAsync(
                        cancellationToken);

            return CalcularHash(new
            {
                categorias,
                publicaciones
            });
        }

        private async Task<string>
            CalcularVersionAlbumAsync(
                CancellationToken cancellationToken)
        {
            var categorias =
                await db
                    .CategoriasAlbumBotanico
                    .AsNoTracking()
                    .Where(x => x.activo)
                    .OrderBy(x =>
                        x.categoriaAlbumBotanicoId)
                    .Select(x => new
                    {
                        x.categoriaAlbumBotanicoId,
                        x.nombreCategoria,
                        x.descripcion,
                        x.rutaImagenPortada,
                        x.activo
                    })
                    .ToListAsync(
                        cancellationToken);

            var registros =
                await db
                    .AlbumesBotanicosCafe
                    .AsNoTracking()
                    .Where(x =>
                        x.activo &&
                        x.Categoria.activo)
                    .OrderBy(x =>
                        x.albumBotanicoCafeId)
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
                    .ToListAsync(
                        cancellationToken);

            var fotos =
                await db
                    .AlbumesBotanicosCafeFotos
                    .AsNoTracking()
                    .Where(x =>
                        x.activo &&
                        x.AlbumBotanicoCafe.activo &&
                        x.AlbumBotanicoCafe
                            .Categoria.activo)
                    .OrderBy(x =>
                        x.albumBotanicoCafeFotoId)
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
                    .ToListAsync(
                        cancellationToken);

            return CalcularHash(new
            {
                categorias,
                registros,
                fotos
            });
        }

        private async Task<string>
            CalcularVersionCatalogosConCacheAsync(
                CancellationToken cancellationToken)
        {
            DateTime ahora = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(
                    versionCatalogosCache) &&
                ahora < versionCatalogosExpiraUtc)
            {
                return versionCatalogosCache;
            }

            await CatalogosVersionLock.WaitAsync(
                cancellationToken);

            try
            {
                ahora = DateTime.UtcNow;

                if (!string.IsNullOrWhiteSpace(
                        versionCatalogosCache) &&
                    ahora < versionCatalogosExpiraUtc)
                {
                    return versionCatalogosCache;
                }

                versionCatalogosCache =
                    await CalcularVersionCatalogosAsync(
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

        private async Task<string>
            CalcularVersionCatalogosAsync(
                CancellationToken cancellationToken)
        {
            var tablas =
                new SortedDictionary<string, object?>(
                    StringComparer.Ordinal)
                {
                    ["pais"] =
                        await CapturarTablaAsync(
                            db.Pais,
                            cancellationToken),

                    ["departamento"] =
                        await CapturarTablaAsync(
                            db.Departamento,
                            cancellationToken),

                    ["municipio"] =
                        await CapturarTablaAsync(
                            db.Municipios,
                            cancellationToken),

                    ["terreno"] =
                        await CapturarTablaAsync(
                            db.Terreno,
                            cancellationToken),

                    ["tipoCultivo"] =
                        await CapturarTablaAsync(
                            db.TipoCultivos,
                            cancellationToken),

                    ["tipoAnalisisSuelo"] =
                        await CapturarTablaAsync(
                            db.TipoAnalisisSuelos,
                            cancellationToken),

                    ["elementoQuimico"] =
                        await CapturarTablaAsync(
                            db.elementoQuimico,
                            cancellationToken),

                    ["unidadMedida"] =
                        await CapturarTablaAsync(
                            db.UnidadMedidas,
                            cancellationToken),

                    ["elementoQuimicoUnidadMedida"] =
                        await CapturarTablaAsync(
                            db.Set<
                                ElementoQuimicoUnidadMedida>(),
                            cancellationToken),

                    ["materiaOrganicaUnidadMedida"] =
                        await CapturarTablaAsync(
                            db.Set<
                                MateriaOrganicaUnidadMedida>(),
                            cancellationToken),

                    ["fuenteNutriente"] =
                        await CapturarTablaAsync(
                            db.fuenteNutriente,
                            cancellationToken),

                    ["fuenteNutrienteElementoQuimico"] =
                        await CapturarTablaAsync(
                            db.fuenteNutrienteElementoQuimico,
                            cancellationToken),

                    ["fuenteFertilizacionMixta"] =
                        await CapturarTablaAsync(
                            db.fuenteFertilizacionMixta,
                            cancellationToken),

                    ["rangoNutrimental"] =
                        await CapturarTablaAsync(
                            db.RangoNutrimentales,
                            cancellationToken),

                    ["parametroRangoNutrienteCultivo"] =
                        await CapturarTablaAsync(
                            db.ParametroRangoNutrienteCultivo,
                            cancellationToken),

                    ["parametroExtraccionNutrienteCafe"] =
                        await CapturarTablaAsync(
                            db.ParametroExtraccionNutrienteCafe,
                            cancellationToken),

                    ["parametroEnmiendaCalcarea"] =
                        await CapturarTablaAsync(
                            db.ParametroEnmiendaCalcarea,
                            cancellationToken),

                    ["parametroFuenteOrganicaAporte"] =
                        await CapturarTablaAsync(
                            db.ParametroFuenteOrganicaAporte,
                            cancellationToken)
                };

            return CalcularHash(tablas);
        }

        private async Task<
            List<SortedDictionary<string, object?>>>
            CapturarTablaAsync<TEntity>(
                IQueryable<TEntity> query,
                CancellationToken cancellationToken)
            where TEntity : class
        {
            var entityType =
                db.Model.FindEntityType(
                    typeof(TEntity))
                ?? throw new InvalidOperationException(
                    $"La entidad {typeof(TEntity).Name} " +
                    "no está registrada en el modelo.");

            var propiedades =
                entityType
                    .GetProperties()
                    .Where(x =>
                        x.PropertyInfo != null)
                    .OrderBy(x =>
                        x.Name,
                        StringComparer.Ordinal)
                    .ToList();

            string[] llaves =
                entityType
                    .FindPrimaryKey()?
                    .Properties
                    .Select(x => x.Name)
                    .ToArray()
                ?? Array.Empty<string>();

            List<TEntity> registros =
                await query
                    .AsNoTracking()
                    .ToListAsync(
                        cancellationToken);

            var filas =
                new List<
                    SortedDictionary<string, object?>>(
                        registros.Count);

            foreach (TEntity registro in registros)
            {
                var fila =
                    new SortedDictionary<
                        string,
                        object?>(
                            StringComparer.Ordinal);

                foreach (var propiedad in propiedades)
                {
                    fila[propiedad.Name] =
                        propiedad.PropertyInfo!
                            .GetValue(registro);
                }

                filas.Add(fila);
            }

            return filas
                .OrderBy(
                    fila => ConstruirClaveOrden(
                        fila,
                        llaves),
                    StringComparer.Ordinal)
                .ToList();
        }

        private static string ConstruirClaveOrden(
            IReadOnlyDictionary<string, object?> fila,
            IEnumerable<string> llaves)
        {
            return string.Join(
                "|",
                llaves.Select(nombre =>
                    fila.TryGetValue(
                        nombre,
                        out object? valor)
                        ? Convert.ToString(
                            valor,
                            System.Globalization
                                .CultureInfo.InvariantCulture)
                          ?? string.Empty
                        : string.Empty));
        }

        private static string CalcularHash<T>(T value)
        {
            string json =
                JsonSerializer.Serialize(value);

            byte[] bytes =
                Encoding.UTF8.GetBytes(json);

            byte[] hash =
                SHA256.HashData(bytes);

            return Convert
                .ToHexString(hash)
                .ToLowerInvariant();
        }
    }
}
