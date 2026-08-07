using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Persistencia del ciclo analizador → técnico y del catálogo de motivos.
    /// Toda la instalación es idempotente para conservar el esquema actual sin
    /// requerir scripts manuales al publicar el backend.
    /// </summary>
    public sealed class InspeccionFitosanitariaDevolucionDatabase
    {
        private static readonly SemaphoreSlim InicializacionLock = new(1, 1);
        private static volatile bool inicializado;

        private readonly DiagnosticoIADbContext db;

        public InspeccionFitosanitariaDevolucionDatabase(
            DiagnosticoIADbContext db)
        {
            this.db = db;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            /*
             * La revisión depende de las columnas del flujo por fotografía y
             * del control de etapas. Se inicializan antes del catálogo para que
             * una instalación nueva o parcialmente actualizada pueda repararse
             * sin ejecutar scripts manuales.
             */
            await new InspeccionFitosanitariaDatabase(db)
                .InicializarAsync(cancellationToken);
            await new InspeccionFitosanitariaControlDatabaseInitializer(db)
                .InicializarAsync(cancellationToken);

            if (inicializado)
                return;

            await InicializacionLock.WaitAsync(cancellationToken);

            try
            {
                if (inicializado)
                    return;

                await InicializarTablasAsync(cancellationToken);
                await CompletarColumnasAsync(cancellationToken);
                await AsegurarEsquemaContextoAsync(cancellationToken);
                await InicializarIndicesYRelacionesAsync(cancellationToken);
                await SembrarMotivosPredeterminadosAsync(cancellationToken);

                inicializado = true;
            }
            catch
            {
                inicializado = false;
                throw;
            }
            finally
            {
                InicializacionLock.Release();
            }
        }

        /// <summary>
        /// Crea las tablas nuevas en lotes separados. Separar creación,
        /// compatibilidad e índices evita que SQL Server intente compilar una
        /// referencia a una columna que todavía se está agregando en el mismo
        /// lote, situación que provocaba el error genérico de base de datos.
        /// </summary>
        private async Task InicializarTablasAsync(
            CancellationToken cancellationToken)
        {
            const string sql = """
IF OBJECT_ID(N'[dbo].[motivoDevolucionTecnico]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[motivoDevolucionTecnico]
    (
        [MotivoDevolucionTecnicoId] INT IDENTITY(1,1) NOT NULL,
        [Codigo] NVARCHAR(60) NOT NULL,
        [Nombre] NVARCHAR(140) NOT NULL,
        [Descripcion] NVARCHAR(700) NOT NULL
            CONSTRAINT [DF_motivoDevTec_descripcion] DEFAULT(N''),
        [InstruccionSugerida] NVARCHAR(2000) NOT NULL
            CONSTRAINT [DF_motivoDevTec_instruccion] DEFAULT(N''),
        [RequiereNuevaFotografia] BIT NOT NULL
            CONSTRAINT [DF_motivoDevTec_nuevaFoto] DEFAULT(0),
        [PermiteCorregirMetadatos] BIT NOT NULL
            CONSTRAINT [DF_motivoDevTec_metadatos] DEFAULT(1),
        [Orden] INT NOT NULL
            CONSTRAINT [DF_motivoDevTec_orden] DEFAULT(1),
        [Activo] BIT NOT NULL
            CONSTRAINT [DF_motivoDevTec_activo] DEFAULT(1),
        [FechaCreacionUtc] DATETIME2(0) NOT NULL
            CONSTRAINT [DF_motivoDevTec_fechaCreacion] DEFAULT(SYSUTCDATETIME()),
        [UsuarioCreacionId] INT NULL,
        [FechaModificacionUtc] DATETIME2(0) NOT NULL
            CONSTRAINT [DF_motivoDevTec_fechaModificacion] DEFAULT(SYSUTCDATETIME()),
        [UsuarioModificacionId] INT NULL,
        CONSTRAINT [PK_motivoDevolucionTecnico]
            PRIMARY KEY ([MotivoDevolucionTecnicoId])
    );
END;

IF OBJECT_ID(N'[dbo].[diagnosticoIAImagenDevolucionTecnico]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAImagenDevolucionTecnico]
    (
        [DiagnosticoIAImagenDevolucionTecnicoId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAImagenId] INT NOT NULL,
        [MotivoDevolucionTecnicoId] INT NOT NULL,
        [MotivoCodigo] NVARCHAR(60) NOT NULL,
        [MotivoNombre] NVARCHAR(140) NOT NULL,
        [MotivoDescripcion] NVARCHAR(700) NOT NULL
            CONSTRAINT [DF_diagIAImgDevTec_descripcion] DEFAULT(N''),
        [InstruccionSugerida] NVARCHAR(2000) NOT NULL
            CONSTRAINT [DF_diagIAImgDevTec_sugerida] DEFAULT(N''),
        [InstruccionesAnalizador] NVARCHAR(3000) NOT NULL
            CONSTRAINT [DF_diagIAImgDevTec_instrucciones] DEFAULT(N''),
        [RequiereNuevaFotografia] BIT NOT NULL
            CONSTRAINT [DF_diagIAImgDevTec_nuevaFoto] DEFAULT(0),
        [PermiteCorregirMetadatos] BIT NOT NULL
            CONSTRAINT [DF_diagIAImgDevTec_metadatos] DEFAULT(1),
        [UsuarioAnalizadorId] INT NOT NULL,
        [FechaDevolucionUtc] DATETIME2(0) NOT NULL
            CONSTRAINT [DF_diagIAImgDevTec_fecha] DEFAULT(SYSUTCDATETIME()),
        [Estado] NVARCHAR(20) NOT NULL
            CONSTRAINT [DF_diagIAImgDevTec_estado] DEFAULT(N'PENDIENTE'),
        [RespuestaTecnico] NVARCHAR(2000) NOT NULL
            CONSTRAINT [DF_diagIAImgDevTec_respuesta] DEFAULT(N''),
        [UsuarioTecnicoId] INT NULL,
        [FechaResolucionUtc] DATETIME2(0) NULL,
        CONSTRAINT [PK_diagIAImagenDevolucionTecnico]
            PRIMARY KEY ([DiagnosticoIAImagenDevolucionTecnicoId])
    );
END;

IF OBJECT_ID(N'[dbo].[diagnosticoIARevisionAnalizadorControl]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIARevisionAnalizadorControl]
    (
        [DiagnosticoIAId] INT NOT NULL,
        [EtapaAnalizadorFinalizada] BIT NOT NULL
            CONSTRAINT [DF_diagIARevAna_finalizada] DEFAULT(0),
        [FechaFinEtapaAnalizadorUtc] DATETIME2(0) NULL,
        [UsuarioFinEtapaAnalizadorId] INT NULL,
        CONSTRAINT [PK_diagnosticoIARevisionAnalizadorControl]
            PRIMARY KEY ([DiagnosticoIAId])
    );
END;
""";

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        private async Task CompletarColumnasAsync(
            CancellationToken cancellationToken)
        {
            const string sql = """
IF COL_LENGTH(N'dbo.motivoDevolucionTecnico', N'Codigo') IS NULL
    ALTER TABLE dbo.motivoDevolucionTecnico ADD Codigo NVARCHAR(60) NOT NULL CONSTRAINT DF_motivoDevTec_codigoCompat DEFAULT(N'') WITH VALUES;
IF COL_LENGTH(N'dbo.motivoDevolucionTecnico', N'Nombre') IS NULL
    ALTER TABLE dbo.motivoDevolucionTecnico ADD Nombre NVARCHAR(140) NOT NULL CONSTRAINT DF_motivoDevTec_nombreCompat DEFAULT(N'') WITH VALUES;
IF COL_LENGTH(N'dbo.motivoDevolucionTecnico', N'Descripcion') IS NULL
    ALTER TABLE dbo.motivoDevolucionTecnico ADD Descripcion NVARCHAR(700) NOT NULL CONSTRAINT DF_motivoDevTec_descCompat DEFAULT(N'') WITH VALUES;
IF COL_LENGTH(N'dbo.motivoDevolucionTecnico', N'InstruccionSugerida') IS NULL
    ALTER TABLE dbo.motivoDevolucionTecnico ADD InstruccionSugerida NVARCHAR(2000) NOT NULL CONSTRAINT DF_motivoDevTec_instCompat DEFAULT(N'') WITH VALUES;
IF COL_LENGTH(N'dbo.motivoDevolucionTecnico', N'RequiereNuevaFotografia') IS NULL
    ALTER TABLE dbo.motivoDevolucionTecnico ADD RequiereNuevaFotografia BIT NOT NULL CONSTRAINT DF_motivoDevTec_fotoCompat DEFAULT(0) WITH VALUES;
IF COL_LENGTH(N'dbo.motivoDevolucionTecnico', N'PermiteCorregirMetadatos') IS NULL
    ALTER TABLE dbo.motivoDevolucionTecnico ADD PermiteCorregirMetadatos BIT NOT NULL CONSTRAINT DF_motivoDevTec_metaCompat DEFAULT(1) WITH VALUES;
IF COL_LENGTH(N'dbo.motivoDevolucionTecnico', N'Orden') IS NULL
    ALTER TABLE dbo.motivoDevolucionTecnico ADD Orden INT NOT NULL CONSTRAINT DF_motivoDevTec_ordenCompat DEFAULT(1) WITH VALUES;
IF COL_LENGTH(N'dbo.motivoDevolucionTecnico', N'Activo') IS NULL
    ALTER TABLE dbo.motivoDevolucionTecnico ADD Activo BIT NOT NULL CONSTRAINT DF_motivoDevTec_activoCompat DEFAULT(1) WITH VALUES;
IF COL_LENGTH(N'dbo.motivoDevolucionTecnico', N'FechaCreacionUtc') IS NULL
    ALTER TABLE dbo.motivoDevolucionTecnico ADD FechaCreacionUtc DATETIME2(0) NOT NULL CONSTRAINT DF_motivoDevTec_creacionCompat DEFAULT(SYSUTCDATETIME()) WITH VALUES;
IF COL_LENGTH(N'dbo.motivoDevolucionTecnico', N'UsuarioCreacionId') IS NULL
    ALTER TABLE dbo.motivoDevolucionTecnico ADD UsuarioCreacionId INT NULL;
IF COL_LENGTH(N'dbo.motivoDevolucionTecnico', N'FechaModificacionUtc') IS NULL
    ALTER TABLE dbo.motivoDevolucionTecnico ADD FechaModificacionUtc DATETIME2(0) NOT NULL CONSTRAINT DF_motivoDevTec_modCompat DEFAULT(SYSUTCDATETIME()) WITH VALUES;
IF COL_LENGTH(N'dbo.motivoDevolucionTecnico', N'UsuarioModificacionId') IS NULL
    ALTER TABLE dbo.motivoDevolucionTecnico ADD UsuarioModificacionId INT NULL;

IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'DiagnosticoIAImagenId') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD DiagnosticoIAImagenId INT NOT NULL CONSTRAINT DF_diagIAImgDevTec_imgCompat DEFAULT(0) WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'MotivoDevolucionTecnicoId') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD MotivoDevolucionTecnicoId INT NOT NULL CONSTRAINT DF_diagIAImgDevTec_motivoCompat DEFAULT(0) WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'MotivoCodigo') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD MotivoCodigo NVARCHAR(60) NOT NULL CONSTRAINT DF_diagIAImgDevTec_codigoCompat DEFAULT(N'') WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'MotivoNombre') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD MotivoNombre NVARCHAR(140) NOT NULL CONSTRAINT DF_diagIAImgDevTec_nombreCompat DEFAULT(N'') WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'MotivoDescripcion') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD MotivoDescripcion NVARCHAR(700) NOT NULL CONSTRAINT DF_diagIAImgDevTec_descCompat DEFAULT(N'') WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'InstruccionSugerida') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD InstruccionSugerida NVARCHAR(2000) NOT NULL CONSTRAINT DF_diagIAImgDevTec_sugCompat DEFAULT(N'') WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'InstruccionesAnalizador') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD InstruccionesAnalizador NVARCHAR(3000) NOT NULL CONSTRAINT DF_diagIAImgDevTec_instCompat DEFAULT(N'') WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'RequiereNuevaFotografia') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD RequiereNuevaFotografia BIT NOT NULL CONSTRAINT DF_diagIAImgDevTec_fotoCompat DEFAULT(0) WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'PermiteCorregirMetadatos') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD PermiteCorregirMetadatos BIT NOT NULL CONSTRAINT DF_diagIAImgDevTec_metaCompat DEFAULT(1) WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'UsuarioAnalizadorId') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD UsuarioAnalizadorId INT NOT NULL CONSTRAINT DF_diagIAImgDevTec_usrAnaCompat DEFAULT(0) WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'FechaDevolucionUtc') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD FechaDevolucionUtc DATETIME2(0) NOT NULL CONSTRAINT DF_diagIAImgDevTec_fechaCompat DEFAULT(SYSUTCDATETIME()) WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'Estado') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD Estado NVARCHAR(20) NOT NULL CONSTRAINT DF_diagIAImgDevTec_estadoCompat DEFAULT(N'PENDIENTE') WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'RespuestaTecnico') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD RespuestaTecnico NVARCHAR(2000) NOT NULL CONSTRAINT DF_diagIAImgDevTec_respCompat DEFAULT(N'') WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'UsuarioTecnicoId') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD UsuarioTecnicoId INT NULL;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenDevolucionTecnico', N'FechaResolucionUtc') IS NULL
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico ADD FechaResolucionUtc DATETIME2(0) NULL;

IF COL_LENGTH(N'dbo.diagnosticoIARevisionAnalizadorControl', N'EtapaAnalizadorFinalizada') IS NULL
    ALTER TABLE dbo.diagnosticoIARevisionAnalizadorControl ADD EtapaAnalizadorFinalizada BIT NOT NULL CONSTRAINT DF_diagIARevAna_finCompat DEFAULT(0) WITH VALUES;
IF COL_LENGTH(N'dbo.diagnosticoIARevisionAnalizadorControl', N'FechaFinEtapaAnalizadorUtc') IS NULL
    ALTER TABLE dbo.diagnosticoIARevisionAnalizadorControl ADD FechaFinEtapaAnalizadorUtc DATETIME2(0) NULL;
IF COL_LENGTH(N'dbo.diagnosticoIARevisionAnalizadorControl', N'UsuarioFinEtapaAnalizadorId') IS NULL
    ALTER TABLE dbo.diagnosticoIARevisionAnalizadorControl ADD UsuarioFinEtapaAnalizadorId INT NULL;
""";

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        /// <summary>
        /// Repara la tabla auxiliar utilizada por el contexto de revisión. Las
        /// primeras versiones del flujo podían dejar esta tabla creada sin su
        /// identificador principal cuando una publicación se interrumpía. Cada
        /// paso se ejecuta en un lote separado para que SQL Server no compile
        /// referencias a columnas que todavía no existen.
        /// </summary>
        private async Task AsegurarEsquemaContextoAsync(
            CancellationToken cancellationToken)
        {
            const string sqlAgregarIdentificador = """
IF OBJECT_ID(N'[dbo].[diagnosticoIARevisionAnalizadorControl]', N'U') IS NOT NULL
   AND COL_LENGTH(
       N'dbo.diagnosticoIARevisionAnalizadorControl',
       N'DiagnosticoIAId') IS NULL
BEGIN
    ALTER TABLE dbo.diagnosticoIARevisionAnalizadorControl
        ADD DiagnosticoIAId INT NULL;
END;
""";

            await db.Database.ExecuteSqlRawAsync(
                sqlAgregarIdentificador,
                cancellationToken);

            const string sqlRepararRegistros = """
IF OBJECT_ID(N'[dbo].[diagnosticoIARevisionAnalizadorControl]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(
           N'dbo.diagnosticoIARevisionAnalizadorControl',
           N'InspeccionId') IS NOT NULL
    BEGIN
        EXEC(N'
UPDATE dbo.diagnosticoIARevisionAnalizadorControl
SET DiagnosticoIAId = InspeccionId
WHERE DiagnosticoIAId IS NULL;');
    END;

    DELETE controlRevision
    FROM dbo.diagnosticoIARevisionAnalizadorControl controlRevision
    LEFT JOIN dbo.diagnosticoIA diagnostico
        ON diagnostico.DiagnosticoIAId =
           controlRevision.DiagnosticoIAId
    WHERE controlRevision.DiagnosticoIAId IS NULL
       OR diagnostico.DiagnosticoIAId IS NULL;

    ;WITH duplicados AS
    (
        SELECT
            DiagnosticoIAId,
            ROW_NUMBER() OVER
            (
                PARTITION BY DiagnosticoIAId
                ORDER BY
                    ISNULL(FechaFinEtapaAnalizadorUtc, '19000101') DESC
            ) AS Numero
        FROM dbo.diagnosticoIARevisionAnalizadorControl
    )
    DELETE FROM duplicados
    WHERE Numero > 1;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(
            N'dbo.diagnosticoIARevisionAnalizadorControl')
          AND name = N'DiagnosticoIAId'
          AND is_nullable = 1
    )
    AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.diagnosticoIARevisionAnalizadorControl
        WHERE DiagnosticoIAId IS NULL
    )
    BEGIN
        EXEC(N'
ALTER TABLE dbo.diagnosticoIARevisionAnalizadorControl
ALTER COLUMN DiagnosticoIAId INT NOT NULL;');
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes indice
        INNER JOIN sys.index_columns columnaIndice
            ON columnaIndice.object_id = indice.object_id
           AND columnaIndice.index_id = indice.index_id
        INNER JOIN sys.columns columna
            ON columna.object_id = columnaIndice.object_id
           AND columna.column_id = columnaIndice.column_id
        WHERE indice.object_id = OBJECT_ID(
                  N'dbo.diagnosticoIARevisionAnalizadorControl')
          AND indice.is_unique = 1
          AND columna.name = N'DiagnosticoIAId'
          AND columnaIndice.key_ordinal = 1
    )
    BEGIN
        CREATE UNIQUE INDEX UX_diagIARevAnaControl_diagnostico
            ON dbo.diagnosticoIARevisionAnalizadorControl
               (DiagnosticoIAId);
    END;
END;
""";

            await db.Database.ExecuteSqlRawAsync(
                sqlRepararRegistros,
                cancellationToken);
        }

        private async Task InicializarIndicesYRelacionesAsync(
            CancellationToken cancellationToken)
        {
            const string sql = """
UPDATE dbo.motivoDevolucionTecnico
SET Codigo = N'MIGRADO_' + CONVERT(NVARCHAR(20), MotivoDevolucionTecnicoId)
WHERE LEN(LTRIM(RTRIM(ISNULL(Codigo, N'')))) = 0;

;WITH repetidos AS
(
    SELECT
        MotivoDevolucionTecnicoId,
        ROW_NUMBER() OVER
        (
            PARTITION BY UPPER(LTRIM(RTRIM(Codigo)))
            ORDER BY MotivoDevolucionTecnicoId
        ) AS Numero
    FROM dbo.motivoDevolucionTecnico
)
UPDATE m
SET Codigo = LEFT(m.Codigo, 40) + N'_' +
             CONVERT(NVARCHAR(20), m.MotivoDevolucionTecnicoId)
FROM dbo.motivoDevolucionTecnico m
INNER JOIN repetidos r
    ON r.MotivoDevolucionTecnicoId = m.MotivoDevolucionTecnicoId
WHERE r.Numero > 1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_motivoDevolucionTecnico_codigo'
      AND object_id = OBJECT_ID(N'dbo.motivoDevolucionTecnico')
)
BEGIN
    CREATE UNIQUE INDEX UX_motivoDevolucionTecnico_codigo
        ON dbo.motivoDevolucionTecnico(Codigo);
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_motivoDevolucionTecnico_activoOrden'
      AND object_id = OBJECT_ID(N'dbo.motivoDevolucionTecnico')
)
BEGIN
    CREATE INDEX IX_motivoDevolucionTecnico_activoOrden
        ON dbo.motivoDevolucionTecnico(Activo, Orden, Nombre);
END;

UPDATE dbo.diagnosticoIAImagenDevolucionTecnico
SET Estado = N'RESUELTA',
    FechaResolucionUtc = COALESCE(FechaResolucionUtc, SYSUTCDATETIME()),
    RespuestaTecnico = CASE
        WHEN LEN(LTRIM(RTRIM(ISNULL(RespuestaTecnico, N'')))) = 0
            THEN N'Registro de compatibilidad cerrado durante la actualización del esquema.'
        ELSE RespuestaTecnico
    END
WHERE DiagnosticoIAImagenId <= 0
   OR MotivoDevolucionTecnicoId <= 0;

;WITH pendientesDuplicadas AS
(
    SELECT
        DiagnosticoIAImagenDevolucionTecnicoId,
        ROW_NUMBER() OVER
        (
            PARTITION BY DiagnosticoIAImagenId
            ORDER BY FechaDevolucionUtc DESC,
                     DiagnosticoIAImagenDevolucionTecnicoId DESC
        ) AS Numero
    FROM dbo.diagnosticoIAImagenDevolucionTecnico
    WHERE Estado = N'PENDIENTE'
)
UPDATE d
SET Estado = N'RESUELTA',
    FechaResolucionUtc = COALESCE(d.FechaResolucionUtc, SYSUTCDATETIME()),
    RespuestaTecnico = CASE
        WHEN LEN(LTRIM(RTRIM(ISNULL(d.RespuestaTecnico, N'')))) = 0
            THEN N'Devolución anterior cerrada al conservar únicamente la solicitud pendiente más reciente.'
        ELSE d.RespuestaTecnico
    END
FROM dbo.diagnosticoIAImagenDevolucionTecnico d
INNER JOIN pendientesDuplicadas p
    ON p.DiagnosticoIAImagenDevolucionTecnicoId =
       d.DiagnosticoIAImagenDevolucionTecnicoId
WHERE p.Numero > 1;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_diagIAImgDevTec_imagenEstadoFecha'
      AND object_id = OBJECT_ID(N'dbo.diagnosticoIAImagenDevolucionTecnico')
)
BEGIN
    CREATE INDEX IX_diagIAImgDevTec_imagenEstadoFecha
        ON dbo.diagnosticoIAImagenDevolucionTecnico
           (DiagnosticoIAImagenId, Estado, FechaDevolucionUtc DESC);
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_diagIAImgDevTec_unaPendientePorFoto'
      AND object_id = OBJECT_ID(N'dbo.diagnosticoIAImagenDevolucionTecnico')
)
BEGIN
    CREATE UNIQUE INDEX UX_diagIAImgDevTec_unaPendientePorFoto
        ON dbo.diagnosticoIAImagenDevolucionTecnico(DiagnosticoIAImagenId)
        WHERE Estado = N'PENDIENTE';
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_diagIAImagenDevolucionTecnico_imagen'
)
AND NOT EXISTS
(
    SELECT 1
    FROM dbo.diagnosticoIAImagenDevolucionTecnico d
    LEFT JOIN dbo.diagnosticoIAImagen i
        ON i.DiagnosticoIAImagenId = d.DiagnosticoIAImagenId
    WHERE i.DiagnosticoIAImagenId IS NULL
)
BEGIN
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico
        ADD CONSTRAINT FK_diagIAImagenDevolucionTecnico_imagen
        FOREIGN KEY (DiagnosticoIAImagenId)
        REFERENCES dbo.diagnosticoIAImagen(DiagnosticoIAImagenId);
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_diagIAImagenDevolucionTecnico_motivo'
)
AND NOT EXISTS
(
    SELECT 1
    FROM dbo.diagnosticoIAImagenDevolucionTecnico d
    LEFT JOIN dbo.motivoDevolucionTecnico m
        ON m.MotivoDevolucionTecnicoId = d.MotivoDevolucionTecnicoId
    WHERE m.MotivoDevolucionTecnicoId IS NULL
)
BEGIN
    ALTER TABLE dbo.diagnosticoIAImagenDevolucionTecnico
        ADD CONSTRAINT FK_diagIAImagenDevolucionTecnico_motivo
        FOREIGN KEY (MotivoDevolucionTecnicoId)
        REFERENCES dbo.motivoDevolucionTecnico(MotivoDevolucionTecnicoId);
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_diagIARevAnaControl_diagnostico'
)
AND NOT EXISTS
(
    SELECT 1
    FROM dbo.diagnosticoIARevisionAnalizadorControl c
    LEFT JOIN dbo.diagnosticoIA d
        ON d.DiagnosticoIAId = c.DiagnosticoIAId
    WHERE d.DiagnosticoIAId IS NULL
)
BEGIN
    ALTER TABLE dbo.diagnosticoIARevisionAnalizadorControl
        ADD CONSTRAINT FK_diagIARevAnaControl_diagnostico
        FOREIGN KEY (DiagnosticoIAId)
        REFERENCES dbo.diagnosticoIA(DiagnosticoIAId);
END;
""";

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        private async Task SembrarMotivosPredeterminadosAsync(
            CancellationToken cancellationToken)
        {
            const string sql = """
DECLARE @semilla TABLE
(
    Codigo NVARCHAR(60) NOT NULL,
    Nombre NVARCHAR(140) NOT NULL,
    Descripcion NVARCHAR(700) NOT NULL,
    InstruccionSugerida NVARCHAR(2000) NOT NULL,
    RequiereNuevaFotografia BIT NOT NULL,
    PermiteCorregirMetadatos BIT NOT NULL,
    Orden INT NOT NULL
);

INSERT INTO @semilla
(
    Codigo, Nombre, Descripcion, InstruccionSugerida,
    RequiereNuevaFotografia, PermiteCorregirMetadatos, Orden
)
VALUES
(N'TIPO_FOTOGRAFIA_INCORRECTO', N'Tipo de fotografía incorrecto', N'La parte o estructura seleccionada no corresponde con la evidencia visible.', N'Corrija el tipo de fotografía y ejecute nuevamente el análisis con IA.', 0, 1, 1),
(N'IMAGEN_BORROSA', N'Imagen borrosa', N'La falta de nitidez impide observar síntomas o estructuras con seguridad.', N'Tome una nueva fotografía nítida, estabilice el dispositivo y asegure buen enfoque.', 1, 0, 2),
(N'IMAGEN_DEMASIADO_LEJANA', N'Imagen demasiado lejana', N'La evidencia no tiene el acercamiento necesario para confirmar el hallazgo.', N'Tome una nueva fotografía más cercana del síntoma o estructura señalada.', 1, 0, 3),
(N'PARTE_PLANTA_INCORRECTA', N'Parte de la planta incorrecta', N'La imagen no muestra la parte requerida para validar el diagnóstico.', N'Tome una nueva fotografía de la parte de la planta solicitada por el analizador.', 1, 0, 4),
(N'EVIDENCIA_INSUFICIENTE', N'Evidencia insuficiente', N'La fotografía no aporta suficientes elementos visibles para emitir una clasificación humana.', N'Agregue una nueva evidencia con mejor detalle y contexto del síntoma observado.', 1, 0, 5),
(N'POSIBLE_FOTOGRAFIA_DUPLICADA', N'Posible fotografía duplicada', N'La evidencia parece repetir otra fotografía de la misma inspección.', N'Verifique la duplicidad. Si está repetida, descarte esta evidencia; de lo contrario explique la diferencia.', 0, 1, 6),
(N'FECHA_O_DATOS_CAMPO_INCORRECTOS', N'Fecha o datos de campo incorrectos', N'La fecha de identificación o los metadatos no coinciden con la evidencia reportada.', N'Corrija la fecha de identificación y los datos disponibles antes de reenviar la fotografía.', 0, 1, 7),
(N'NUEVO_ANGULO_O_ACERCAMIENTO', N'Necesita otro ángulo o acercamiento', N'La evidencia requiere una vista complementaria para confirmar el diagnóstico.', N'Tome una nueva fotografía desde el ángulo o acercamiento indicado en las instrucciones.', 1, 0, 8),
(N'POSIBLE_PLANTA_NO_CAFE', N'La planta posiblemente no es café', N'La morfología visible no permite confirmar que la planta fotografiada sea café.', N'Verifique la planta en campo y agregue una fotografía completa junto con un acercamiento de hojas o frutos.', 1, 0, 9),
(N'OTRA_CORRECCION_TECNICA', N'Otra corrección técnica', N'Motivo adicional que requiere intervención del técnico.', N'Atienda las instrucciones específicas registradas por el analizador.', 0, 1, 10);

INSERT INTO dbo.motivoDevolucionTecnico
(
    Codigo, Nombre, Descripcion, InstruccionSugerida,
    RequiereNuevaFotografia, PermiteCorregirMetadatos,
    Orden, Activo, FechaCreacionUtc, FechaModificacionUtc
)
SELECT
    s.Codigo, s.Nombre, s.Descripcion, s.InstruccionSugerida,
    s.RequiereNuevaFotografia, s.PermiteCorregirMetadatos,
    s.Orden, 1, SYSUTCDATETIME(), SYSUTCDATETIME()
FROM @semilla s
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.motivoDevolucionTecnico m
    WHERE UPPER(LTRIM(RTRIM(m.Codigo))) =
          UPPER(LTRIM(RTRIM(s.Codigo)))
);
""";

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        public async Task<List<MotivoDevolucionTecnicoRespuesta>>
            ListarMotivosAsync(
                bool incluirInactivos,
                string? buscar,
                CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
SELECT
    [MotivoDevolucionTecnicoId], [Codigo], [Nombre], [Descripcion],
    [InstruccionSugerida], [RequiereNuevaFotografia],
    [PermiteCorregirMetadatos], [Orden], [Activo],
    [FechaCreacionUtc], [FechaModificacionUtc]
FROM [dbo].[motivoDevolucionTecnico]
WHERE (@incluirInactivos = 1 OR [Activo] = 1)
  AND
  (
      @buscar = N'' OR
      [Codigo] LIKE N'%' + @buscar + N'%' OR
      [Nombre] LIKE N'%' + @buscar + N'%' OR
      [Descripcion] LIKE N'%' + @buscar + N'%' OR
      [InstruccionSugerida] LIKE N'%' + @buscar + N'%'
  )
ORDER BY [Orden], [Nombre];
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@incluirInactivos", incluirInactivos);
                AgregarParametro(comando, "@buscar", Normalizar(buscar));

                var items = new List<MotivoDevolucionTecnicoRespuesta>();
                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                    items.Add(LeerMotivo(reader));

                return items;
            }, cancellationToken);
        }

        public async Task<MotivoDevolucionTecnicoRespuesta?> ObtenerMotivoAsync(
            int id,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
SELECT
    [MotivoDevolucionTecnicoId], [Codigo], [Nombre], [Descripcion],
    [InstruccionSugerida], [RequiereNuevaFotografia],
    [PermiteCorregirMetadatos], [Orden], [Activo],
    [FechaCreacionUtc], [FechaModificacionUtc]
FROM [dbo].[motivoDevolucionTecnico]
WHERE [MotivoDevolucionTecnicoId] = @id;
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@id", id);
                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                return await reader.ReadAsync(cancellationToken)
                    ? LeerMotivo(reader)
                    : null;
            }, cancellationToken);
        }

        public async Task<int> CrearMotivoAsync(
            MotivoDevolucionTecnicoGuardarRequest request,
            int usuarioId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
INSERT INTO [dbo].[motivoDevolucionTecnico]
(
    [Codigo], [Nombre], [Descripcion], [InstruccionSugerida],
    [RequiereNuevaFotografia], [PermiteCorregirMetadatos],
    [Orden], [Activo], [FechaCreacionUtc], [UsuarioCreacionId],
    [FechaModificacionUtc], [UsuarioModificacionId]
)
VALUES
(
    @codigo, @nombre, @descripcion, @instruccion,
    @requiereNueva, @permiteMetadatos,
    @orden, 1, SYSUTCDATETIME(), @usuarioId,
    SYSUTCDATETIME(), @usuarioId
);
SELECT CAST(SCOPE_IDENTITY() AS INT);
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarDatosMotivo(comando, request, usuarioId);
                object? valor = await comando.ExecuteScalarAsync(cancellationToken);
                return Convert.ToInt32(valor);
            }, cancellationToken);
        }

        public async Task ActualizarMotivoAsync(
            int id,
            MotivoDevolucionTecnicoGuardarRequest request,
            int usuarioId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
UPDATE [dbo].[motivoDevolucionTecnico]
SET [Nombre] = @nombre,
    [Descripcion] = @descripcion,
    [InstruccionSugerida] = @instruccion,
    [RequiereNuevaFotografia] = @requiereNueva,
    [PermiteCorregirMetadatos] = @permiteMetadatos,
    [Orden] = @orden,
    [FechaModificacionUtc] = SYSUTCDATETIME(),
    [UsuarioModificacionId] = @usuarioId
WHERE [MotivoDevolucionTecnicoId] = @id
  AND [Codigo] = @codigo;
""";

            await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@id", id);
                AgregarDatosMotivo(comando, request, usuarioId);
                await comando.ExecuteNonQueryAsync(cancellationToken);
                return true;
            }, cancellationToken);
        }

        public async Task CambiarEstadoMotivoAsync(
            int id,
            bool activo,
            int usuarioId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
UPDATE [dbo].[motivoDevolucionTecnico]
SET [Activo] = @activo,
    [FechaModificacionUtc] = SYSUTCDATETIME(),
    [UsuarioModificacionId] = @usuarioId
WHERE [MotivoDevolucionTecnicoId] = @id;
""";

            await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@id", id);
                AgregarParametro(comando, "@activo", activo);
                AgregarParametro(comando, "@usuarioId", usuarioId);
                await comando.ExecuteNonQueryAsync(cancellationToken);
                return true;
            }, cancellationToken);
        }

        public async Task<ContextoRevisionAnalizadorDto> ObtenerContextoAsync(
            int inspeccionId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            try
            {
                return await ConstruirContextoAsync(
                    inspeccionId,
                    cancellationToken);
            }
            catch (DbException)
            {
                /*
                 * Una publicación interrumpida puede haber dejado una tabla
                 * auxiliar incompleta. Se repara y se realiza una sola segunda
                 * lectura antes de propagar el error real al controlador.
                 */
                await AsegurarEsquemaContextoAsync(cancellationToken);

                return await ConstruirContextoAsync(
                    inspeccionId,
                    cancellationToken);
            }
        }

        private async Task<ContextoRevisionAnalizadorDto>
            ConstruirContextoAsync(
                int inspeccionId,
                CancellationToken cancellationToken)
        {
            ResumenRevisionAnalizadorDto resumen =
                await ObtenerResumenAsync(inspeccionId, cancellationToken);

            List<DevolucionTecnicoFotografiaDto> devoluciones =
                await ObtenerUltimasDevolucionesAsync(
                    inspeccionId,
                    cancellationToken);

            return new ContextoRevisionAnalizadorDto
            {
                Resumen = resumen,
                Devoluciones = devoluciones
            };
        }

        public async Task<ResumenRevisionAnalizadorDto> ObtenerResumenAsync(
            int inspeccionId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            const string sql = """
WITH fotos AS
(
    SELECT
        i.DiagnosticoIAImagenId,
        UPPER(ISNULL(i.Estado, N'BORRADOR')) AS Estado,
        CONVERT(BIT, CASE
            WHEN ISNULL(i.Descartada, 0) = 1
              OR UPPER(ISNULL(i.Estado, N'BORRADOR')) = N'DESCARTADA'
                THEN 1
            ELSE 0
        END) AS Descartada,
        CASE
            WHEN i.FechaAnalisisHumanoUtc IS NOT NULL
             AND
             (
                 i.FechaAnalisisIAUtc IS NULL OR
                 i.FechaAnalisisHumanoUtc >= i.FechaAnalisisIAUtc
             )
                THEN 1
            ELSE 0
        END AS TieneAnalisisHumano
    FROM dbo.diagnosticoIAImagen i
    WHERE i.DiagnosticoIAId = @id
      AND ISNULL(i.Activo, 1) = 1
)
SELECT
    @id,
    COUNT(f.DiagnosticoIAImagenId),
    SUM(CASE
        WHEN f.DiagnosticoIAImagenId IS NOT NULL
         AND f.Descartada = 0
        THEN 1 ELSE 0 END),
    SUM(CASE
        WHEN f.Descartada = 1 OR f.Estado = N'DESCARTADA'
        THEN 1 ELSE 0 END),
    SUM(CASE
        WHEN f.Descartada = 0 AND f.Estado IN
        (
            N'PENDIENTE_ANALIZADOR', N'EN_ANALISIS_HUMANO',
            N'DEVUELTA_AL_ANALIZADOR', N'PENDIENTE_APROBACION',
            N'APROBADA', N'APROBADA_CON_CORRECCION', N'RECHAZADA',
            N'NO_CONCLUYENTE', N'PUBLICADA_ALBUM'
        ) THEN 1 ELSE 0 END),
    SUM(CASE
        WHEN f.Descartada = 0 AND f.Estado IN
        (
            N'BORRADOR', N'PENDIENTE_IA', N'ANALIZANDO_IA', N'ERROR_IA',
            N'PENDIENTE_DECISION_TECNICO', N'DEVUELTA_AL_TECNICO'
        ) THEN 1 ELSE 0 END),
    SUM(CASE
        WHEN f.Descartada = 0
         AND f.Estado = N'DEVUELTA_AL_TECNICO'
        THEN 1 ELSE 0 END),
    SUM(CASE
        WHEN f.Descartada = 0 AND f.Estado = N'ERROR_IA'
        THEN 1 ELSE 0 END),
    SUM(CASE
        WHEN f.Descartada = 0 AND f.Estado = N'ANALIZANDO_IA'
        THEN 1 ELSE 0 END),
    SUM(CASE
        WHEN f.Descartada = 0
         AND f.Estado = N'PENDIENTE_DECISION_TECNICO'
        THEN 1 ELSE 0 END),
    SUM(CASE
        WHEN f.Descartada = 0 AND f.TieneAnalisisHumano = 1
        THEN 1 ELSE 0 END),
    SUM(CASE
        WHEN f.Descartada = 0
         AND f.Estado IN
         (
             N'PENDIENTE_ANALIZADOR', N'EN_ANALISIS_HUMANO',
             N'DEVUELTA_AL_ANALIZADOR'
         )
         AND f.TieneAnalisisHumano = 0
        THEN 1 ELSE 0 END),
    CONVERT(BIT, ISNULL(d.EtapaTecnicaFinalizada, 0)),
    CONVERT(BIT, ISNULL(c.EtapaAnalizadorFinalizada, 0)),
    c.FechaFinEtapaAnalizadorUtc
FROM dbo.diagnosticoIA d
LEFT JOIN dbo.diagnosticoIARevisionAnalizadorControl c
    ON c.DiagnosticoIAId = d.DiagnosticoIAId
LEFT JOIN fotos f ON 1 = 1
WHERE d.DiagnosticoIAId = @id
GROUP BY d.EtapaTecnicaFinalizada,
         c.EtapaAnalizadorFinalizada,
         c.FechaFinEtapaAnalizadorUtc;
""";

            ResumenRevisionAnalizadorDto resumen =
                await EjecutarAsync(async conexion =>
                {
                    await using DbCommand comando = CrearComando(conexion, sql);
                    AgregarParametro(comando, "@id", inspeccionId);
                    await using DbDataReader reader =
                        await comando.ExecuteReaderAsync(cancellationToken);

                    if (!await reader.ReadAsync(cancellationToken))
                    {
                        return new ResumenRevisionAnalizadorDto
                        {
                            InspeccionId = inspeccionId,
                            MotivoNoPuedeFinalizarRevision =
                                "No se encontró la inspección indicada."
                        };
                    }

                    return new ResumenRevisionAnalizadorDto
                    {
                        InspeccionId = reader.GetInt32(0),
                        TotalRegistradas = Entero(reader, 1),
                        TotalEvaluables = Entero(reader, 2),
                        TotalDescartadasTecnico = Entero(reader, 3),
                        TotalRecibidasAnalizador = Entero(reader, 4),
                        TotalPendientesTecnico = Entero(reader, 5),
                        TotalDevueltasTecnico = Entero(reader, 6),
                        TotalErroresIA = Entero(reader, 7),
                        TotalProcesandoIA = Entero(reader, 8),
                        TotalPendienteDecisionTecnico = Entero(reader, 9),
                        TotalClasificadasHumano = Entero(reader, 10),
                        TotalPendientesClasificacionHumana = Entero(reader, 11),
                        EtapaTecnicaFinalizada = reader.GetBoolean(12),
                        EtapaAnalizadorFinalizada = reader.GetBoolean(13),
                        FechaFinEtapaAnalizadorUtc = reader.IsDBNull(14)
                            ? null
                            : DateTime.SpecifyKind(
                                reader.GetDateTime(14),
                                DateTimeKind.Utc)
                    };
                }, cancellationToken);

            AplicarEstadoFinalizacion(resumen);
            return resumen;
        }

        public async Task<DevolucionTecnicoFotografiaDto> DevolverAlTecnicoAsync(
            int inspeccionId,
            int fotografiaId,
            MotivoDevolucionTecnicoRespuesta motivo,
            string instrucciones,
            int usuarioAnalizadorId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            DbConnection conexion = db.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;
            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            await using DbTransaction transaccion =
                await conexion.BeginTransactionAsync(cancellationToken);

            try
            {
                const string sqlValidar = """
SELECT COUNT(1)
FROM dbo.diagnosticoIAImagen WITH (UPDLOCK, HOLDLOCK)
WHERE DiagnosticoIAId = @inspeccionId
  AND DiagnosticoIAImagenId = @fotoId
  AND ISNULL(Activo, 1) = 1
  AND ISNULL(Descartada, 0) = 0
  AND UPPER(ISNULL(Estado, N'BORRADOR')) IN
  (
      N'PENDIENTE_ANALIZADOR',
      N'EN_ANALISIS_HUMANO',
      N'DEVUELTA_AL_ANALIZADOR'
  );
""";

                await using (DbCommand validar = CrearComando(
                    conexion,
                    sqlValidar,
                    transaccion))
                {
                    AgregarParametro(validar, "@inspeccionId", inspeccionId);
                    AgregarParametro(validar, "@fotoId", fotografiaId);
                    int cantidad = Convert.ToInt32(
                        await validar.ExecuteScalarAsync(cancellationToken));

                    if (cantidad != 1)
                    {
                        throw new InvalidOperationException(
                            "La fotografía no está disponible para devolución al técnico.");
                    }
                }

                const string sql = """
DECLARE @ahora DATETIME2(0) = SYSUTCDATETIME();
DECLARE @devolucionId INT;
DECLARE @estadoAnterior NVARCHAR(40) =
(
    SELECT UPPER(ISNULL(Estado, N'BORRADOR'))
    FROM dbo.diagnosticoIAImagen
    WHERE DiagnosticoIAImagenId = @fotoId
);

INSERT INTO dbo.diagnosticoIAImagenDevolucionTecnico
(
    DiagnosticoIAImagenId, MotivoDevolucionTecnicoId,
    MotivoCodigo, MotivoNombre, MotivoDescripcion,
    InstruccionSugerida, InstruccionesAnalizador,
    RequiereNuevaFotografia, PermiteCorregirMetadatos,
    UsuarioAnalizadorId, FechaDevolucionUtc, Estado
)
VALUES
(
    @fotoId, @motivoId,
    @motivoCodigo, @motivoNombre, @motivoDescripcion,
    @instruccionSugerida, @instrucciones,
    @requiereNueva, @permiteMetadatos,
    @usuarioId, @ahora, N'PENDIENTE'
);

SET @devolucionId = CAST(SCOPE_IDENTITY() AS INT);

UPDATE dbo.diagnosticoIAImagen
SET Estado = N'DEVUELTA_AL_TECNICO'
WHERE DiagnosticoIAImagenId = @fotoId;

INSERT INTO dbo.diagnosticoIAImagenHistorialV2
(
    DiagnosticoIAImagenId, UsuarioId, EstadoAnterior,
    EstadoNuevo, Accion, Detalle, FechaUtc
)
VALUES
(
    @fotoId, @usuarioId, @estadoAnterior,
    N'DEVUELTA_AL_TECNICO', N'ANALIZADOR_DEVUELVE_TECNICO',
    @detalleHistorial, @ahora
);

UPDATE dbo.diagnosticoIA
SET EtapaTecnicaFinalizada = 0,
    FechaFinEtapaTecnicaUtc = NULL,
    UsuarioFinEtapaTecnicaId = NULL,
    Estado = N'EN_PROCESO'
WHERE DiagnosticoIAId = @inspeccionId;

MERGE dbo.diagnosticoIARevisionAnalizadorControl AS destino
USING (SELECT @inspeccionId AS DiagnosticoIAId) AS origen
ON destino.DiagnosticoIAId = origen.DiagnosticoIAId
WHEN MATCHED THEN
    UPDATE SET EtapaAnalizadorFinalizada = 0,
               FechaFinEtapaAnalizadorUtc = NULL,
               UsuarioFinEtapaAnalizadorId = NULL
WHEN NOT MATCHED THEN
    INSERT
    (
        DiagnosticoIAId, EtapaAnalizadorFinalizada
    )
    VALUES
    (
        @inspeccionId, 0
    );

SELECT @devolucionId;
""";

                int devolucionId;
                await using (DbCommand comando = CrearComando(
                    conexion,
                    sql,
                    transaccion))
                {
                    AgregarParametro(comando, "@inspeccionId", inspeccionId);
                    AgregarParametro(comando, "@fotoId", fotografiaId);
                    AgregarParametro(
                        comando,
                        "@motivoId",
                        motivo.MotivoDevolucionTecnicoId);
                    AgregarParametro(comando, "@motivoCodigo", motivo.Codigo);
                    AgregarParametro(comando, "@motivoNombre", motivo.Nombre);
                    AgregarParametro(
                        comando,
                        "@motivoDescripcion",
                        motivo.Descripcion);
                    AgregarParametro(
                        comando,
                        "@instruccionSugerida",
                        motivo.InstruccionSugerida);
                    AgregarParametro(
                        comando,
                        "@instrucciones",
                        Limitar(instrucciones, 3000));
                    AgregarParametro(
                        comando,
                        "@requiereNueva",
                        motivo.RequiereNuevaFotografia);
                    AgregarParametro(
                        comando,
                        "@permiteMetadatos",
                        motivo.PermiteCorregirMetadatos);
                    AgregarParametro(comando, "@usuarioId", usuarioAnalizadorId);
                    AgregarParametro(
                        comando,
                        "@detalleHistorial",
                        Limitar(
                            $"Motivo: {motivo.Nombre}. {instrucciones}",
                            2000));

                    object? valor =
                        await comando.ExecuteScalarAsync(cancellationToken);
                    devolucionId = Convert.ToInt32(valor);
                }

                await transaccion.CommitAsync(cancellationToken);

                return new DevolucionTecnicoFotografiaDto
                {
                    DevolucionTecnicoId = devolucionId,
                    FotografiaId = fotografiaId,
                    MotivoDevolucionTecnicoId =
                        motivo.MotivoDevolucionTecnicoId,
                    MotivoCodigo = motivo.Codigo,
                    MotivoNombre = motivo.Nombre,
                    MotivoDescripcion = motivo.Descripcion,
                    InstruccionSugerida = motivo.InstruccionSugerida,
                    InstruccionesAnalizador = instrucciones,
                    RequiereNuevaFotografia =
                        motivo.RequiereNuevaFotografia,
                    PermiteCorregirMetadatos =
                        motivo.PermiteCorregirMetadatos,
                    UsuarioAnalizadorId = usuarioAnalizadorId,
                    FechaDevolucionUtc = DateTime.UtcNow,
                    Estado = "PENDIENTE"
                };
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            finally
            {
                if (cerrar)
                    await conexion.CloseAsync();
            }
        }

        public async Task ResolverDevolucionAsync(
            int inspeccionId,
            ResolverDevolucionTecnicoRequest request,
            int usuarioTecnicoId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            DevolucionTecnicoFotografiaDto? devolucion =
                (await ObtenerUltimasDevolucionesAsync(
                    inspeccionId,
                    cancellationToken))
                .FirstOrDefault(item =>
                    item.FotografiaId == request.FotografiaId &&
                    string.Equals(
                        item.Estado,
                        "PENDIENTE",
                        StringComparison.OrdinalIgnoreCase));

            if (devolucion == null)
            {
                throw new InvalidOperationException(
                    "La fotografía no tiene una devolución pendiente.");
            }

            if (!devolucion.PermiteCorregirMetadatos)
            {
                throw new InvalidOperationException(
                    "Este motivo requiere una nueva fotografía. Agregue la nueva evidencia y descarte la fotografía devuelta indicando que fue sustituida.");
            }

            DbConnection conexion = db.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;
            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            await using DbTransaction transaccion =
                await conexion.BeginTransactionAsync(cancellationToken);

            try
            {
                const string sqlResolverDevolucion = """
UPDATE dbo.diagnosticoIAImagenDevolucionTecnico
SET Estado = N'RESUELTA',
    RespuestaTecnico = @respuesta,
    UsuarioTecnicoId = @usuarioId,
    FechaResolucionUtc = SYSUTCDATETIME()
WHERE DiagnosticoIAImagenDevolucionTecnicoId = @devolucionId
  AND DiagnosticoIAImagenId = @fotoId
  AND Estado = N'PENDIENTE';
""";

                await using (DbCommand comando = CrearComando(
                    conexion,
                    sqlResolverDevolucion,
                    transaccion))
                {
                    AgregarParametro(
                        comando,
                        "@devolucionId",
                        devolucion.DevolucionTecnicoId);
                    AgregarParametro(comando, "@fotoId", request.FotografiaId);
                    AgregarParametro(comando, "@usuarioId", usuarioTecnicoId);
                    AgregarParametro(
                        comando,
                        "@respuesta",
                        Limitar(request.RespuestaTecnico, 2000));

                    int actualizadas =
                        await comando.ExecuteNonQueryAsync(cancellationToken);

                    if (actualizadas != 1)
                    {
                        throw new InvalidOperationException(
                            "La devolución ya fue atendida o cambió mientras se procesaba la corrección.");
                    }
                }

                const string sqlActualizarFotografia = """
UPDATE dbo.diagnosticoIAImagen
SET TipoFotografia = @tipoFotografia,
    FechaIdentificacionCampo = @fechaCampo,
    Estado = N'PENDIENTE_IA',
    ErrorProcesamiento = N''
WHERE DiagnosticoIAId = @inspeccionId
  AND DiagnosticoIAImagenId = @fotoId
  AND ISNULL(Activo, 1) = 1
  AND ISNULL(Descartada, 0) = 0
  AND UPPER(ISNULL(Estado, N'')) = N'DEVUELTA_AL_TECNICO';
""";

                await using (DbCommand comando = CrearComando(
                    conexion,
                    sqlActualizarFotografia,
                    transaccion))
                {
                    AgregarParametro(comando, "@inspeccionId", inspeccionId);
                    AgregarParametro(comando, "@fotoId", request.FotografiaId);
                    AgregarParametro(
                        comando,
                        "@tipoFotografia",
                        NormalizarCodigo(request.TipoFotografia));
                    AgregarParametro(
                        comando,
                        "@fechaCampo",
                        request.FechaIdentificacionCampo.Date);

                    int actualizadas =
                        await comando.ExecuteNonQueryAsync(cancellationToken);

                    if (actualizadas != 1)
                    {
                        throw new InvalidOperationException(
                            "La fotografía ya no está disponible para corregir la devolución.");
                    }
                }

                const string sqlHistorial = """
DECLARE @ahora DATETIME2(0) = SYSUTCDATETIME();

INSERT INTO dbo.diagnosticoIAImagenHistorialV2
(
    DiagnosticoIAImagenId, UsuarioId, EstadoAnterior,
    EstadoNuevo, Accion, Detalle, FechaUtc
)
VALUES
(
    @fotoId, @usuarioId, N'DEVUELTA_AL_TECNICO',
    N'PENDIENTE_IA', N'TECNICO_RESUELVE_DEVOLUCION',
    @detalle, @ahora
);

UPDATE dbo.diagnosticoIA
SET Estado = N'EN_PROCESO'
WHERE DiagnosticoIAId = @inspeccionId;
""";

                await using (DbCommand comando = CrearComando(
                    conexion,
                    sqlHistorial,
                    transaccion))
                {
                    AgregarParametro(comando, "@inspeccionId", inspeccionId);
                    AgregarParametro(comando, "@fotoId", request.FotografiaId);
                    AgregarParametro(comando, "@usuarioId", usuarioTecnicoId);
                    AgregarParametro(
                        comando,
                        "@detalle",
                        Limitar(
                            $"El técnico corrigió la evidencia. {request.RespuestaTecnico}",
                            2000));
                    await comando.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaccion.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            finally
            {
                if (cerrar)
                    await conexion.CloseAsync();
            }
        }

        /// <summary>
        /// Cierra las devoluciones pendientes cuando el técnico sustituye la
        /// evidencia y utiliza el descarte normal del flujo. La fotografía y el
        /// motivo de descarte permanecen disponibles en la auditoría.
        /// </summary>
        public async Task ResolverDevolucionesPorDescarteAsync(
            int inspeccionId,
            IEnumerable<int> fotografiaIds,
            int usuarioTecnicoId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            int[] ids = (fotografiaIds ?? [])
                .Where(item => item > 0)
                .Distinct()
                .ToArray();

            if (ids.Length == 0)
                return;

            string parametros = string.Join(
                ",",
                ids.Select((_, indice) => $"@foto{indice}"));

            string sql = $"""
UPDATE d
SET d.Estado = N'RESUELTA',
    d.RespuestaTecnico =
        N'La evidencia fue descartada por el técnico después de atender la devolución.',
    d.UsuarioTecnicoId = @usuarioId,
    d.FechaResolucionUtc = SYSUTCDATETIME()
FROM dbo.diagnosticoIAImagenDevolucionTecnico d
INNER JOIN dbo.diagnosticoIAImagen i
    ON i.DiagnosticoIAImagenId = d.DiagnosticoIAImagenId
WHERE i.DiagnosticoIAId = @inspeccionId
  AND d.Estado = N'PENDIENTE'
  AND UPPER(ISNULL(i.Estado, N'')) = N'DESCARTADA'
  AND d.DiagnosticoIAImagenId IN ({parametros});
""";

            await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@inspeccionId", inspeccionId);
                AgregarParametro(comando, "@usuarioId", usuarioTecnicoId);
                for (int indice = 0; indice < ids.Length; indice++)
                    AgregarParametro(comando, $"@foto{indice}", ids[indice]);

                await comando.ExecuteNonQueryAsync(cancellationToken);
                return true;
            }, cancellationToken);
        }

        public async Task<(bool Exitoso, string Mensaje)> FinalizarAnalizadorAsync(
            int inspeccionId,
            int usuarioAnalizadorId,
            CancellationToken cancellationToken = default)
        {
            await InicializarAsync(cancellationToken);

            ResumenRevisionAnalizadorDto resumen =
                await ObtenerResumenAsync(inspeccionId, cancellationToken);

            if (!resumen.PuedeFinalizarRevision)
                return (false, resumen.MotivoNoPuedeFinalizarRevision);

            DbConnection conexion = db.Database.GetDbConnection();
            bool cerrar = conexion.State != ConnectionState.Open;
            if (cerrar)
                await conexion.OpenAsync(cancellationToken);

            await using DbTransaction transaccion =
                await conexion.BeginTransactionAsync(cancellationToken);

            try
            {
                /*
                 * La validación se repite dentro de la transacción para evitar
                 * que una devolución, un nuevo análisis o una corrección cambie
                 * el expediente entre la lectura del resumen y el cierre global.
                 */
                const string sqlValidarCierre = """
SELECT CASE WHEN
    EXISTS
    (
        SELECT 1
        FROM dbo.diagnosticoIA d WITH (UPDLOCK, HOLDLOCK)
        WHERE d.DiagnosticoIAId = @inspeccionId
          AND ISNULL(d.EtapaTecnicaFinalizada, 0) = 1
          AND ISNULL(d.CerradaDefinitiva, 0) = 0
          AND ISNULL(d.Activo, 1) = 1
    )
    AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.diagnosticoIARevisionAnalizadorControl c
             WITH (UPDLOCK, HOLDLOCK)
        WHERE c.DiagnosticoIAId = @inspeccionId
          AND ISNULL(c.EtapaAnalizadorFinalizada, 0) = 1
    )
    AND EXISTS
    (
        SELECT 1
        FROM dbo.diagnosticoIAImagen i WITH (UPDLOCK, HOLDLOCK)
        WHERE i.DiagnosticoIAId = @inspeccionId
          AND ISNULL(i.Activo, 1) = 1
          AND ISNULL(i.Descartada, 0) = 0
          AND UPPER(ISNULL(i.Estado, N'BORRADOR')) <> N'DESCARTADA'
    )
    AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.diagnosticoIAImagen i WITH (UPDLOCK, HOLDLOCK)
        WHERE i.DiagnosticoIAId = @inspeccionId
          AND ISNULL(i.Activo, 1) = 1
          AND ISNULL(i.Descartada, 0) = 0
          AND UPPER(ISNULL(i.Estado, N'BORRADOR')) <> N'DESCARTADA'
          AND UPPER(ISNULL(i.Estado, N'BORRADOR')) NOT IN
          (
              N'PENDIENTE_ANALIZADOR',
              N'EN_ANALISIS_HUMANO',
              N'DEVUELTA_AL_ANALIZADOR',
              N'PENDIENTE_APROBACION',
              N'APROBADA',
              N'APROBADA_CON_CORRECCION',
              N'RECHAZADA',
              N'NO_CONCLUYENTE',
              N'PUBLICADA_ALBUM'
          )
    )
    AND NOT EXISTS
    (
        SELECT 1
        FROM dbo.diagnosticoIAImagen i WITH (UPDLOCK, HOLDLOCK)
        WHERE i.DiagnosticoIAId = @inspeccionId
          AND ISNULL(i.Activo, 1) = 1
          AND ISNULL(i.Descartada, 0) = 0
          AND UPPER(ISNULL(i.Estado, N'BORRADOR')) <> N'DESCARTADA'
          AND
          (
              i.FechaAnalisisHumanoUtc IS NULL
              OR
              (
                  i.FechaAnalisisIAUtc IS NOT NULL
                  AND i.FechaAnalisisHumanoUtc < i.FechaAnalisisIAUtc
              )
          )
    )
THEN 1 ELSE 0 END;
""";

                await using (DbCommand validar = CrearComando(
                    conexion,
                    sqlValidarCierre,
                    transaccion))
                {
                    AgregarParametro(validar, "@inspeccionId", inspeccionId);
                    int listo = Convert.ToInt32(
                        await validar.ExecuteScalarAsync(cancellationToken));

                    if (listo != 1)
                    {
                        throw new InvalidOperationException(
                            "La inspección cambió y ya no cumple todas las condiciones para finalizar la revisión humana. Actualice la pantalla e inténtelo nuevamente.");
                    }
                }

                const string sql = """
DECLARE @ahora DATETIME2(0) = SYSUTCDATETIME();

WITH ultimos AS
(
    SELECT
        h.DiagnosticoIAImagenAnalisisHumanoId,
        ROW_NUMBER() OVER
        (
            PARTITION BY h.DiagnosticoIAImagenId
            ORDER BY h.Version DESC,
                     h.DiagnosticoIAImagenAnalisisHumanoId DESC
        ) AS rn
    FROM dbo.diagnosticoIAImagenAnalisisHumano h
    INNER JOIN dbo.diagnosticoIAImagen i
        ON i.DiagnosticoIAImagenId = h.DiagnosticoIAImagenId
    WHERE i.DiagnosticoIAId = @inspeccionId
      AND ISNULL(i.Activo, 1) = 1
      AND ISNULL(i.Descartada, 0) = 0
)
UPDATE h
SET h.EstadoRegistro = N'ENVIADO',
    h.FechaActualizacionUtc = @ahora,
    h.FechaEnvioUtc = COALESCE(h.FechaEnvioUtc, @ahora)
FROM dbo.diagnosticoIAImagenAnalisisHumano h
INNER JOIN ultimos u
    ON u.DiagnosticoIAImagenAnalisisHumanoId =
       h.DiagnosticoIAImagenAnalisisHumanoId
WHERE u.rn = 1;

INSERT INTO dbo.diagnosticoIAImagenHistorialV2
(
    DiagnosticoIAImagenId, UsuarioId, EstadoAnterior,
    EstadoNuevo, Accion, Detalle, FechaUtc
)
SELECT
    i.DiagnosticoIAImagenId, @usuarioId,
    UPPER(ISNULL(i.Estado, N'BORRADOR')),
    N'PENDIENTE_APROBACION', N'ANALISIS_HUMANO_ENVIADO',
    N'El analizador finalizó la revisión completa y envió la evidencia al aprobador.',
    @ahora
FROM dbo.diagnosticoIAImagen i
WHERE i.DiagnosticoIAId = @inspeccionId
  AND ISNULL(i.Activo, 1) = 1
  AND ISNULL(i.Descartada, 0) = 0
  AND UPPER(ISNULL(i.Estado, N'BORRADOR')) IN
  (
      N'PENDIENTE_ANALIZADOR',
      N'EN_ANALISIS_HUMANO',
      N'DEVUELTA_AL_ANALIZADOR'
  );

UPDATE dbo.diagnosticoIAImagen
SET Estado = N'PENDIENTE_APROBACION',
    FechaAnalisisHumanoUtc = COALESCE(FechaAnalisisHumanoUtc, @ahora)
WHERE DiagnosticoIAId = @inspeccionId
  AND ISNULL(Activo, 1) = 1
  AND ISNULL(Descartada, 0) = 0
  AND UPPER(ISNULL(Estado, N'BORRADOR')) IN
  (
      N'PENDIENTE_ANALIZADOR',
      N'EN_ANALISIS_HUMANO',
      N'DEVUELTA_AL_ANALIZADOR'
  );

MERGE dbo.diagnosticoIARevisionAnalizadorControl AS destino
USING (SELECT @inspeccionId AS DiagnosticoIAId) AS origen
ON destino.DiagnosticoIAId = origen.DiagnosticoIAId
WHEN MATCHED THEN
    UPDATE SET EtapaAnalizadorFinalizada = 1,
               FechaFinEtapaAnalizadorUtc = @ahora,
               UsuarioFinEtapaAnalizadorId = @usuarioId
WHEN NOT MATCHED THEN
    INSERT
    (
        DiagnosticoIAId, EtapaAnalizadorFinalizada,
        FechaFinEtapaAnalizadorUtc, UsuarioFinEtapaAnalizadorId
    )
    VALUES
    (
        @inspeccionId, 1, @ahora, @usuarioId
    );

UPDATE dbo.diagnosticoIA
SET Estado = N'PENDIENTE_APROBACION'
WHERE DiagnosticoIAId = @inspeccionId;
""";

                await using DbCommand comando = CrearComando(
                    conexion,
                    sql,
                    transaccion);
                AgregarParametro(comando, "@inspeccionId", inspeccionId);
                AgregarParametro(comando, "@usuarioId", usuarioAnalizadorId);
                await comando.ExecuteNonQueryAsync(cancellationToken);

                await transaccion.CommitAsync(cancellationToken);
                return (
                    true,
                    "La revisión humana fue finalizada y todas las fotografías quedaron disponibles para el aprobador.");
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
            finally
            {
                if (cerrar)
                    await conexion.CloseAsync();
            }
        }

        private async Task<List<DevolucionTecnicoFotografiaDto>>
            ObtenerUltimasDevolucionesAsync(
                int inspeccionId,
                CancellationToken cancellationToken)
        {
            const string sql = """
WITH ultimas AS
(
    SELECT
        d.DiagnosticoIAImagenDevolucionTecnicoId,
        d.DiagnosticoIAImagenId,
        d.MotivoDevolucionTecnicoId,
        d.MotivoCodigo,
        d.MotivoNombre,
        d.MotivoDescripcion,
        d.InstruccionSugerida,
        d.InstruccionesAnalizador,
        d.RequiereNuevaFotografia,
        d.PermiteCorregirMetadatos,
        d.UsuarioAnalizadorId,
        d.FechaDevolucionUtc,
        CASE
            WHEN UPPER(ISNULL(i.Estado, N'')) = N'DEVUELTA_AL_TECNICO'
                THEN d.Estado
            ELSE N'RESUELTA'
        END AS Estado,
        d.RespuestaTecnico,
        d.UsuarioTecnicoId,
        d.FechaResolucionUtc,
        ROW_NUMBER() OVER
        (
            PARTITION BY d.DiagnosticoIAImagenId
            ORDER BY d.FechaDevolucionUtc DESC,
                     d.DiagnosticoIAImagenDevolucionTecnicoId DESC
        ) AS rn
    FROM dbo.diagnosticoIAImagenDevolucionTecnico d
    INNER JOIN dbo.diagnosticoIAImagen i
        ON i.DiagnosticoIAImagenId = d.DiagnosticoIAImagenId
    WHERE i.DiagnosticoIAId = @id
)
SELECT
    DiagnosticoIAImagenDevolucionTecnicoId,
    DiagnosticoIAImagenId,
    MotivoDevolucionTecnicoId,
    MotivoCodigo,
    MotivoNombre,
    MotivoDescripcion,
    InstruccionSugerida,
    InstruccionesAnalizador,
    RequiereNuevaFotografia,
    PermiteCorregirMetadatos,
    UsuarioAnalizadorId,
    FechaDevolucionUtc,
    Estado,
    RespuestaTecnico,
    UsuarioTecnicoId,
    FechaResolucionUtc
FROM ultimas
WHERE rn = 1;
""";

            return await EjecutarAsync(async conexion =>
            {
                await using DbCommand comando = CrearComando(conexion, sql);
                AgregarParametro(comando, "@id", inspeccionId);
                var items = new List<DevolucionTecnicoFotografiaDto>();
                await using DbDataReader reader =
                    await comando.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    items.Add(new DevolucionTecnicoFotografiaDto
                    {
                        DevolucionTecnicoId = reader.GetInt32(0),
                        FotografiaId = reader.GetInt32(1),
                        MotivoDevolucionTecnicoId = reader.GetInt32(2),
                        MotivoCodigo = Texto(reader, 3),
                        MotivoNombre = Texto(reader, 4),
                        MotivoDescripcion = Texto(reader, 5),
                        InstruccionSugerida = Texto(reader, 6),
                        InstruccionesAnalizador = Texto(reader, 7),
                        RequiereNuevaFotografia = reader.GetBoolean(8),
                        PermiteCorregirMetadatos = reader.GetBoolean(9),
                        UsuarioAnalizadorId = reader.GetInt32(10),
                        FechaDevolucionUtc = DateTime.SpecifyKind(
                            reader.GetDateTime(11),
                            DateTimeKind.Utc),
                        Estado = Texto(reader, 12),
                        RespuestaTecnico = Texto(reader, 13),
                        UsuarioTecnicoId = reader.IsDBNull(14)
                            ? null
                            : reader.GetInt32(14),
                        FechaResolucionUtc = reader.IsDBNull(15)
                            ? null
                            : DateTime.SpecifyKind(
                                reader.GetDateTime(15),
                                DateTimeKind.Utc)
                    });
                }

                return items;
            }, cancellationToken);
        }

        private static void AplicarEstadoFinalizacion(
            ResumenRevisionAnalizadorDto resumen)
        {
            resumen.PuedeFinalizarRevision =
                resumen.TotalEvaluables > 0 &&
                resumen.EtapaTecnicaFinalizada &&
                !resumen.EtapaAnalizadorFinalizada &&
                resumen.TotalPendientesTecnico == 0 &&
                resumen.TotalDevueltasTecnico == 0 &&
                resumen.TotalErroresIA == 0 &&
                resumen.TotalProcesandoIA == 0 &&
                resumen.TotalRecibidasAnalizador == resumen.TotalEvaluables &&
                resumen.TotalClasificadasHumano == resumen.TotalEvaluables &&
                resumen.TotalPendientesClasificacionHumana == 0;

            if (resumen.EtapaAnalizadorFinalizada)
            {
                resumen.MotivoNoPuedeFinalizarRevision =
                    "La revisión humana ya fue finalizada y enviada al aprobador.";
            }
            else if (!resumen.EtapaTecnicaFinalizada)
            {
                resumen.MotivoNoPuedeFinalizarRevision =
                    resumen.TotalPendientesTecnico > 0
                        ? $"El técnico todavía debe resolver, enviar o descartar {resumen.TotalPendientesTecnico} fotografía(s)."
                        : "El técnico todavía debe finalizar su etapa.";
            }
            else if (resumen.TotalDevueltasTecnico > 0)
            {
                resumen.MotivoNoPuedeFinalizarRevision =
                    $"Existen {resumen.TotalDevueltasTecnico} fotografía(s) devueltas al técnico.";
            }
            else if (resumen.TotalErroresIA > 0)
            {
                resumen.MotivoNoPuedeFinalizarRevision =
                    $"Existen {resumen.TotalErroresIA} fotografía(s) con error de IA.";
            }
            else if (resumen.TotalProcesandoIA > 0)
            {
                resumen.MotivoNoPuedeFinalizarRevision =
                    $"Espere a que terminen {resumen.TotalProcesandoIA} análisis de IA.";
            }
            else if (resumen.TotalPendientesClasificacionHumana > 0 ||
                     resumen.TotalClasificadasHumano < resumen.TotalEvaluables)
            {
                int faltantes = Math.Max(
                    resumen.TotalPendientesClasificacionHumana,
                    resumen.TotalEvaluables -
                    resumen.TotalClasificadasHumano);

                resumen.MotivoNoPuedeFinalizarRevision =
                    $"Faltan {faltantes} fotografía(s) por clasificar humanamente.";
            }
            else if (resumen.TotalEvaluables == 0)
            {
                resumen.MotivoNoPuedeFinalizarRevision =
                    "La inspección no conserva fotografías evaluables.";
            }
            else
            {
                resumen.MotivoNoPuedeFinalizarRevision =
                    "La revisión está completa y puede enviarse al aprobador.";
            }
        }

        private static MotivoDevolucionTecnicoRespuesta LeerMotivo(
            DbDataReader reader) =>
            new()
            {
                MotivoDevolucionTecnicoId = reader.GetInt32(0),
                Codigo = Texto(reader, 1),
                Nombre = Texto(reader, 2),
                Descripcion = Texto(reader, 3),
                InstruccionSugerida = Texto(reader, 4),
                RequiereNuevaFotografia = reader.GetBoolean(5),
                PermiteCorregirMetadatos = reader.GetBoolean(6),
                Orden = reader.GetInt32(7),
                Activo = reader.GetBoolean(8),
                FechaCreacionUtc = DateTime.SpecifyKind(
                    reader.GetDateTime(9),
                    DateTimeKind.Utc),
                FechaModificacionUtc = DateTime.SpecifyKind(
                    reader.GetDateTime(10),
                    DateTimeKind.Utc)
            };

        private static int Entero(DbDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));

        private static string Texto(DbDataReader reader, int ordinal) =>
            reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

        private static string Normalizar(string? valor) =>
            string.IsNullOrWhiteSpace(valor) ? string.Empty : valor.Trim();

        private static string NormalizarCodigo(string? valor) =>
            string.IsNullOrWhiteSpace(valor)
                ? string.Empty
                : valor.Trim().ToUpperInvariant().Replace(' ', '_');

        private static string Limitar(string? valor, int longitud)
        {
            string texto = valor?.Trim() ?? string.Empty;
            return texto.Length <= longitud ? texto : texto[..longitud];
        }

        private static void AgregarDatosMotivo(
            DbCommand comando,
            MotivoDevolucionTecnicoGuardarRequest request,
            int usuarioId)
        {
            AgregarParametro(
                comando,
                "@codigo",
                NormalizarCodigo(request.Codigo));
            AgregarParametro(comando, "@nombre", Limitar(request.Nombre, 140));
            AgregarParametro(
                comando,
                "@descripcion",
                Limitar(request.Descripcion, 700));
            AgregarParametro(
                comando,
                "@instruccion",
                Limitar(request.InstruccionSugerida, 2000));
            AgregarParametro(
                comando,
                "@requiereNueva",
                request.RequiereNuevaFotografia);
            AgregarParametro(
                comando,
                "@permiteMetadatos",
                request.PermiteCorregirMetadatos);
            AgregarParametro(
                comando,
                "@orden",
                Math.Clamp(request.Orden, 1, 999));
            AgregarParametro(comando, "@usuarioId", usuarioId);
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

            if (transaccion != null)
            {
                comando.Transaction = transaccion;
            }
            else if (db.Database.CurrentTransaction != null)
            {
                comando.Transaction =
                    db.Database.CurrentTransaction.GetDbTransaction();
            }

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
    }
}
