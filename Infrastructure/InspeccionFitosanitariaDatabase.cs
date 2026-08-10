using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Persistencia del expediente independiente por fotografía. El control de
    /// etapas se delega a InspeccionFitosanitariaControlDatabaseInitializer y
    /// no conserva ningún campo heredado llamado CerradaTecnico.
    ///
    /// Las valoraciones visuales de IA se guardan en una tabla de historial
    /// separada para conservar todas las revisiones sin alterar el resultado
    /// resumen vigente utilizado por las pantallas anteriores.
    /// </summary>
    public sealed class InspeccionFitosanitariaDatabase
    {
        private static readonly SemaphoreSlim InicializacionLock = new(1, 1);
        private static readonly SemaphoreSlim ProveedorInicializacionLock =
            new(1, 1);
        private static volatile bool inicializada;
        private static volatile bool proveedorInicializado;
        private readonly DiagnosticoIADbContext db;

        public InspeccionFitosanitariaDatabase(DiagnosticoIADbContext db)
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

                await InicializarColumnasImagenAsync(cancellationToken);
                await NormalizarImagenesExistentesAsync(cancellationToken);
                await InicializarTablasFlujoAsync(cancellationToken);
                await new InspeccionFitosanitariaControlDatabaseInitializer(db)
                    .InicializarAsync(cancellationToken);
                await new InspeccionFitosanitariaAsignacionDatabase(db)
                    .InicializarAsync(cancellationToken);

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

        public async Task InicializarProveedorAsync(
            CancellationToken cancellationToken = default)
        {
            if (proveedorInicializado)
                return;

            await ProveedorInicializacionLock.WaitAsync(cancellationToken);
            try
            {
                if (proveedorInicializado)
                    return;

                const string sql = """
IF OBJECT_ID(N'[dbo].[diagnosticoIAProveedorConfiguracion]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAProveedorConfiguracion]
    (
        [DiagnosticoIAProveedorConfiguracionId] INT NOT NULL,
        [Proveedor] NVARCHAR(40) NOT NULL,
        [Protocolo] NVARCHAR(40) NOT NULL,
        [BaseUrl] NVARCHAR(500) NOT NULL,
        [Endpoint] NVARCHAR(300) NOT NULL,
        [ApiKeyProtegida] NVARCHAR(MAX) NOT NULL
            CONSTRAINT [DF_diagIAProv_key] DEFAULT(N''),
        [ApiKeyMascara] NVARCHAR(80) NOT NULL
            CONSTRAINT [DF_diagIAProv_mask] DEFAULT(N''),
        [ModeloPrincipal] NVARCHAR(160) NOT NULL,
        [ModeloRespaldo] NVARCHAR(160) NOT NULL
            CONSTRAINT [DF_diagIAProv_fallback] DEFAULT(N''),
        [TimeoutSegundos] INT NOT NULL
            CONSTRAINT [DF_diagIAProv_timeout] DEFAULT(180),
        [Activo] BIT NOT NULL
            CONSTRAINT [DF_diagIAProv_activo] DEFAULT(1),
        [FechaModificacionUtc] DATETIME2(0) NOT NULL,
        [UsuarioModificacionId] INT NULL,
        [RowVersion] ROWVERSION,
        CONSTRAINT [PK_diagIAProveedorConfiguracion]
            PRIMARY KEY ([DiagnosticoIAProveedorConfiguracionId])
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM [dbo].[diagnosticoIAProveedorConfiguracion]
    WHERE [DiagnosticoIAProveedorConfiguracionId] = 1
)
BEGIN
    INSERT INTO [dbo].[diagnosticoIAProveedorConfiguracion]
    (
        [DiagnosticoIAProveedorConfiguracionId], [Proveedor], [Protocolo],
        [BaseUrl], [Endpoint], [ModeloPrincipal], [ModeloRespaldo],
        [TimeoutSegundos], [Activo], [FechaModificacionUtc]
    )
    VALUES
    (
        1, N'GEMINI', N'GEMINI_NATIVO',
        N'https://generativelanguage.googleapis.com/',
        N'v1beta/models/{model}:generateContent',
        N'gemini-3.6-flash', N'gemini-3.5-flash',
        180, 1, SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(
    N'[dbo].[diagnosticoIAProveedorConfiguracionHistorial]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAProveedorConfiguracionHistorial]
    (
        [DiagnosticoIAProveedorConfiguracionHistorialId]
            INT IDENTITY(1,1) NOT NULL,
        [ConfiguracionJson] NVARCHAR(MAX) NOT NULL,
        [UsuarioId] INT NOT NULL,
        [FechaUtc] DATETIME2(0) NOT NULL,
        CONSTRAINT [PK_diagIAProveedorConfiguracionHistorial]
            PRIMARY KEY ([DiagnosticoIAProveedorConfiguracionHistorialId])
    );
END;
""";

                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
                proveedorInicializado = true;
            }
            catch
            {
                proveedorInicializado = false;
                throw;
            }
            finally
            {
                ProveedorInicializacionLock.Release();
            }
        }

        private async Task InicializarColumnasImagenAsync(
            CancellationToken cancellationToken)
        {
            const string sql = """
IF OBJECT_ID(N'[dbo].[diagnosticoIAImagen]', N'U') IS NULL
BEGIN
    THROW 51000, N'La tabla base diagnosticoIAImagen no existe.', 1;
END;

IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'FechaIdentificacionCampo') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen
        ADD FechaIdentificacionCampo DATE NULL;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'FechaRegistroSistemaUtc') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen
        ADD FechaRegistroSistemaUtc DATETIME2(0) NULL;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'FechaAnalisisIAUtc') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen
        ADD FechaAnalisisIAUtc DATETIME2(0) NULL;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'FechaAnalisisHumanoUtc') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen
        ADD FechaAnalisisHumanoUtc DATETIME2(0) NULL;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'FechaAprobacionUtc') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen
        ADD FechaAprobacionUtc DATETIME2(0) NULL;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'Estado') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen
        ADD Estado NVARCHAR(40) NOT NULL
            CONSTRAINT DF_diagIAImg_estadoV2 DEFAULT(N'BORRADOR') WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'ErrorProcesamiento') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen
        ADD ErrorProcesamiento NVARCHAR(2000) NOT NULL
            CONSTRAINT DF_diagIAImg_errorV2 DEFAULT(N'') WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'ModeloIAUtilizado') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen
        ADD ModeloIAUtilizado NVARCHAR(160) NOT NULL
            CONSTRAINT DF_diagIAImg_modeloV2 DEFAULT(N'') WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'IntentosIA') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen
        ADD IntentosIA INT NOT NULL
            CONSTRAINT DF_diagIAImg_intentosV2 DEFAULT(0) WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'Descartada') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen
        ADD Descartada BIT NOT NULL
            CONSTRAINT DF_diagIAImg_descartadaV2 DEFAULT(0) WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'MotivoDescarte') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen
        ADD MotivoDescarte NVARCHAR(1000) NOT NULL
            CONSTRAINT DF_diagIAImg_motivoDescarteV2 DEFAULT(N'') WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'UsuarioDescarteId') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen ADD UsuarioDescarteId INT NULL;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'FechaDescarteUtc') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen ADD FechaDescarteUtc DATETIME2(0) NULL;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'Activo') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen
        ADD Activo BIT NOT NULL
            CONSTRAINT DF_diagIAImg_activoV2 DEFAULT(1) WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagen', N'RowVersion') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagen ADD RowVersion ROWVERSION;
""";

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        private async Task NormalizarImagenesExistentesAsync(
            CancellationToken cancellationToken)
        {
            const string sql = """
EXEC(N'UPDATE dbo.diagnosticoIAImagen
SET FechaRegistroSistemaUtc =
    ISNULL(FechaRegistroSistemaUtc, FechaRegistroUtc)
WHERE FechaRegistroSistemaUtc IS NULL;');

IF OBJECT_ID(N'dbo.diagnosticoIAImagenEvaluacion', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.diagnosticoIAAprobacion', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.diagnosticoIAImagenResultadoIA', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.diagnosticoIAAlbumPublicacion', N'U') IS NOT NULL
BEGIN
    BEGIN TRY
        EXEC(N'UPDATE imagen
SET Estado = CASE
    WHEN publicacion.DiagnosticoIAImagenId IS NOT NULL
        THEN N''PUBLICADA_ALBUM''
    WHEN aprobacion.Decision IN
        (N''APROBAR_SIN_CAMBIOS'', N''APROBAR'')
        THEN N''APROBADA''
    WHEN aprobacion.Decision = N''APROBAR_CON_CORRECCION''
        THEN N''APROBADA_CON_CORRECCION''
    WHEN aprobacion.Decision IN
        (N''RECHAZAR_DIAGNOSTICO'', N''RECHAZAR'')
        THEN N''RECHAZADA''
    WHEN aprobacion.Decision IN
        (N''MARCAR_NO_CONCLUYENTE'', N''NO_CONCLUYENTE'')
        THEN N''NO_CONCLUYENTE''
    WHEN resultado.DiagnosticoIAImagenResultadoIAId IS NOT NULL
        THEN N''PENDIENTE_DECISION_TECNICO''
    ELSE N''BORRADOR''
END
FROM dbo.diagnosticoIAImagen imagen
OUTER APPLY
(
    SELECT TOP(1) a.Decision
    FROM dbo.diagnosticoIAImagenEvaluacion e
    INNER JOIN dbo.diagnosticoIAAprobacion a
        ON a.DiagnosticoIAAprobacionId = e.DiagnosticoIAAprobacionId
    WHERE e.DiagnosticoIAImagenId = imagen.DiagnosticoIAImagenId
    ORDER BY a.FechaAprobacionUtc DESC
) aprobacion
LEFT JOIN dbo.diagnosticoIAImagenResultadoIA resultado
    ON resultado.DiagnosticoIAImagenId = imagen.DiagnosticoIAImagenId
LEFT JOIN
(
    SELECT DISTINCT DiagnosticoIAImagenId
    FROM dbo.diagnosticoIAAlbumPublicacion
    WHERE Activo = 1
) publicacion
    ON publicacion.DiagnosticoIAImagenId = imagen.DiagnosticoIAImagenId
WHERE imagen.Estado = N''BORRADOR'';');
    END TRY
    BEGIN CATCH
        PRINT ERROR_MESSAGE();
    END CATCH;
END;
""";

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        private async Task InicializarTablasFlujoAsync(
            CancellationToken cancellationToken)
        {
            const string sql = """
IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_diagIAImagen_estadoV2'
      AND object_id = OBJECT_ID(N'dbo.diagnosticoIAImagen')
)
BEGIN
    CREATE INDEX IX_diagIAImagen_estadoV2
        ON dbo.diagnosticoIAImagen
           (DiagnosticoIAId, Estado, Activo);
END;

IF OBJECT_ID(N'dbo.diagnosticoIAImagenRevisionIA', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.diagnosticoIAImagenRevisionIA
    (
        DiagnosticoIAImagenRevisionIAId INT IDENTITY(1,1) NOT NULL,
        DiagnosticoIAImagenId INT NOT NULL,
        UsuarioSolicitanteId INT NOT NULL,
        TipoRevision NVARCHAR(30) NOT NULL,
        Retroalimentacion NVARCHAR(2000) NOT NULL
            CONSTRAINT DF_diagIAImgRev_feedback DEFAULT(N''),
        DiagnosticoPropuesto NVARCHAR(300) NOT NULL
            CONSTRAINT DF_diagIAImgRev_propuesto DEFAULT(N''),
        ProveedorIA NVARCHAR(40) NOT NULL
            CONSTRAINT DF_diagIAImgRev_proveedor DEFAULT(N''),
        ModeloIA NVARCHAR(160) NOT NULL
            CONSTRAINT DF_diagIAImgRev_modelo DEFAULT(N''),
        Estado NVARCHAR(30) NOT NULL,
        RespuestaJson NVARCHAR(MAX) NOT NULL
            CONSTRAINT DF_diagIAImgRev_respuesta DEFAULT(N''),
        Error NVARCHAR(2000) NOT NULL
            CONSTRAINT DF_diagIAImgRev_error DEFAULT(N''),
        FechaSolicitudUtc DATETIME2(0) NOT NULL,
        FechaRespuestaUtc DATETIME2(0) NULL,
        CONSTRAINT PK_diagIAImagenRevisionIA
            PRIMARY KEY (DiagnosticoIAImagenRevisionIAId),
        CONSTRAINT FK_diagIAImagenRevisionIA_imagen
            FOREIGN KEY (DiagnosticoIAImagenId)
            REFERENCES dbo.diagnosticoIAImagen(DiagnosticoIAImagenId)
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_diagIAImagenRevisionIA_imagenFecha'
      AND object_id = OBJECT_ID(N'dbo.diagnosticoIAImagenRevisionIA')
)
    CREATE INDEX IX_diagIAImagenRevisionIA_imagenFecha
        ON dbo.diagnosticoIAImagenRevisionIA
           (DiagnosticoIAImagenId, FechaSolicitudUtc DESC);

/*
 * Historial visual de IA. No sustituye el resultado resumen vigente de la
 * tabla histórica; conserva todas las reevaluaciones y la ruta de cada copia
 * marcada generada por backend.
 */
IF OBJECT_ID(N'dbo.diagnosticoIAImagenResultadoVisualV2', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.diagnosticoIAImagenResultadoVisualV2
    (
        DiagnosticoIAImagenResultadoVisualId INT IDENTITY(1,1) NOT NULL,
        DiagnosticoIAImagenId INT NOT NULL,
        Revision INT NOT NULL,
        EsVigente BIT NOT NULL
            CONSTRAINT DF_diagIAVisual_vigente DEFAULT(1),
        DiagnosticosJson NVARCHAR(MAX) NOT NULL
            CONSTRAINT DF_diagIAVisual_diag DEFAULT(N'[]'),
        RutaImagenMarcada NVARCHAR(600) NOT NULL
            CONSTRAINT DF_diagIAVisual_ruta DEFAULT(N''),
        ProveedorIA NVARCHAR(40) NOT NULL
            CONSTRAINT DF_diagIAVisual_proveedor DEFAULT(N''),
        ModeloIA NVARCHAR(160) NOT NULL
            CONSTRAINT DF_diagIAVisual_modelo DEFAULT(N''),
        FechaGeneracionUtc DATETIME2(0) NOT NULL,
        CONSTRAINT PK_diagIAImagenResultadoVisualV2
            PRIMARY KEY (DiagnosticoIAImagenResultadoVisualId),
        CONSTRAINT FK_diagIAImagenResultadoVisualV2_imagen
            FOREIGN KEY (DiagnosticoIAImagenId)
            REFERENCES dbo.diagnosticoIAImagen(DiagnosticoIAImagenId)
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_diagIAVisual_fotoRevision'
      AND object_id = OBJECT_ID(N'dbo.diagnosticoIAImagenResultadoVisualV2')
)
    CREATE UNIQUE INDEX UX_diagIAVisual_fotoRevision
        ON dbo.diagnosticoIAImagenResultadoVisualV2
           (DiagnosticoIAImagenId, Revision);

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_diagIAVisual_vigente'
      AND object_id = OBJECT_ID(N'dbo.diagnosticoIAImagenResultadoVisualV2')
)
    CREATE INDEX IX_diagIAVisual_vigente
        ON dbo.diagnosticoIAImagenResultadoVisualV2
           (DiagnosticoIAImagenId, EsVigente, FechaGeneracionUtc DESC);

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_diagIAVisual_unVigentePorFoto'
      AND object_id = OBJECT_ID(N'dbo.diagnosticoIAImagenResultadoVisualV2')
)
    CREATE UNIQUE INDEX UX_diagIAVisual_unVigentePorFoto
        ON dbo.diagnosticoIAImagenResultadoVisualV2
           (DiagnosticoIAImagenId)
        WHERE EsVigente = 1;

IF OBJECT_ID(N'dbo.diagnosticoIAImagenAnalisisHumano', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.diagnosticoIAImagenAnalisisHumano
    (
        DiagnosticoIAImagenAnalisisHumanoId INT IDENTITY(1,1) NOT NULL,
        DiagnosticoIAImagenId INT NOT NULL,
        UsuarioAnalizadorId INT NOT NULL,
        Version INT NOT NULL,
        EstadoRegistro NVARCHAR(30) NOT NULL,
        CalidadEvaluacion NVARCHAR(30) NOT NULL,
        EstadoGeneral NVARCHAR(40) NOT NULL,
        CategoriaPrincipal NVARCHAR(50) NOT NULL,
        CategoriasSecundariasJson NVARCHAR(MAX) NOT NULL
            CONSTRAINT DF_diagIAImgHum_cat DEFAULT(N'[]'),
        Diagnostico NVARCHAR(300) NOT NULL,
        TipoDiagnostico NVARCHAR(80) NOT NULL
            CONSTRAINT DF_diagIAImgHum_tipo DEFAULT(N''),
        Severidad NVARCHAR(30) NOT NULL,
        NivelCerteza NVARCHAR(30) NOT NULL,
        Observaciones NVARCHAR(3000) NOT NULL
            CONSTRAINT DF_diagIAImgHum_obs DEFAULT(N''),
        DiagnosticosJson NVARCHAR(MAX) NOT NULL
            CONSTRAINT DF_diagIAImgHum_diags DEFAULT(N'[]'),
        FechaCreacionUtc DATETIME2(0) NOT NULL,
        FechaActualizacionUtc DATETIME2(0) NOT NULL,
        FechaEnvioUtc DATETIME2(0) NULL,
        CONSTRAINT PK_diagIAImagenAnalisisHumano
            PRIMARY KEY (DiagnosticoIAImagenAnalisisHumanoId),
        CONSTRAINT FK_diagIAImagenAnalisisHumano_imagen
            FOREIGN KEY (DiagnosticoIAImagenId)
            REFERENCES dbo.diagnosticoIAImagen(DiagnosticoIAImagenId)
    );
END;

IF COL_LENGTH(N'dbo.diagnosticoIAImagenAnalisisHumano', N'DiagnosticosJson') IS NULL
BEGIN
    ALTER TABLE dbo.diagnosticoIAImagenAnalisisHumano
        ADD DiagnosticosJson NVARCHAR(MAX) NOT NULL
            CONSTRAINT DF_diagIAImgHum_diags DEFAULT(N'[]') WITH VALUES;
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_diagIAImagenAnalisisHumano_version'
      AND object_id = OBJECT_ID(N'dbo.diagnosticoIAImagenAnalisisHumano')
)
    CREATE UNIQUE INDEX UX_diagIAImagenAnalisisHumano_version
        ON dbo.diagnosticoIAImagenAnalisisHumano
           (DiagnosticoIAImagenId, Version);

IF OBJECT_ID(N'dbo.diagnosticoIAImagenAprobacionV2', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.diagnosticoIAImagenAprobacionV2
    (
        DiagnosticoIAImagenAprobacionId INT IDENTITY(1,1) NOT NULL,
        DiagnosticoIAImagenId INT NOT NULL,
        DiagnosticoIAImagenAnalisisHumanoId INT NULL,
        UsuarioAprobadorId INT NOT NULL,
        Decision NVARCHAR(40) NOT NULL,
        CalidadEvaluacionFinal NVARCHAR(30) NOT NULL
            CONSTRAINT DF_diagIAImgApr_calidad DEFAULT(N''),
        EstadoGeneralFinal NVARCHAR(40) NOT NULL
            CONSTRAINT DF_diagIAImgApr_estado DEFAULT(N''),
        CategoriaPrincipalFinal NVARCHAR(50) NOT NULL
            CONSTRAINT DF_diagIAImgApr_categoria DEFAULT(N''),
        CategoriasSecundariasFinalJson NVARCHAR(MAX) NOT NULL
            CONSTRAINT DF_diagIAImgApr_catSec DEFAULT(N'[]'),
        DiagnosticoFinal NVARCHAR(300) NOT NULL
            CONSTRAINT DF_diagIAImgApr_diag DEFAULT(N''),
        TipoDiagnosticoFinal NVARCHAR(80) NOT NULL
            CONSTRAINT DF_diagIAImgApr_tipo DEFAULT(N''),
        SeveridadFinal NVARCHAR(30) NOT NULL
            CONSTRAINT DF_diagIAImgApr_sev DEFAULT(N''),
        NivelCertezaFinal NVARCHAR(30) NOT NULL
            CONSTRAINT DF_diagIAImgApr_certeza DEFAULT(N''),
        Observaciones NVARCHAR(3000) NOT NULL
            CONSTRAINT DF_diagIAImgApr_obs DEFAULT(N''),
        DiagnosticosFinalesJson NVARCHAR(MAX) NOT NULL
            CONSTRAINT DF_diagIAImgApr_diags DEFAULT(N'[]'),
        AutorizaPublicacionAlbum BIT NOT NULL
            CONSTRAINT DF_diagIAImgApr_album DEFAULT(0),
        MismoUsuarioQueAnalizo BIT NOT NULL
            CONSTRAINT DF_diagIAImgApr_mismo DEFAULT(0),
        FechaAprobacionUtc DATETIME2(0) NOT NULL,
        CONSTRAINT PK_diagIAImagenAprobacionV2
            PRIMARY KEY (DiagnosticoIAImagenAprobacionId),
        CONSTRAINT FK_diagIAImagenAprobacionV2_imagen
            FOREIGN KEY (DiagnosticoIAImagenId)
            REFERENCES dbo.diagnosticoIAImagen(DiagnosticoIAImagenId),
        CONSTRAINT FK_diagIAImagenAprobacionV2_humano
            FOREIGN KEY (DiagnosticoIAImagenAnalisisHumanoId)
            REFERENCES dbo.diagnosticoIAImagenAnalisisHumano
                       (DiagnosticoIAImagenAnalisisHumanoId)
    );
END;

IF COL_LENGTH(N'dbo.diagnosticoIAImagenAprobacionV2', N'DiagnosticosFinalesJson') IS NULL
BEGIN
    ALTER TABLE dbo.diagnosticoIAImagenAprobacionV2
        ADD DiagnosticosFinalesJson NVARCHAR(MAX) NOT NULL
            CONSTRAINT DF_diagIAImgApr_diags DEFAULT(N'[]') WITH VALUES;
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_diagIAImagenAprobacionV2_imagenFecha'
      AND object_id = OBJECT_ID(N'dbo.diagnosticoIAImagenAprobacionV2')
)
    CREATE INDEX IX_diagIAImagenAprobacionV2_imagenFecha
        ON dbo.diagnosticoIAImagenAprobacionV2
           (DiagnosticoIAImagenId, FechaAprobacionUtc DESC);

IF OBJECT_ID(N'dbo.diagnosticoIAImagenHistorialV2', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.diagnosticoIAImagenHistorialV2
    (
        DiagnosticoIAImagenHistorialId INT IDENTITY(1,1) NOT NULL,
        DiagnosticoIAImagenId INT NOT NULL,
        UsuarioId INT NOT NULL,
        EstadoAnterior NVARCHAR(40) NOT NULL
            CONSTRAINT DF_diagIAImgHist_anterior DEFAULT(N''),
        EstadoNuevo NVARCHAR(40) NOT NULL
            CONSTRAINT DF_diagIAImgHist_nuevo DEFAULT(N''),
        Accion NVARCHAR(80) NOT NULL,
        Detalle NVARCHAR(2000) NOT NULL
            CONSTRAINT DF_diagIAImgHist_detalle DEFAULT(N''),
        FechaUtc DATETIME2(0) NOT NULL,
        CONSTRAINT PK_diagIAImagenHistorialV2
            PRIMARY KEY (DiagnosticoIAImagenHistorialId),
        CONSTRAINT FK_diagIAImagenHistorialV2_imagen
            FOREIGN KEY (DiagnosticoIAImagenId)
            REFERENCES dbo.diagnosticoIAImagen(DiagnosticoIAImagenId)
    );
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_diagIAImagenHistorialV2_imagenFecha'
      AND object_id = OBJECT_ID(N'dbo.diagnosticoIAImagenHistorialV2')
)
    CREATE INDEX IX_diagIAImagenHistorialV2_imagenFecha
        ON dbo.diagnosticoIAImagenHistorialV2
           (DiagnosticoIAImagenId, FechaUtc DESC);
""";

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        public async Task<FotoMetadatos?> ObtenerFotoAsync(
            int fotografiaId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);
            const string sql = """
SELECT TOP(1)
    DiagnosticoIAImagenId, DiagnosticoIAId, Estado,
    FechaIdentificacionCampo, FechaRegistroSistemaUtc,
    FechaAnalisisIAUtc, FechaAnalisisHumanoUtc, FechaAprobacionUtc,
    ErrorProcesamiento, ModeloIAUtilizado, IntentosIA,
    Descartada, MotivoDescarte, UsuarioDescarteId,
    FechaDescarteUtc, Activo
FROM dbo.diagnosticoIAImagen
WHERE DiagnosticoIAImagenId = @fotoId;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@fotoId", fotografiaId);
            await AbrirAsync(comando.Connection!, cancellationToken);
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? LeerFoto(reader)
                : null;
        }

        public async Task<List<FotoMetadatos>> ObtenerFotosAsync(
            int diagnosticoId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);
            const string sql = """
SELECT
    DiagnosticoIAImagenId, DiagnosticoIAId, Estado,
    FechaIdentificacionCampo, FechaRegistroSistemaUtc,
    FechaAnalisisIAUtc, FechaAnalisisHumanoUtc, FechaAprobacionUtc,
    ErrorProcesamiento, ModeloIAUtilizado, IntentosIA,
    Descartada, MotivoDescarte, UsuarioDescarteId,
    FechaDescarteUtc, Activo
FROM dbo.diagnosticoIAImagen
WHERE DiagnosticoIAId = @diagnosticoId
ORDER BY Orden;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@diagnosticoId", diagnosticoId);
            await AbrirAsync(comando.Connection!, cancellationToken);
            var resultado = new List<FotoMetadatos>();
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                resultado.Add(LeerFoto(reader));
            return resultado;
        }

        public async Task<Dictionary<int, List<FotoMetadatos>>>
            ObtenerFotosPorDiagnosticosAsync(
                IEnumerable<int> diagnosticoIds,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);
            int[] ids = diagnosticoIds
                .Where(item => item > 0)
                .Distinct()
                .ToArray();
            var resultado = ids.ToDictionary(
                item => item,
                _ => new List<FotoMetadatos>());
            if (ids.Length == 0)
                return resultado;

            string parametros = string.Join(
                ",",
                ids.Select((_, indice) => $"@id{indice}"));
            string sql = $"""
SELECT
    DiagnosticoIAImagenId, DiagnosticoIAId, Estado,
    FechaIdentificacionCampo, FechaRegistroSistemaUtc,
    FechaAnalisisIAUtc, FechaAnalisisHumanoUtc, FechaAprobacionUtc,
    ErrorProcesamiento, ModeloIAUtilizado, IntentosIA,
    Descartada, MotivoDescarte, UsuarioDescarteId,
    FechaDescarteUtc, Activo
FROM dbo.diagnosticoIAImagen
WHERE DiagnosticoIAId IN ({parametros})
ORDER BY DiagnosticoIAId, Orden;
""";

            await using DbCommand comando = CrearComando(sql);
            for (int indice = 0; indice < ids.Length; indice++)
                AgregarParametro(comando, $"@id{indice}", ids[indice]);
            await AbrirAsync(comando.Connection!, cancellationToken);
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                FotoMetadatos foto = LeerFoto(reader);
                resultado.GetValueOrDefault(foto.DiagnosticoId)?.Add(foto);
            }
            return resultado;
        }

        public async Task RegistrarFotoAsync(
            int fotografiaId,
            DateTime? fechaIdentificacionCampo,
            DateTime fechaRegistroSistemaUtc,
            int usuarioId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);
            const string sql = """
UPDATE dbo.diagnosticoIAImagen
SET FechaIdentificacionCampo = @fechaCampo,
    FechaRegistroSistemaUtc = @fechaRegistro,
    Estado = N'PENDIENTE_IA',
    Activo = 1
WHERE DiagnosticoIAImagenId = @fotoId;

INSERT INTO dbo.diagnosticoIAImagenHistorialV2
(
    DiagnosticoIAImagenId, UsuarioId, EstadoAnterior,
    EstadoNuevo, Accion, Detalle, FechaUtc
)
VALUES
(
    @fotoId, @usuarioId, N'', N'PENDIENTE_IA',
    N'FOTO_REGISTRADA', N'La fotografía fue incorporada a la inspección.',
    @fechaRegistro
);
""";

            await EjecutarAsync(
                sql,
                cancellationToken,
                ("@fotoId", fotografiaId),
                ("@fechaCampo", fechaIdentificacionCampo),
                ("@fechaRegistro", fechaRegistroSistemaUtc),
                ("@usuarioId", usuarioId));
        }

        public async Task CambiarEstadoFotoAsync(
            int fotografiaId,
            int usuarioId,
            string estadoNuevo,
            string accion,
            string detalle,
            DateTime? fechaAnalisisIAUtc = null,
            DateTime? fechaAnalisisHumanoUtc = null,
            DateTime? fechaAprobacionUtc = null,
            string? error = null,
            string? modeloIA = null,
            bool incrementarIntento = false,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);
            FotoMetadatos? actual = await ObtenerFotoAsync(
                fotografiaId,
                cancellationToken);
            if (actual == null)
                throw new InvalidOperationException("La fotografía no existe.");

            const string sql = """
UPDATE dbo.diagnosticoIAImagen
SET Estado = @estadoNuevo,
    FechaAnalisisIAUtc = COALESCE(@fechaIA, FechaAnalisisIAUtc),
    FechaAnalisisHumanoUtc = COALESCE(@fechaHumano, FechaAnalisisHumanoUtc),
    FechaAprobacionUtc = COALESCE(@fechaAprobacion, FechaAprobacionUtc),
    ErrorProcesamiento = @error,
    ModeloIAUtilizado = CASE
        WHEN @modelo = N'' THEN ModeloIAUtilizado ELSE @modelo END,
    IntentosIA = IntentosIA + @incremento
WHERE DiagnosticoIAImagenId = @fotoId;

INSERT INTO dbo.diagnosticoIAImagenHistorialV2
(
    DiagnosticoIAImagenId, UsuarioId, EstadoAnterior,
    EstadoNuevo, Accion, Detalle, FechaUtc
)
VALUES
(
    @fotoId, @usuarioId, @estadoAnterior, @estadoNuevo,
    @accion, @detalle, SYSUTCDATETIME()
);
""";

            await EjecutarAsync(
                sql,
                cancellationToken,
                ("@fotoId", fotografiaId),
                ("@usuarioId", usuarioId),
                ("@estadoAnterior", actual.Estado),
                ("@estadoNuevo", estadoNuevo),
                ("@accion", Limitar(accion, 80)),
                ("@detalle", Limitar(detalle, 2000)),
                ("@fechaIA", fechaAnalisisIAUtc),
                ("@fechaHumano", fechaAnalisisHumanoUtc),
                ("@fechaAprobacion", fechaAprobacionUtc),
                ("@error", Limitar(error, 2000)),
                ("@modelo", Limitar(modeloIA, 160)),
                ("@incremento", incrementarIntento ? 1 : 0));
        }

        public async Task DescartarFotoAsync(
            int fotografiaId,
            int usuarioId,
            string motivo,
            CancellationToken cancellationToken = default)
        {
            FotoMetadatos? actual = await ObtenerFotoAsync(
                fotografiaId,
                cancellationToken);
            if (actual == null)
                throw new InvalidOperationException("La fotografía no existe.");

            const string sql = """
UPDATE dbo.diagnosticoIAImagen
SET Estado = N'DESCARTADA',
    Descartada = 1,
    MotivoDescarte = @motivo,
    UsuarioDescarteId = @usuarioId,
    FechaDescarteUtc = SYSUTCDATETIME()
WHERE DiagnosticoIAImagenId = @fotoId;

INSERT INTO dbo.diagnosticoIAImagenHistorialV2
(
    DiagnosticoIAImagenId, UsuarioId, EstadoAnterior,
    EstadoNuevo, Accion, Detalle, FechaUtc
)
VALUES
(
    @fotoId, @usuarioId, @estadoAnterior, N'DESCARTADA',
    N'FOTO_DESCARTADA', @motivo, SYSUTCDATETIME()
);
""";

            await EjecutarAsync(
                sql,
                cancellationToken,
                ("@fotoId", fotografiaId),
                ("@usuarioId", usuarioId),
                ("@estadoAnterior", actual.Estado),
                ("@motivo", Limitar(motivo, 1000)));
        }

        public async Task<int> CrearRevisionIAAsync(
            int fotografiaId,
            int usuarioId,
            string tipoRevision,
            string retroalimentacion,
            string diagnosticoPropuesto,
            string proveedor,
            string modelo,
            CancellationToken cancellationToken = default)
        {
            const string sql = """
INSERT INTO dbo.diagnosticoIAImagenRevisionIA
(
    DiagnosticoIAImagenId, UsuarioSolicitanteId, TipoRevision,
    Retroalimentacion, DiagnosticoPropuesto, ProveedorIA,
    ModeloIA, Estado, FechaSolicitudUtc
)
VALUES
(
    @fotoId, @usuarioId, @tipo, @retroalimentacion,
    @diagnosticoPropuesto, @proveedor, @modelo, N'ANALIZANDO',
    SYSUTCDATETIME()
);
SELECT CAST(SCOPE_IDENTITY() AS INT);
""";

            return await EjecutarEscalarIntAsync(
                sql,
                cancellationToken,
                ("@fotoId", fotografiaId),
                ("@usuarioId", usuarioId),
                ("@tipo", Limitar(tipoRevision, 30)),
                ("@retroalimentacion", Limitar(retroalimentacion, 2000)),
                ("@diagnosticoPropuesto", Limitar(diagnosticoPropuesto, 300)),
                ("@proveedor", Limitar(proveedor, 40)),
                ("@modelo", Limitar(modelo, 160)));
        }

        public Task CompletarRevisionIAAsync(
            int revisionId,
            string estado,
            string respuestaJson,
            string error,
            CancellationToken cancellationToken = default) =>
            EjecutarAsync(
                """
UPDATE dbo.diagnosticoIAImagenRevisionIA
SET Estado = @estado,
    RespuestaJson = @respuesta,
    Error = @error,
    FechaRespuestaUtc = SYSUTCDATETIME()
WHERE DiagnosticoIAImagenRevisionIAId = @revisionId;
""",
                cancellationToken,
                ("@revisionId", revisionId),
                ("@estado", Limitar(estado, 30)),
                ("@respuesta", respuestaJson ?? string.Empty),
                ("@error", Limitar(error, 2000)));

        /// <summary>
        /// Guarda una nueva revisión visual y deja de marcar como vigente a las
        /// anteriores. Los archivos físicos anteriores no se eliminan.
        /// </summary>
        public Task GuardarResultadoVisualAsync(
            int fotografiaId,
            int revision,
            string diagnosticosJson,
            string rutaImagenMarcada,
            string proveedor,
            string modelo,
            CancellationToken cancellationToken = default) =>
            EjecutarAsync(
                """
SET XACT_ABORT ON;
BEGIN TRANSACTION;
BEGIN TRY
    UPDATE dbo.diagnosticoIAImagenResultadoVisualV2
    SET EsVigente = 0
    WHERE DiagnosticoIAImagenId = @fotoId
      AND EsVigente = 1;

    INSERT INTO dbo.diagnosticoIAImagenResultadoVisualV2
    (
        DiagnosticoIAImagenId, Revision, EsVigente,
        DiagnosticosJson, RutaImagenMarcada,
        ProveedorIA, ModeloIA, FechaGeneracionUtc
    )
    VALUES
    (
        @fotoId, @revision, 1, @diagnosticosJson, @rutaMarcada,
        @proveedor, @modelo, SYSUTCDATETIME()
    );

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
""",
                cancellationToken,
                ("@fotoId", fotografiaId),
                ("@revision", revision),
                ("@diagnosticosJson", string.IsNullOrWhiteSpace(diagnosticosJson)
                    ? "[]"
                    : diagnosticosJson),
                ("@rutaMarcada", Limitar(rutaImagenMarcada, 600)),
                ("@proveedor", Limitar(proveedor, 40)),
                ("@modelo", Limitar(modelo, 160)));

        public async Task<ResultadoVisualRegistro?>
            ObtenerResultadoVisualVigenteAsync(
                int fotografiaId,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);
            const string sql = """
SELECT TOP(1)
    DiagnosticoIAImagenResultadoVisualId,
    DiagnosticoIAImagenId, Revision, EsVigente,
    DiagnosticosJson, RutaImagenMarcada,
    ProveedorIA, ModeloIA, FechaGeneracionUtc
FROM dbo.diagnosticoIAImagenResultadoVisualV2
WHERE DiagnosticoIAImagenId = @fotoId
ORDER BY EsVigente DESC, Revision DESC,
         DiagnosticoIAImagenResultadoVisualId DESC;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@fotoId", fotografiaId);
            await AbrirAsync(comando.Connection!, cancellationToken);
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? LeerResultadoVisual(reader)
                : null;
        }

        public async Task<Dictionary<int, ResultadoVisualRegistro>>
            ObtenerResultadosVisualesVigentesAsync(
                int diagnosticoId,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);
            const string sql = """
WITH visuales AS
(
    SELECT
        v.DiagnosticoIAImagenResultadoVisualId,
        v.DiagnosticoIAImagenId, v.Revision, v.EsVigente,
        v.DiagnosticosJson, v.RutaImagenMarcada,
        v.ProveedorIA, v.ModeloIA, v.FechaGeneracionUtc,
        ROW_NUMBER() OVER
        (
            PARTITION BY v.DiagnosticoIAImagenId
            ORDER BY v.EsVigente DESC, v.Revision DESC,
                     v.DiagnosticoIAImagenResultadoVisualId DESC
        ) AS rn
    FROM dbo.diagnosticoIAImagenResultadoVisualV2 v
    INNER JOIN dbo.diagnosticoIAImagen i
        ON i.DiagnosticoIAImagenId = v.DiagnosticoIAImagenId
    WHERE i.DiagnosticoIAId = @diagnosticoId
)
SELECT
    DiagnosticoIAImagenResultadoVisualId,
    DiagnosticoIAImagenId, Revision, EsVigente,
    DiagnosticosJson, RutaImagenMarcada,
    ProveedorIA, ModeloIA, FechaGeneracionUtc
FROM visuales
WHERE rn = 1;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@diagnosticoId", diagnosticoId);
            await AbrirAsync(comando.Connection!, cancellationToken);
            var resultado = new Dictionary<int, ResultadoVisualRegistro>();
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ResultadoVisualRegistro registro = LeerResultadoVisual(reader);
                resultado[registro.FotografiaId] = registro;
            }
            return resultado;
        }

        /// <summary>
        /// Compatibilidad con controladores que todavía reenvían el último
        /// análisis humano sin conocer la nueva colección Diagnosticos.
        /// Conserva automáticamente dicha colección para no perder el trabajo
        /// por diagnóstico al crear una nueva versión ENVIADO.
        /// </summary>
        public async Task<int> GuardarAnalisisHumanoAsync(
            int fotografiaId,
            int usuarioId,
            string calidad,
            string estadoGeneral,
            string categoria,
            string categoriasJson,
            string diagnostico,
            string tipo,
            string severidad,
            string certeza,
            string observaciones,
            bool enviar,
            CancellationToken cancellationToken = default)
        {
            AnalisisHumanoRegistro? anterior =
                await ObtenerUltimoAnalisisHumanoAsync(
                    fotografiaId,
                    cancellationToken);

            return await GuardarAnalisisHumanoAsync(
                fotografiaId,
                usuarioId,
                calidad,
                estadoGeneral,
                categoria,
                categoriasJson,
                diagnostico,
                tipo,
                severidad,
                certeza,
                observaciones,
                anterior?.DiagnosticosJson ?? "[]",
                enviar,
                cancellationToken);
        }

        public async Task<int> GuardarAnalisisHumanoAsync(
            int fotografiaId,
            int usuarioId,
            string calidad,
            string estadoGeneral,
            string categoria,
            string categoriasJson,
            string diagnostico,
            string tipo,
            string severidad,
            string certeza,
            string observaciones,
            string diagnosticosJson,
            bool enviar,
            CancellationToken cancellationToken = default)
        {
            const string sql = """
DECLARE @version INT =
(
    SELECT ISNULL(MAX(Version), 0) + 1
    FROM dbo.diagnosticoIAImagenAnalisisHumano WITH (UPDLOCK, HOLDLOCK)
    WHERE DiagnosticoIAImagenId = @fotoId
);

INSERT INTO dbo.diagnosticoIAImagenAnalisisHumano
(
    DiagnosticoIAImagenId, UsuarioAnalizadorId, Version,
    EstadoRegistro, CalidadEvaluacion, EstadoGeneral,
    CategoriaPrincipal, CategoriasSecundariasJson, Diagnostico,
    TipoDiagnostico, Severidad, NivelCerteza, Observaciones,
    DiagnosticosJson, FechaCreacionUtc, FechaActualizacionUtc,
    FechaEnvioUtc
)
VALUES
(
    @fotoId, @usuarioId, @version,
    CASE WHEN @enviar = 1 THEN N'ENVIADO' ELSE N'BORRADOR' END,
    @calidad, @estadoGeneral, @categoria, @categoriasJson, @diagnostico,
    @tipo, @severidad, @certeza, @observaciones, @diagnosticosJson,
    SYSUTCDATETIME(), SYSUTCDATETIME(),
    CASE WHEN @enviar = 1 THEN SYSUTCDATETIME() ELSE NULL END
);
SELECT CAST(SCOPE_IDENTITY() AS INT);
""";

            return await EjecutarEscalarIntAsync(
                sql,
                cancellationToken,
                ("@fotoId", fotografiaId),
                ("@usuarioId", usuarioId),
                ("@enviar", enviar),
                ("@calidad", Limitar(calidad, 30)),
                ("@estadoGeneral", Limitar(estadoGeneral, 40)),
                ("@categoria", Limitar(categoria, 50)),
                ("@categoriasJson", categoriasJson),
                ("@diagnostico", Limitar(diagnostico, 300)),
                ("@tipo", Limitar(tipo, 80)),
                ("@severidad", Limitar(severidad, 30)),
                ("@certeza", Limitar(certeza, 30)),
                ("@observaciones", Limitar(observaciones, 3000)),
                ("@diagnosticosJson", string.IsNullOrWhiteSpace(diagnosticosJson)
                    ? "[]"
                    : diagnosticosJson));
        }

        public async Task<AnalisisHumanoRegistro?> ObtenerUltimoAnalisisHumanoAsync(
            int fotografiaId,
            CancellationToken cancellationToken = default)
        {
            const string sql = """
SELECT TOP(1)
    DiagnosticoIAImagenAnalisisHumanoId, DiagnosticoIAImagenId,
    UsuarioAnalizadorId, Version, EstadoRegistro,
    CalidadEvaluacion, EstadoGeneral, CategoriaPrincipal,
    CategoriasSecundariasJson, Diagnostico, TipoDiagnostico,
    Severidad, NivelCerteza, Observaciones, DiagnosticosJson,
    FechaCreacionUtc, FechaEnvioUtc
FROM dbo.diagnosticoIAImagenAnalisisHumano
WHERE DiagnosticoIAImagenId = @fotoId
ORDER BY Version DESC, DiagnosticoIAImagenAnalisisHumanoId DESC;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@fotoId", fotografiaId);
            await AbrirAsync(comando.Connection!, cancellationToken);
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? LeerAnalisisHumano(reader)
                : null;
        }

        public async Task<Dictionary<int, AnalisisHumanoRegistro>>
            ObtenerUltimosAnalisisHumanosAsync(
                int diagnosticoId,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);
            const string sql = """
WITH ultimos AS
(
    SELECT
        h.DiagnosticoIAImagenAnalisisHumanoId,
        h.DiagnosticoIAImagenId, h.UsuarioAnalizadorId,
        h.Version, h.EstadoRegistro, h.CalidadEvaluacion,
        h.EstadoGeneral, h.CategoriaPrincipal,
        h.CategoriasSecundariasJson, h.Diagnostico,
        h.TipoDiagnostico, h.Severidad, h.NivelCerteza,
        h.Observaciones, h.DiagnosticosJson,
        h.FechaCreacionUtc, h.FechaEnvioUtc,
        ROW_NUMBER() OVER
        (
            PARTITION BY h.DiagnosticoIAImagenId
            ORDER BY h.Version DESC,
                     h.DiagnosticoIAImagenAnalisisHumanoId DESC
        ) AS rn
    FROM dbo.diagnosticoIAImagenAnalisisHumano h
    INNER JOIN dbo.diagnosticoIAImagen i
        ON i.DiagnosticoIAImagenId = h.DiagnosticoIAImagenId
    WHERE i.DiagnosticoIAId = @diagnosticoId
)
SELECT
    DiagnosticoIAImagenAnalisisHumanoId, DiagnosticoIAImagenId,
    UsuarioAnalizadorId, Version, EstadoRegistro,
    CalidadEvaluacion, EstadoGeneral, CategoriaPrincipal,
    CategoriasSecundariasJson, Diagnostico, TipoDiagnostico,
    Severidad, NivelCerteza, Observaciones, DiagnosticosJson,
    FechaCreacionUtc, FechaEnvioUtc
FROM ultimos
WHERE rn = 1;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@diagnosticoId", diagnosticoId);
            await AbrirAsync(comando.Connection!, cancellationToken);
            var resultado = new Dictionary<int, AnalisisHumanoRegistro>();
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                AnalisisHumanoRegistro registro = LeerAnalisisHumano(reader);
                resultado[registro.FotografiaId] = registro;
            }
            return resultado;
        }

        public async Task<int> RegistrarAprobacionAsync(
            int fotografiaId,
            int? analisisHumanoId,
            int usuarioAprobadorId,
            string decision,
            string calidad,
            string estadoGeneral,
            string categoria,
            string categoriasJson,
            string diagnostico,
            string tipo,
            string severidad,
            string certeza,
            string observaciones,
            string diagnosticosFinalesJson,
            bool autorizaAlbum,
            bool mismoUsuario,
            CancellationToken cancellationToken = default)
        {
            const string sql = """
INSERT INTO dbo.diagnosticoIAImagenAprobacionV2
(
    DiagnosticoIAImagenId, DiagnosticoIAImagenAnalisisHumanoId,
    UsuarioAprobadorId, Decision, CalidadEvaluacionFinal,
    EstadoGeneralFinal, CategoriaPrincipalFinal,
    CategoriasSecundariasFinalJson, DiagnosticoFinal,
    TipoDiagnosticoFinal, SeveridadFinal, NivelCertezaFinal,
    Observaciones, DiagnosticosFinalesJson, AutorizaPublicacionAlbum,
    MismoUsuarioQueAnalizo, FechaAprobacionUtc
)
VALUES
(
    @fotoId, @analisisId, @usuarioId, @decision, @calidad,
    @estadoGeneral, @categoria, @categoriasJson, @diagnostico,
    @tipo, @severidad, @certeza, @observaciones, @diagnosticosFinalesJson,
    @autorizaAlbum, @mismoUsuario, SYSUTCDATETIME()
);
SELECT CAST(SCOPE_IDENTITY() AS INT);
""";

            return await EjecutarEscalarIntAsync(
                sql,
                cancellationToken,
                ("@fotoId", fotografiaId),
                ("@analisisId", analisisHumanoId),
                ("@usuarioId", usuarioAprobadorId),
                ("@decision", Limitar(decision, 40)),
                ("@calidad", Limitar(calidad, 30)),
                ("@estadoGeneral", Limitar(estadoGeneral, 40)),
                ("@categoria", Limitar(categoria, 50)),
                ("@categoriasJson", categoriasJson),
                ("@diagnostico", Limitar(diagnostico, 300)),
                ("@tipo", Limitar(tipo, 80)),
                ("@severidad", Limitar(severidad, 30)),
                ("@certeza", Limitar(certeza, 30)),
                ("@observaciones", Limitar(observaciones, 3000)),
                ("@diagnosticosFinalesJson",
                    string.IsNullOrWhiteSpace(diagnosticosFinalesJson)
                        ? "[]"
                        : diagnosticosFinalesJson),
                ("@autorizaAlbum", autorizaAlbum),
                ("@mismoUsuario", mismoUsuario));
        }

        public async Task<AprobacionRegistro?> ObtenerUltimaAprobacionAsync(
            int fotografiaId,
            CancellationToken cancellationToken = default)
        {
            const string sql = """
SELECT TOP(1)
    DiagnosticoIAImagenAprobacionId, DiagnosticoIAImagenId,
    DiagnosticoIAImagenAnalisisHumanoId, UsuarioAprobadorId,
    Decision, DiagnosticoFinal, Observaciones,
    DiagnosticosFinalesJson, AutorizaPublicacionAlbum,
    MismoUsuarioQueAnalizo, FechaAprobacionUtc
FROM dbo.diagnosticoIAImagenAprobacionV2
WHERE DiagnosticoIAImagenId = @fotoId
ORDER BY FechaAprobacionUtc DESC,
         DiagnosticoIAImagenAprobacionId DESC;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@fotoId", fotografiaId);
            await AbrirAsync(comando.Connection!, cancellationToken);
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? LeerAprobacion(reader)
                : null;
        }

        public async Task<Dictionary<int, AprobacionRegistro>>
            ObtenerUltimasAprobacionesAsync(
                int diagnosticoId,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);
            const string sql = """
WITH ultimas AS
(
    SELECT
        a.DiagnosticoIAImagenAprobacionId,
        a.DiagnosticoIAImagenId,
        a.DiagnosticoIAImagenAnalisisHumanoId,
        a.UsuarioAprobadorId, a.Decision,
        a.DiagnosticoFinal, a.Observaciones,
        a.DiagnosticosFinalesJson,
        a.AutorizaPublicacionAlbum, a.MismoUsuarioQueAnalizo,
        a.FechaAprobacionUtc,
        ROW_NUMBER() OVER
        (
            PARTITION BY a.DiagnosticoIAImagenId
            ORDER BY a.FechaAprobacionUtc DESC,
                     a.DiagnosticoIAImagenAprobacionId DESC
        ) AS rn
    FROM dbo.diagnosticoIAImagenAprobacionV2 a
    INNER JOIN dbo.diagnosticoIAImagen i
        ON i.DiagnosticoIAImagenId = a.DiagnosticoIAImagenId
    WHERE i.DiagnosticoIAId = @diagnosticoId
)
SELECT
    DiagnosticoIAImagenAprobacionId, DiagnosticoIAImagenId,
    DiagnosticoIAImagenAnalisisHumanoId, UsuarioAprobadorId,
    Decision, DiagnosticoFinal, Observaciones,
    DiagnosticosFinalesJson, AutorizaPublicacionAlbum,
    MismoUsuarioQueAnalizo, FechaAprobacionUtc
FROM ultimas
WHERE rn = 1;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@diagnosticoId", diagnosticoId);
            await AbrirAsync(comando.Connection!, cancellationToken);
            var resultado = new Dictionary<int, AprobacionRegistro>();
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                AprobacionRegistro registro = LeerAprobacion(reader);
                resultado[registro.FotografiaId] = registro;
            }
            return resultado;
        }

        public async Task<List<HistorialFotoRegistro>> ObtenerHistorialAsync(
            int fotografiaId,
            CancellationToken cancellationToken = default)
        {
            const string sql = """
SELECT
    DiagnosticoIAImagenHistorialId, DiagnosticoIAImagenId,
    UsuarioId, EstadoAnterior, EstadoNuevo, Accion,
    Detalle, FechaUtc
FROM dbo.diagnosticoIAImagenHistorialV2
WHERE DiagnosticoIAImagenId = @fotoId
ORDER BY FechaUtc DESC, DiagnosticoIAImagenHistorialId DESC;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@fotoId", fotografiaId);
            await AbrirAsync(comando.Connection!, cancellationToken);
            var resultado = new List<HistorialFotoRegistro>();
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                resultado.Add(LeerHistorial(reader));
            return resultado;
        }

        public async Task<Dictionary<int, List<HistorialFotoRegistro>>>
            ObtenerHistorialInspeccionAsync(
                int diagnosticoId,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);
            const string sql = """
SELECT
    h.DiagnosticoIAImagenHistorialId, h.DiagnosticoIAImagenId,
    h.UsuarioId, h.EstadoAnterior, h.EstadoNuevo, h.Accion,
    h.Detalle, h.FechaUtc
FROM dbo.diagnosticoIAImagenHistorialV2 h
INNER JOIN dbo.diagnosticoIAImagen i
    ON i.DiagnosticoIAImagenId = h.DiagnosticoIAImagenId
WHERE i.DiagnosticoIAId = @diagnosticoId
ORDER BY h.DiagnosticoIAImagenId, h.FechaUtc DESC,
         h.DiagnosticoIAImagenHistorialId DESC;
""";

            await using DbCommand comando = CrearComando(sql);
            AgregarParametro(comando, "@diagnosticoId", diagnosticoId);
            await AbrirAsync(comando.Connection!, cancellationToken);
            var resultado =
                new Dictionary<int, List<HistorialFotoRegistro>>();
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                HistorialFotoRegistro registro = LeerHistorial(reader);
                if (!resultado.TryGetValue(
                        registro.FotografiaId,
                        out List<HistorialFotoRegistro>? lista))
                {
                    lista = [];
                    resultado[registro.FotografiaId] = lista;
                }
                lista.Add(registro);
            }
            return resultado;
        }

        public async Task<ProveedorConfiguracionRegistro> ObtenerProveedorAsync(
            CancellationToken cancellationToken = default)
        {
            await InicializarProveedorAsync(cancellationToken);
            const string sql = """
SELECT TOP(1)
    Proveedor, Protocolo, BaseUrl, Endpoint,
    ApiKeyProtegida, ApiKeyMascara, ModeloPrincipal,
    ModeloRespaldo, TimeoutSegundos, Activo,
    FechaModificacionUtc, UsuarioModificacionId
FROM dbo.diagnosticoIAProveedorConfiguracion
WHERE DiagnosticoIAProveedorConfiguracionId = 1;
""";

            await using DbCommand comando = CrearComando(sql);
            await AbrirAsync(comando.Connection!, cancellationToken);
            await using DbDataReader reader =
                await comando.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "No existe la configuración del proveedor de IA.");
            }

            return new ProveedorConfiguracionRegistro(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetInt32(8),
                reader.GetBoolean(9),
                reader.GetDateTime(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11));
        }

        public async Task GuardarProveedorAsync(
            ProveedorConfiguracionRegistro configuracion,
            int usuarioId,
            string historialJson,
            CancellationToken cancellationToken = default)
        {
            await InicializarProveedorAsync(cancellationToken);
            const string sql = """
UPDATE dbo.diagnosticoIAProveedorConfiguracion
SET Proveedor = @proveedor,
    Protocolo = @protocolo,
    BaseUrl = @baseUrl,
    Endpoint = @endpoint,
    ApiKeyProtegida = @apiKey,
    ApiKeyMascara = @mascara,
    ModeloPrincipal = @modelo,
    ModeloRespaldo = @respaldo,
    TimeoutSegundos = @timeout,
    Activo = @activo,
    FechaModificacionUtc = SYSUTCDATETIME(),
    UsuarioModificacionId = @usuarioId
WHERE DiagnosticoIAProveedorConfiguracionId = 1;

INSERT INTO dbo.diagnosticoIAProveedorConfiguracionHistorial
(
    ConfiguracionJson, UsuarioId, FechaUtc
)
VALUES
(
    @historialJson, @usuarioId, SYSUTCDATETIME()
);
""";

            await EjecutarAsync(
                sql,
                cancellationToken,
                ("@proveedor", Limitar(configuracion.Proveedor, 40)),
                ("@protocolo", Limitar(configuracion.Protocolo, 40)),
                ("@baseUrl", Limitar(configuracion.BaseUrl, 500)),
                ("@endpoint", Limitar(configuracion.Endpoint, 300)),
                ("@apiKey", configuracion.ApiKeyProtegida),
                ("@mascara", Limitar(configuracion.ApiKeyMascara, 80)),
                ("@modelo", Limitar(configuracion.ModeloPrincipal, 160)),
                ("@respaldo", Limitar(configuracion.ModeloRespaldo, 160)),
                ("@timeout", Math.Clamp(configuracion.TimeoutSegundos, 15, 600)),
                ("@activo", configuracion.Activo),
                ("@usuarioId", usuarioId),
                ("@historialJson", historialJson));
        }

        private DbCommand CrearComando(string sql)
        {
            DbConnection conexion = db.Database.GetDbConnection();
            DbCommand comando = conexion.CreateCommand();
            comando.CommandText = sql;
            comando.CommandType = CommandType.Text;
            comando.CommandTimeout = 180;
            comando.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            return comando;
        }

        private async Task EjecutarAsync(
            string sql,
            CancellationToken cancellationToken,
            params (string Nombre, object? Valor)[] parametros)
        {
            await using DbCommand comando = CrearComando(sql);
            foreach ((string nombre, object? valor) in parametros)
                AgregarParametro(comando, nombre, valor);
            await AbrirAsync(comando.Connection!, cancellationToken);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task<int> EjecutarEscalarIntAsync(
            string sql,
            CancellationToken cancellationToken,
            params (string Nombre, object? Valor)[] parametros)
        {
            await using DbCommand comando = CrearComando(sql);
            foreach ((string nombre, object? valorParametro) in parametros)
                AgregarParametro(comando, nombre, valorParametro);
            await AbrirAsync(comando.Connection!, cancellationToken);
            object? resultado = await comando.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(resultado);
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

        private static async Task AbrirAsync(
            DbConnection conexion,
            CancellationToken cancellationToken)
        {
            if (conexion.State != ConnectionState.Open)
                await conexion.OpenAsync(cancellationToken);
        }

        private static FotoMetadatos LeerFoto(DbDataReader reader) =>
            new(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                reader.IsDBNull(4) ? DateTime.UtcNow : reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetInt32(10),
                reader.GetBoolean(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetInt32(13),
                reader.IsDBNull(14) ? null : reader.GetDateTime(14),
                reader.GetBoolean(15));

        private static ResultadoVisualRegistro LeerResultadoVisual(
            DbDataReader reader) =>
            new(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetBoolean(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetDateTime(8));

        private static AnalisisHumanoRegistro LeerAnalisisHumano(
            DbDataReader reader) =>
            new(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.GetString(13),
                reader.GetString(14),
                reader.GetDateTime(15),
                reader.IsDBNull(16) ? null : reader.GetDateTime(16));

        private static AprobacionRegistro LeerAprobacion(
            DbDataReader reader) =>
            new(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                reader.GetDateTime(10));

        private static HistorialFotoRegistro LeerHistorial(
            DbDataReader reader) =>
            new(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDateTime(7));

        private static string Limitar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo ? texto : texto[..maximo];
        }
    }

    public sealed record FotoMetadatos(
        int FotografiaId,
        int DiagnosticoId,
        string Estado,
        DateTime? FechaIdentificacionCampo,
        DateTime FechaRegistroSistemaUtc,
        DateTime? FechaAnalisisIAUtc,
        DateTime? FechaAnalisisHumanoUtc,
        DateTime? FechaAprobacionUtc,
        string ErrorProcesamiento,
        string ModeloIAUtilizado,
        int IntentosIA,
        bool Descartada,
        string MotivoDescarte,
        int? UsuarioDescarteId,
        DateTime? FechaDescarteUtc,
        bool Activo);

    public sealed record ResultadoVisualRegistro(
        int ResultadoVisualId,
        int FotografiaId,
        int Revision,
        bool EsVigente,
        string DiagnosticosJson,
        string RutaImagenMarcada,
        string ProveedorIA,
        string ModeloIA,
        DateTime FechaGeneracionUtc);

    public sealed record AnalisisHumanoRegistro(
        int AnalisisHumanoId,
        int FotografiaId,
        int UsuarioAnalizadorId,
        int Version,
        string EstadoRegistro,
        string CalidadEvaluacion,
        string EstadoGeneral,
        string CategoriaPrincipal,
        string CategoriasSecundariasJson,
        string Diagnostico,
        string TipoDiagnostico,
        string Severidad,
        string NivelCerteza,
        string Observaciones,
        string DiagnosticosJson,
        DateTime FechaCreacionUtc,
        DateTime? FechaEnvioUtc);

    public sealed record AprobacionRegistro(
        int AprobacionId,
        int FotografiaId,
        int? AnalisisHumanoId,
        int UsuarioAprobadorId,
        string Decision,
        string DiagnosticoFinal,
        string Observaciones,
        string DiagnosticosFinalesJson,
        bool AutorizaPublicacionAlbum,
        bool MismoUsuarioQueAnalizo,
        DateTime FechaAprobacionUtc);

    public sealed record HistorialFotoRegistro(
        int HistorialId,
        int FotografiaId,
        int UsuarioId,
        string EstadoAnterior,
        string EstadoNuevo,
        string Accion,
        string Detalle,
        DateTime FechaUtc);

    public sealed record ProveedorConfiguracionRegistro(
        string Proveedor,
        string Protocolo,
        string BaseUrl,
        string Endpoint,
        string ApiKeyProtegida,
        string ApiKeyMascara,
        string ModeloPrincipal,
        string ModeloRespaldo,
        int TimeoutSegundos,
        bool Activo,
        DateTime FechaModificacionUtc,
        int? UsuarioModificacionId);
}
