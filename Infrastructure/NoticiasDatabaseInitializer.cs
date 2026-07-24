using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    public sealed class NoticiasDatabaseInitializer
    {
        private readonly NoticiasDbContext db;
        private readonly ILogger<NoticiasDatabaseInitializer> logger;

        public NoticiasDatabaseInitializer(
            NoticiasDbContext db,
            ILogger<NoticiasDatabaseInitializer> logger)
        {
            this.db = db;
            this.logger = logger;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            const string sql = """
IF OBJECT_ID(N'[dbo].[categoriaPublicacion]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[categoriaPublicacion]
    (
        [categoriaPublicacionId] INT IDENTITY(1,1) NOT NULL,
        [nombreCategoriaPublicacion] NVARCHAR(80) NOT NULL,
        [descripcionCategoriaPublicacion] NVARCHAR(250) NOT NULL
            CONSTRAINT [DF_categoriaPublicacion_descripcion] DEFAULT(N''),
        [colorHex] NVARCHAR(7) NOT NULL
            CONSTRAINT [DF_categoriaPublicacion_color] DEFAULT(N'#3B655B'),
        [orden] INT NOT NULL
            CONSTRAINT [DF_categoriaPublicacion_orden] DEFAULT(0),
        [activo] BIT NOT NULL
            CONSTRAINT [DF_categoriaPublicacion_activo] DEFAULT(1),
        CONSTRAINT [PK_categoriaPublicacion]
            PRIMARY KEY CLUSTERED ([categoriaPublicacionId]),
        CONSTRAINT [UQ_categoriaPublicacion_nombre]
            UNIQUE ([nombreCategoriaPublicacion])
    );
END;

IF OBJECT_ID(N'[dbo].[publicacion]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[publicacion]
    (
        [publicacionId] INT IDENTITY(1,1) NOT NULL,
        [categoriaPublicacionId] INT NOT NULL,
        [titulo] NVARCHAR(180) NOT NULL,
        [resumen] NVARCHAR(500) NOT NULL,
        [contenido] NVARCHAR(MAX) NOT NULL,
        [rutaImagenPortada] NVARCHAR(500) NOT NULL
            CONSTRAINT [DF_publicacion_portada] DEFAULT(N''),
        [enlaceExterno] NVARCHAR(1000) NOT NULL
            CONSTRAINT [DF_publicacion_enlace] DEFAULT(N''),
        [textoEnlace] NVARCHAR(120) NOT NULL
            CONSTRAINT [DF_publicacion_textoEnlace] DEFAULT(N''),
        [ubicacion] NVARCHAR(300) NOT NULL
            CONSTRAINT [DF_publicacion_ubicacion] DEFAULT(N''),
        [fechaEventoInicioUtc] DATETIME2 NULL,
        [fechaEventoFinUtc] DATETIME2 NULL,
        [fechaInicioPublicacionUtc] DATETIME2 NOT NULL,
        [fechaFinPublicacionUtc] DATETIME2 NULL,
        [estadoPublicacion] NVARCHAR(20) NOT NULL
            CONSTRAINT [DF_publicacion_estado] DEFAULT(N'BORRADOR'),
        [destacada] BIT NOT NULL
            CONSTRAINT [DF_publicacion_destacada] DEFAULT(0),
        [usuarioCreacionId] INT NOT NULL,
        [usuarioUltimaModificacionId] INT NOT NULL,
        [fechaCreacionUtc] DATETIME2 NOT NULL,
        [fechaUltimaModificacionUtc] DATETIME2 NOT NULL,
        [activo] BIT NOT NULL
            CONSTRAINT [DF_publicacion_activo] DEFAULT(1),
        CONSTRAINT [PK_publicacion]
            PRIMARY KEY CLUSTERED ([publicacionId]),
        CONSTRAINT [FK_publicacion_categoria]
            FOREIGN KEY ([categoriaPublicacionId])
            REFERENCES [dbo].[categoriaPublicacion]
                ([categoriaPublicacionId]),
        CONSTRAINT [FK_publicacion_usuarioCreacion]
            FOREIGN KEY ([usuarioCreacionId])
            REFERENCES [dbo].[usuario] ([UsuarioId]),
        CONSTRAINT [FK_publicacion_usuarioModificacion]
            FOREIGN KEY ([usuarioUltimaModificacionId])
            REFERENCES [dbo].[usuario] ([UsuarioId])
    );

    CREATE INDEX [IX_publicacion_feed]
        ON [dbo].[publicacion]
        ([activo], [estadoPublicacion], [fechaInicioPublicacionUtc]);

    CREATE INDEX [IX_publicacion_destacada]
        ON [dbo].[publicacion]
        ([destacada], [fechaInicioPublicacionUtc]);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM [dbo].[categoriaPublicacion]
)
BEGIN
    INSERT INTO [dbo].[categoriaPublicacion]
    (
        [nombreCategoriaPublicacion],
        [descripcionCategoriaPublicacion],
        [colorHex],
        [orden],
        [activo]
    )
    VALUES
        (N'Noticia', N'Información institucional y novedades de CONATRADEC.', N'#3B655B', 1, 1),
        (N'Oferta', N'Ofertas, promociones y oportunidades disponibles.', N'#FF9800', 2, 1),
        (N'Evento', N'Eventos, capacitaciones y actividades programadas.', N'#2F80ED', 3, 1),
        (N'Convocatoria', N'Convocatorias, invitaciones y procesos abiertos.', N'#9B552C', 4, 1),
        (N'Aviso', N'Avisos importantes para los usuarios.', N'#F2C94C', 5, 1);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM [dbo].[interfaz]
    WHERE [nombreInterfaz] = N'noticiasPage'
)
BEGIN
    INSERT INTO [dbo].[interfaz]
    (
        [nombreInterfaz],
        [descripcionInterfaz],
        [activo]
    )
    VALUES
    (
        N'noticiasPage',
        N'Centro de noticias, ofertas, eventos y avisos de CONATRADEC.',
        1
    );
END
ELSE
BEGIN
    UPDATE [dbo].[interfaz]
    SET
        [descripcionInterfaz] =
            N'Centro de noticias, ofertas, eventos y avisos de CONATRADEC.',
        [activo] = 1
    WHERE [nombreInterfaz] = N'noticiasPage';
END;


IF NOT EXISTS
(
    SELECT 1
    FROM [dbo].[interfaz]
    WHERE [nombreInterfaz] = N'categoriaPublicacionPage'
)
BEGIN
    INSERT INTO [dbo].[interfaz]
    (
        [nombreInterfaz],
        [descripcionInterfaz],
        [activo]
    )
    VALUES
    (
        N'categoriaPublicacionPage',
        N'Catálogo de tipos y categorías utilizadas por las publicaciones.',
        1
    );
END
ELSE
BEGIN
    UPDATE [dbo].[interfaz]
    SET
        [descripcionInterfaz] =
            N'Catálogo de tipos y categorías utilizadas por las publicaciones.',
        [activo] = 1
    WHERE [nombreInterfaz] = N'categoriaPublicacionPage';
END;

DECLARE @interfazNoticiasId INT =
(
    SELECT TOP (1) [interfazId]
    FROM [dbo].[interfaz]
    WHERE [nombreInterfaz] = N'noticiasPage'
);

INSERT INTO [dbo].[rolInterfaz]
(
    [leer],
    [agregar],
    [actualizar],
    [eliminar],
    [rolId],
    [interfazId]
)
SELECT
    CAST(1 AS BIT),
    CAST(CASE
        WHEN UPPER(ISNULL(r.[nombreRol], N'')) LIKE N'%ADMIN%'
            THEN 1 ELSE 0 END AS BIT),
    CAST(CASE
        WHEN UPPER(ISNULL(r.[nombreRol], N'')) LIKE N'%ADMIN%'
            THEN 1 ELSE 0 END AS BIT),
    CAST(CASE
        WHEN UPPER(ISNULL(r.[nombreRol], N'')) LIKE N'%ADMIN%'
            THEN 1 ELSE 0 END AS BIT),
    r.[rolId],
    @interfazNoticiasId
FROM [dbo].[rol] r
WHERE r.[activo] = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM [dbo].[rolInterfaz] ri
      WHERE ri.[rolId] = r.[rolId]
        AND ri.[interfazId] = @interfazNoticiasId
  );

DECLARE @interfazCategoriaPublicacionId INT =
(
    SELECT TOP (1) [interfazId]
    FROM [dbo].[interfaz]
    WHERE [nombreInterfaz] = N'categoriaPublicacionPage'
);

INSERT INTO [dbo].[rolInterfaz]
(
    [leer],
    [agregar],
    [actualizar],
    [eliminar],
    [rolId],
    [interfazId]
)
SELECT
    CAST(CASE
        WHEN UPPER(ISNULL(r.[nombreRol], N'')) LIKE N'%ADMIN%'
            THEN 1 ELSE 0 END AS BIT),
    CAST(CASE
        WHEN UPPER(ISNULL(r.[nombreRol], N'')) LIKE N'%ADMIN%'
            THEN 1 ELSE 0 END AS BIT),
    CAST(CASE
        WHEN UPPER(ISNULL(r.[nombreRol], N'')) LIKE N'%ADMIN%'
            THEN 1 ELSE 0 END AS BIT),
    CAST(CASE
        WHEN UPPER(ISNULL(r.[nombreRol], N'')) LIKE N'%ADMIN%'
            THEN 1 ELSE 0 END AS BIT),
    r.[rolId],
    @interfazCategoriaPublicacionId
FROM [dbo].[rol] r
WHERE r.[activo] = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM [dbo].[rolInterfaz] ri
      WHERE ri.[rolId] = r.[rolId]
        AND ri.[interfazId] = @interfazCategoriaPublicacionId
  );
""";

            await db.Database.ExecuteSqlRawAsync(
                sql,
                cancellationToken);

            logger.LogInformation(
                "Estructura del módulo de noticias verificada correctamente.");
        }
    }
}
