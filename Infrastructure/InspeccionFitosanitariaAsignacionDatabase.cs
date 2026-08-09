using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Controla la asignación exclusiva de las etapas de análisis y aprobación.
    /// Tomar una inspección es una acción explícita; las operaciones técnicas
    /// posteriores únicamente validan que el usuario ya sea el responsable.
    ///
    /// Un mismo usuario puede asumir análisis y aprobación únicamente cuando
    /// sus permisos de interfaz lo autorizan. La trazabilidad conserva ambos
    /// identificadores para que el historial indique esa situación.
    /// </summary>
    public sealed class InspeccionFitosanitariaAsignacionDatabase
    {
        private static readonly SemaphoreSlim InicializacionLock = new(1, 1);
        private static volatile bool inicializada;
        private readonly DiagnosticoIADbContext db;

        public InspeccionFitosanitariaAsignacionDatabase(
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
IF OBJECT_ID(N'[dbo].[diagnosticoIAAsignacionFlujo]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAAsignacionFlujo]
    (
        [DiagnosticoIAId] INT NOT NULL,
        [UsuarioAnalizadorId] INT NULL,
        [FechaAsignacionAnalizadorUtc] DATETIME2(0) NULL,
        [UsuarioAprobadorId] INT NULL,
        [FechaAsignacionAprobadorUtc] DATETIME2(0) NULL,
        [FechaModificacionUtc] DATETIME2(0) NOT NULL
            CONSTRAINT [DF_diagIAAsignacion_fechaMod]
            DEFAULT(SYSUTCDATETIME()),
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT [PK_diagnosticoIAAsignacionFlujo]
            PRIMARY KEY ([DiagnosticoIAId]),
        CONSTRAINT [FK_diagIAAsignacion_diagnostico]
            FOREIGN KEY ([DiagnosticoIAId])
            REFERENCES [dbo].[diagnosticoIA]([DiagnosticoIAId])
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_diagIAAsignacion_analizador'
      AND [object_id] = OBJECT_ID(N'[dbo].[diagnosticoIAAsignacionFlujo]')
)
BEGIN
    EXEC(N'CREATE INDEX [IX_diagIAAsignacion_analizador]
        ON [dbo].[diagnosticoIAAsignacionFlujo]
           ([UsuarioAnalizadorId], [DiagnosticoIAId]);');
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_diagIAAsignacion_aprobador'
      AND [object_id] = OBJECT_ID(N'[dbo].[diagnosticoIAAsignacionFlujo]')
)
BEGIN
    EXEC(N'CREATE INDEX [IX_diagIAAsignacion_aprobador]
        ON [dbo].[diagnosticoIAAsignacionFlujo]
           ([UsuarioAprobadorId], [DiagnosticoIAId]);');
END;
""";

                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
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

        public async Task<InspeccionFitosanitariaAsignacionRegistro> ObtenerAsync(
            int inspeccionId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
SELECT
    DiagnosticoIAId,
    UsuarioAnalizadorId,
    FechaAsignacionAnalizadorUtc,
    UsuarioAprobadorId,
    FechaAsignacionAprobadorUtc,
    FechaModificacionUtc,
    RowVersion
FROM dbo.diagnosticoIAAsignacionFlujo
WHERE DiagnosticoIAId = @id;
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@id", inspeccionId);

                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                {
                    return new InspeccionFitosanitariaAsignacionRegistro
                    {
                        InspeccionId = inspeccionId
                    };
                }

                return Leer(reader);
            }, cancellationToken);
        }

        /// <summary>
        /// Valida que el usuario ya sea el analizador asignado. No crea una
        /// asignación implícita; una etapa libre debe tomarse expresamente.
        /// </summary>
        public Task<ResultadoAsignacionFlujo> TomarAnalizadorAsync(
            int inspeccionId,
            int usuarioId,
            CancellationToken cancellationToken = default) =>
            ValidarResponsableAsync(
                inspeccionId,
                usuarioId,
                etapaAnalizador: true,
                cancellationToken);

        /// <summary>
        /// Valida que el usuario ya sea el aprobador asignado. No crea una
        /// asignación implícita; una etapa libre debe tomarse expresamente.
        /// </summary>
        public Task<ResultadoAsignacionFlujo> TomarAprobadorAsync(
            int inspeccionId,
            int usuarioId,
            CancellationToken cancellationToken = default) =>
            ValidarResponsableAsync(
                inspeccionId,
                usuarioId,
                etapaAnalizador: false,
                cancellationToken);

        /// <summary>
        /// Asigna de forma explícita una etapa libre al analizador autenticado.
        /// Se usa únicamente por la acción visible "Tomar inspección".
        /// </summary>
        public Task<ResultadoAsignacionFlujo> AsignarAnalizadorAsync(
            int inspeccionId,
            int usuarioId,
            CancellationToken cancellationToken = default) =>
            AsignarSiDisponibleAsync(
                inspeccionId,
                usuarioId,
                etapaAnalizador: true,
                cancellationToken);

        /// <summary>
        /// Asigna de forma explícita una etapa libre al aprobador autenticado.
        /// Se usa únicamente por la acción visible "Tomar inspección".
        /// </summary>
        public Task<ResultadoAsignacionFlujo> AsignarAprobadorAsync(
            int inspeccionId,
            int usuarioId,
            CancellationToken cancellationToken = default) =>
            AsignarSiDisponibleAsync(
                inspeccionId,
                usuarioId,
                etapaAnalizador: false,
                cancellationToken);

        private async Task<ResultadoAsignacionFlujo> ValidarResponsableAsync(
            int inspeccionId,
            int usuarioId,
            bool etapaAnalizador,
            CancellationToken cancellationToken)
        {
            InspeccionFitosanitariaAsignacionRegistro registro =
                await ObtenerAsync(inspeccionId, cancellationToken);

            int? asignado = etapaAnalizador
                ? registro.UsuarioAnalizadorId
                : registro.UsuarioAprobadorId;

            if (!asignado.HasValue)
            {
                return new ResultadoAsignacionFlujo(
                    false,
                    etapaAnalizador
                        ? "La inspección todavía no tiene analizador asignado. Use 'Tomar inspección' antes de realizar acciones de análisis."
                        : "La inspección todavía no tiene aprobador asignado. Use 'Tomar inspección' antes de realizar acciones de aprobación.",
                    registro);
            }

            if (asignado.Value != usuarioId)
            {
                return new ResultadoAsignacionFlujo(
                    false,
                    $"La inspección ya está asignada al usuario #{asignado.Value} para esta etapa.",
                    registro);
            }

            return new ResultadoAsignacionFlujo(
                true,
                etapaAnalizador
                    ? "El usuario actual es el analizador responsable."
                    : "El usuario actual es el aprobador responsable.",
                registro);
        }

        private async Task<ResultadoAsignacionFlujo> AsignarSiDisponibleAsync(
            int inspeccionId,
            int usuarioId,
            bool etapaAnalizador,
            CancellationToken cancellationToken)
        {
            await InicializarAsync(cancellationToken);

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
                const string asegurar = """
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.diagnosticoIAAsignacionFlujo WITH (UPDLOCK, HOLDLOCK)
    WHERE DiagnosticoIAId = @id
)
BEGIN
    INSERT INTO dbo.diagnosticoIAAsignacionFlujo
    (
        DiagnosticoIAId,
        FechaModificacionUtc
    )
    VALUES
    (
        @id,
        SYSUTCDATETIME()
    );
END;
""";

                await using (DbCommand comando = CrearComando(
                    conexion,
                    asegurar,
                    transaccion))
                {
                    AgregarParametro(comando, "@id", inspeccionId);
                    await comando.ExecuteNonQueryAsync(cancellationToken);
                }

                string sql = etapaAnalizador
                    ? """
UPDATE dbo.diagnosticoIAAsignacionFlujo WITH (UPDLOCK, HOLDLOCK)
SET UsuarioAnalizadorId = COALESCE(UsuarioAnalizadorId, @usuarioId),
    FechaAsignacionAnalizadorUtc = COALESCE(
        FechaAsignacionAnalizadorUtc,
        SYSUTCDATETIME()),
    FechaModificacionUtc = SYSUTCDATETIME()
WHERE DiagnosticoIAId = @id
  AND (UsuarioAnalizadorId IS NULL OR UsuarioAnalizadorId = @usuarioId);
"""
                    : """
UPDATE dbo.diagnosticoIAAsignacionFlujo WITH (UPDLOCK, HOLDLOCK)
SET UsuarioAprobadorId = COALESCE(UsuarioAprobadorId, @usuarioId),
    FechaAsignacionAprobadorUtc = COALESCE(
        FechaAsignacionAprobadorUtc,
        SYSUTCDATETIME()),
    FechaModificacionUtc = SYSUTCDATETIME()
WHERE DiagnosticoIAId = @id
  AND (UsuarioAprobadorId IS NULL OR UsuarioAprobadorId = @usuarioId);
""";

                int actualizadas;
                await using (DbCommand comando = CrearComando(
                    conexion,
                    sql,
                    transaccion))
                {
                    AgregarParametro(comando, "@id", inspeccionId);
                    AgregarParametro(comando, "@usuarioId", usuarioId);
                    actualizadas = await comando.ExecuteNonQueryAsync(
                        cancellationToken);
                }

                InspeccionFitosanitariaAsignacionRegistro registro;
                const string seleccionar = """
SELECT
    DiagnosticoIAId,
    UsuarioAnalizadorId,
    FechaAsignacionAnalizadorUtc,
    UsuarioAprobadorId,
    FechaAsignacionAprobadorUtc,
    FechaModificacionUtc,
    RowVersion
FROM dbo.diagnosticoIAAsignacionFlujo WITH (HOLDLOCK)
WHERE DiagnosticoIAId = @id;
""";

                await using (DbCommand comando = CrearComando(
                    conexion,
                    seleccionar,
                    transaccion))
                {
                    AgregarParametro(comando, "@id", inspeccionId);
                    await using DbDataReader reader =
                        await comando.ExecuteReaderAsync(cancellationToken);
                    await reader.ReadAsync(cancellationToken);
                    registro = Leer(reader);
                }

                if (transaccionPropia != null)
                    await transaccionPropia.CommitAsync(cancellationToken);

                if (actualizadas == 1)
                {
                    return new ResultadoAsignacionFlujo(
                        true,
                        etapaAnalizador
                            ? "La inspección quedó asignada al analizador actual."
                            : "La inspección quedó asignada al aprobador actual.",
                        registro);
                }

                int? asignado = etapaAnalizador
                    ? registro.UsuarioAnalizadorId
                    : registro.UsuarioAprobadorId;

                string mensaje = asignado.HasValue
                    ? $"La inspección ya está asignada al usuario #{asignado.Value} para esta etapa."
                    : "No fue posible tomar la inspección porque cambió durante la operación.";

                return new ResultadoAsignacionFlujo(false, mensaje, registro);
            }
            catch
            {
                if (transaccionPropia != null)
                    await transaccionPropia.RollbackAsync(CancellationToken.None);
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

        public async Task ReabrirEtapaAnalizadorAsync(
            int inspeccionId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
UPDATE dbo.diagnosticoIARevisionAnalizadorControl
SET EtapaAnalizadorFinalizada = 0,
    FechaFinEtapaAnalizadorUtc = NULL,
    UsuarioFinEtapaAnalizadorId = NULL
WHERE DiagnosticoIAId = @id;

UPDATE dbo.diagnosticoIA
SET Estado = N'PENDIENTE_REVISION'
WHERE DiagnosticoIAId = @id
  AND ISNULL(CerradaDefinitiva, 0) = 0;
""";

            await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@id", inspeccionId);
                await comando.ExecuteNonQueryAsync(cancellationToken);
                return 0;
            }, cancellationToken);
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
            comando.CommandTimeout = 180;
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

        private static InspeccionFitosanitariaAsignacionRegistro Leer(
            DbDataReader reader) =>
            new()
            {
                InspeccionId = reader.GetInt32(0),
                UsuarioAnalizadorId = reader.IsDBNull(1)
                    ? null
                    : reader.GetInt32(1),
                FechaAsignacionAnalizadorUtc = reader.IsDBNull(2)
                    ? null
                    : DateTime.SpecifyKind(
                        reader.GetDateTime(2),
                        DateTimeKind.Utc),
                UsuarioAprobadorId = reader.IsDBNull(3)
                    ? null
                    : reader.GetInt32(3),
                FechaAsignacionAprobadorUtc = reader.IsDBNull(4)
                    ? null
                    : DateTime.SpecifyKind(
                        reader.GetDateTime(4),
                        DateTimeKind.Utc),
                FechaModificacionUtc = DateTime.SpecifyKind(
                    reader.GetDateTime(5),
                    DateTimeKind.Utc),
                VersionConcurrencia = Convert.ToBase64String(
                    (byte[])reader.GetValue(6))
            };
    }

    public sealed class InspeccionFitosanitariaAsignacionRegistro
    {
        public int InspeccionId { get; set; }
        public int? UsuarioAnalizadorId { get; set; }
        public DateTime? FechaAsignacionAnalizadorUtc { get; set; }
        public int? UsuarioAprobadorId { get; set; }
        public DateTime? FechaAsignacionAprobadorUtc { get; set; }
        public DateTime FechaModificacionUtc { get; set; }
        public string VersionConcurrencia { get; set; } = string.Empty;
    }

    public sealed record ResultadoAsignacionFlujo(
        bool Exitoso,
        string Mensaje,
        InspeccionFitosanitariaAsignacionRegistro Asignacion);
}
