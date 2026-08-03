using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    /// Mantiene las sesiones activas en SQL Server.
    ///
    /// A diferencia del registro anterior en memoria, las sesiones sobreviven
    /// a reciclajes de IIS y pueden ser consultadas por varias instancias del
    /// backend que utilicen la misma base de datos.
    /// </summary>
    public sealed class SesionActivaService
    {
        private static long registrosCreados;

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

            if (Interlocked.Increment(ref registrosCreados) % 100 == 0)
            {
                await LimpiarExpiradasAsync(cancellationToken);
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

            SesionPersistida? sesion =
                await ObtenerAsync(
                    sesionId,
                    cancellationToken);

            if (sesion == null)
                return EstadoSesionToken.NoRegistrada;

            if (sesion.Revocada)
                return EstadoSesionToken.Revocada;

            if (sesion.UsuarioId != usuarioId ||
                sesion.VersionSesion != versionSesion)
            {
                await RevocarAsync(
                    sesionId,
                    "IDENTIDAD_NO_COINCIDE",
                    CancellationToken.None);

                return EstadoSesionToken.NoCoincide;
            }

            DateTime ahoraUtc = DateTime.UtcNow;

            if (ahoraUtc >= sesion.ExpiraUtc)
            {
                await RevocarAsync(
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
                await RevocarAsync(
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
                        5,
                        300);

                if (ahoraUtc - sesion.UltimaActividadUtc >=
                    TimeSpan.FromSeconds(segundosActualizacion))
                {
                    await ActualizarActividadAsync(
                        sesionId,
                        ahoraUtc,
                        segundosActualizacion,
                        cancellationToken);
                }
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

        private async Task<SesionPersistida?> ObtenerAsync(
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

                command.CommandText = """
                    SELECT TOP (1)
                           [UsuarioId],
                           [VersionSesion],
                           [UltimaActividadUtc],
                           [ExpiraUtc],
                           [Revocada]
                      FROM [dbo].[sesionActiva]
                     WHERE [SesionId] = @SesionId;
                    """;

                AgregarParametro(
                    command,
                    "@SesionId",
                    DbType.String,
                    sesionId,
                    64);

                await using DbDataReader reader =
                    await command.ExecuteReaderAsync(cancellationToken);

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
                        reader.GetBoolean(4));
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

                await command.ExecuteNonQueryAsync(cancellationToken);
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
            bool Revocada);
    }
}
