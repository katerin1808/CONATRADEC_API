using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Instala de forma idempotente el permiso y la bitácora específica del
    /// Centro de Control Fitosanitario. El administrador recibe el permiso solo
    /// al crearse la relación por primera vez; después, cualquier cambio hecho
    /// desde la matriz de permisos se respeta igual que para los demás roles.
    /// </summary>
    public sealed class InspeccionFitosanitariaAdministracionDatabaseInitializer
    {
        public const string InterfazControl = "diagnosticoIAControlPage";

        private static readonly SemaphoreSlim InicializacionLock = new(1, 1);
        private static volatile bool inicializada;
        private readonly DiagnosticoIADbContext db;

        public InspeccionFitosanitariaAdministracionDatabaseInitializer(
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
IF OBJECT_ID(N'dbo.interfaz', N'U') IS NULL
    THROW 51000, N'No existe dbo.interfaz.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.interfaz
    WHERE nombreInterfaz = N'diagnosticoIAControlPage'
)
BEGIN
    INSERT INTO dbo.interfaz
    (
        nombreInterfaz,
        nombreAmigableInterfaz,
        descripcionInterfaz,
        activo
    )
    VALUES
    (
        N'diagnosticoIAControlPage',
        N'Control fitosanitario',
        N'Centro de control, auditoría y supervisión fitosanitaria',
        1
    );
END
ELSE
BEGIN
    UPDATE dbo.interfaz
    SET nombreAmigableInterfaz = N'Control fitosanitario',
        descripcionInterfaz =
            N'Centro de control, auditoría y supervisión fitosanitaria',
        activo = 1
    WHERE nombreInterfaz = N'diagnosticoIAControlPage';
END;

/*
 * Se crea una asignación inicial para el rol Administrador únicamente cuando
 * todavía no existe. No se sobrescriben permisos ya configurados manualmente.
 */
IF OBJECT_ID(N'dbo.Rol', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.rolInterfaz', N'U') IS NOT NULL
BEGIN
    DECLARE @interfazId INT =
    (
        SELECT TOP(1) interfazId
        FROM dbo.interfaz
        WHERE nombreInterfaz = N'diagnosticoIAControlPage'
    );

    DECLARE @rolAdministradorId INT =
    (
        SELECT TOP(1) rolId
        FROM dbo.Rol
        WHERE UPPER(LTRIM(RTRIM(nombreRol))) = N'ADMINISTRADOR'
          AND activo = 1
        ORDER BY rolId
    );

    IF @interfazId IS NOT NULL AND @rolAdministradorId IS NOT NULL
    BEGIN
        IF NOT EXISTS
        (
            SELECT 1
            FROM dbo.rolInterfaz
            WHERE rolId = @rolAdministradorId
              AND interfazId = @interfazId
        )
        BEGIN
            INSERT INTO dbo.rolInterfaz
            (
                rolId,
                interfazId,
                leer,
                agregar,
                actualizar,
                eliminar
            )
            VALUES
            (
                @rolAdministradorId,
                @interfazId,
                1,
                1,
                1,
                1
            );
        END
    END;
END;

IF OBJECT_ID(N'dbo.diagnosticoIAAdministracionHistorial', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.diagnosticoIAAdministracionHistorial
    (
        DiagnosticoIAAdministracionHistorialId INT IDENTITY(1,1) NOT NULL,
        DiagnosticoIAId INT NOT NULL,
        UsuarioEjecutorId INT NOT NULL,
        Accion NVARCHAR(80) NOT NULL,
        Etapa NVARCHAR(20) NOT NULL
            CONSTRAINT DF_diagIAAdminHist_etapa DEFAULT(N''),
        UsuarioAnteriorId INT NULL,
        UsuarioNuevoId INT NULL,
        Motivo NVARCHAR(1000) NOT NULL,
        Detalle NVARCHAR(2000) NOT NULL
            CONSTRAINT DF_diagIAAdminHist_detalle DEFAULT(N''),
        FechaUtc DATETIME2(0) NOT NULL,
        CONSTRAINT PK_diagnosticoIAAdministracionHistorial
            PRIMARY KEY (DiagnosticoIAAdministracionHistorialId),
        CONSTRAINT FK_diagIAAdminHist_diagnostico
            FOREIGN KEY (DiagnosticoIAId)
            REFERENCES dbo.diagnosticoIA(DiagnosticoIAId)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_diagIAAdminHist_inspeccionFecha'
      AND object_id = OBJECT_ID(N'dbo.diagnosticoIAAdministracionHistorial')
)
BEGIN
    CREATE INDEX IX_diagIAAdminHist_inspeccionFecha
        ON dbo.diagnosticoIAAdministracionHistorial
           (DiagnosticoIAId, FechaUtc DESC);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_diagIAAdminHist_fecha'
      AND object_id = OBJECT_ID(N'dbo.diagnosticoIAAdministracionHistorial')
)
BEGIN
    CREATE INDEX IX_diagIAAdminHist_fecha
        ON dbo.diagnosticoIAAdministracionHistorial
           (FechaUtc DESC, UsuarioEjecutorId);
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
    }
}
