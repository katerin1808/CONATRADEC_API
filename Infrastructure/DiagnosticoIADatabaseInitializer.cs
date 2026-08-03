using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Instala las tablas y el permiso del módulo sin depender de migraciones.
    /// Puede ejecutarse varias veces de forma segura durante el arranque.
    /// </summary>
    public sealed class DiagnosticoIADatabaseInitializer
    {
        public const string PermisoDiagnosticoIA =
            "diagnosticoIAPage";

        private readonly DiagnosticoIADbContext db;

        public DiagnosticoIADatabaseInitializer(
            DiagnosticoIADbContext db)
        {
            this.db = db;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            const string sql = """
IF OBJECT_ID(N'[dbo].[diagnosticoIA]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIA]
    (
        [DiagnosticoIAId] INT IDENTITY(1,1) NOT NULL,
        [TerrenoId] INT NULL,
        [CodigoTerreno] NVARCHAR(50) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_codigoTerreno] DEFAULT(N''),
        [UsuarioSolicitanteId] INT NOT NULL,
        [FechaSolicitudUtc] DATETIME2(0) NOT NULL,
        [FechaRespuestaIAUtc] DATETIME2(0) NULL,
        [Estado] NVARCHAR(40) NOT NULL,
        [ModeloGemini] NVARCHAR(80) NOT NULL,
        [ObservacionUsuario] NVARCHAR(1000) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_observacion] DEFAULT(N''),
        [ImagenValida] BIT NOT NULL
            CONSTRAINT [DF_diagnosticoIA_imagenValida] DEFAULT(0),
        [ParecePlantaCafe] BIT NOT NULL
            CONSTRAINT [DF_diagnosticoIA_pareceCafe] DEFAULT(0),
        [ResultadoConcluyente] BIT NOT NULL
            CONSTRAINT [DF_diagnosticoIA_concluyente] DEFAULT(0),
        [PosibleDanoNoBiotico] BIT NOT NULL
            CONSTRAINT [DF_diagnosticoIA_noBiotico] DEFAULT(0),
        [DiagnosticoSugerido] NVARCHAR(300) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_diagnostico] DEFAULT(N''),
        [NivelCoincidencia] NVARCHAR(30) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_nivel] DEFAULT(N'NO_DETERMINADO'),
        [Resumen] NVARCHAR(2000) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_resumen] DEFAULT(N''),
        [PosibleCausaNoBiotica] NVARCHAR(500) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_causaNoBiotica] DEFAULT(N''),
        [SintomasVisiblesJson] NVARCHAR(MAX) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_sintomas] DEFAULT(N'[]'),
        [DiagnosticosAlternativosJson] NVARCHAR(MAX) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_alternativos] DEFAULT(N'[]'),
        [RecomendacionesCapturaJson] NVARCHAR(MAX) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_captura] DEFAULT(N'[]'),
        [AdvertenciasJson] NVARCHAR(MAX) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_advertencias] DEFAULT(N'[]'),
        [RespuestaOriginalJson] NVARCHAR(MAX) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_respuesta] DEFAULT(N''),
        [ErrorAnalisis] NVARCHAR(2000) NOT NULL
            CONSTRAINT [DF_diagnosticoIA_error] DEFAULT(N''),
        [RequiereValidacionHumana] BIT NOT NULL
            CONSTRAINT [DF_diagnosticoIA_validacion] DEFAULT(1),
        [Activo] BIT NOT NULL
            CONSTRAINT [DF_diagnosticoIA_activo] DEFAULT(1),

        CONSTRAINT [PK_diagnosticoIA]
            PRIMARY KEY CLUSTERED ([DiagnosticoIAId]),

        CONSTRAINT [FK_diagnosticoIA_terreno]
            FOREIGN KEY ([TerrenoId])
            REFERENCES [dbo].[terreno]([terrenoId]),

        CONSTRAINT [FK_diagnosticoIA_usuarioSolicitante]
            FOREIGN KEY ([UsuarioSolicitanteId])
            REFERENCES [dbo].[usuario]([UsuarioId])
    );

    CREATE INDEX [IX_diagnosticoIA_usuarioFecha]
        ON [dbo].[diagnosticoIA]
        ([UsuarioSolicitanteId], [FechaSolicitudUtc] DESC);

    CREATE INDEX [IX_diagnosticoIA_estadoFecha]
        ON [dbo].[diagnosticoIA]
        ([Estado], [Activo], [FechaSolicitudUtc] DESC);
END;

IF OBJECT_ID(N'[dbo].[diagnosticoIAImagen]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAImagen]
    (
        [DiagnosticoIAImagenId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAId] INT NOT NULL,
        [UrlImagen] NVARCHAR(1000) NOT NULL,
        [RutaRelativa] NVARCHAR(600) NOT NULL,
        [NombreArchivoOriginal] NVARCHAR(255) NOT NULL
            CONSTRAINT [DF_diagnosticoIAImagen_nombre] DEFAULT(N''),
        [TipoFotografia] NVARCHAR(40) NOT NULL
            CONSTRAINT [DF_diagnosticoIAImagen_tipo] DEFAULT(N'EVIDENCIA'),
        [Orden] INT NOT NULL,
        [FechaRegistroUtc] DATETIME2(0) NOT NULL,

        CONSTRAINT [PK_diagnosticoIAImagen]
            PRIMARY KEY CLUSTERED ([DiagnosticoIAImagenId]),

        CONSTRAINT [FK_diagnosticoIAImagen_diagnostico]
            FOREIGN KEY ([DiagnosticoIAId])
            REFERENCES [dbo].[diagnosticoIA]([DiagnosticoIAId])
            ON DELETE CASCADE
    );

    CREATE INDEX [IX_diagnosticoIAImagen_orden]
        ON [dbo].[diagnosticoIAImagen]
        ([DiagnosticoIAId], [Orden]);
END;

IF OBJECT_ID(N'[dbo].[diagnosticoIAValidacion]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAValidacion]
    (
        [DiagnosticoIAValidacionId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAId] INT NOT NULL,
        [UsuarioClasificadorId] INT NOT NULL,
        [Decision] NVARCHAR(30) NOT NULL,
        [DiagnosticoFinal] NVARCHAR(300) NOT NULL
            CONSTRAINT [DF_diagnosticoIAValidacion_final] DEFAULT(N''),
        [CoincideConGemini] BIT NULL,
        [Observaciones] NVARCHAR(2000) NOT NULL
            CONSTRAINT [DF_diagnosticoIAValidacion_observaciones] DEFAULT(N''),
        [FechaValidacionUtc] DATETIME2(0) NOT NULL,

        CONSTRAINT [PK_diagnosticoIAValidacion]
            PRIMARY KEY CLUSTERED ([DiagnosticoIAValidacionId]),

        CONSTRAINT [FK_diagnosticoIAValidacion_diagnostico]
            FOREIGN KEY ([DiagnosticoIAId])
            REFERENCES [dbo].[diagnosticoIA]([DiagnosticoIAId])
            ON DELETE CASCADE,

        CONSTRAINT [FK_diagnosticoIAValidacion_usuario]
            FOREIGN KEY ([UsuarioClasificadorId])
            REFERENCES [dbo].[usuario]([UsuarioId])
    );

    CREATE INDEX [IX_diagnosticoIAValidacion_fecha]
        ON [dbo].[diagnosticoIAValidacion]
        ([DiagnosticoIAId], [FechaValidacionUtc] DESC);
END;

IF OBJECT_ID(N'[dbo].[diagnosticoIARevision]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIARevision]
    (
        [DiagnosticoIARevisionId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAId] INT NOT NULL,
        [UsuarioClasificadorId] INT NOT NULL,
        [RetroalimentacionClasificador] NVARCHAR(2000) NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_retroalimentacion] DEFAULT(N''),
        [DiagnosticoPropuestoClasificador] NVARCHAR(300) NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_propuesto] DEFAULT(N''),
        [FechaSolicitudRevisionUtc] DATETIME2(0) NOT NULL,
        [FechaRespuestaRevisionUtc] DATETIME2(0) NULL,
        [Estado] NVARCHAR(30) NOT NULL,
        [ImagenValida] BIT NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_imagenValida] DEFAULT(0),
        [ResultadoConcluyente] BIT NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_concluyente] DEFAULT(0),
        [MantieneVeredictoOriginal] BIT NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_mantiene] DEFAULT(0),
        [RelacionConCriterioTecnico] NVARCHAR(30) NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_relacion] DEFAULT(N'NO_EVALUABLE'),
        [DiagnosticoRevisado] NVARCHAR(300) NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_diagnostico] DEFAULT(N''),
        [NivelCoincidencia] NVARCHAR(30) NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_nivel] DEFAULT(N'NO_DETERMINADO'),
        [ResumenRevision] NVARCHAR(2000) NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_resumen] DEFAULT(N''),
        [EvidenciasApoyoJson] NVARCHAR(MAX) NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_apoyo] DEFAULT(N'[]'),
        [EvidenciasContradiccionJson] NVARCHAR(MAX) NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_contradiccion] DEFAULT(N'[]'),
        [InformacionFaltanteJson] NVARCHAR(MAX) NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_faltante] DEFAULT(N'[]'),
        [RecomendacionesCapturaJson] NVARCHAR(MAX) NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_captura] DEFAULT(N'[]'),
        [AdvertenciasJson] NVARCHAR(MAX) NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_advertencias] DEFAULT(N'[]'),
        [RespuestaOriginalJson] NVARCHAR(MAX) NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_respuesta] DEFAULT(N''),
        [ErrorRevision] NVARCHAR(2000) NOT NULL
            CONSTRAINT [DF_diagnosticoIARevision_error] DEFAULT(N''),

        CONSTRAINT [PK_diagnosticoIARevision]
            PRIMARY KEY CLUSTERED ([DiagnosticoIARevisionId]),

        CONSTRAINT [FK_diagnosticoIARevision_diagnostico]
            FOREIGN KEY ([DiagnosticoIAId])
            REFERENCES [dbo].[diagnosticoIA]([DiagnosticoIAId])
            ON DELETE CASCADE,

        CONSTRAINT [FK_diagnosticoIARevision_usuario]
            FOREIGN KEY ([UsuarioClasificadorId])
            REFERENCES [dbo].[usuario]([UsuarioId])
    );

    CREATE INDEX [IX_diagnosticoIARevision_fecha]
        ON [dbo].[diagnosticoIARevision]
        ([DiagnosticoIAId], [FechaSolicitudRevisionUtc] DESC);
END;

MERGE [dbo].[interfaz] AS destino
USING
(
    SELECT
        N'diagnosticoIAPage' AS [NombreInterfaz],
        N'Diagnóstico de enfermedades con IA' AS [NombreAmigable],
        N'Solicitud de análisis visual con Gemini y validación humana por permisos.' AS [Descripcion]
) AS origen
ON destino.[nombreInterfaz] = origen.[NombreInterfaz]
WHEN MATCHED THEN
    UPDATE SET
        destino.[nombreAmigableInterfaz] = origen.[NombreAmigable],
        destino.[descripcionInterfaz] = origen.[Descripcion],
        destino.[activo] = 1
WHEN NOT MATCHED THEN
    INSERT
    (
        [nombreInterfaz],
        [nombreAmigableInterfaz],
        [descripcionInterfaz],
        [activo]
    )
    VALUES
    (
        origen.[NombreInterfaz],
        origen.[NombreAmigable],
        origen.[Descripcion],
        1
    );

/*
 * El Administrador recibe acceso completo por compatibilidad con el
 * comportamiento actual del sistema. Los demás roles se configuran desde
 * la matriz de permisos: Agregar = solicitar; Actualizar = clasificar.
 */
DECLARE @InterfazDiagnosticoId INT =
(
    SELECT TOP (1) [interfazId]
    FROM [dbo].[interfaz]
    WHERE [nombreInterfaz] = N'diagnosticoIAPage'
);

MERGE [dbo].[rolInterfaz] AS destino
USING
(
    SELECT
        [rolId],
        @InterfazDiagnosticoId AS [interfazId]
    FROM [dbo].[Rol]
    WHERE [activo] = 1
      AND UPPER(LTRIM(RTRIM([nombreRol]))) = N'ADMINISTRADOR'
) AS origen
ON destino.[rolId] = origen.[rolId]
   AND destino.[interfazId] = origen.[interfazId]
WHEN MATCHED THEN
    UPDATE SET
        destino.[leer] = 1,
        destino.[agregar] = 1,
        destino.[actualizar] = 1,
        destino.[eliminar] = 1
WHEN NOT MATCHED AND origen.[interfazId] IS NOT NULL THEN
    INSERT
    (
        [rolId],
        [interfazId],
        [leer],
        [agregar],
        [actualizar],
        [eliminar]
    )
    VALUES
    (
        origen.[rolId],
        origen.[interfazId],
        1,
        1,
        1,
        1
    );
""";

            await db.Database.ExecuteSqlRawAsync(
                sql,
                cancellationToken);
        }
    }
}
