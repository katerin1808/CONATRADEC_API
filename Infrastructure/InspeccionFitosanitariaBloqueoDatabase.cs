using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Mantiene un bloqueo temporal por inspección y etapa de trabajo.
    ///
    /// La etapa ANALIZADOR tiene un bloqueo independiente de APROBADOR para
    /// conservar el flujo por fotografía: un analizador puede continuar con
    /// evidencias pendientes mientras un aprobador atiende otras ya enviadas.
    /// Dentro de una misma etapa solo una sesión puede mantener abierta la
    /// inspección a la vez.
    /// </summary>
    public sealed class InspeccionFitosanitariaBloqueoDatabase
    {
        private static readonly SemaphoreSlim InicializacionLock = new(1, 1);
        private static volatile bool inicializada;

        private readonly DiagnosticoIADbContext db;

        public const int VigenciaSegundos = 180;

        public InspeccionFitosanitariaBloqueoDatabase(
            DiagnosticoIADbContext db)
        {
            this.db = db;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            if (inicializada)
                return;

            await InicializacionLock.WaitAsync(cancellationToken);
            try
            {
                if (inicializada)
                    return;

                const string sql = """
IF OBJECT_ID(N'[dbo].[diagnosticoIAEdicionBloqueo]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAEdicionBloqueo]
    (
        [DiagnosticoIAId] INT NOT NULL,
        [Etapa] NVARCHAR(20) NOT NULL,
        [UsuarioId] INT NOT NULL,
        [TokenSesion] UNIQUEIDENTIFIER NOT NULL,
        [FechaAdquisicionUtc] DATETIME2(0) NOT NULL,
        [UltimoHeartbeatUtc] DATETIME2(0) NOT NULL,
        [ExpiraUtc] DATETIME2(0) NOT NULL,
        CONSTRAINT [PK_diagnosticoIAEdicionBloqueo]
            PRIMARY KEY ([DiagnosticoIAId], [Etapa]),
        CONSTRAINT [FK_diagIAEdicionBloqueo_diagnostico]
            FOREIGN KEY ([DiagnosticoIAId])
            REFERENCES [dbo].[diagnosticoIA]([DiagnosticoIAId])
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_diagIAEdicionBloqueo_expira'
      AND [object_id] = OBJECT_ID(N'[dbo].[diagnosticoIAEdicionBloqueo]')
)
BEGIN
    CREATE INDEX [IX_diagIAEdicionBloqueo_expira]
        ON [dbo].[diagnosticoIAEdicionBloqueo]
           ([ExpiraUtc], [DiagnosticoIAId], [Etapa]);
END;
""";

                await db.Database.ExecuteSqlRawAsync(
                    sql,
                    cancellationToken);

                inicializada = true;
            }
            catch
            {
                inicializada = false;
                throw;
            }
            finally
            {
                InicializacionLock.Release();
            }
        }

        public async Task<ResultadoAdquisicionBloqueoInspeccion> AdquirirAsync(
            int inspeccionId,
            int usuarioId,
            string etapa,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            string etapaNormalizada = NormalizarEtapa(etapa);
            if (string.IsNullOrWhiteSpace(etapaNormalizada))
            {
                return new ResultadoAdquisicionBloqueoInspeccion(
                    false,
                    "La etapa indicada no admite bloqueo exclusivo.",
                    null);
            }

            DbConnection conexion = db.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;
            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            DbTransaction? transaccionPropia = null;
            DbTransaction? transaccion =
                db.Database.CurrentTransaction?.GetDbTransaction();

            if (transaccion == null)
            {
                transaccionPropia = await conexion.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                transaccion = transaccionPropia;
            }

            try
            {
                const string limpiar = """
DELETE FROM dbo.diagnosticoIAEdicionBloqueo
WHERE DiagnosticoIAId = @id
  AND Etapa = @etapa
  AND ExpiraUtc <= SYSUTCDATETIME();
""";

                await using (DbCommand comando = CrearComando(
                    conexion,
                    limpiar,
                    transaccion))
                {
                    AgregarParametro(comando, "@id", inspeccionId);
                    AgregarParametro(comando, "@etapa", etapaNormalizada);
                    await comando.ExecuteNonQueryAsync(cancellationToken);
                }

                BloqueoInspeccionRegistro? existente =
                    await ObtenerDentroTransaccionAsync(
                        conexion,
                        transaccion,
                        inspeccionId,
                        etapaNormalizada,
                        cancellationToken);

                if (existente != null)
                {
                    if (transaccionPropia != null)
                        await transaccionPropia.CommitAsync(cancellationToken);

                    string mensaje = existente.UsuarioId == usuarioId
                        ? "Esta misma cuenta ya tiene abierta la inspección en otra sesión o ventana. Cierre la otra sesión o espere a que el bloqueo venza automáticamente."
                        : "La inspección ya está abierta por otro usuario en esta etapa.";

                    return new ResultadoAdquisicionBloqueoInspeccion(
                        false,
                        mensaje,
                        existente);
                }

                Guid token = Guid.NewGuid();
                DateTime ahora = DateTime.UtcNow;
                DateTime expira = ahora.AddSeconds(VigenciaSegundos);

                const string insertar = """
INSERT INTO dbo.diagnosticoIAEdicionBloqueo
(
    DiagnosticoIAId,
    Etapa,
    UsuarioId,
    TokenSesion,
    FechaAdquisicionUtc,
    UltimoHeartbeatUtc,
    ExpiraUtc
)
VALUES
(
    @id,
    @etapa,
    @usuarioId,
    @token,
    @ahora,
    @ahora,
    @expira
);
""";

                await using (DbCommand comando = CrearComando(
                    conexion,
                    insertar,
                    transaccion))
                {
                    AgregarParametro(comando, "@id", inspeccionId);
                    AgregarParametro(comando, "@etapa", etapaNormalizada);
                    AgregarParametro(comando, "@usuarioId", usuarioId);
                    AgregarParametro(comando, "@token", token);
                    AgregarParametro(comando, "@ahora", ahora);
                    AgregarParametro(comando, "@expira", expira);
                    await comando.ExecuteNonQueryAsync(cancellationToken);
                }

                if (transaccionPropia != null)
                    await transaccionPropia.CommitAsync(cancellationToken);

                var registro = new BloqueoInspeccionRegistro
                {
                    InspeccionId = inspeccionId,
                    Etapa = etapaNormalizada,
                    UsuarioId = usuarioId,
                    TokenSesion = token,
                    FechaAdquisicionUtc = ahora,
                    UltimoHeartbeatUtc = ahora,
                    ExpiraUtc = expira
                };

                return new ResultadoAdquisicionBloqueoInspeccion(
                    true,
                    "Bloqueo exclusivo adquirido correctamente.",
                    registro);
            }
            catch
            {
                if (transaccionPropia != null)
                {
                    await transaccionPropia.RollbackAsync(
                        CancellationToken.None);
                }

                throw;
            }
            finally
            {
                if (transaccionPropia != null)
                    await transaccionPropia.DisposeAsync();

                if (cerrar && db.Database.CurrentTransaction == null)
                    await conexion.CloseAsync();
            }
        }

        public async Task<BloqueoInspeccionRegistro?> RenovarAsync(
            int inspeccionId,
            int usuarioId,
            string etapa,
            Guid tokenSesion,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            string etapaNormalizada = NormalizarEtapa(etapa);
            if (string.IsNullOrWhiteSpace(etapaNormalizada))
                return null;

            const string sql = """
UPDATE dbo.diagnosticoIAEdicionBloqueo
SET UltimoHeartbeatUtc = SYSUTCDATETIME(),
    ExpiraUtc = DATEADD(SECOND, @vigencia, SYSUTCDATETIME())
WHERE DiagnosticoIAId = @id
  AND Etapa = @etapa
  AND UsuarioId = @usuarioId
  AND TokenSesion = @token
  AND ExpiraUtc > SYSUTCDATETIME();

SELECT
    DiagnosticoIAId,
    Etapa,
    UsuarioId,
    TokenSesion,
    FechaAdquisicionUtc,
    UltimoHeartbeatUtc,
    ExpiraUtc
FROM dbo.diagnosticoIAEdicionBloqueo
WHERE DiagnosticoIAId = @id
  AND Etapa = @etapa
  AND UsuarioId = @usuarioId
  AND TokenSesion = @token
  AND ExpiraUtc > SYSUTCDATETIME();
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(
                    conexion,
                    sql);

                AgregarParametro(comando, "@id", inspeccionId);
                AgregarParametro(comando, "@etapa", etapaNormalizada);
                AgregarParametro(comando, "@usuarioId", usuarioId);
                AgregarParametro(comando, "@token", tokenSesion);
                AgregarParametro(
                    comando,
                    "@vigencia",
                    VigenciaSegundos);

                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                    return null;

                return Leer(reader);
            }, cancellationToken);
        }

        public async Task LiberarAsync(
            int inspeccionId,
            int usuarioId,
            string etapa,
            Guid tokenSesion,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            string etapaNormalizada = NormalizarEtapa(etapa);
            if (string.IsNullOrWhiteSpace(etapaNormalizada))
                return;

            const string sql = """
DELETE FROM dbo.diagnosticoIAEdicionBloqueo
WHERE DiagnosticoIAId = @id
  AND Etapa = @etapa
  AND UsuarioId = @usuarioId
  AND TokenSesion = @token;
""";

            await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(
                    conexion,
                    sql);

                AgregarParametro(comando, "@id", inspeccionId);
                AgregarParametro(comando, "@etapa", etapaNormalizada);
                AgregarParametro(comando, "@usuarioId", usuarioId);
                AgregarParametro(comando, "@token", tokenSesion);
                await comando.ExecuteNonQueryAsync(cancellationToken);
                return 0;
            }, cancellationToken);
        }

        public async Task<ResultadoValidacionBloqueoInspeccion>
            ValidarPropietarioActivoAsync(
                int inspeccionId,
                int usuarioId,
                string etapa,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            string etapaNormalizada = NormalizarEtapa(etapa);
            if (string.IsNullOrWhiteSpace(etapaNormalizada))
            {
                return new ResultadoValidacionBloqueoInspeccion(
                    false,
                    "La etapa indicada no admite bloqueo exclusivo.",
                    null);
            }

            BloqueoInspeccionRegistro? bloqueo = await ObtenerActivoAsync(
                inspeccionId,
                etapaNormalizada,
                cancellationToken);

            if (bloqueo == null)
            {
                return new ResultadoValidacionBloqueoInspeccion(
                    false,
                    "La sesión exclusiva de esta ficha ya no está activa. Regrese a la bandeja y abra nuevamente la inspección.",
                    null);
            }

            if (bloqueo.UsuarioId != usuarioId)
            {
                return new ResultadoValidacionBloqueoInspeccion(
                    false,
                    "La ficha está bloqueada por otro usuario en esta etapa.",
                    bloqueo);
            }

            return new ResultadoValidacionBloqueoInspeccion(
                true,
                "La sesión mantiene el bloqueo exclusivo.",
                bloqueo);
        }

        public async Task<BloqueoInspeccionRegistro?> ObtenerActivoAsync(
            int inspeccionId,
            string etapa,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            string etapaNormalizada = NormalizarEtapa(etapa);
            if (string.IsNullOrWhiteSpace(etapaNormalizada))
                return null;

            const string sql = """
DELETE FROM dbo.diagnosticoIAEdicionBloqueo
WHERE DiagnosticoIAId = @id
  AND Etapa = @etapa
  AND ExpiraUtc <= SYSUTCDATETIME();

SELECT
    DiagnosticoIAId,
    Etapa,
    UsuarioId,
    TokenSesion,
    FechaAdquisicionUtc,
    UltimoHeartbeatUtc,
    ExpiraUtc
FROM dbo.diagnosticoIAEdicionBloqueo
WHERE DiagnosticoIAId = @id
  AND Etapa = @etapa
  AND ExpiraUtc > SYSUTCDATETIME();
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(
                    conexion,
                    sql);

                AgregarParametro(comando, "@id", inspeccionId);
                AgregarParametro(comando, "@etapa", etapaNormalizada);

                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                    return null;

                return Leer(reader);
            }, cancellationToken);
        }

        public static string NormalizarEtapa(string? etapa)
        {
            string valor = (etapa ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            return valor switch
            {
                "ANALIZADOR" => "ANALIZADOR",
                "APROBADOR" => "APROBADOR",
                _ => string.Empty
            };
        }

        private async Task<BloqueoInspeccionRegistro?>
            ObtenerDentroTransaccionAsync(
                DbConnection conexion,
                DbTransaction transaccion,
                int inspeccionId,
                string etapa,
                CancellationToken cancellationToken)
        {
            const string sql = """
SELECT
    DiagnosticoIAId,
    Etapa,
    UsuarioId,
    TokenSesion,
    FechaAdquisicionUtc,
    UltimoHeartbeatUtc,
    ExpiraUtc
FROM dbo.diagnosticoIAEdicionBloqueo WITH (UPDLOCK, HOLDLOCK)
WHERE DiagnosticoIAId = @id
  AND Etapa = @etapa;
""";

            await using DbCommand comando = CrearComando(
                conexion,
                sql,
                transaccion);

            AgregarParametro(comando, "@id", inspeccionId);
            AgregarParametro(comando, "@etapa", etapa);

            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
                return null;

            return Leer(reader);
        }

        private async Task<T> EjecutarAsync<T>(
            Func<DbConnection, Task<T>> accion,
            CancellationToken cancellationToken)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;
            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            try
            {
                return await accion(conexion);
            }
            finally
            {
                if (cerrar && db.Database.CurrentTransaction == null)
                    await conexion.CloseAsync();
            }
        }

        private DbCommand CrearComando(
            DbConnection conexion,
            string sql,
            DbTransaction? transaccion = null)
        {
            DbCommand comando = conexion.CreateCommand();
            comando.CommandText = sql;
            comando.CommandType = CommandType.Text;
            comando.CommandTimeout = 60;
            comando.Transaction = transaccion ??
                db.Database.CurrentTransaction?.GetDbTransaction();
            return comando;
        }

        private static void AgregarParametro(
            DbCommand comando,
            string nombre,
            object? valor)
        {
            DbParameter parametro = comando.CreateParameter();
            parametro.ParameterName = nombre;
            parametro.Value = valor ?? DBNull.Value;
            comando.Parameters.Add(parametro);
        }

        private static BloqueoInspeccionRegistro Leer(
            DbDataReader reader) =>
            new()
            {
                InspeccionId = reader.GetInt32(0),
                Etapa = reader.GetString(1),
                UsuarioId = reader.GetInt32(2),
                TokenSesion = reader.GetGuid(3),
                FechaAdquisicionUtc = DateTime.SpecifyKind(
                    reader.GetDateTime(4),
                    DateTimeKind.Utc),
                UltimoHeartbeatUtc = DateTime.SpecifyKind(
                    reader.GetDateTime(5),
                    DateTimeKind.Utc),
                ExpiraUtc = DateTime.SpecifyKind(
                    reader.GetDateTime(6),
                    DateTimeKind.Utc)
            };
    }

    public sealed class BloqueoInspeccionRegistro
    {
        public int InspeccionId { get; set; }
        public string Etapa { get; set; } = string.Empty;
        public int UsuarioId { get; set; }
        public Guid TokenSesion { get; set; }
        public DateTime FechaAdquisicionUtc { get; set; }
        public DateTime UltimoHeartbeatUtc { get; set; }
        public DateTime ExpiraUtc { get; set; }
    }

    public sealed record ResultadoAdquisicionBloqueoInspeccion(
        bool Exitoso,
        string Mensaje,
        BloqueoInspeccionRegistro? Bloqueo);

    public sealed record ResultadoValidacionBloqueoInspeccion(
        bool Permitido,
        string Mensaje,
        BloqueoInspeccionRegistro? Bloqueo);
}
