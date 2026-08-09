using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Instala de forma idempotente la interfaz y la bitácora específica del
    /// Centro de Control Fitosanitario. La creación de la interfaz no asigna
    /// permisos a ningún rol; estos se administran únicamente desde la matriz.
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

/*
 * Compatibilidad con bases históricas:
 * descripcionInterfaz puede conservar NVARCHAR(80) y además participar en
 * índices existentes. Los textos funcionales se mantienen dentro de 80
 * caracteres para no alterar el esquema ni reconstruir índices al arrancar.
 */
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
 * Nombres funcionales mostrados en la matriz. Los códigos internos no cambian
 * para conservar compatibilidad con Web, Android, Windows y la API.
 */
UPDATE dbo.interfaz
SET nombreAmigableInterfaz = N'Inspección fitosanitaria - Técnico',
    descripcionInterfaz = N'Crear inspecciones, gestionar fotos y ejecutar decisiones de la etapa técnica.'
WHERE nombreInterfaz = N'diagnosticoIASolicitudPage';

UPDATE dbo.interfaz
SET nombreAmigableInterfaz = N'Inspección fitosanitaria - Analizador',
    descripcionInterfaz = N'Tomar expedientes, realizar análisis humano y enviarlos a aprobación.'
WHERE nombreInterfaz = N'diagnosticoIAAnalizadorPage';

UPDATE dbo.interfaz
SET nombreAmigableInterfaz = N'Inspección fitosanitaria - Aprobador',
    descripcionInterfaz = N'Tomar expedientes, aprobar, devolver o rechazar diagnósticos fitosanitarios.'
WHERE nombreInterfaz = N'diagnosticoIAAprobadorPage';

UPDATE dbo.interfaz
SET nombreAmigableInterfaz = N'Configuración fitosanitaria',
    descripcionInterfaz = N'Administrar parámetros, tipos de fotografía y catálogos del flujo fitosanitario.'
WHERE nombreInterfaz = N'diagnosticoIAConfiguracionPage';

/*
 * La interfaz se registra sin conceder permisos automáticamente. Cualquier
 * rol, incluido el que administre el sistema, debe recibir Leer/Agregar/
 * Actualizar/Eliminar desde la matriz de permisos.
 */

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
