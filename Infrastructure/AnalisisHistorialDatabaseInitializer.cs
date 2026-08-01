using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure;

/// <summary>
/// Crea de forma idempotente la estructura utilizada para:
/// - control optimista de concurrencia;
/// - fecha real de creación en el dispositivo;
/// - origen online/offline;
/// - versiones inmutables del reporte de análisis.
///
/// La estructura se crea en varios lotes SQL. Esto es necesario porque
/// SQL Server compila un lote completo antes de ejecutarlo y no permite que
/// un UPDATE del mismo lote haga referencia a columnas que todavía no existían
/// al comenzar la compilación.
/// </summary>
public sealed class AnalisisHistorialDatabaseInitializer
{
    private readonly DBContext db;
    private readonly ILogger<AnalisisHistorialDatabaseInitializer> logger;

    public AnalisisHistorialDatabaseInitializer(
        DBContext db,
        ILogger<AnalisisHistorialDatabaseInitializer> logger)
    {
        this.db = db;
        this.logger = logger;
    }

    public async Task InicializarAsync(
        CancellationToken cancellationToken = default)
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await AsegurarColumnasCalculoAsync(cancellationToken);
            await AsegurarTablaSnapshotAsync(cancellationToken);
            await AsegurarColumnasEIndicesSnapshotAsync(cancellationToken);
            await NormalizarRegistrosExistentesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Estructura de historial y versiones de análisis verificada correctamente.");
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);

            logger.LogError(
                ex,
                "No fue posible inicializar la estructura histórica de los análisis.");

            throw;
        }
    }

    private async Task AsegurarColumnasCalculoAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
IF OBJECT_ID(N'[dbo].[analisisSueloCalculo]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.analisisSueloCalculo', N'fechaCreacionClienteUtc') IS NULL
    BEGIN
        ALTER TABLE [dbo].[analisisSueloCalculo]
        ADD [fechaCreacionClienteUtc] DATETIME2(7) NULL;
    END;

    IF COL_LENGTH(N'dbo.analisisSueloCalculo', N'fechaUltimaModificacionUtc') IS NULL
    BEGIN
        ALTER TABLE [dbo].[analisisSueloCalculo]
        ADD [fechaUltimaModificacionUtc] DATETIME2(7) NULL;
    END;

    IF COL_LENGTH(N'dbo.analisisSueloCalculo', N'versionRegistro') IS NULL
    BEGIN
        ALTER TABLE [dbo].[analisisSueloCalculo]
        ADD [versionRegistro] INT NOT NULL
            CONSTRAINT [DF_analisisSueloCalculo_versionRegistro]
            DEFAULT (1);
    END;

    IF COL_LENGTH(N'dbo.analisisSueloCalculo', N'origenRegistro') IS NULL
    BEGIN
        ALTER TABLE [dbo].[analisisSueloCalculo]
        ADD [origenRegistro] NVARCHAR(20) NOT NULL
            CONSTRAINT [DF_analisisSueloCalculo_origenRegistro]
            DEFAULT (N'ONLINE');
    END;
END;
""";

        await db.Database.ExecuteSqlRawAsync(
            sql,
            cancellationToken);
    }

    private async Task AsegurarTablaSnapshotAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
IF OBJECT_ID(N'[dbo].[analisisReporteSnapshot]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[analisisReporteSnapshot]
    (
        [analisisReporteSnapshotId] BIGINT IDENTITY(1,1) NOT NULL,
        [analisisSueloCalculoId] INT NOT NULL,
        [versionRegistro] INT NOT NULL,
        [tipoEvento] NVARCHAR(30) NOT NULL,
        [origen] NVARCHAR(20) NOT NULL,
        [fechaCreacionClienteUtc] DATETIME2(7) NULL,
        [fechaOperacionClienteUtc] DATETIME2(7) NULL,
        [fechaOperacionUtc] DATETIME2(7) NOT NULL,
        [usuarioId] INT NULL,
        [datosJson] NVARCHAR(MAX) NOT NULL,
        [hashSha256] CHAR(64) NOT NULL,
        [vigente] BIT NOT NULL,
        [activo] BIT NOT NULL,

        CONSTRAINT [PK_analisisReporteSnapshot]
            PRIMARY KEY ([analisisReporteSnapshotId]),

        CONSTRAINT [UQ_analisisReporteSnapshot_calculo_version]
            UNIQUE ([analisisSueloCalculoId], [versionRegistro])
    );
END;
""";

        await db.Database.ExecuteSqlRawAsync(
            sql,
            cancellationToken);
    }

    private async Task AsegurarColumnasEIndicesSnapshotAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
IF OBJECT_ID(N'[dbo].[analisisReporteSnapshot]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.analisisReporteSnapshot', N'origen') IS NULL
    BEGIN
        ALTER TABLE [dbo].[analisisReporteSnapshot]
        ADD [origen] NVARCHAR(20) NOT NULL
            CONSTRAINT [DF_analisisReporteSnapshot_origen]
            DEFAULT (N'ONLINE');
    END;

    IF COL_LENGTH(N'dbo.analisisReporteSnapshot', N'fechaCreacionClienteUtc') IS NULL
    BEGIN
        ALTER TABLE [dbo].[analisisReporteSnapshot]
        ADD [fechaCreacionClienteUtc] DATETIME2(7) NULL;
    END;

    IF COL_LENGTH(N'dbo.analisisReporteSnapshot', N'fechaOperacionClienteUtc') IS NULL
    BEGIN
        ALTER TABLE [dbo].[analisisReporteSnapshot]
        ADD [fechaOperacionClienteUtc] DATETIME2(7) NULL;
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[dbo].[analisisReporteSnapshot]')
          AND name = N'UQ_analisisReporteSnapshot_calculo_version'
    )
    BEGIN
        CREATE UNIQUE INDEX [UQ_analisisReporteSnapshot_calculo_version]
            ON [dbo].[analisisReporteSnapshot]
            ([analisisSueloCalculoId], [versionRegistro]);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[dbo].[analisisReporteSnapshot]')
          AND name = N'IX_analisisReporteSnapshot_vigente'
    )
    BEGIN
        CREATE INDEX [IX_analisisReporteSnapshot_vigente]
            ON [dbo].[analisisReporteSnapshot]
            ([analisisSueloCalculoId], [vigente], [activo]);
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[dbo].[analisisReporteSnapshot]')
          AND name = N'IX_analisisReporteSnapshot_fecha'
    )
    BEGIN
        CREATE INDEX [IX_analisisReporteSnapshot_fecha]
            ON [dbo].[analisisReporteSnapshot]
            ([fechaOperacionUtc] DESC);
    END;
END;
""";

        await db.Database.ExecuteSqlRawAsync(
            sql,
            cancellationToken);
    }

    private async Task NormalizarRegistrosExistentesAsync(
        CancellationToken cancellationToken)
    {
        /*
         * Este UPDATE se ejecuta en un lote independiente, después de que las
         * columnas ya fueron creadas. Así se evita el error de SQL Server:
         * "Invalid column name 'versionRegistro/origenRegistro'".
         */
        const string sql = """
IF OBJECT_ID(N'[dbo].[analisisSueloCalculo]', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.analisisSueloCalculo', N'versionRegistro') IS NOT NULL
   AND COL_LENGTH(N'dbo.analisisSueloCalculo', N'origenRegistro') IS NOT NULL
BEGIN
    UPDATE [dbo].[analisisSueloCalculo]
    SET
        [versionRegistro] = CASE
            WHEN [versionRegistro] < 1 THEN 1
            ELSE [versionRegistro]
        END,
        [origenRegistro] = CASE
            WHEN NULLIF(LTRIM(RTRIM([origenRegistro])), N'') IS NULL
                THEN N'ONLINE'
            ELSE UPPER(LTRIM(RTRIM([origenRegistro])))
        END
    WHERE
        [versionRegistro] < 1 OR
        NULLIF(LTRIM(RTRIM([origenRegistro])), N'') IS NULL;
END;
""";

        await db.Database.ExecuteSqlRawAsync(
            sql,
            cancellationToken);
    }
}
