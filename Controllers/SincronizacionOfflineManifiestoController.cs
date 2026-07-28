using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Devuelve un manifiesto pequeño con la versión actual de cada módulo
    /// utilizado por la descarga offline.
    ///
    /// No devuelve registros, fotografías ni archivos. Solamente genera una
    /// huella por módulo para que la aplicación determine si su copia local
    /// continúa vigente.
    /// </summary>
    [ApiController]
    [Route("api/sincronizacion-offline")]
    public sealed class SincronizacionOfflineManifiestoController :
        ControllerBase
    {
        private const int EsquemaVersion = 2;

        private static readonly HashSet<string> EntidadesMotor =
            new(StringComparer.Ordinal)
            {
                "FuenteNutriente",
                "ElementoQuimico",
                "FuenteNutrienteElementoQuimico",
                "TipoCultivo",
                "TipoAnalisisSuelo",
                "UnidadMedida",
                "RangoNutrimental",
                "ParametroExtraccionNutrienteCafe",
                "ParametroRangoNutrienteCultivo",
                "ParametroEnmiendaCalcarea",
                "ParametroFuenteOrganicaAporte",
                "FuenteFertilizacionMixta"
            };

        /*
         * Los catálogos se separan por interfaz para que la aplicación pueda
         * indicar exactamente qué sección cambió, en lugar de mostrar un
         * mensaje general de "Catálogos y terrenos".
         */
        private static readonly HashSet<string> EntidadesTerrenos =
            new(StringComparer.Ordinal)
            {
                "Terreno",
                "FotoTerreno"
            };

        private static readonly HashSet<string> EntidadesPaises =
            new(StringComparer.Ordinal)
            {
                "Pais"
            };

        private static readonly HashSet<string> EntidadesDepartamentos =
            new(StringComparer.Ordinal)
            {
                "Departamento"
            };

        private static readonly HashSet<string> EntidadesMunicipios =
            new(StringComparer.Ordinal)
            {
                "Municipio"
            };

        private static readonly HashSet<string> EntidadesProcedencias =
            new(StringComparer.Ordinal)
            {
                "Procedencia"
            };

        private static readonly HashSet<string> EntidadesTiposCultivo =
            new(StringComparer.Ordinal)
            {
                "TipoCultivo"
            };

        private static readonly HashSet<string> EntidadesTiposAnalisis =
            new(StringComparer.Ordinal)
            {
                "TipoAnalisisSuelo"
            };

        private static readonly HashSet<string> EntidadesUnidadesMedida =
            new(StringComparer.Ordinal)
            {
                "UnidadMedida"
            };

        private static readonly HashSet<string> EntidadesElementosQuimicos =
            new(StringComparer.Ordinal)
            {
                "ElementoQuimico"
            };

        private static readonly HashSet<string> EntidadesFuentesNutrientes =
            new(StringComparer.Ordinal)
            {
                "FuenteNutriente",
                "FuenteNutrienteElementoQuimico"
            };

        private static readonly HashSet<string> EntidadesAnalisis =
            new(StringComparer.Ordinal)
            {
                "AnalisisSuelo",
                "AnalisisSueloElementoQuimico",
                "AnalisisSueloCalculo",
                "AnalisisSueloCalculoElementoQuimico",
                "FormulaNutricional",
                "FormulaNutricionalDetalle",
                "FormulaNutricionalAporte",
                "EnmiendaCalcarea",
                "FertilizacionMixta",
                "FertilizacionMixtaFuente",
                "FertilizacionMixtaDetalle"
            };

        private static readonly HashSet<string> EntidadesAlbum =
            new(StringComparer.Ordinal)
            {
                "CategoriaAlbumBotanico",
                "AlbumBotanicoCafe",
                "AlbumBotanicoCafeFoto"
            };

        private static readonly HashSet<string> EntidadesNoticias =
            new(StringComparer.Ordinal)
            {
                "CategoriaPublicacion",
                "Publicacion"
            };

        private readonly DBContext db;
        private readonly NoticiasDbContext noticiasDb;

        public SincronizacionOfflineManifiestoController(
            DBContext db,
            NoticiasDbContext noticiasDb)
        {
            this.db = db;
            this.noticiasDb = noticiasDb;
        }

        [HttpGet("manifiesto")]
        public async Task<IActionResult> ObtenerManifiesto(
            [FromHeader(Name = "X-Usuario-Id")]
                int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            (IActionResult? Error, UsuarioAcceso? Usuario) acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    cancellationToken);

            if (acceso.Error != null)
                return acceso.Error;

            UsuarioAcceso usuario = acceso.Usuario!;

            bool noticiasHabilitadas =
                await TienePermisoLecturaAsync(
                    usuario.RolId,
                    "noticiasPage",
                    cancellationToken);

            bool albumHabilitado =
                await TienePermisoLecturaAsync(
                    usuario.RolId,
                    "albumFotosPage",
                    cancellationToken);

            string tokenUsuarioAnalisis =
                await ObtenerTokenAnalisisUsuarioAsync(
                    usuario.UsuarioId,
                    cancellationToken);

            ModuloManifiesto motor =
                await CalcularModuloAsync(
                    db,
                    "motor",
                    "Motor de cálculo",
                    EntidadesMotor,
                    habilitado: true,
                    semillaAdicional: string.Empty,
                    cancellationToken);

            ModuloManifiesto terrenos =
                await CalcularModuloAsync(
                    db,
                    "catalogo-terrenos",
                    "Terrenos",
                    EntidadesTerrenos,
                    habilitado: true,
                    semillaAdicional: string.Empty,
                    cancellationToken);

            ModuloManifiesto paises =
                await CalcularModuloAsync(
                    db,
                    "catalogo-paises",
                    "Países",
                    EntidadesPaises,
                    habilitado: true,
                    semillaAdicional: string.Empty,
                    cancellationToken);

            ModuloManifiesto departamentos =
                await CalcularModuloAsync(
                    db,
                    "catalogo-departamentos",
                    "Departamentos",
                    EntidadesDepartamentos,
                    habilitado: true,
                    semillaAdicional: string.Empty,
                    cancellationToken);

            ModuloManifiesto municipios =
                await CalcularModuloAsync(
                    db,
                    "catalogo-municipios",
                    "Municipios",
                    EntidadesMunicipios,
                    habilitado: true,
                    semillaAdicional: string.Empty,
                    cancellationToken);

            ModuloManifiesto procedencias =
                await CalcularModuloAsync(
                    db,
                    "catalogo-procedencias",
                    "Procedencias",
                    EntidadesProcedencias,
                    habilitado: true,
                    semillaAdicional: string.Empty,
                    cancellationToken);

            ModuloManifiesto tiposCultivo =
                await CalcularModuloAsync(
                    db,
                    "catalogo-tipos-cultivo",
                    "Tipos de cultivo",
                    EntidadesTiposCultivo,
                    habilitado: true,
                    semillaAdicional: string.Empty,
                    cancellationToken);

            ModuloManifiesto tiposAnalisis =
                await CalcularModuloAsync(
                    db,
                    "catalogo-tipos-analisis",
                    "Tipos de análisis de suelo",
                    EntidadesTiposAnalisis,
                    habilitado: true,
                    semillaAdicional: string.Empty,
                    cancellationToken);

            ModuloManifiesto unidadesMedida =
                await CalcularModuloAsync(
                    db,
                    "catalogo-unidades-medida",
                    "Unidades de medida",
                    EntidadesUnidadesMedida,
                    habilitado: true,
                    semillaAdicional: string.Empty,
                    cancellationToken);

            ModuloManifiesto elementosQuimicos =
                await CalcularModuloAsync(
                    db,
                    "catalogo-elementos-quimicos",
                    "Elementos químicos",
                    EntidadesElementosQuimicos,
                    habilitado: true,
                    semillaAdicional: string.Empty,
                    cancellationToken);

            ModuloManifiesto fuentesNutrientes =
                await CalcularModuloAsync(
                    db,
                    "catalogo-fuentes-nutrientes",
                    "Fuentes de nutrientes",
                    EntidadesFuentesNutrientes,
                    habilitado: true,
                    semillaAdicional: string.Empty,
                    cancellationToken);

            ModuloManifiesto analisis =
                await CalcularModuloAsync(
                    db,
                    "analisis",
                    "Historial de análisis",
                    EntidadesAnalisis,
                    habilitado: true,
                    semillaAdicional: tokenUsuarioAnalisis,
                    cancellationToken);

            ModuloManifiesto noticias = noticiasHabilitadas
                ? await CalcularModuloAsync(
                    noticiasDb,
                    "noticias",
                    "Noticias",
                    EntidadesNoticias,
                    habilitado: true,
                    semillaAdicional: string.Empty,
                    cancellationToken)
                : ModuloManifiesto.NoHabilitado(
                    "noticias",
                    "Noticias");

            ModuloManifiesto album = albumHabilitado
                ? await CalcularModuloAsync(
                    db,
                    "album",
                    "Álbum de fotos",
                    EntidadesAlbum,
                    habilitado: true,
                    semillaAdicional: string.Empty,
                    cancellationToken)
                : ModuloManifiesto.NoHabilitado(
                    "album",
                    "Álbum de fotos");

            List<ModuloManifiesto> modulos =
                new()
                {
                    motor,
                    terrenos,
                    paises,
                    departamentos,
                    municipios,
                    procedencias,
                    tiposCultivo,
                    tiposAnalisis,
                    unidadesMedida,
                    elementosQuimicos,
                    fuentesNutrientes,
                    analisis,
                    noticias,
                    album
                };

            string versionGeneral = CalcularSha256(
                string.Join(
                    "|",
                    modulos
                        .Where(x => x.Habilitado)
                        .Select(x =>
                            $"{x.Codigo}:{x.Version}")));

            return Ok(new
            {
                success = true,
                message =
                    "Se comprobó la versión de los datos sin conexión.",
                data = new
                {
                    esquemaVersion = EsquemaVersion,
                    usuarioId = usuario.UsuarioId,
                    generadoUtc = DateTime.UtcNow,
                    versionGeneral,
                    modulos
                }
            });
        }

        private async Task<(
            IActionResult? Error,
            UsuarioAcceso? Usuario)> ValidarAccesoAsync(
                int? usuarioSesionId,
                CancellationToken cancellationToken)
        {
            if (usuarioSesionId is not > 0)
            {
                return (
                    Unauthorized(new
                    {
                        success = false,
                        message = "No se recibió una sesión válida."
                    }),
                    null);
            }

            UsuarioAcceso? usuario =
                await db.Usuarios
                    .AsNoTracking()
                    .Where(x =>
                        x.UsuarioId == usuarioSesionId.Value &&
                        x.activo)
                    .Select(x => new UsuarioAcceso
                    {
                        UsuarioId = x.UsuarioId,
                        RolId = x.rolId
                    })
                    .FirstOrDefaultAsync(cancellationToken);

            if (usuario == null)
            {
                return (
                    Unauthorized(new
                    {
                        success = false,
                        message =
                            "La sesión no pertenece a un usuario activo."
                    }),
                    null);
            }

            bool permitido = await TienePermisoLecturaAsync(
                usuario.RolId,
                "datosSinConexionPage",
                cancellationToken);

            if (!permitido)
            {
                return (
                    StatusCode(
                        StatusCodes.Status403Forbidden,
                        new
                        {
                            success = false,
                            message =
                                "Su usuario no tiene habilitados los datos sin conexión."
                        }),
                    null);
            }

            return (null, usuario);
        }

        private async Task<bool> TienePermisoLecturaAsync(
            int rolId,
            string interfazCodigo,
            CancellationToken cancellationToken)
        {
            return await (
                from relacion in db.RolInterfaz.AsNoTracking()
                join interfaz in db.Interfaz.AsNoTracking()
                    on relacion.interfazId equals interfaz.interfazId
                where
                    relacion.rolId == rolId &&
                    interfaz.activo &&
                    interfaz.nombreInterfaz == interfazCodigo &&
                    relacion.leer == true
                select relacion.rolInterfazId
            ).AnyAsync(cancellationToken);
        }

        private async Task<string> ObtenerTokenAnalisisUsuarioAsync(
            int usuarioId,
            CancellationToken cancellationToken)
        {
            long total = await db.AnalisisSueloCalculos
                .AsNoTracking()
                .LongCountAsync(
                    x => x.usuarioId == usuarioId,
                    cancellationToken);

            int ultimoId = await db.AnalisisSueloCalculos
                .AsNoTracking()
                .Where(x => x.usuarioId == usuarioId)
                .MaxAsync(
                    x => (int?)x.analisisSueloCalculoId,
                    cancellationToken) ?? 0;

            DateTime? ultimaFecha =
                await db.AnalisisSueloCalculos
                    .AsNoTracking()
                    .Where(x => x.usuarioId == usuarioId)
                    .MaxAsync(
                        x => (DateTime?)x.fechaCalculo,
                        cancellationToken);

            return
                $"usuario:{usuarioId};" +
                $"total:{total};" +
                $"ultimo:{ultimoId};" +
                $"fecha:{ultimaFecha:O}";
        }

        private static async Task<ModuloManifiesto>
            CalcularModuloAsync(
                DbContext context,
                string codigo,
                string nombre,
                ISet<string> entidades,
                bool habilitado,
                string semillaAdicional,
                CancellationToken cancellationToken)
        {
            if (!habilitado)
                return ModuloManifiesto.NoHabilitado(
                    codigo,
                    nombre);

            List<TablaVersion> tablas =
                ConstruirTablas(context, entidades);

            var partes = new List<string>
            {
                $"esquema:{EsquemaVersion}",
                $"modulo:{codigo}",
                semillaAdicional
            };

            long totalRegistros = 0;

            DbConnection connection =
                context.Database.GetDbConnection();

            bool cerrar =
                connection.State != ConnectionState.Open;

            if (cerrar)
                await connection.OpenAsync(cancellationToken);

            try
            {
                foreach (TablaVersion tabla in tablas)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ResultadoTabla resultado =
                        await CalcularTablaAsync(
                            connection,
                            tabla,
                            cancellationToken);

                    totalRegistros += resultado.TotalRegistros;

                    partes.Add(
                        $"{tabla.Esquema}.{tabla.Nombre}:" +
                        $"{resultado.TotalRegistros}:" +
                        $"{resultado.ChecksumAgregado}:" +
                        $"{resultado.SumaChecksums}");
                }
            }
            finally
            {
                if (cerrar &&
                    connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }

            return new ModuloManifiesto
            {
                Codigo = codigo,
                Nombre = nombre,
                Habilitado = true,
                Version = CalcularSha256(
                    string.Join("|", partes)),
                TotalRegistros = totalRegistros
            };
        }

        private static List<TablaVersion> ConstruirTablas(
            DbContext context,
            ISet<string> entidades)
        {
            var tablas = new Dictionary<
                string,
                TablaVersion>(StringComparer.OrdinalIgnoreCase);

            foreach (IEntityType entityType in context.Model
                         .GetEntityTypes()
                         .Where(x =>
                             entidades.Contains(x.ClrType.Name)))
            {
                string? tableName = entityType.GetTableName();
                if (string.IsNullOrWhiteSpace(tableName))
                    continue;

                string schema = entityType.GetSchema() ?? "dbo";
                string key = $"{schema}.{tableName}";

                if (!tablas.TryGetValue(
                        key,
                        out TablaVersion? tabla))
                {
                    tabla = new TablaVersion
                    {
                        Esquema = schema,
                        Nombre = tableName
                    };

                    tablas[key] = tabla;
                }

                StoreObjectIdentifier storeObject =
                    StoreObjectIdentifier.Table(
                        tableName,
                        schema);

                foreach (IProperty property in entityType.GetProperties())
                {
                    string? columnName =
                        property.GetColumnName(storeObject);

                    if (string.IsNullOrWhiteSpace(columnName))
                        continue;

                    string storeType =
                        property.GetColumnType() ??
                        property
                            .GetRelationalTypeMapping()
                            .StoreType;

                    if (EsTipoNoCompatible(storeType))
                        continue;

                    tabla.Columnas.Add(columnName);
                }
            }

            return tablas.Values
                .OrderBy(x => x.Esquema)
                .ThenBy(x => x.Nombre)
                .ToList();
        }

        private static async Task<ResultadoTabla>
            CalcularTablaAsync(
                DbConnection connection,
                TablaVersion tabla,
                CancellationToken cancellationToken)
        {
            string tablaSql =
                $"{EscaparIdentificador(tabla.Esquema)}." +
                EscaparIdentificador(tabla.Nombre);

            string[] columnas = tabla.Columnas
                .OrderBy(x => x)
                .Select(EscaparIdentificador)
                .ToArray();

            string checksumFila = columnas.Length == 0
                ? "0"
                : $"BINARY_CHECKSUM({string.Join(",", columnas)})";

            await using DbCommand command =
                connection.CreateCommand();

            command.CommandText =
                $"SELECT COUNT_BIG(1), " +
                $"COALESCE(CHECKSUM_AGG({checksumFila}), 0), " +
                $"COALESCE(SUM(CAST({checksumFila} AS BIGINT)), 0) " +
                $"FROM {tablaSql} WITH (READUNCOMMITTED);";

            try
            {
                await using DbDataReader reader =
                    await command.ExecuteReaderAsync(
                        cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                    return new ResultadoTabla();

                return new ResultadoTabla
                {
                    TotalRegistros = Convert.ToInt64(
                        reader.GetValue(0)),
                    ChecksumAgregado = Convert.ToInt64(
                        reader.GetValue(1)),
                    SumaChecksums = Convert.ToInt64(
                        reader.GetValue(2))
                };
            }
            catch (DbException)
            {
                /*
                 * Respaldo para una columna futura cuyo tipo no sea admitido
                 * por BINARY_CHECKSUM. El conteo continúa detectando altas y
                 * bajas, sin impedir que la página compruebe los demás módulos.
                 */
                await using DbCommand fallback =
                    connection.CreateCommand();

                fallback.CommandText =
                    $"SELECT COUNT_BIG(1) " +
                    $"FROM {tablaSql} WITH (READUNCOMMITTED);";

                object? total = await fallback.ExecuteScalarAsync(
                    cancellationToken);

                return new ResultadoTabla
                {
                    TotalRegistros = Convert.ToInt64(total ?? 0),
                    ChecksumAgregado = 0,
                    SumaChecksums = 0
                };
            }
        }

        private static bool EsTipoNoCompatible(string storeType)
        {
            string type = storeType
                .Trim()
                .ToLowerInvariant();

            return type.StartsWith("text") ||
                   type.StartsWith("ntext") ||
                   type.StartsWith("image") ||
                   type.StartsWith("xml") ||
                   type.StartsWith("geography") ||
                   type.StartsWith("geometry") ||
                   type.StartsWith("hierarchyid") ||
                   type.StartsWith("sql_variant");
        }

        private static string EscaparIdentificador(string value) =>
            "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";

        private static string CalcularSha256(string value)
        {
            byte[] hash = SHA256.HashData(
                Encoding.UTF8.GetBytes(value));

            return Convert.ToHexString(hash)
                .ToLowerInvariant();
        }

        private sealed class UsuarioAcceso
        {
            public int UsuarioId { get; init; }
            public int RolId { get; init; }
        }

        private sealed class TablaVersion
        {
            public string Esquema { get; init; } = "dbo";
            public string Nombre { get; init; } = string.Empty;
            public HashSet<string> Columnas { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ResultadoTabla
        {
            public long TotalRegistros { get; init; }
            public long ChecksumAgregado { get; init; }
            public long SumaChecksums { get; init; }
        }

        private sealed class ModuloManifiesto
        {
            public string Codigo { get; init; } = string.Empty;
            public string Nombre { get; init; } = string.Empty;
            public bool Habilitado { get; init; }
            public string Version { get; init; } = string.Empty;
            public long TotalRegistros { get; init; }

            public static ModuloManifiesto NoHabilitado(
                string codigo,
                string nombre) =>
                new()
                {
                    Codigo = codigo,
                    Nombre = nombre,
                    Habilitado = false,
                    Version = string.Empty,
                    TotalRegistros = 0
                };
        }
    }
}
