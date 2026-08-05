using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Mantiene el nombre y el cierre definitivo de cada inspección. La
    /// inicialización es idempotente y no requiere ejecutar scripts manuales.
    /// </summary>
    public sealed class InspeccionFitosanitariaControlDatabaseInitializer
    {
        private static readonly SemaphoreSlim InicializacionLock = new(1, 1);
        private static volatile bool inicializada;
        private readonly DiagnosticoIADbContext db;

        public InspeccionFitosanitariaControlDatabaseInitializer(
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
IF COL_LENGTH(N'dbo.diagnosticoIA', N'NombreInspeccion') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [NombreInspeccion] NVARCHAR(150) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_nombreInspeccion] DEFAULT(N'');

IF COL_LENGTH(N'dbo.diagnosticoIA', N'CerradaTecnico') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [CerradaTecnico] BIT NOT NULL
            CONSTRAINT [DF_diagnosticoIA_cerradaTecnico] DEFAULT(0);

IF COL_LENGTH(N'dbo.diagnosticoIA', N'FechaCierreTecnicoUtc') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [FechaCierreTecnicoUtc] DATETIME2(0) NULL;

IF COL_LENGTH(N'dbo.diagnosticoIA', N'UsuarioCierreTecnicoId') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA]
        ADD [UsuarioCierreTecnicoId] INT NULL;

EXEC(N'
UPDATE dbo.diagnosticoIA
SET NombreInspeccion = N''Inspección #'' +
    CONVERT(NVARCHAR(20), DiagnosticoIAId)
WHERE LEN(LTRIM(RTRIM(NombreInspeccion))) = 0;');

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE [name] = N'IX_diagnosticoIA_cierre_bandeja'
      AND [object_id] = OBJECT_ID(N'[dbo].[diagnosticoIA]')
)
BEGIN
    EXEC(N'CREATE INDEX [IX_diagnosticoIA_cierre_bandeja]
        ON [dbo].[diagnosticoIA]
           ([CerradaTecnico], [FechaSolicitudUtc] DESC,
            [DiagnosticoIAId] DESC)
        INCLUDE ([NombreInspeccion], [UsuarioSolicitanteId], [Activo]);');
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

        public async Task<InspeccionFitosanitariaControlRegistro?> ObtenerAsync(
            int inspeccionId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
SELECT
    DiagnosticoIAId,
    UsuarioSolicitanteId,
    NombreInspeccion,
    CerradaTecnico,
    FechaCierreTecnicoUtc,
    UsuarioCierreTecnicoId,
    Activo
FROM dbo.diagnosticoIA
WHERE DiagnosticoIAId = @id;
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = conexion.CreateCommand();
                comando.CommandText = sql;
                AgregarParametro(comando, "@id", inspeccionId);

                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                if (!await reader.ReadAsync(cancellationToken))
                    return null;

                return new InspeccionFitosanitariaControlRegistro
                {
                    InspeccionId = reader.GetInt32(0),
                    UsuarioSolicitanteId = reader.GetInt32(1),
                    NombreInspeccion = reader.IsDBNull(2)
                        ? string.Empty
                        : reader.GetString(2),
                    CerradaTecnico = reader.GetBoolean(3),
                    FechaCierreTecnicoUtc = reader.IsDBNull(4)
                        ? null
                        : DateTime.SpecifyKind(
                            reader.GetDateTime(4), DateTimeKind.Utc),
                    UsuarioCierreTecnicoId = reader.IsDBNull(5)
                        ? null
                        : reader.GetInt32(5),
                    Activo = reader.GetBoolean(6)
                };
            }, cancellationToken);
        }

        public async Task ActualizarNombreAsync(
            int inspeccionId,
            string? nombre,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);
            string valor = (nombre ?? string.Empty).Trim();
            if (valor.Length == 0)
                valor = $"Inspección #{inspeccionId}";
            if (valor.Length > 150)
                valor = valor[..150];

            const string sql = """
UPDATE dbo.diagnosticoIA
SET NombreInspeccion = @nombre
WHERE DiagnosticoIAId = @id
  AND Activo = 1
  AND CerradaTecnico = 0;
""";

            await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = conexion.CreateCommand();
                comando.CommandText = sql;
                AgregarParametro(comando, "@id", inspeccionId);
                AgregarParametro(comando, "@nombre", valor);
                await comando.ExecuteNonQueryAsync(cancellationToken);
                return 0;
            }, cancellationToken);
        }

        public async Task<bool> TieneProcesamientoActivoAsync(
            int inspeccionId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);
            const string sql = """
SELECT CASE WHEN EXISTS
(
    SELECT 1
    FROM dbo.diagnosticoIAImagen
    WHERE DiagnosticoIAId = @id
      AND ISNULL(Activo, 1) = 1
      AND UPPER(ISNULL(Estado, N'BORRADOR')) IN
          (N'PENDIENTE_IA', N'ANALIZANDO_IA')
) THEN 1 ELSE 0 END;
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = conexion.CreateCommand();
                comando.CommandText = sql;
                AgregarParametro(comando, "@id", inspeccionId);
                object? valor = await comando.ExecuteScalarAsync(cancellationToken);
                return Convert.ToInt32(valor ?? 0) == 1;
            }, cancellationToken);
        }

        public async Task<bool> CerrarDefinitivamenteAsync(
            int inspeccionId,
            int usuarioId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);
            const string sql = """
UPDATE dbo.diagnosticoIA
SET CerradaTecnico = 1,
    FechaCierreTecnicoUtc = SYSUTCDATETIME(),
    UsuarioCierreTecnicoId = @usuarioId
WHERE DiagnosticoIAId = @id
  AND UsuarioSolicitanteId = @usuarioId
  AND Activo = 1
  AND CerradaTecnico = 0
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.diagnosticoIAImagen i
      WHERE i.DiagnosticoIAId = dbo.diagnosticoIA.DiagnosticoIAId
        AND ISNULL(i.Activo, 1) = 1
        AND UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
            (N'PENDIENTE_IA', N'ANALIZANDO_IA')
  );
SELECT @@ROWCOUNT;
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = conexion.CreateCommand();
                comando.CommandText = sql;
                AgregarParametro(comando, "@id", inspeccionId);
                AgregarParametro(comando, "@usuarioId", usuarioId);
                object? valor = await comando.ExecuteScalarAsync(cancellationToken);
                return Convert.ToInt32(valor ?? 0) == 1;
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
                if (cerrar)
                    await conexion.CloseAsync();
            }
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
    }

    public sealed class InspeccionFitosanitariaControlRegistro
    {
        public int InspeccionId { get; set; }
        public int UsuarioSolicitanteId { get; set; }
        public string NombreInspeccion { get; set; } = string.Empty;
        public bool CerradaTecnico { get; set; }
        public DateTime? FechaCierreTecnicoUtc { get; set; }
        public int? UsuarioCierreTecnicoId { get; set; }
        public bool Activo { get; set; }
    }
}
