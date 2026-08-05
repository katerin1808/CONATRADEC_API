using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Inicializa de forma idempotente la jerarquía del Álbum Botánico:
    /// Categoría -> Subcategoría -> Ficha -> Fotografías.
    ///
    /// La inicialización conserva toda la información existente, crea las
    /// subcategorías base y clasifica las fichas anteriores usando reglas
    /// conservadoras. Los administradores pueden corregir después cualquier
    /// asignación desde el formulario del álbum.
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
IF OBJECT_ID(N'dbo.SubcategoriaAlbumBotanico', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SubcategoriaAlbumBotanico
    (
        SubcategoriaAlbumBotanicoId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_SubcategoriaAlbumBotanico PRIMARY KEY,
        CategoriaAlbumBotanicoId INT NOT NULL,
        NombreSubcategoria NVARCHAR(120) NOT NULL,
        Descripcion NVARCHAR(600) NULL,
        Activo BIT NOT NULL
            CONSTRAINT DF_SubcategoriaAlbumBotanico_Activo DEFAULT (1),
        FechaCreacionUtc DATETIME2 NOT NULL
            CONSTRAINT DF_SubcategoriaAlbumBotanico_FechaCreacionUtc
            DEFAULT (SYSUTCDATETIME()),
        FechaActualizacionUtc DATETIME2 NULL,
        CONSTRAINT FK_SubcategoriaAlbumBotanico_Categoria
            FOREIGN KEY (CategoriaAlbumBotanicoId)
            REFERENCES dbo.CategoriaAlbumBotanico(categoriaAlbumBotanicoId)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_SubcategoriaAlbumBotanico_Categoria_Nombre'
      AND object_id = OBJECT_ID(N'dbo.SubcategoriaAlbumBotanico')
)
BEGIN
    CREATE UNIQUE INDEX UX_SubcategoriaAlbumBotanico_Categoria_Nombre
        ON dbo.SubcategoriaAlbumBotanico
        (
            CategoriaAlbumBotanicoId,
            NombreSubcategoria
        );
END;

IF COL_LENGTH(N'dbo.AlbumBotanicoCafe', N'subcategoriaAlbumBotanicoId') IS NULL
BEGIN
    ALTER TABLE dbo.AlbumBotanicoCafe
        ADD subcategoriaAlbumBotanicoId INT NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_AlbumBotanicoCafe_SubcategoriaAlbumBotanico'
)
BEGIN
    ALTER TABLE dbo.AlbumBotanicoCafe WITH CHECK
        ADD CONSTRAINT FK_AlbumBotanicoCafe_SubcategoriaAlbumBotanico
        FOREIGN KEY (subcategoriaAlbumBotanicoId)
        REFERENCES dbo.SubcategoriaAlbumBotanico(SubcategoriaAlbumBotanicoId);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_AlbumBotanicoCafe_SubcategoriaAlbumBotanicoId'
      AND object_id = OBJECT_ID(N'dbo.AlbumBotanicoCafe')
)
BEGIN
    CREATE INDEX IX_AlbumBotanicoCafe_SubcategoriaAlbumBotanicoId
        ON dbo.AlbumBotanicoCafe(subcategoriaAlbumBotanicoId);
END;

IF OBJECT_ID(N'dbo.diagnosticoIAClasificacionJerarquia', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.diagnosticoIAClasificacionJerarquia
    (
        DiagnosticoIAClasificacionJerarquiaId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_diagnosticoIAClasificacionJerarquia PRIMARY KEY,
        DiagnosticoIAImagenId INT NOT NULL,
        CategoriaAlbumBotanicoIdSugerida INT NULL,
        SubcategoriaAlbumBotanicoIdSugerida INT NULL,
        AlbumBotanicoCafeIdSugerido INT NULL,
        CategoriaSugerida NVARCHAR(150) NOT NULL
            CONSTRAINT DF_diagnosticoIAJerarquia_CategoriaSugerida DEFAULT (N''),
        SubcategoriaSugerida NVARCHAR(150) NOT NULL
            CONSTRAINT DF_diagnosticoIAJerarquia_SubcategoriaSugerida DEFAULT (N''),
        FichaSugerida NVARCHAR(200) NOT NULL
            CONSTRAINT DF_diagnosticoIAJerarquia_FichaSugerida DEFAULT (N''),
        NombreCientificoSugerido NVARCHAR(200) NOT NULL
            CONSTRAINT DF_diagnosticoIAJerarquia_NombreCientifico DEFAULT (N''),
        MotivoSugerencia NVARCHAR(1200) NOT NULL
            CONSTRAINT DF_diagnosticoIAJerarquia_Motivo DEFAULT (N''),
        CategoriaAlbumBotanicoIdSeleccionada INT NULL,
        SubcategoriaAlbumBotanicoIdSeleccionada INT NULL,
        AlbumBotanicoCafeIdSeleccionado INT NULL,
        CategoriaSeleccionada NVARCHAR(150) NOT NULL
            CONSTRAINT DF_diagnosticoIAJerarquia_CategoriaSeleccionada DEFAULT (N''),
        SubcategoriaSeleccionada NVARCHAR(150) NOT NULL
            CONSTRAINT DF_diagnosticoIAJerarquia_SubcategoriaSeleccionada DEFAULT (N''),
        FichaSeleccionada NVARCHAR(200) NOT NULL
            CONSTRAINT DF_diagnosticoIAJerarquia_FichaSeleccionada DEFAULT (N''),
        ProponeCategoria BIT NOT NULL
            CONSTRAINT DF_diagnosticoIAJerarquia_ProponeCategoria DEFAULT (0),
        ProponeSubcategoria BIT NOT NULL
            CONSTRAINT DF_diagnosticoIAJerarquia_ProponeSubcategoria DEFAULT (0),
        ProponeFicha BIT NOT NULL
            CONSTRAINT DF_diagnosticoIAJerarquia_ProponeFicha DEFAULT (0),
        Estado NVARCHAR(40) NOT NULL
            CONSTRAINT DF_diagnosticoIAJerarquia_Estado DEFAULT (N'SUGERIDA_IA'),
        UsuarioActualizacionId INT NULL,
        FechaActualizacionUtc DATETIME2 NOT NULL
            CONSTRAINT DF_diagnosticoIAJerarquia_Fecha DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_diagnosticoIAJerarquia_Imagen
            FOREIGN KEY (DiagnosticoIAImagenId)
            REFERENCES dbo.diagnosticoIAImagen(DiagnosticoIAImagenId)
            ON DELETE CASCADE
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_diagnosticoIAJerarquia_Imagen'
      AND object_id = OBJECT_ID(N'dbo.diagnosticoIAClasificacionJerarquia')
)
BEGIN
    CREATE UNIQUE INDEX UX_diagnosticoIAJerarquia_Imagen
        ON dbo.diagnosticoIAClasificacionJerarquia(DiagnosticoIAImagenId);
END;

/*
 * Catálogo base. Solo se insertan nombres que todavía no existen dentro de
 * la categoría correspondiente. Las comparaciones se hacen por el nombre de
 * la categoría para respetar los identificadores actuales de cada instalación.
 */
DECLARE @Semillas TABLE
(
    PatronCategoria NVARCHAR(100) NOT NULL,
    NombreSubcategoria NVARCHAR(120) NOT NULL,
    Descripcion NVARCHAR(600) NULL,
    Orden INT NOT NULL
);

INSERT INTO @Semillas
(
    PatronCategoria,
    NombreSubcategoria,
    Descripcion,
    Orden
)
VALUES
(N'ENFERMED', N'Enfermedades fúngicas', N'Enfermedades causadas o asociadas a hongos fitopatógenos.', 10),
(N'ENFERMED', N'Enfermedades bacterianas', N'Enfermedades causadas o asociadas a bacterias.', 20),
(N'ENFERMED', N'Enfermedades virales', N'Enfermedades causadas o asociadas a virus.', 30),
(N'ENFERMED', N'Otras enfermedades', N'Enfermedades que requieren una clasificación más específica.', 90),
(N'PLAGA', N'Insectos', N'Plagas de origen insectil.', 10),
(N'PLAGA', N'Ácaros', N'Plagas causadas por ácaros.', 20),
(N'PLAGA', N'Nematodos', N'Plagas y daños asociados a nematodos.', 30),
(N'PLAGA', N'Moluscos', N'Daños asociados a babosas, caracoles y otros moluscos.', 40),
(N'PLAGA', N'Otras plagas', N'Plagas que requieren una clasificación más específica.', 90),
(N'NUTRIC', N'Deficiencias de macronutrientes', N'Deficiencias de N, P, K, Ca, Mg o S.', 10),
(N'NUTRIC', N'Deficiencias de micronutrientes', N'Deficiencias de Fe, Zn, B, Mn, Cu, Mo u otros micronutrientes.', 20),
(N'NUTRIC', N'Otras alteraciones nutricionales', N'Desequilibrios nutricionales no clasificados en los grupos anteriores.', 90),
(N'DEFICI', N'Deficiencias de macronutrientes', N'Deficiencias de N, P, K, Ca, Mg o S.', 10),
(N'DEFICI', N'Deficiencias de micronutrientes', N'Deficiencias de Fe, Zn, B, Mn, Cu, Mo u otros micronutrientes.', 20),
(N'DEFICI', N'Otras alteraciones nutricionales', N'Desequilibrios nutricionales no clasificados en los grupos anteriores.', 90),
(N'ESTRES', N'Estrés hídrico', N'Daños por sequía, exceso de agua o encharcamiento.', 10),
(N'ESTRES', N'Estrés térmico', N'Daños asociados a temperaturas extremas.', 20),
(N'ESTRES', N'Daño químico', N'Daños por herbicidas, fitotoxicidad u otras sustancias.', 30),
(N'ESTRES', N'Otros daños no bióticos', N'Daños abióticos que requieren una clasificación más específica.', 90),
(N'SANA', N'Planta completa', N'Plantas completas sin síntomas visibles relevantes.', 10),
(N'SANA', N'Hojas sanas', N'Hojas sin síntomas visibles relevantes.', 20),
(N'SANA', N'Frutos sanos', N'Frutos sin síntomas visibles relevantes.', 30),
(N'SANA', N'Tallos y ramas sanas', N'Tallos y ramas sin síntomas visibles relevantes.', 40),
(N'MECAN', N'Daño por manejo', N'Daños físicos asociados al manejo, poda o labores culturales.', 10),
(N'MECAN', N'Daño climático', N'Daños físicos por viento, granizo, lluvia u otros eventos.', 20),
(N'MECAN', N'Otro daño físico', N'Daños físicos que requieren una clasificación más específica.', 90);

INSERT INTO dbo.SubcategoriaAlbumBotanico
(
    CategoriaAlbumBotanicoId,
    NombreSubcategoria,
    Descripcion,
    Activo,
    FechaCreacionUtc
)
SELECT DISTINCT
    c.categoriaAlbumBotanicoId,
    s.NombreSubcategoria,
    s.Descripcion,
    1,
    SYSUTCDATETIME()
FROM dbo.CategoriaAlbumBotanico c
INNER JOIN @Semillas s
    ON UPPER(c.nombreCategoria) LIKE N'%' + s.PatronCategoria + N'%'
WHERE c.activo = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.SubcategoriaAlbumBotanico existente
      WHERE existente.CategoriaAlbumBotanicoId = c.categoriaAlbumBotanicoId
        AND UPPER(LTRIM(RTRIM(existente.NombreSubcategoria))) =
            UPPER(LTRIM(RTRIM(s.NombreSubcategoria)))
  );

/*
 * Normalización inicial de fichas existentes. Se asigna únicamente cuando la
 * ficha todavía no tiene subcategoría. Si no hay una coincidencia específica,
 * se utiliza la subcategoría genérica de la categoría correspondiente.
 */
UPDATE registro
SET subcategoriaAlbumBotanicoId = candidato.SubcategoriaAlbumBotanicoId
FROM dbo.AlbumBotanicoCafe registro
CROSS APPLY
(
    SELECT TOP (1)
        sub.SubcategoriaAlbumBotanicoId
    FROM dbo.SubcategoriaAlbumBotanico sub
    WHERE sub.CategoriaAlbumBotanicoId = registro.categoriaAlbumBotanicoId
      AND sub.Activo = 1
    ORDER BY
        CASE
            WHEN UPPER(registro.titulo) LIKE N'%MINADOR%'
                 AND sub.NombreSubcategoria = N'Insectos' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%BROCA%'
                 AND sub.NombreSubcategoria = N'Insectos' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%COCHINILLA%'
                 AND sub.NombreSubcategoria = N'Insectos' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%PULGON%'
                 AND sub.NombreSubcategoria = N'Insectos' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%TRIPS%'
                 AND sub.NombreSubcategoria = N'Insectos' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%ACARO%'
                 AND sub.NombreSubcategoria = N'Ácaros' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%ARAÑ%'
                 AND sub.NombreSubcategoria = N'Ácaros' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%NEMATOD%'
                 AND sub.NombreSubcategoria = N'Nematodos' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%ROYA%'
                 AND sub.NombreSubcategoria = N'Enfermedades fúngicas' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%MANCHA DE HIERRO%'
                 AND sub.NombreSubcategoria = N'Enfermedades fúngicas' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%CERCOSPORA%'
                 AND sub.NombreSubcategoria = N'Enfermedades fúngicas' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%OJO DE GALLO%'
                 AND sub.NombreSubcategoria = N'Enfermedades fúngicas' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%ANTRACNOSIS%'
                 AND sub.NombreSubcategoria = N'Enfermedades fúngicas' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%PHOMA%'
                 AND sub.NombreSubcategoria = N'Enfermedades fúngicas' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%BACTER%'
                 AND sub.NombreSubcategoria = N'Enfermedades bacterianas' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%VIR%'
                 AND sub.NombreSubcategoria = N'Enfermedades virales' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%NITROGEN%'
                 AND sub.NombreSubcategoria = N'Deficiencias de macronutrientes' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%FOSFOR%'
                 AND sub.NombreSubcategoria = N'Deficiencias de macronutrientes' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%POTAS%'
                 AND sub.NombreSubcategoria = N'Deficiencias de macronutrientes' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%HIERRO%'
                 AND sub.NombreSubcategoria = N'Deficiencias de micronutrientes' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%ZINC%'
                 AND sub.NombreSubcategoria = N'Deficiencias de micronutrientes' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%BORO%'
                 AND sub.NombreSubcategoria = N'Deficiencias de micronutrientes' THEN 1
            WHEN UPPER(registro.titulo) LIKE N'%SANA%'
                 AND sub.NombreSubcategoria = N'Planta completa' THEN 1
            WHEN sub.NombreSubcategoria IN
            (
                N'Otras enfermedades',
                N'Otras plagas',
                N'Otras alteraciones nutricionales',
                N'Otros daños no bióticos',
                N'Otro daño físico',
                N'Planta completa'
            ) THEN 50
            ELSE 100
        END,
        sub.SubcategoriaAlbumBotanicoId
) candidato
WHERE registro.subcategoriaAlbumBotanicoId IS NULL;
""";

            try
            {
                DbConnection connection = db.Database.GetDbConnection();
                bool debeCerrar =
                    connection.State != System.Data.ConnectionState.Open;

                if (debeCerrar)
                    await connection.OpenAsync(cancellationToken);

                try
                {
                    await using DbCommand command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.CommandTimeout = 180;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                finally
                {
                    if (debeCerrar)
                        await connection.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "No fue posible inicializar la jerarquía del Álbum Botánico.");
                throw;
            }
        }
    }
}
