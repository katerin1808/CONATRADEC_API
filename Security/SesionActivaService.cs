using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;

namespace CONATRADEC_API.Security
{
    public enum EstadoSesionToken
    {
        Valida,
        NoRegistrada,
        NoCoincide,
        Inactiva,
        Expirada,
        Revocada
    }

    /// <summary>
    /// Mantiene las sesiones activas en SQL Server y utiliza una caché local
    /// de corta duración para evitar una consulta SQL en cada solicitud.
    ///
    /// SQL Server continúa siendo la fuente definitiva. La caché únicamente
    /// conserva validaciones recientes y se descarta automáticamente.
    /// </summary>
    public sealed class SesionActivaService
    {
        private static readonly ConcurrentDictionary<
            string,
            SesionCacheEntry> cacheSesiones =
                new(StringComparer.Ordinal);

        private static long sesionesRegistradas;
        private static long validacionesRealizadas;

        private readonly DBContext db;
        private readonly IOptions<JwtOptions> options;

        public SesionActivaService(
            DBContext db,
            IOptions<JwtOptions> options)
        {
            this.db = db;
            this.options = options;
        }

        public async Task RegistrarAsync(
            string sesionId,
            int usuarioId,
            int versionSesion,
            DateTime expiraUtc,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sesionId))
            {
                throw new ArgumentException(
                    "El identificador de sesión es obligatorio.",
                    nameof(sesionId));
            }

            DateTime ahoraUtc = DateTime.UtcNow;

            const string sql = """
                INSERT INTO [dbo].[sesionActiva]
                (
                    [SesionId],
                    [UsuarioId],
                    [VersionSesion],
                    [CreadaUtc],
                    [UltimaActividadUtc],
                    [ExpiraUtc],
                    [Revocada],
                    [FechaRevocacionUtc],
                    [MotivoRevocacion],
                    [UltimaActualizacionUtc]
                )
                VALUES
                (
                    @SesionId,
                    @UsuarioId,
                    @VersionSesion,
                    @CreadaUtc,
                    @UltimaActividadUtc,
                    @ExpiraUtc,
                    0,
                    NULL,
                    NULL,
                    @UltimaActualizacionUtc
                );
                """;

            await EjecutarAsync(
                sql,
                command =>
                {
                    AgregarParametro(
                        command,
                        "@SesionId",
                        DbType.String,
                        sesionId,
                        64);

                    AgregarParametro(
                        command,
                        "@UsuarioId",
                        DbType.Int32,
                        usuarioId);

                    AgregarParametro(
                        command,
                        "@VersionSesion",
                        DbType.Int32,
                        versionSesion);

                    AgregarParametro(
                        command,
                        "@CreadaUtc",
                        DbType.DateTime2,
                        ahoraUtc);

                    AgregarParametro(
                        command,
                        "@UltimaActividadUtc",
                        DbType.DateTime2,
                        ahoraUtc);

                    AgregarParametro(
                        command,
                        "@ExpiraUtc",
                        DbType.DateTime2,
                        expiraUtc);

                    AgregarParametro(
                        command,
                        "@UltimaActualizacionUtc",
                        DbType.DateTime2,
                        ahoraUtc);
                },
                cancellationToken);

            GuardarEnCache(
                sesionId,
                new SesionPersistida(
                    UsuarioId: usuarioId,
                    VersionSesion: versionSesion,
                    UltimaActividadUtc: ahoraUtc,
                    ExpiraUtc: expiraUtc,
                    Revocada: false,
                    UsuarioActivo: true,
                    VersionUsuario: versionSesion),
                ahoraUtc);

            if (Interlocked.Increment(
                    ref sesionesRegistradas) % 100 == 0)
            {
                await LimpiarExpiradasAsync(cancellationToken);
                LimpiarCacheVencida(ahoraUtc);
            }
        }

        public async Task<EstadoSesionToken>
            ValidarYRegistrarActividadAsync(
                string sesionId,
                int usuarioId,
                int versionSesion,
                bool registrarActividad,
                CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sesionId))
            {
                throw new ArgumentException(
                    "El identificador de sesión es obligatorio.",
                    nameof(sesionId));
            }

            DateTime ahoraUtc = DateTime.UtcNow;

            SesionPersistida? sesion =
                await ObtenerConCacheAsync(
                    sesionId,
                    ahoraUtc,
                    cancellationToken);

            if (sesion == null)
                return EstadoSesionToken.NoRegistrada;

            if (sesion.Revocada)
            {
                cacheSesiones.TryRemove(sesionId, out _);
                return EstadoSesionToken.Revocada;
            }

            /*
             * Esta consulta ya contiene el estado y la versión actual del
             * usuario. Por eso VersionSesionMiddleware no necesita consultar
             * dbo.usuario nuevamente en todas las solicitudes.
             */
            if (!sesion.UsuarioActivo ||
                sesion.UsuarioId != usuarioId ||
                sesion.VersionSesion != versionSesion ||
                sesion.VersionUsuario != versionSesion)
            {
                cacheSesiones.TryRemove(sesionId, out _);

                await RevocarEnBaseAsync(
                    sesionId,
                    sesion.UsuarioActivo
                        ? "IDENTIDAD_O_VERSION_NO_COINCIDE"
                        : "USUARIO_INACTIVO",
                    CancellationToken.None);

                return EstadoSesionToken.NoCoincide;
            }

            if (ahoraUtc >= sesion.ExpiraUtc)
            {
                cacheSesiones.TryRemove(sesionId, out _);

                await RevocarEnBaseAsync(
                    sesionId,
                    "TOKEN_EXPIRADO",
                    CancellationToken.None);

                return EstadoSesionToken.Expirada;
            }

            int minutosInactividad =
                Math.Clamp(
                    options.Value.InactivityMinutes,
                    1,
                    1440);

            if (ahoraUtc - sesion.UltimaActividadUtc >=
                TimeSpan.FromMinutes(minutosInactividad))
            {
                cacheSesiones.TryRemove(sesionId, out _);

                await RevocarEnBaseAsync(
                    sesionId,
                    "INACTIVIDAD",
                    CancellationToken.None);

                return EstadoSesionToken.Inactiva;
            }

            if (registrarActividad)
            {
                int segundosActualizacion =
                    Math.Clamp(
                        options.Value.ActivityUpdateSeconds,
                        15,
                        300);

                if (ahoraUtc - sesion.UltimaActividadUtc >=
                    TimeSpan.FromSeconds(segundosActualizacion))
                {
                    await ActualizarActividadAsync(
                        sesionId,
                        ahoraUtc,
                        segundosActualizacion,
                        cancellationToken);

                    sesion = sesion with
                    {
                        UltimaActividadUtc = ahoraUtc
                    };

                    GuardarEnCache(
                        sesionId,
                        sesion,
                        ahoraUtc);
                }
            }

            if (Interlocked.Increment(
                    ref validacionesRealizadas) % 1000 == 0)
            {
                LimpiarCacheVencida(ahoraUtc);
            }

            return EstadoSesionToken.Valida;
        }

        public Task RevocarAsync(
            string sesionId,
            CancellationToken cancellationToken = default) =>
            RevocarAsync(
                sesionId,
                "CIERRE_DE_SESION",
                cancellationToken);

        public async Task RevocarAsync(
            string sesionId,
            string motivo,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sesionId))
                return;

            cacheSesiones.TryRemove(sesionId, out _);

            await RevocarEnBaseAsync(
                sesionId,
                motivo,
                cancellationToken);
        }

        private async Task<SesionPersistida?>
            ObtenerConCacheAsync(
                string sesionId,
                DateTime ahoraUtc,
                CancellationToken cancellationToken)
        {
            if (cacheSesiones.TryGetValue(
                    sesionId,
                    out SesionCacheEntry? cache) &&
                ahoraUtc < cache.VigenteHastaUtc)
            {
                return cache.Sesion;
            }

            cacheSesiones.TryRemove(sesionId, out _);

            SesionPersistida? sesion =
                await ObtenerDesdeBaseAsync(
                    sesionId,
                    cancellationToken);

            if (sesion != null)
            {
                GuardarEnCache(
                    sesionId,
                    sesion,
                    ahoraUtc);
            }

            return sesion;
        }

        private async Task<SesionPersistida?>
            ObtenerDesdeBaseAsync(
                string sesionId,
                CancellationToken cancellationToken)
        {
            DbConnection connection =
                db.Database.GetDbConnection();

            bool cerrarConexion =
                connection.State != ConnectionState.Open;

            if (cerrarConexion)
                await connection.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand command =
                    connection.CreateCommand();

                /*
                 * En una sola consulta se valida:
                 * - la sesión persistida;
                 * - el usuario activo;
                 * - la versión actual del usuario.
                 */
                command.CommandText = """
                    SELECT TOP (1)
                           s.[UsuarioId],
                           s.[VersionSesion],
                           s.[UltimaActividadUtc],
                           s.[ExpiraUtc],
                           s.[Revocada],
                           u.[activo],
                           u.[versionSesion]
                      FROM [dbo].[sesionActiva] AS s
                      INNER JOIN [dbo].[usuario] AS u
                              ON u.[UsuarioId] = s.[UsuarioId]
                     WHERE s.[SesionId] = @SesionId;
                    """;

                AgregarParametro(
                    command,
                    "@SesionId",
                    DbType.String,
                    sesionId,
                    64);

                await using DbDataReader reader =
                    await command.ExecuteReaderAsync(
                        cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                    return null;

                return new SesionPersistida(
                    UsuarioId:
                        reader.GetInt32(0),
                    VersionSesion:
                        reader.GetInt32(1),
                    UltimaActividadUtc:
                        AsegurarUtc(reader.GetDateTime(2)),
                    ExpiraUtc:
                        AsegurarUtc(reader.GetDateTime(3)),
                    Revocada:
                        reader.GetBoolean(4),
                    UsuarioActivo:
                        reader.GetBoolean(5),
                    VersionUsuario:
                        reader.GetInt32(6));
            }
            finally
            {
                if (cerrarConexion &&
                    connection.State != ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private async Task ActualizarActividadAsync(
            string sesionId,
            DateTime ahoraUtc,
            int segundosActualizacion,
            CancellationToken cancellationToken)
        {
            DateTime limiteAnterior =
                ahoraUtc.AddSeconds(-segundosActualizacion);

            const string sql = """
                UPDATE [dbo].[sesionActiva]
                   SET [UltimaActividadUtc] = @AhoraUtc,
                       [UltimaActualizacionUtc] = @AhoraUtc
                 WHERE [SesionId] = @SesionId
                   AND [Revocada] = 0
                   AND [UltimaActividadUtc] <= @LimiteAnterior;
                """;

            await EjecutarAsync(
                sql,
                command =>
                {
                    AgregarParametro(
                        command,
                        "@SesionId",
                        DbType.String,
                        sesionId,
                        64);

                    AgregarParametro(
                        command,
                        "@AhoraUtc",
                        DbType.DateTime2,
                        ahoraUtc);

                    AgregarParametro(
                        command,
                        "@LimiteAnterior",
                        DbType.DateTime2,
                        limiteAnterior);
                },
                cancellationToken);
        }

        private async Task RevocarEnBaseAsync(
            string sesionId,
            string motivo,
            CancellationToken cancellationToken)
        {
            DateTime ahoraUtc = DateTime.UtcNow;

            const string sql = """
                UPDATE [dbo].[sesionActiva]
                   SET [Revocada] = 1,
                       [FechaRevocacionUtc] =
                           COALESCE([FechaRevocacionUtc], @AhoraUtc),
                       [MotivoRevocacion] =
                           COALESCE([MotivoRevocacion], @Motivo),
                       [UltimaActualizacionUtc] = @AhoraUtc
                 WHERE [SesionId] = @SesionId
                   AND [Revocada] = 0;
                """;

            await EjecutarAsync(
                sql,
                command =>
                {
                    AgregarParametro(
                        command,
                        "@SesionId",
                        DbType.String,
                        sesionId,
                        64);

                    AgregarParametro(
                        command,
                        "@Motivo",
                        DbType.String,
                        NormalizarMotivo(motivo),
                        100);

                    AgregarParametro(
                        command,
                        "@AhoraUtc",
                        DbType.DateTime2,
                        ahoraUtc);
                },
                cancellationToken);
        }

        private void GuardarEnCache(
            string sesionId,
            SesionPersistida sesion,
            DateTime ahoraUtc)
        {
            int segundosCache =
                Math.Clamp(
                    options.Value.SessionCacheSeconds,
                    2,
                    60);

            DateTime vigenteHastaUtc =
                ahoraUtc.AddSeconds(segundosCache);

            if (vigenteHastaUtc > sesion.ExpiraUtc)
                vigenteHastaUtc = sesion.ExpiraUtc;

            cacheSesiones[sesionId] =
                new SesionCacheEntry(
                    sesion,
                    vigenteHastaUtc);
        }

        private static void LimpiarCacheVencida(
            DateTime ahoraUtc)
        {
            foreach (KeyValuePair<
                         string,
                         SesionCacheEntry> item
                     in cacheSesiones)
            {
                if (ahoraUtc >= item.Value.VigenteHastaUtc ||
                    ahoraUtc >= item.Value.Sesion.ExpiraUtc ||
                    item.Value.Sesion.Revocada)
                {
                    cacheSesiones.TryRemove(
                        item.Key,
                        out _);
                }
            }
        }

        private async Task LimpiarExpiradasAsync(
            CancellationToken cancellationToken)
        {
            const string sql = """
                DELETE FROM [dbo].[sesionActiva]
                 WHERE [ExpiraUtc] <
                       DATEADD(DAY, -1, SYSUTCDATETIME())
                    OR
                       (
                           [Revocada] = 1
                           AND [FechaRevocacionUtc] <
                               DATEADD(DAY, -1, SYSUTCDATETIME())
                       );
                """;

            await EjecutarAsync(
                sql,
                configure: null,
                cancellationToken);
        }

        private async Task EjecutarAsync(
            string sql,
            Action<DbCommand>? configure,
            CancellationToken cancellationToken)
        {
            DbConnection connection =
                db.Database.GetDbConnection();

            bool cerrarConexion =
                connection.State != ConnectionState.Open;

            if (cerrarConexion)
                await connection.OpenAsync(cancellationToken);

            try
            {
                await using DbCommand command =
                    connection.CreateCommand();

                command.CommandText = sql;
                configure?.Invoke(command);

                await command.ExecuteNonQueryAsync(
                    cancellationToken);
            }
            finally
            {
                if (cerrarConexion &&
                    connection.State != ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private static void AgregarParametro(
            DbCommand command,
            string nombre,
            DbType tipo,
            object valor,
            int? tamano = null)
        {
            DbParameter parameter =
                command.CreateParameter();

            parameter.ParameterName = nombre;
            parameter.DbType = tipo;
            parameter.Value = valor;

            if (tamano.HasValue)
                parameter.Size = tamano.Value;

            command.Parameters.Add(parameter);
        }

        private static DateTime AsegurarUtc(
            DateTime value) =>
            value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(
                    value,
                    DateTimeKind.Utc);

        private static string NormalizarMotivo(
            string? motivo)
        {
            string value =
                string.IsNullOrWhiteSpace(motivo)
                    ? "REVOCADA"
                    : motivo.Trim();

            return value.Length <= 100
                ? value
                : value[..100];
        }

        private sealed record SesionPersistida(
            int UsuarioId,
            int VersionSesion,
            DateTime UltimaActividadUtc,
            DateTime ExpiraUtc,
            bool Revocada,
            bool UsuarioActivo,
            int VersionUsuario);

        private sealed record SesionCacheEntry(
            SesionPersistida Sesion,
            DateTime VigenteHastaUtc);
    }
}
