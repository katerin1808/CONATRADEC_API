using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Instala y actualiza de forma idempotente la base del módulo. Se ejecuta
    /// durante el arranque y no requiere scripts manuales ni migraciones.
    /// </summary>
    public sealed class DiagnosticoIADatabaseInitializer
    {
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
        [CodigoTerreno] NVARCHAR(50) NOT NULL CONSTRAINT [DF_diagIA_codigo] DEFAULT(N''),
        [UsuarioSolicitanteId] INT NOT NULL,
        [FechaSolicitudUtc] DATETIME2(0) NOT NULL,
        [FechaRespuestaIAUtc] DATETIME2(0) NULL,
        [Estado] NVARCHAR(40) NOT NULL,
        [ModeloGemini] NVARCHAR(80) NOT NULL,
        [ObservacionUsuario] NVARCHAR(1000) NOT NULL CONSTRAINT [DF_diagIA_observacion] DEFAULT(N''),
        [ImagenValida] BIT NOT NULL CONSTRAINT [DF_diagIA_imagenValida] DEFAULT(0),
        [ParecePlantaCafe] BIT NOT NULL CONSTRAINT [DF_diagIA_pareceCafe] DEFAULT(0),
        [ResultadoConcluyente] BIT NOT NULL CONSTRAINT [DF_diagIA_concluyente] DEFAULT(0),
        [CalidadEvaluacionIA] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIA_calidad] DEFAULT(N'NO_EVALUABLE'),
        [EstadoGeneralIA] NVARCHAR(40) NOT NULL CONSTRAINT [DF_diagIA_estadoGeneral] DEFAULT(N'INDETERMINADA'),
        [CategoriaPrincipalIA] NVARCHAR(50) NOT NULL CONSTRAINT [DF_diagIA_categoria] DEFAULT(N'NO_APLICA'),
        [CategoriasSecundariasIAJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIA_categoriasSec] DEFAULT(N'[]'),
        [DiagnosticoSugerido] NVARCHAR(300) NOT NULL CONSTRAINT [DF_diagIA_diagnostico] DEFAULT(N''),
        [TipoDiagnosticoIA] NVARCHAR(80) NOT NULL CONSTRAINT [DF_diagIA_tipo] DEFAULT(N''),
        [SeveridadVisualIA] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIA_severidad] DEFAULT(N'NO_EVALUABLE'),
        [NivelCoincidencia] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIA_certeza] DEFAULT(N'NO_DETERMINADO'),
        [Resumen] NVARCHAR(2000) NOT NULL CONSTRAINT [DF_diagIA_resumen] DEFAULT(N''),
        [PartesAfectadasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIA_partes] DEFAULT(N'[]'),
        [SintomasVisiblesJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIA_sintomas] DEFAULT(N'[]'),
        [EvidenciasNoObservadasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIA_noObservadas] DEFAULT(N'[]'),
        [DiagnosticosAlternativosJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIA_alternativos] DEFAULT(N'[]'),
        [InformacionFaltanteJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIA_faltante] DEFAULT(N'[]'),
        [RecomendacionesCapturaJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIA_captura] DEFAULT(N'[]'),
        [AdvertenciasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIA_advertencias] DEFAULT(N'[]'),
        [PosibleDanoNoBiotico] BIT NOT NULL CONSTRAINT [DF_diagIA_noBiotico] DEFAULT(0),
        [PosibleCausaNoBiotica] NVARCHAR(500) NOT NULL CONSTRAINT [DF_diagIA_causaNoBiotica] DEFAULT(N''),
        [RespuestaOriginalJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIA_respuesta] DEFAULT(N''),
        [ErrorAnalisis] NVARCHAR(2000) NOT NULL CONSTRAINT [DF_diagIA_error] DEFAULT(N''),
        [RequiereValidacionHumana] BIT NOT NULL CONSTRAINT [DF_diagIA_requiereHumano] DEFAULT(1),
        [Activo] BIT NOT NULL CONSTRAINT [DF_diagIA_activo] DEFAULT(1),
        CONSTRAINT [PK_diagnosticoIA] PRIMARY KEY CLUSTERED ([DiagnosticoIAId]),
        CONSTRAINT [FK_diagIA_terreno] FOREIGN KEY ([TerrenoId]) REFERENCES [dbo].[terreno]([terrenoId]),
        CONSTRAINT [FK_diagIA_usuario] FOREIGN KEY ([UsuarioSolicitanteId]) REFERENCES [dbo].[usuario]([UsuarioId])
    );
END;

IF COL_LENGTH(N'dbo.diagnosticoIA', N'CalidadEvaluacionIA') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA] ADD [CalidadEvaluacionIA] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIA_calidad_v2] DEFAULT(N'NO_EVALUABLE');
IF COL_LENGTH(N'dbo.diagnosticoIA', N'EstadoGeneralIA') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA] ADD [EstadoGeneralIA] NVARCHAR(40) NOT NULL CONSTRAINT [DF_diagIA_estadoGeneral_v2] DEFAULT(N'INDETERMINADA');
IF COL_LENGTH(N'dbo.diagnosticoIA', N'CategoriaPrincipalIA') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA] ADD [CategoriaPrincipalIA] NVARCHAR(50) NOT NULL CONSTRAINT [DF_diagIA_categoria_v2] DEFAULT(N'NO_APLICA');
IF COL_LENGTH(N'dbo.diagnosticoIA', N'CategoriasSecundariasIAJson') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA] ADD [CategoriasSecundariasIAJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIA_catSec_v2] DEFAULT(N'[]');
IF COL_LENGTH(N'dbo.diagnosticoIA', N'TipoDiagnosticoIA') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA] ADD [TipoDiagnosticoIA] NVARCHAR(80) NOT NULL CONSTRAINT [DF_diagIA_tipo_v2] DEFAULT(N'');
IF COL_LENGTH(N'dbo.diagnosticoIA', N'SeveridadVisualIA') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA] ADD [SeveridadVisualIA] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIA_sev_v2] DEFAULT(N'NO_EVALUABLE');
IF COL_LENGTH(N'dbo.diagnosticoIA', N'PartesAfectadasJson') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA] ADD [PartesAfectadasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIA_partes_v2] DEFAULT(N'[]');
IF COL_LENGTH(N'dbo.diagnosticoIA', N'EvidenciasNoObservadasJson') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA] ADD [EvidenciasNoObservadasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIA_noObs_v2] DEFAULT(N'[]');
IF COL_LENGTH(N'dbo.diagnosticoIA', N'InformacionFaltanteJson') IS NULL
    ALTER TABLE [dbo].[diagnosticoIA] ADD [InformacionFaltanteJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIA_faltante_v2] DEFAULT(N'[]');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_diagnosticoIA_usuarioFecha' AND object_id = OBJECT_ID(N'[dbo].[diagnosticoIA]'))
    CREATE INDEX [IX_diagnosticoIA_usuarioFecha] ON [dbo].[diagnosticoIA]([UsuarioSolicitanteId], [FechaSolicitudUtc] DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_diagnosticoIA_estadoFecha' AND object_id = OBJECT_ID(N'[dbo].[diagnosticoIA]'))
    CREATE INDEX [IX_diagnosticoIA_estadoFecha] ON [dbo].[diagnosticoIA]([Estado], [Activo], [FechaSolicitudUtc] DESC);

IF OBJECT_ID(N'[dbo].[diagnosticoIAImagen]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAImagen]
    (
        [DiagnosticoIAImagenId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAId] INT NOT NULL,
        [UrlImagen] NVARCHAR(1000) NOT NULL,
        [RutaRelativa] NVARCHAR(600) NOT NULL,
        [NombreArchivoOriginal] NVARCHAR(255) NOT NULL CONSTRAINT [DF_diagIAImg_nombre] DEFAULT(N''),
        [TipoFotografia] NVARCHAR(40) NOT NULL CONSTRAINT [DF_diagIAImg_tipo] DEFAULT(N'EVIDENCIA'),
        [Orden] INT NOT NULL,
        [FechaRegistroUtc] DATETIME2(0) NOT NULL,
        CONSTRAINT [PK_diagnosticoIAImagen] PRIMARY KEY CLUSTERED ([DiagnosticoIAImagenId]),
        CONSTRAINT [FK_diagIAImg_diag] FOREIGN KEY ([DiagnosticoIAId]) REFERENCES [dbo].[diagnosticoIA]([DiagnosticoIAId]) ON DELETE CASCADE
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_diagnosticoIAImagen_orden' AND object_id = OBJECT_ID(N'[dbo].[diagnosticoIAImagen]'))
    CREATE INDEX [IX_diagnosticoIAImagen_orden] ON [dbo].[diagnosticoIAImagen]([DiagnosticoIAId], [Orden]);

IF OBJECT_ID(N'[dbo].[diagnosticoIAImagenResultadoIA]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAImagenResultadoIA]
    (
        [DiagnosticoIAImagenResultadoIAId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAImagenId] INT NOT NULL,
        [ImagenValida] BIT NOT NULL CONSTRAINT [DF_diagIAImgRes_valida] DEFAULT(0),
        [ParecePlantaCafe] BIT NOT NULL CONSTRAINT [DF_diagIAImgRes_cafe] DEFAULT(0),
        [ResultadoConcluyente] BIT NOT NULL CONSTRAINT [DF_diagIAImgRes_concluyente] DEFAULT(0),
        [PartePlanta] NVARCHAR(80) NOT NULL CONSTRAINT [DF_diagIAImgRes_parte] DEFAULT(N''),
        [CalidadEvaluacion] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIAImgRes_calidad] DEFAULT(N'NO_EVALUABLE'),
        [EstadoGeneral] NVARCHAR(40) NOT NULL CONSTRAINT [DF_diagIAImgRes_estado] DEFAULT(N'INDETERMINADA'),
        [CategoriaPrincipal] NVARCHAR(50) NOT NULL CONSTRAINT [DF_diagIAImgRes_categoria] DEFAULT(N'NO_APLICA'),
        [CategoriasSecundariasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIAImgRes_catSec] DEFAULT(N'[]'),
        [DiagnosticoProbable] NVARCHAR(300) NOT NULL CONSTRAINT [DF_diagIAImgRes_diag] DEFAULT(N''),
        [TipoDiagnostico] NVARCHAR(80) NOT NULL CONSTRAINT [DF_diagIAImgRes_tipo] DEFAULT(N''),
        [SeveridadVisual] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIAImgRes_sev] DEFAULT(N'NO_EVALUABLE'),
        [NivelCerteza] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIAImgRes_certeza] DEFAULT(N'NO_DETERMINADO'),
        [CategoriaAlbumBotanicoIdSugerida] INT NULL,
        [AlbumBotanicoCafeIdSugerido] INT NULL,
        [CategoriaAlbumSugerida] NVARCHAR(150) NOT NULL CONSTRAINT [DF_diagIAImgRes_catAlbumSug] DEFAULT(N''),
        [ClasificacionAlbumSugerida] NVARCHAR(200) NOT NULL CONSTRAINT [DF_diagIAImgRes_clasAlbumSug] DEFAULT(N''),
        [NombreCientificoSugerido] NVARCHAR(200) NOT NULL CONSTRAINT [DF_diagIAImgRes_cientificoSug] DEFAULT(N''),
        [CoincideCatalogoAlbum] BIT NOT NULL CONSTRAINT [DF_diagIAImgRes_coincideAlbum] DEFAULT(0),
        [RequiereDecisionClasificacion] BIT NOT NULL CONSTRAINT [DF_diagIAImgRes_reqDecisionAlbum] DEFAULT(0),
        [MotivoClasificacionAlbum] NVARCHAR(1000) NOT NULL CONSTRAINT [DF_diagIAImgRes_motivoAlbum] DEFAULT(N''),
        [CategoriaAlbumBotanicoIdSeleccionada] INT NULL,
        [AlbumBotanicoCafeIdSeleccionado] INT NULL,
        [CategoriaAlbumSeleccionada] NVARCHAR(150) NOT NULL CONSTRAINT [DF_diagIAImgRes_catAlbumSel] DEFAULT(N''),
        [ClasificacionAlbumSeleccionada] NVARCHAR(200) NOT NULL CONSTRAINT [DF_diagIAImgRes_clasAlbumSel] DEFAULT(N''),
        [EstadoClasificacionAlbum] NVARCHAR(40) NOT NULL CONSTRAINT [DF_diagIAImgRes_estadoAlbum] DEFAULT(N'NO_APLICA'),
        [ResumenImagen] NVARCHAR(1600) NOT NULL CONSTRAINT [DF_diagIAImgRes_resumen] DEFAULT(N''),
        [SintomasVisiblesJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIAImgRes_sintomas] DEFAULT(N'[]'),
        [EvidenciasObservadasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIAImgRes_evidencias] DEFAULT(N'[]'),
        [EvidenciasNoObservadasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIAImgRes_noObs] DEFAULT(N'[]'),
        [DiagnosticosAlternativosJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIAImgRes_alternativos] DEFAULT(N'[]'),
        [InformacionFaltanteJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIAImgRes_faltante] DEFAULT(N'[]'),
        [RecomendacionesCapturaJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIAImgRes_captura] DEFAULT(N'[]'),
        [AdvertenciasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIAImgRes_advertencias] DEFAULT(N'[]'),
        [FechaResultadoUtc] DATETIME2(0) NOT NULL,
        CONSTRAINT [PK_diagnosticoIAImagenResultadoIA] PRIMARY KEY CLUSTERED ([DiagnosticoIAImagenResultadoIAId]),
        CONSTRAINT [FK_diagIAImgRes_img] FOREIGN KEY ([DiagnosticoIAImagenId])
            REFERENCES [dbo].[diagnosticoIAImagen]([DiagnosticoIAImagenId]) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX [UX_diagIAImgRes_imagen]
        ON [dbo].[diagnosticoIAImagenResultadoIA]([DiagnosticoIAImagenId]);
END;

IF COL_LENGTH(N'dbo.diagnosticoIAImagenResultadoIA', N'CategoriaAlbumBotanicoIdSugerida') IS NULL
    ALTER TABLE [dbo].[diagnosticoIAImagenResultadoIA] ADD [CategoriaAlbumBotanicoIdSugerida] INT NULL;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenResultadoIA', N'AlbumBotanicoCafeIdSugerido') IS NULL
    ALTER TABLE [dbo].[diagnosticoIAImagenResultadoIA] ADD [AlbumBotanicoCafeIdSugerido] INT NULL;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenResultadoIA', N'CategoriaAlbumSugerida') IS NULL
    ALTER TABLE [dbo].[diagnosticoIAImagenResultadoIA] ADD [CategoriaAlbumSugerida] NVARCHAR(150) NOT NULL CONSTRAINT [DF_diagIAImgRes_catAlbumSug_v2] DEFAULT(N'');
IF COL_LENGTH(N'dbo.diagnosticoIAImagenResultadoIA', N'ClasificacionAlbumSugerida') IS NULL
    ALTER TABLE [dbo].[diagnosticoIAImagenResultadoIA] ADD [ClasificacionAlbumSugerida] NVARCHAR(200) NOT NULL CONSTRAINT [DF_diagIAImgRes_clasAlbumSug_v2] DEFAULT(N'');
IF COL_LENGTH(N'dbo.diagnosticoIAImagenResultadoIA', N'NombreCientificoSugerido') IS NULL
    ALTER TABLE [dbo].[diagnosticoIAImagenResultadoIA] ADD [NombreCientificoSugerido] NVARCHAR(200) NOT NULL CONSTRAINT [DF_diagIAImgRes_cientificoSug_v2] DEFAULT(N'');
IF COL_LENGTH(N'dbo.diagnosticoIAImagenResultadoIA', N'CoincideCatalogoAlbum') IS NULL
    ALTER TABLE [dbo].[diagnosticoIAImagenResultadoIA] ADD [CoincideCatalogoAlbum] BIT NOT NULL CONSTRAINT [DF_diagIAImgRes_coincideAlbum_v2] DEFAULT(0);
IF COL_LENGTH(N'dbo.diagnosticoIAImagenResultadoIA', N'RequiereDecisionClasificacion') IS NULL
    ALTER TABLE [dbo].[diagnosticoIAImagenResultadoIA] ADD [RequiereDecisionClasificacion] BIT NOT NULL CONSTRAINT [DF_diagIAImgRes_reqDecisionAlbum_v2] DEFAULT(0);
IF COL_LENGTH(N'dbo.diagnosticoIAImagenResultadoIA', N'MotivoClasificacionAlbum') IS NULL
    ALTER TABLE [dbo].[diagnosticoIAImagenResultadoIA] ADD [MotivoClasificacionAlbum] NVARCHAR(1000) NOT NULL CONSTRAINT [DF_diagIAImgRes_motivoAlbum_v2] DEFAULT(N'');
IF COL_LENGTH(N'dbo.diagnosticoIAImagenResultadoIA', N'CategoriaAlbumBotanicoIdSeleccionada') IS NULL
    ALTER TABLE [dbo].[diagnosticoIAImagenResultadoIA] ADD [CategoriaAlbumBotanicoIdSeleccionada] INT NULL;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenResultadoIA', N'AlbumBotanicoCafeIdSeleccionado') IS NULL
    ALTER TABLE [dbo].[diagnosticoIAImagenResultadoIA] ADD [AlbumBotanicoCafeIdSeleccionado] INT NULL;
IF COL_LENGTH(N'dbo.diagnosticoIAImagenResultadoIA', N'CategoriaAlbumSeleccionada') IS NULL
    ALTER TABLE [dbo].[diagnosticoIAImagenResultadoIA] ADD [CategoriaAlbumSeleccionada] NVARCHAR(150) NOT NULL CONSTRAINT [DF_diagIAImgRes_catAlbumSel_v2] DEFAULT(N'');
IF COL_LENGTH(N'dbo.diagnosticoIAImagenResultadoIA', N'ClasificacionAlbumSeleccionada') IS NULL
    ALTER TABLE [dbo].[diagnosticoIAImagenResultadoIA] ADD [ClasificacionAlbumSeleccionada] NVARCHAR(200) NOT NULL CONSTRAINT [DF_diagIAImgRes_clasAlbumSel_v2] DEFAULT(N'');
IF COL_LENGTH(N'dbo.diagnosticoIAImagenResultadoIA', N'EstadoClasificacionAlbum') IS NULL
    ALTER TABLE [dbo].[diagnosticoIAImagenResultadoIA] ADD [EstadoClasificacionAlbum] NVARCHAR(40) NOT NULL CONSTRAINT [DF_diagIAImgRes_estadoAlbum_v2] DEFAULT(N'NO_APLICA');

IF OBJECT_ID(N'[dbo].[diagnosticoIAValidacion]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAValidacion]
    (
        [DiagnosticoIAValidacionId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAId] INT NOT NULL,
        [UsuarioClasificadorId] INT NOT NULL,
        [Decision] NVARCHAR(30) NOT NULL,
        [DiagnosticoFinal] NVARCHAR(300) NOT NULL CONSTRAINT [DF_diagIAVal_final] DEFAULT(N''),
        [CoincideConGemini] BIT NULL,
        [Observaciones] NVARCHAR(2000) NOT NULL CONSTRAINT [DF_diagIAVal_obs] DEFAULT(N''),
        [FechaValidacionUtc] DATETIME2(0) NOT NULL,
        CONSTRAINT [PK_diagnosticoIAValidacion] PRIMARY KEY CLUSTERED ([DiagnosticoIAValidacionId]),
        CONSTRAINT [FK_diagIAVal_diag] FOREIGN KEY ([DiagnosticoIAId]) REFERENCES [dbo].[diagnosticoIA]([DiagnosticoIAId]) ON DELETE CASCADE,
        CONSTRAINT [FK_diagIAVal_usuario] FOREIGN KEY ([UsuarioClasificadorId]) REFERENCES [dbo].[usuario]([UsuarioId])
    );
END;
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_diagnosticoIAValidacion_fecha'
      AND object_id = OBJECT_ID(N'[dbo].[diagnosticoIAValidacion]')
)
    CREATE INDEX [IX_diagnosticoIAValidacion_fecha]
        ON [dbo].[diagnosticoIAValidacion]
        ([DiagnosticoIAId], [FechaValidacionUtc] DESC);

IF OBJECT_ID(N'[dbo].[diagnosticoIARevision]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIARevision]
    (
        [DiagnosticoIARevisionId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAId] INT NOT NULL,
        [UsuarioClasificadorId] INT NOT NULL,
        [RetroalimentacionClasificador] NVARCHAR(2000) NOT NULL CONSTRAINT [DF_diagIARev_retro] DEFAULT(N''),
        [DiagnosticoPropuestoClasificador] NVARCHAR(300) NOT NULL CONSTRAINT [DF_diagIARev_prop] DEFAULT(N''),
        [FechaSolicitudRevisionUtc] DATETIME2(0) NOT NULL,
        [FechaRespuestaRevisionUtc] DATETIME2(0) NULL,
        [Estado] NVARCHAR(30) NOT NULL,
        [ImagenValida] BIT NOT NULL CONSTRAINT [DF_diagIARev_img] DEFAULT(0),
        [ResultadoConcluyente] BIT NOT NULL CONSTRAINT [DF_diagIARev_conc] DEFAULT(0),
        [MantieneVeredictoOriginal] BIT NOT NULL CONSTRAINT [DF_diagIARev_mant] DEFAULT(0),
        [RelacionConCriterioTecnico] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIARev_rel] DEFAULT(N'NO_EVALUABLE'),
        [CalidadEvaluacion] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIARev_calidad] DEFAULT(N'NO_EVALUABLE'),
        [EstadoGeneral] NVARCHAR(40) NOT NULL CONSTRAINT [DF_diagIARev_estadoGeneral] DEFAULT(N'INDETERMINADA'),
        [CategoriaPrincipal] NVARCHAR(50) NOT NULL CONSTRAINT [DF_diagIARev_categoria] DEFAULT(N'NO_APLICA'),
        [CategoriasSecundariasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIARev_catSec] DEFAULT(N'[]'),
        [DiagnosticoRevisado] NVARCHAR(300) NOT NULL CONSTRAINT [DF_diagIARev_diag] DEFAULT(N''),
        [TipoDiagnostico] NVARCHAR(80) NOT NULL CONSTRAINT [DF_diagIARev_tipo] DEFAULT(N''),
        [SeveridadVisual] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIARev_sev] DEFAULT(N'NO_EVALUABLE'),
        [NivelCoincidencia] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIARev_nivel] DEFAULT(N'NO_DETERMINADO'),
        [ResumenRevision] NVARCHAR(2000) NOT NULL CONSTRAINT [DF_diagIARev_res] DEFAULT(N''),
        [PartesAfectadasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIARev_partes] DEFAULT(N'[]'),
        [EvidenciasApoyoJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIARev_apoyo] DEFAULT(N'[]'),
        [EvidenciasContradiccionJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIARev_contra] DEFAULT(N'[]'),
        [InformacionFaltanteJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIARev_faltante] DEFAULT(N'[]'),
        [RecomendacionesCapturaJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIARev_captura] DEFAULT(N'[]'),
        [AdvertenciasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIARev_adv] DEFAULT(N'[]'),
        [RespuestaOriginalJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIARev_resp] DEFAULT(N''),
        [ErrorRevision] NVARCHAR(2000) NOT NULL CONSTRAINT [DF_diagIARev_error] DEFAULT(N''),
        CONSTRAINT [PK_diagnosticoIARevision] PRIMARY KEY CLUSTERED ([DiagnosticoIARevisionId]),
        CONSTRAINT [FK_diagIARev_diag] FOREIGN KEY ([DiagnosticoIAId]) REFERENCES [dbo].[diagnosticoIA]([DiagnosticoIAId]) ON DELETE CASCADE,
        CONSTRAINT [FK_diagIARev_usuario] FOREIGN KEY ([UsuarioClasificadorId]) REFERENCES [dbo].[usuario]([UsuarioId])
    );
END;
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_diagnosticoIARevision_fecha'
      AND object_id = OBJECT_ID(N'[dbo].[diagnosticoIARevision]')
)
    CREATE INDEX [IX_diagnosticoIARevision_fecha]
        ON [dbo].[diagnosticoIARevision]
        ([DiagnosticoIAId], [FechaSolicitudRevisionUtc] DESC);

IF COL_LENGTH(N'dbo.diagnosticoIARevision', N'CalidadEvaluacion') IS NULL
    ALTER TABLE [dbo].[diagnosticoIARevision] ADD [CalidadEvaluacion] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIARev_calidad_v2] DEFAULT(N'NO_EVALUABLE');
IF COL_LENGTH(N'dbo.diagnosticoIARevision', N'EstadoGeneral') IS NULL
    ALTER TABLE [dbo].[diagnosticoIARevision] ADD [EstadoGeneral] NVARCHAR(40) NOT NULL CONSTRAINT [DF_diagIARev_estado_v2] DEFAULT(N'INDETERMINADA');
IF COL_LENGTH(N'dbo.diagnosticoIARevision', N'CategoriaPrincipal') IS NULL
    ALTER TABLE [dbo].[diagnosticoIARevision] ADD [CategoriaPrincipal] NVARCHAR(50) NOT NULL CONSTRAINT [DF_diagIARev_cat_v2] DEFAULT(N'NO_APLICA');
IF COL_LENGTH(N'dbo.diagnosticoIARevision', N'CategoriasSecundariasJson') IS NULL
    ALTER TABLE [dbo].[diagnosticoIARevision] ADD [CategoriasSecundariasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIARev_catSec_v2] DEFAULT(N'[]');
IF COL_LENGTH(N'dbo.diagnosticoIARevision', N'TipoDiagnostico') IS NULL
    ALTER TABLE [dbo].[diagnosticoIARevision] ADD [TipoDiagnostico] NVARCHAR(80) NOT NULL CONSTRAINT [DF_diagIARev_tipo_v2] DEFAULT(N'');
IF COL_LENGTH(N'dbo.diagnosticoIARevision', N'SeveridadVisual') IS NULL
    ALTER TABLE [dbo].[diagnosticoIARevision] ADD [SeveridadVisual] NVARCHAR(30) NOT NULL CONSTRAINT [DF_diagIARev_sev_v2] DEFAULT(N'NO_EVALUABLE');
IF COL_LENGTH(N'dbo.diagnosticoIARevision', N'PartesAfectadasJson') IS NULL
    ALTER TABLE [dbo].[diagnosticoIARevision] ADD [PartesAfectadasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIARev_partes_v2] DEFAULT(N'[]');

IF OBJECT_ID(N'[dbo].[diagnosticoIAAnalisisHumano]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAAnalisisHumano]
    (
        [DiagnosticoIAAnalisisHumanoId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAId] INT NOT NULL,
        [UsuarioAnalizadorId] INT NOT NULL,
        [Version] INT NOT NULL,
        [EstadoRegistro] NVARCHAR(30) NOT NULL,
        [CalidadEvaluacion] NVARCHAR(30) NOT NULL,
        [EstadoGeneral] NVARCHAR(40) NOT NULL,
        [CategoriaPrincipal] NVARCHAR(50) NOT NULL,
        [CategoriasSecundariasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIAHum_catSec] DEFAULT(N'[]'),
        [DiagnosticoPropuesto] NVARCHAR(300) NOT NULL CONSTRAINT [DF_diagIAHum_diag] DEFAULT(N''),
        [TipoDiagnostico] NVARCHAR(80) NOT NULL CONSTRAINT [DF_diagIAHum_tipo] DEFAULT(N''),
        [SeveridadPropuesta] NVARCHAR(30) NOT NULL,
        [NivelCerteza] NVARCHAR(30) NOT NULL,
        [PartesAfectadasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIAHum_partes] DEFAULT(N'[]'),
        [EvidenciasObservadasJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIAHum_evidencias] DEFAULT(N'[]'),
        [Observaciones] NVARCHAR(3000) NOT NULL CONSTRAINT [DF_diagIAHum_obs] DEFAULT(N''),
        [FechaCreacionUtc] DATETIME2(0) NOT NULL,
        [FechaActualizacionUtc] DATETIME2(0) NOT NULL,
        [FechaEnvioUtc] DATETIME2(0) NULL,
        CONSTRAINT [PK_diagnosticoIAAnalisisHumano] PRIMARY KEY CLUSTERED ([DiagnosticoIAAnalisisHumanoId]),
        CONSTRAINT [FK_diagIAHum_diag] FOREIGN KEY ([DiagnosticoIAId]) REFERENCES [dbo].[diagnosticoIA]([DiagnosticoIAId]) ON DELETE CASCADE,
        CONSTRAINT [FK_diagIAHum_usuario] FOREIGN KEY ([UsuarioAnalizadorId]) REFERENCES [dbo].[usuario]([UsuarioId])
    );
    CREATE UNIQUE INDEX [UX_diagIAHum_version] ON [dbo].[diagnosticoIAAnalisisHumano]([DiagnosticoIAId], [Version]);
END;

IF OBJECT_ID(N'[dbo].[diagnosticoIAAprobacion]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAAprobacion]
    (
        [DiagnosticoIAAprobacionId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAId] INT NOT NULL,
        [DiagnosticoIAAnalisisHumanoId] INT NOT NULL,
        [UsuarioAprobadorId] INT NOT NULL,
        [Decision] NVARCHAR(40) NOT NULL,
        [CalidadEvaluacionFinal] NVARCHAR(30) NOT NULL,
        [EstadoGeneralFinal] NVARCHAR(40) NOT NULL,
        [CategoriaPrincipalFinal] NVARCHAR(50) NOT NULL,
        [CategoriasSecundariasFinalJson] NVARCHAR(MAX) NOT NULL CONSTRAINT [DF_diagIAAprob_catSec] DEFAULT(N'[]'),
        [DiagnosticoFinal] NVARCHAR(300) NOT NULL CONSTRAINT [DF_diagIAAprob_diag] DEFAULT(N''),
        [TipoDiagnosticoFinal] NVARCHAR(80) NOT NULL CONSTRAINT [DF_diagIAAprob_tipo] DEFAULT(N''),
        [SeveridadFinal] NVARCHAR(30) NOT NULL,
        [NivelCertezaFinal] NVARCHAR(30) NOT NULL,
        [Observaciones] NVARCHAR(3000) NOT NULL CONSTRAINT [DF_diagIAAprob_obs] DEFAULT(N''),
        [AutorizaPublicacionAlbum] BIT NOT NULL CONSTRAINT [DF_diagIAAprob_album] DEFAULT(0),
        [MismoUsuarioQueAnalizo] BIT NOT NULL CONSTRAINT [DF_diagIAAprob_mismo] DEFAULT(0),
        [FechaAprobacionUtc] DATETIME2(0) NOT NULL,
        CONSTRAINT [PK_diagnosticoIAAprobacion] PRIMARY KEY CLUSTERED ([DiagnosticoIAAprobacionId]),
        CONSTRAINT [FK_diagIAAprob_diag] FOREIGN KEY ([DiagnosticoIAId]) REFERENCES [dbo].[diagnosticoIA]([DiagnosticoIAId]) ON DELETE CASCADE,
        CONSTRAINT [FK_diagIAAprob_analisis] FOREIGN KEY ([DiagnosticoIAAnalisisHumanoId]) REFERENCES [dbo].[diagnosticoIAAnalisisHumano]([DiagnosticoIAAnalisisHumanoId]),
        CONSTRAINT [FK_diagIAAprob_usuario] FOREIGN KEY ([UsuarioAprobadorId]) REFERENCES [dbo].[usuario]([UsuarioId])
    );
    CREATE INDEX [IX_diagIAAprob_fecha] ON [dbo].[diagnosticoIAAprobacion]([DiagnosticoIAId], [FechaAprobacionUtc] DESC);
END;

IF OBJECT_ID(N'[dbo].[diagnosticoIAImagenEvaluacion]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAImagenEvaluacion]
    (
        [DiagnosticoIAImagenEvaluacionId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAAprobacionId] INT NOT NULL,
        [DiagnosticoIAImagenId] INT NOT NULL,
        [UsuarioAprobadorId] INT NOT NULL,
        [CalidadTecnica] NVARCHAR(30) NOT NULL,
        [EsEvidenciaValida] BIT NOT NULL,
        [AptaParaAlbum] BIT NOT NULL,
        [Observacion] NVARCHAR(1000) NOT NULL CONSTRAINT [DF_diagIAImgEval_obs] DEFAULT(N''),
        [FechaEvaluacionUtc] DATETIME2(0) NOT NULL,
        CONSTRAINT [PK_diagnosticoIAImagenEvaluacion] PRIMARY KEY CLUSTERED ([DiagnosticoIAImagenEvaluacionId]),
        CONSTRAINT [FK_diagIAImgEval_aprob] FOREIGN KEY ([DiagnosticoIAAprobacionId]) REFERENCES [dbo].[diagnosticoIAAprobacion]([DiagnosticoIAAprobacionId]) ON DELETE CASCADE,
        CONSTRAINT [FK_diagIAImgEval_img] FOREIGN KEY ([DiagnosticoIAImagenId]) REFERENCES [dbo].[diagnosticoIAImagen]([DiagnosticoIAImagenId]),
        CONSTRAINT [FK_diagIAImgEval_usuario] FOREIGN KEY ([UsuarioAprobadorId]) REFERENCES [dbo].[usuario]([UsuarioId])
    );
    CREATE UNIQUE INDEX [UX_diagIAImgEval_aprobImg] ON [dbo].[diagnosticoIAImagenEvaluacion]([DiagnosticoIAAprobacionId], [DiagnosticoIAImagenId]);
END;

IF OBJECT_ID(N'[dbo].[diagnosticoIAAlbumPublicacion]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAAlbumPublicacion]
    (
        [DiagnosticoIAAlbumPublicacionId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAId] INT NOT NULL,
        [DiagnosticoIAImagenId] INT NOT NULL,
        [CategoriaAlbumBotanicoId] INT NOT NULL,
        [AlbumBotanicoCafeId] INT NOT NULL,
        [AlbumBotanicoCafeFotoId] INT NOT NULL,
        [UsuarioPublicacionId] INT NOT NULL,
        [FechaPublicacionUtc] DATETIME2(0) NOT NULL,
        [DescripcionPublicacion] NVARCHAR(1000) NOT NULL CONSTRAINT [DF_diagIAPub_desc] DEFAULT(N''),
        [ClasificacionFinal] NVARCHAR(50) NOT NULL CONSTRAINT [DF_diagIAPub_clas] DEFAULT(N''),
        [DiagnosticoFinal] NVARCHAR(300) NOT NULL CONSTRAINT [DF_diagIAPub_diag] DEFAULT(N''),
        [RutaFotoAlbum] NVARCHAR(600) NOT NULL CONSTRAINT [DF_diagIAPub_ruta] DEFAULT(N''),
        [Activo] BIT NOT NULL CONSTRAINT [DF_diagIAPub_activo] DEFAULT(1),
        CONSTRAINT [PK_diagnosticoIAAlbumPublicacion] PRIMARY KEY CLUSTERED ([DiagnosticoIAAlbumPublicacionId]),
        CONSTRAINT [FK_diagIAPub_diag] FOREIGN KEY ([DiagnosticoIAId]) REFERENCES [dbo].[diagnosticoIA]([DiagnosticoIAId]) ON DELETE CASCADE,
        CONSTRAINT [FK_diagIAPub_img] FOREIGN KEY ([DiagnosticoIAImagenId]) REFERENCES [dbo].[diagnosticoIAImagen]([DiagnosticoIAImagenId]),
        CONSTRAINT [FK_diagIAPub_albumFoto] FOREIGN KEY ([AlbumBotanicoCafeFotoId]) REFERENCES [dbo].[AlbumBotanicoCafeFoto]([albumBotanicoCafeFotoId]),
        CONSTRAINT [FK_diagIAPub_usuario] FOREIGN KEY ([UsuarioPublicacionId]) REFERENCES [dbo].[usuario]([UsuarioId])
    );
    CREATE INDEX [IX_diagIAPub_imagen] ON [dbo].[diagnosticoIAAlbumPublicacion]([DiagnosticoIAImagenId], [Activo]);
END;

IF OBJECT_ID(N'[dbo].[diagnosticoIAHistorial]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAHistorial]
    (
        [DiagnosticoIAHistorialId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAId] INT NOT NULL,
        [UsuarioId] INT NOT NULL,
        [EstadoAnterior] NVARCHAR(40) NOT NULL CONSTRAINT [DF_diagIAHist_anterior] DEFAULT(N''),
        [EstadoNuevo] NVARCHAR(40) NOT NULL CONSTRAINT [DF_diagIAHist_nuevo] DEFAULT(N''),
        [Accion] NVARCHAR(80) NOT NULL CONSTRAINT [DF_diagIAHist_accion] DEFAULT(N''),
        [Detalle] NVARCHAR(2000) NOT NULL CONSTRAINT [DF_diagIAHist_detalle] DEFAULT(N''),
        [FechaUtc] DATETIME2(0) NOT NULL,
        CONSTRAINT [PK_diagnosticoIAHistorial] PRIMARY KEY CLUSTERED ([DiagnosticoIAHistorialId]),
        CONSTRAINT [FK_diagIAHist_diag] FOREIGN KEY ([DiagnosticoIAId]) REFERENCES [dbo].[diagnosticoIA]([DiagnosticoIAId]) ON DELETE CASCADE,
        CONSTRAINT [FK_diagIAHist_usuario] FOREIGN KEY ([UsuarioId]) REFERENCES [dbo].[usuario]([UsuarioId])
    );
    CREATE INDEX [IX_diagIAHist_fecha] ON [dbo].[diagnosticoIAHistorial]([DiagnosticoIAId], [FechaUtc] DESC);
END;


IF OBJECT_ID(N'[dbo].[diagnosticoIAConfiguracion]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAConfiguracion]
    (
        [DiagnosticoIAConfiguracionId] INT NOT NULL,
        [MaximoRevisionesGemini] INT NOT NULL
            CONSTRAINT [DF_diagIAConfig_maxRev] DEFAULT(2),
        [RevisionesIlimitadas] BIT NOT NULL
            CONSTRAINT [DF_diagIAConfig_ilimitadas] DEFAULT(0),
        [FechaModificacionUtc] DATETIME2(0) NOT NULL
            CONSTRAINT [DF_diagIAConfig_fecha] DEFAULT(SYSUTCDATETIME()),
        [UsuarioModificacionId] INT NULL,
        [RowVersion] ROWVERSION NOT NULL,
        CONSTRAINT [PK_diagnosticoIAConfiguracion]
            PRIMARY KEY CLUSTERED ([DiagnosticoIAConfiguracionId]),
        CONSTRAINT [CK_diagIAConfig_unica]
            CHECK ([DiagnosticoIAConfiguracionId] = 1),
        CONSTRAINT [CK_diagIAConfig_maxRev]
            CHECK ([MaximoRevisionesGemini] BETWEEN 1 AND 20),
        CONSTRAINT [FK_diagIAConfig_usuario]
            FOREIGN KEY ([UsuarioModificacionId])
            REFERENCES [dbo].[usuario]([UsuarioId])
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM [dbo].[diagnosticoIAConfiguracion]
    WHERE [DiagnosticoIAConfiguracionId] = 1
)
BEGIN
    INSERT INTO [dbo].[diagnosticoIAConfiguracion]
    (
        [DiagnosticoIAConfiguracionId],
        [MaximoRevisionesGemini],
        [RevisionesIlimitadas],
        [FechaModificacionUtc],
        [UsuarioModificacionId]
    )
    VALUES (1, 2, 0, SYSUTCDATETIME(), NULL);
END;

IF OBJECT_ID(N'[dbo].[diagnosticoIAConfiguracionHistorial]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[diagnosticoIAConfiguracionHistorial]
    (
        [DiagnosticoIAConfiguracionHistorialId] INT IDENTITY(1,1) NOT NULL,
        [DiagnosticoIAConfiguracionId] INT NOT NULL,
        [MaximoAnterior] INT NOT NULL,
        [IlimitadasAnterior] BIT NOT NULL,
        [MaximoNuevo] INT NOT NULL,
        [IlimitadasNuevo] BIT NOT NULL,
        [UsuarioId] INT NOT NULL,
        [FechaUtc] DATETIME2(0) NOT NULL,
        CONSTRAINT [PK_diagnosticoIAConfiguracionHistorial]
            PRIMARY KEY CLUSTERED ([DiagnosticoIAConfiguracionHistorialId]),
        CONSTRAINT [FK_diagIAConfigHist_config]
            FOREIGN KEY ([DiagnosticoIAConfiguracionId])
            REFERENCES [dbo].[diagnosticoIAConfiguracion]
                ([DiagnosticoIAConfiguracionId]),
        CONSTRAINT [FK_diagIAConfigHist_usuario]
            FOREIGN KEY ([UsuarioId])
            REFERENCES [dbo].[usuario]([UsuarioId])
    );

    CREATE INDEX [IX_diagIAConfigHist_fecha]
        ON [dbo].[diagnosticoIAConfiguracionHistorial]([FechaUtc] DESC);
END;

/*
 * Los registros de la versión anterior no tenían las dos etapas humanas.
 * Se conservan sus datos y fotografías, pero los casos que estaban pendientes,
 * confirmados o corregidos vuelven a la cola del analizador para completar el
 * nuevo circuito Analizador → Aprobador sin fabricar aprobaciones inexistentes.
 */
UPDATE [dbo].[diagnosticoIA]
SET [Estado] = CASE
    WHEN [Estado] IN
    (
        N'PENDIENTE_VALIDACION',
        N'CONFIRMADO',
        N'CORREGIDO'
    ) THEN N'PENDIENTE_ANALIZADOR'
    WHEN [Estado] = N'IMAGEN_RECHAZADA' THEN N'RECHAZADO'
    ELSE [Estado]
END
WHERE [Estado] IN
(
    N'PENDIENTE_VALIDACION',
    N'CONFIRMADO',
    N'CORREGIDO',
    N'IMAGEN_RECHAZADA'
);

/*
 * Compatibilidad con bases históricas:
 * descripcionInterfaz puede conservar NVARCHAR(80) y además participar en
 * índices existentes. Los textos funcionales se mantienen dentro de 80
 * caracteres para no alterar el esquema ni reconstruir índices al arrancar.
 */
MERGE [dbo].[interfaz] AS destino
USING
(
    SELECT N'diagnosticoIASolicitudPage', N'Inspección fitosanitaria - Técnico', N'Crear inspecciones, gestionar fotos y ejecutar decisiones de la etapa técnica.'
    UNION ALL
    SELECT N'diagnosticoIAAnalizadorPage', N'Inspección fitosanitaria - Analizador', N'Tomar expedientes, realizar análisis humano y enviarlos a aprobación.'
    UNION ALL
    SELECT N'diagnosticoIAAprobadorPage', N'Inspección fitosanitaria - Aprobador', N'Tomar expedientes, aprobar, devolver o rechazar diagnósticos fitosanitarios.'
    UNION ALL
    SELECT N'diagnosticoIAConfiguracionPage', N'Configuración fitosanitaria', N'Administrar parámetros, tipos de fotografía y catálogos del flujo fitosanitario.'
) AS origen([NombreInterfaz], [NombreAmigable], [Descripcion])
ON destino.[nombreInterfaz] = origen.[NombreInterfaz]
WHEN MATCHED THEN UPDATE SET
    destino.[nombreAmigableInterfaz] = origen.[NombreAmigable],
    destino.[descripcionInterfaz] = origen.[Descripcion],
    destino.[activo] = 1
WHEN NOT MATCHED THEN
    INSERT ([nombreInterfaz], [nombreAmigableInterfaz], [descripcionInterfaz], [activo])
    VALUES (origen.[NombreInterfaz], origen.[NombreAmigable], origen.[Descripcion], 1);

/*
 * Conserva la configuración de roles del módulo anterior una sola vez.
 * La separación entre solicitud, análisis y aprobación queda administrable
 * desde la matriz de permisos después de esta migración.
 */
MERGE [dbo].[rolInterfaz] AS destino
USING
(
    SELECT
        anterior.[rolId],
        nueva.[interfazId],
        anterior.[leer],
        anterior.[agregar],
        anterior.[actualizar],
        anterior.[eliminar]
    FROM [dbo].[rolInterfaz] anterior
    INNER JOIN [dbo].[interfaz] interfazAnterior
        ON interfazAnterior.[interfazId] = anterior.[interfazId]
       AND interfazAnterior.[nombreInterfaz] = N'diagnosticoIAPage'
    CROSS JOIN [dbo].[interfaz] nueva
    WHERE nueva.[nombreInterfaz] IN
    (
        N'diagnosticoIASolicitudPage',
        N'diagnosticoIAAnalizadorPage',
        N'diagnosticoIAAprobadorPage'
    )
) AS origen
ON destino.[rolId] = origen.[rolId]
   AND destino.[interfazId] = origen.[interfazId]
WHEN NOT MATCHED THEN
    INSERT ([rolId], [interfazId], [leer], [agregar], [actualizar], [eliminar])
    VALUES
    (
        origen.[rolId],
        origen.[interfazId],
        origen.[leer],
        origen.[agregar],
        origen.[actualizar],
        origen.[eliminar]
    );

/*
 * Ningún rol recibe permisos automáticamente por su nombre. Las interfaces
 * quedan disponibles en la matriz y cada rol debe configurarse explícitamente.
 */

UPDATE [dbo].[interfaz]
SET [activo] = 0
WHERE [nombreInterfaz] = N'diagnosticoIAPage';
""";

            await db.Database.ExecuteSqlRawAsync(
                sql,
                cancellationToken);
        }
    }
}
