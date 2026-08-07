using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Asegura que el Álbum Botánico utilice únicamente la estructura:
    /// Categoría -> Subcategoría específica -> Fotografías.
    ///
    /// AlbumBotanicoCafe es la subcategoría específica. La migración conserva
    /// sus datos y fotografías, elimina la tabla intermedia anterior y limpia
    /// las columnas históricas que representaban un tercer nivel.
    /// </summary>
    public sealed class AlbumJerarquiaDatabaseInitializer
    {
        private readonly AlbumJerarquiaDbContext db;
        private readonly ILogger<AlbumJerarquiaDatabaseInitializer> logger;

        public AlbumJerarquiaDatabaseInitializer(
            AlbumJerarquiaDbContext db,
            ILogger<AlbumJerarquiaDatabaseInitializer> logger)
        {
            this.db = db;
            this.logger = logger;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            const string sql = """
/*
    CONATRADEC - Migración limpia del Álbum Botánico

    Estructura física y funcional final:
        CategoriaAlbumBotanico
            -> AlbumBotanicoCafe (subcategoría específica)
                -> AlbumBotanicoCafeFoto

    AlbumBotanicoCafe conserva el nombre común, nombre científico,
    descripción, síntomas, causas, recomendaciones y observaciones. Por eso
    representa directamente la subcategoría específica.

    La migración es idempotente. Conserva categorías, subcategorías
    específicas, fotografías y trazabilidad de inspecciones. Elimina el nivel
    intermedio artificial y las columnas históricas que lo representaban.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    /*
        1. Asegura la tabla limpia de trazabilidad para la clasificación:
           Categoría -> Subcategoría específica.
    */
    IF OBJECT_ID(N'dbo.diagnosticoIAClasificacionJerarquia', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.diagnosticoIAClasificacionJerarquia
        (
            DiagnosticoIAClasificacionJerarquiaId INT IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_diagnosticoIAClasificacionJerarquia PRIMARY KEY,
            DiagnosticoIAImagenId INT NOT NULL,
            CategoriaAlbumBotanicoIdSugerida INT NULL,
            AlbumBotanicoCafeIdSugerido INT NULL,
            CategoriaSugerida NVARCHAR(150) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_CategoriaSugerida
                DEFAULT (N''),
            SubcategoriaSugerida NVARCHAR(200) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_SubcategoriaSugerida
                DEFAULT (N''),
            NombreCientificoSugerido NVARCHAR(200) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_NombreCientifico
                DEFAULT (N''),
            MotivoSugerencia NVARCHAR(1200) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_Motivo
                DEFAULT (N''),
            CategoriaAlbumBotanicoIdSeleccionada INT NULL,
            AlbumBotanicoCafeIdSeleccionado INT NULL,
            CategoriaSeleccionada NVARCHAR(150) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_CategoriaSeleccionada
                DEFAULT (N''),
            SubcategoriaSeleccionada NVARCHAR(200) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_SubcategoriaSeleccionada
                DEFAULT (N''),
            ProponeCategoria BIT NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_ProponeCategoria
                DEFAULT (0),
            ProponeSubcategoria BIT NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_ProponeSubcategoria
                DEFAULT (0),
            Estado NVARCHAR(40) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_Estado
                DEFAULT (N'SUGERIDA_IA'),
            UsuarioActualizacionId INT NULL,
            FechaActualizacionUtc DATETIME2 NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_Fecha
                DEFAULT (SYSUTCDATETIME())
        );
    END;

    /* Completa instalaciones que tengan una versión parcial de la tabla. */
    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'CategoriaAlbumBotanicoIdSugerida') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD CategoriaAlbumBotanicoIdSugerida INT NULL;
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'AlbumBotanicoCafeIdSugerido') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD AlbumBotanicoCafeIdSugerido INT NULL;
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'CategoriaSugerida') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD CategoriaSugerida NVARCHAR(150) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_CategoriaSugerida
                DEFAULT (N'');
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'SubcategoriaSugerida') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD SubcategoriaSugerida NVARCHAR(200) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_SubcategoriaSugerida
                DEFAULT (N'');
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'NombreCientificoSugerido') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD NombreCientificoSugerido NVARCHAR(200) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_NombreCientifico
                DEFAULT (N'');
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'MotivoSugerencia') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD MotivoSugerencia NVARCHAR(1200) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_Motivo
                DEFAULT (N'');
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'CategoriaAlbumBotanicoIdSeleccionada') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD CategoriaAlbumBotanicoIdSeleccionada INT NULL;
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'AlbumBotanicoCafeIdSeleccionado') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD AlbumBotanicoCafeIdSeleccionado INT NULL;
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'CategoriaSeleccionada') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD CategoriaSeleccionada NVARCHAR(150) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_CategoriaSeleccionada
                DEFAULT (N'');
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'SubcategoriaSeleccionada') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD SubcategoriaSeleccionada NVARCHAR(200) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_SubcategoriaSeleccionada
                DEFAULT (N'');
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'ProponeCategoria') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD ProponeCategoria BIT NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_ProponeCategoria
                DEFAULT (0);
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'ProponeSubcategoria') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD ProponeSubcategoria BIT NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_ProponeSubcategoria
                DEFAULT (0);
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'Estado') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD Estado NVARCHAR(40) NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_Estado
                DEFAULT (N'SUGERIDA_IA');
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'UsuarioActualizacionId') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD UsuarioActualizacionId INT NULL;
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'FechaActualizacionUtc') IS NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            ADD FechaActualizacionUtc DATETIME2 NOT NULL
                CONSTRAINT DF_diagnosticoIAJerarquia_Fecha
                DEFAULT (SYSUTCDATETIME());
    END;

    /*
        Amplía los nombres específicos a 200 caracteres antes de copiar los
        valores históricos de ficha.
    */
    UPDATE dbo.diagnosticoIAClasificacionJerarquia
    SET SubcategoriaSugerida = N''
    WHERE SubcategoriaSugerida IS NULL;

    UPDATE dbo.diagnosticoIAClasificacionJerarquia
    SET SubcategoriaSeleccionada = N''
    WHERE SubcategoriaSeleccionada IS NULL;

    ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
        ALTER COLUMN SubcategoriaSugerida NVARCHAR(200) NOT NULL;

    ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
        ALTER COLUMN SubcategoriaSeleccionada NVARCHAR(200) NOT NULL;

    /*
        2. Traslada valores históricos de "ficha" a la subcategoría específica
           antes de eliminar las columnas del nivel anterior.
    */
    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'FichaSugerida') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            UPDATE dbo.diagnosticoIAClasificacionJerarquia
            SET SubcategoriaSugerida = COALESCE(
                NULLIF(LTRIM(RTRIM(FichaSugerida)), N''''),
                NULLIF(LTRIM(RTRIM(SubcategoriaSugerida)), N''''),
                N'''');';
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'FichaSeleccionada') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            UPDATE dbo.diagnosticoIAClasificacionJerarquia
            SET SubcategoriaSeleccionada = COALESCE(
                NULLIF(LTRIM(RTRIM(FichaSeleccionada)), N''''),
                NULLIF(LTRIM(RTRIM(SubcategoriaSeleccionada)), N''''),
                N'''');';
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'ProponeFicha') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            UPDATE dbo.diagnosticoIAClasificacionJerarquia
            SET ProponeSubcategoria = CASE
                WHEN ProponeFicha = 1 OR ProponeSubcategoria = 1 THEN 1
                ELSE 0
            END;';
    END;

    /*
        3. Elimina restricciones e índices vinculados al nivel intermedio de
           AlbumBotanicoCafe.
    */
    DECLARE @sql NVARCHAR(MAX) = N'';

    SELECT @sql = @sql +
        N'ALTER TABLE ' +
        QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + N'.' +
        QUOTENAME(OBJECT_NAME(fk.parent_object_id)) +
        N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';' + CHAR(10)
    FROM sys.foreign_keys fk
    WHERE fk.referenced_object_id =
            OBJECT_ID(N'dbo.SubcategoriaAlbumBotanico')
       OR (
            fk.parent_object_id = OBJECT_ID(N'dbo.AlbumBotanicoCafe')
            AND EXISTS
            (
                SELECT 1
                FROM sys.foreign_key_columns columna
                WHERE columna.constraint_object_id = fk.object_id
                  AND COL_NAME(
                        columna.parent_object_id,
                        columna.parent_column_id) =
                        N'subcategoriaAlbumBotanicoId'
            )
       );

    IF LEN(@sql) > 0
        EXEC sys.sp_executesql @sql;

    SET @sql = N'';

    SELECT @sql = @sql +
        N'DROP INDEX ' + QUOTENAME(indice.name) +
        N' ON dbo.AlbumBotanicoCafe;' + CHAR(10)
    FROM sys.indexes indice
    WHERE indice.object_id = OBJECT_ID(N'dbo.AlbumBotanicoCafe')
      AND indice.is_primary_key = 0
      AND indice.is_unique_constraint = 0
      AND EXISTS
      (
          SELECT 1
          FROM sys.index_columns columnaIndice
          INNER JOIN sys.columns columna
              ON columna.object_id = columnaIndice.object_id
             AND columna.column_id = columnaIndice.column_id
          WHERE columnaIndice.object_id = indice.object_id
            AND columnaIndice.index_id = indice.index_id
            AND columna.name = N'subcategoriaAlbumBotanicoId'
      );

    IF LEN(@sql) > 0
        EXEC sys.sp_executesql @sql;

    /* Restricciones DEFAULT de la columna que será retirada. */
    SET @sql = N'';

    SELECT @sql = @sql +
        N'ALTER TABLE dbo.AlbumBotanicoCafe DROP CONSTRAINT ' +
        QUOTENAME(restriccion.name) + N';' + CHAR(10)
    FROM sys.default_constraints restriccion
    INNER JOIN sys.columns columna
        ON columna.object_id = restriccion.parent_object_id
       AND columna.column_id = restriccion.parent_column_id
    WHERE restriccion.parent_object_id =
            OBJECT_ID(N'dbo.AlbumBotanicoCafe')
      AND columna.name = N'subcategoriaAlbumBotanicoId';

    IF LEN(@sql) > 0
        EXEC sys.sp_executesql @sql;

    /* Restricciones CHECK que mencionen la columna anterior. */
    SET @sql = N'';

    SELECT @sql = @sql +
        N'ALTER TABLE dbo.AlbumBotanicoCafe DROP CONSTRAINT ' +
        QUOTENAME(restriccion.name) + N';' + CHAR(10)
    FROM sys.check_constraints restriccion
    WHERE restriccion.parent_object_id =
            OBJECT_ID(N'dbo.AlbumBotanicoCafe')
      AND restriccion.definition LIKE
            N'%subcategoriaAlbumBotanicoId%';

    IF LEN(@sql) > 0
        EXEC sys.sp_executesql @sql;

    /*
        4. Elimina la columna y la tabla intermedia. A partir de aquí no existe
           grupo oculto ni nivel adicional en el esquema del álbum.
    */
    IF COL_LENGTH(
        N'dbo.AlbumBotanicoCafe',
        N'subcategoriaAlbumBotanicoId') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.AlbumBotanicoCafe
            DROP COLUMN subcategoriaAlbumBotanicoId;
    END;

    IF OBJECT_ID(N'dbo.SubcategoriaAlbumBotanico', N'U') IS NOT NULL
    BEGIN
        DROP TABLE dbo.SubcategoriaAlbumBotanico;
    END;

    /*
        5. Limpia la trazabilidad y elimina las columnas históricas que
           representaban el nivel "ficha".
    */
    IF OBJECT_ID(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'U') IS NOT NULL
    BEGIN
        UPDATE clasificacion
        SET CategoriaAlbumBotanicoIdSugerida = NULL
        FROM dbo.diagnosticoIAClasificacionJerarquia clasificacion
        WHERE clasificacion.CategoriaAlbumBotanicoIdSugerida IS NOT NULL
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.CategoriaAlbumBotanico categoria
              WHERE categoria.categoriaAlbumBotanicoId =
                    clasificacion.CategoriaAlbumBotanicoIdSugerida
          );

        UPDATE clasificacion
        SET CategoriaAlbumBotanicoIdSeleccionada = NULL
        FROM dbo.diagnosticoIAClasificacionJerarquia clasificacion
        WHERE clasificacion.CategoriaAlbumBotanicoIdSeleccionada IS NOT NULL
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.CategoriaAlbumBotanico categoria
              WHERE categoria.categoriaAlbumBotanicoId =
                    clasificacion.CategoriaAlbumBotanicoIdSeleccionada
          );

        UPDATE clasificacion
        SET AlbumBotanicoCafeIdSugerido = NULL
        FROM dbo.diagnosticoIAClasificacionJerarquia clasificacion
        WHERE clasificacion.AlbumBotanicoCafeIdSugerido IS NOT NULL
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.AlbumBotanicoCafe subcategoria
              WHERE subcategoria.albumBotanicoCafeId =
                    clasificacion.AlbumBotanicoCafeIdSugerido
          );

        UPDATE clasificacion
        SET AlbumBotanicoCafeIdSeleccionado = NULL
        FROM dbo.diagnosticoIAClasificacionJerarquia clasificacion
        WHERE clasificacion.AlbumBotanicoCafeIdSeleccionado IS NOT NULL
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.AlbumBotanicoCafe subcategoria
              WHERE subcategoria.albumBotanicoCafeId =
                    clasificacion.AlbumBotanicoCafeIdSeleccionado
          );
    END;

    DECLARE @columnasLegadas TABLE
    (
        Nombre SYSNAME NOT NULL
    );

    INSERT INTO @columnasLegadas (Nombre)
    VALUES
        (N'SubcategoriaAlbumBotanicoIdSugerida'),
        (N'FichaSugerida'),
        (N'SubcategoriaAlbumBotanicoIdSeleccionada'),
        (N'FichaSeleccionada'),
        (N'ProponeFicha');

    /* Elimina llaves foráneas asociadas a las columnas legadas. */
    SET @sql = N'';

    SELECT @sql = @sql +
        N'ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia ' +
        N'DROP CONSTRAINT ' + QUOTENAME(fk.name) +
        N';' + CHAR(10)
    FROM sys.foreign_keys fk
    WHERE fk.parent_object_id =
            OBJECT_ID(N'dbo.diagnosticoIAClasificacionJerarquia')
      AND EXISTS
      (
          SELECT 1
          FROM sys.foreign_key_columns columnaFk
          INNER JOIN sys.columns columna
              ON columna.object_id = columnaFk.parent_object_id
             AND columna.column_id = columnaFk.parent_column_id
          INNER JOIN @columnasLegadas legada
              ON legada.Nombre = columna.name
          WHERE columnaFk.constraint_object_id = fk.object_id
      );

    IF LEN(@sql) > 0
        EXEC sys.sp_executesql @sql;

    /* Elimina CHECK asociados a las columnas legadas. */
    SET @sql = N'';

    SELECT @sql = @sql +
        N'ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia ' +
        N'DROP CONSTRAINT ' + QUOTENAME(restriccion.name) +
        N';' + CHAR(10)
    FROM sys.check_constraints restriccion
    INNER JOIN @columnasLegadas legada
        ON restriccion.definition LIKE N'%' + legada.Nombre + N'%'
    WHERE restriccion.parent_object_id =
        OBJECT_ID(N'dbo.diagnosticoIAClasificacionJerarquia');

    IF LEN(@sql) > 0
        EXEC sys.sp_executesql @sql;

    /* Elimina DEFAULT asociados a las columnas legadas. */
    SET @sql = N'';

    SELECT @sql = @sql +
        N'ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia ' +
        N'DROP CONSTRAINT ' + QUOTENAME(restriccion.name) +
        N';' + CHAR(10)
    FROM sys.default_constraints restriccion
    INNER JOIN sys.columns columna
        ON columna.object_id = restriccion.parent_object_id
       AND columna.column_id = restriccion.parent_column_id
    INNER JOIN @columnasLegadas legada
        ON legada.Nombre = columna.name
    WHERE restriccion.parent_object_id =
        OBJECT_ID(N'dbo.diagnosticoIAClasificacionJerarquia');

    IF LEN(@sql) > 0
        EXEC sys.sp_executesql @sql;

    /* Elimina índices que dependan de las columnas legadas. */
    SET @sql = N'';

    SELECT @sql = @sql +
        N'DROP INDEX ' + QUOTENAME(indice.name) +
        N' ON dbo.diagnosticoIAClasificacionJerarquia;' + CHAR(10)
    FROM sys.indexes indice
    WHERE indice.object_id =
            OBJECT_ID(N'dbo.diagnosticoIAClasificacionJerarquia')
      AND indice.is_primary_key = 0
      AND indice.is_unique_constraint = 0
      AND EXISTS
      (
          SELECT 1
          FROM sys.index_columns columnaIndice
          INNER JOIN sys.columns columna
              ON columna.object_id = columnaIndice.object_id
             AND columna.column_id = columnaIndice.column_id
          INNER JOIN @columnasLegadas legada
              ON legada.Nombre = columna.name
          WHERE columnaIndice.object_id = indice.object_id
            AND columnaIndice.index_id = indice.index_id
      );

    IF LEN(@sql) > 0
        EXEC sys.sp_executesql @sql;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'SubcategoriaAlbumBotanicoIdSugerida') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            DROP COLUMN SubcategoriaAlbumBotanicoIdSugerida;
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'FichaSugerida') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            DROP COLUMN FichaSugerida;
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'SubcategoriaAlbumBotanicoIdSeleccionada') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            DROP COLUMN SubcategoriaAlbumBotanicoIdSeleccionada;
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'FichaSeleccionada') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            DROP COLUMN FichaSeleccionada;
    END;

    IF COL_LENGTH(
        N'dbo.diagnosticoIAClasificacionJerarquia',
        N'ProponeFicha') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia
            DROP COLUMN ProponeFicha;
    END;

    /*
        Elimina únicamente filas auxiliares inválidas o duplicadas de la
        trazabilidad. No elimina inspecciones, categorías, subcategorías ni
        fotografías del Álbum Botánico.
    */
    IF OBJECT_ID(N'dbo.diagnosticoIAImagen', N'U') IS NOT NULL
    BEGIN
        DELETE clasificacion
        FROM dbo.diagnosticoIAClasificacionJerarquia clasificacion
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.diagnosticoIAImagen fotografia
            WHERE fotografia.DiagnosticoIAImagenId =
                clasificacion.DiagnosticoIAImagenId
        );
    END;

    ;WITH duplicados AS
    (
        SELECT
            DiagnosticoIAClasificacionJerarquiaId,
            ROW_NUMBER() OVER
            (
                PARTITION BY DiagnosticoIAImagenId
                ORDER BY
                    FechaActualizacionUtc DESC,
                    DiagnosticoIAClasificacionJerarquiaId DESC
            ) AS NumeroFila
        FROM dbo.diagnosticoIAClasificacionJerarquia
    )
    DELETE FROM duplicados
    WHERE NumeroFila > 1;

    /*
        6. Índices y relaciones de la estructura final.
    */
    IF OBJECT_ID(N'dbo.AlbumBotanicoCafe', N'U') IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'dbo.AlbumBotanicoCafe')
             AND name = N'IX_AlbumBotanicoCafe_Categoria_Titulo'
       )
    BEGIN
        CREATE INDEX IX_AlbumBotanicoCafe_Categoria_Titulo
            ON dbo.AlbumBotanicoCafe
            (
                categoriaAlbumBotanicoId,
                titulo
            )
            INCLUDE
            (
                activo,
                nombreCientifico,
                fechaCreacion
            );
    END;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE object_id =
                OBJECT_ID(N'dbo.diagnosticoIAClasificacionJerarquia')
          AND name = N'UX_diagnosticoIAJerarquia_Imagen'
    )
    BEGIN
        CREATE UNIQUE INDEX UX_diagnosticoIAJerarquia_Imagen
            ON dbo.diagnosticoIAClasificacionJerarquia
            (
                DiagnosticoIAImagenId
            );
    END;

    IF OBJECT_ID(N'dbo.diagnosticoIAImagen', N'U') IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM sys.foreign_keys
           WHERE parent_object_id =
                    OBJECT_ID(N'dbo.diagnosticoIAClasificacionJerarquia')
             AND referenced_object_id =
                    OBJECT_ID(N'dbo.diagnosticoIAImagen')
       )
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia WITH CHECK
            ADD CONSTRAINT FK_diagnosticoIAJerarquia_Imagen
            FOREIGN KEY (DiagnosticoIAImagenId)
            REFERENCES dbo.diagnosticoIAImagen(DiagnosticoIAImagenId)
            ON DELETE CASCADE;
    END;

    IF OBJECT_ID(N'dbo.CategoriaAlbumBotanico', N'U') IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM sys.foreign_keys
           WHERE name =
                N'FK_diagnosticoIAJerarquia_CategoriaSugerida'
       )
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia WITH CHECK
            ADD CONSTRAINT FK_diagnosticoIAJerarquia_CategoriaSugerida
            FOREIGN KEY (CategoriaAlbumBotanicoIdSugerida)
            REFERENCES dbo.CategoriaAlbumBotanico(categoriaAlbumBotanicoId);
    END;

    IF OBJECT_ID(N'dbo.CategoriaAlbumBotanico', N'U') IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM sys.foreign_keys
           WHERE name =
                N'FK_diagnosticoIAJerarquia_CategoriaSeleccionada'
       )
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia WITH CHECK
            ADD CONSTRAINT FK_diagnosticoIAJerarquia_CategoriaSeleccionada
            FOREIGN KEY (CategoriaAlbumBotanicoIdSeleccionada)
            REFERENCES dbo.CategoriaAlbumBotanico(categoriaAlbumBotanicoId);
    END;

    IF OBJECT_ID(N'dbo.AlbumBotanicoCafe', N'U') IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM sys.foreign_keys
           WHERE name =
                N'FK_diagnosticoIAJerarquia_SubcategoriaSugerida'
       )
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia WITH CHECK
            ADD CONSTRAINT FK_diagnosticoIAJerarquia_SubcategoriaSugerida
            FOREIGN KEY (AlbumBotanicoCafeIdSugerido)
            REFERENCES dbo.AlbumBotanicoCafe(albumBotanicoCafeId);
    END;

    IF OBJECT_ID(N'dbo.AlbumBotanicoCafe', N'U') IS NOT NULL
       AND NOT EXISTS
       (
           SELECT 1
           FROM sys.foreign_keys
           WHERE name =
                N'FK_diagnosticoIAJerarquia_SubcategoriaSeleccionada'
       )
    BEGIN
        ALTER TABLE dbo.diagnosticoIAClasificacionJerarquia WITH CHECK
            ADD CONSTRAINT FK_diagnosticoIAJerarquia_SubcategoriaSeleccionada
            FOREIGN KEY (AlbumBotanicoCafeIdSeleccionado)
            REFERENCES dbo.AlbumBotanicoCafe(albumBotanicoCafeId);
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;

""";

            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    sql,
                    cancellationToken);

                logger.LogInformation(
                    "Álbum Botánico preparado con la estructura Categoría -> Subcategoría específica -> Fotografías.");
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "No fue posible preparar la estructura limpia del Álbum Botánico.");
                throw;
            }
        }
    }
}
