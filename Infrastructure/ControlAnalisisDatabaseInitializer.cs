using Microsoft.EntityFrameworkCore;
using CONATRADEC_API.Models;

namespace CONATRADEC_API.Infrastructure;

/// <summary>
/// Crea la tabla que conserva el estado exacto previo a una eliminación.
/// No depende de migraciones para poder instalarse en servidores existentes.
/// </summary>
public sealed class ControlAnalisisDatabaseInitializer
{
    private readonly DBContext db;

    public ControlAnalisisDatabaseInitializer(DBContext db)
    {
        this.db = db;
    }

    public async Task InicializarAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
IF OBJECT_ID(N'dbo.analisisSueloEliminacion', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.analisisSueloEliminacion
    (
        analisisSueloEliminacionId BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_analisisSueloEliminacion PRIMARY KEY,
        analisisSueloId INT NOT NULL,
        analisisSueloCalculoId INT NULL,
        usuarioEliminacionId INT NOT NULL,
        fechaEliminacionUtc DATETIME2(0) NOT NULL,
        motivoEliminacion NVARCHAR(500) NOT NULL,
        manifiestoRestauracionJson NVARCHAR(MAX) NOT NULL,
        pdfHistorico VARBINARY(MAX) NULL,
        nombreArchivoPdf NVARCHAR(260) NULL,
        estado NVARCHAR(20) NOT NULL,
        usuarioRecuperacionId INT NULL,
        fechaRecuperacionUtc DATETIME2(0) NULL,
        motivoRecuperacion NVARCHAR(500) NULL
    );

    CREATE INDEX IX_analisisSueloEliminacion_AnalisisEstado
        ON dbo.analisisSueloEliminacion(analisisSueloId, estado, fechaEliminacionUtc DESC);
END;
""";

        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}
