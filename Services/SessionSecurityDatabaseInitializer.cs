using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Services
{
    /// <summary>
    /// Prepara la estructura necesaria para controlar la vigencia de las
    /// sesiones sin requerir migraciones ni scripts manuales.
    /// </summary>
    public sealed class SessionSecurityDatabaseInitializer
    {
        private readonly DBContext db;
        private readonly ILogger<
            SessionSecurityDatabaseInitializer> logger;

        public SessionSecurityDatabaseInitializer(
            DBContext db,
            ILogger<SessionSecurityDatabaseInitializer> logger)
        {
            this.db = db;
            this.logger = logger;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            /*
             * IMPORTANTE:
             * La creación de la columna y las consultas que la utilizan se
             * ejecutan como comandos separados.
             *
             * SQL Server compila un lote completo antes de ejecutarlo. Si el
             * ALTER TABLE y el UPDATE se incluyen en el mismo lote, puede
             * producir "Invalid column name 'versionSesion'" aunque el ALTER
             * aparezca primero.
             */
            const string crearColumnaSql = """
                IF OBJECT_ID(N'[dbo].[usuario]', N'U') IS NULL
                BEGIN
                    THROW 50001,
                        'No existe la tabla dbo.usuario.',
                        1;
                END;

                IF COL_LENGTH(
                    N'dbo.usuario',
                    N'versionSesion') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[usuario]
                    ADD [versionSesion] INT NOT NULL
                        DEFAULT (1) WITH VALUES;
                END;
                """;

            await db.Database.ExecuteSqlRawAsync(
                crearColumnaSql,
                cancellationToken);

            /*
             * Este segundo comando se compila únicamente después de que el
             * primero terminó, por lo que versionSesion ya existe.
             *
             * También corrige instalaciones parciales en las que la columna
             * pudiera existir como nullable o con valores inválidos.
             */
            const string normalizarColumnaSql = """
                UPDATE [dbo].[usuario]
                   SET [versionSesion] = 1
                 WHERE [versionSesion] IS NULL
                    OR [versionSesion] < 1;

                IF EXISTS
                (
                    SELECT 1
                      FROM sys.columns
                     WHERE [object_id] =
                           OBJECT_ID(N'[dbo].[usuario]')
                       AND [name] = N'versionSesion'
                       AND [is_nullable] = 1
                )
                BEGIN
                    ALTER TABLE [dbo].[usuario]
                    ALTER COLUMN [versionSesion] INT NOT NULL;
                END;

                IF NOT EXISTS
                (
                    SELECT 1
                      FROM sys.default_constraints AS dc
                      INNER JOIN sys.columns AS c
                              ON c.[object_id] =
                                 dc.[parent_object_id]
                             AND c.[column_id] =
                                 dc.[parent_column_id]
                     WHERE dc.[parent_object_id] =
                           OBJECT_ID(N'[dbo].[usuario]')
                       AND c.[name] = N'versionSesion'
                )
                BEGIN
                    ALTER TABLE [dbo].[usuario]
                    ADD DEFAULT (1)
                    FOR [versionSesion];
                END;
                """;

            await db.Database.ExecuteSqlRawAsync(
                normalizarColumnaSql,
                cancellationToken);

            /*
             * Únicamente el rol cuyo nombre sea exactamente Administrador
             * queda protegido. Administrador 01 y nombres similares siguen
             * siendo roles normales y manipulables.
             */
            const string restaurarAdministradorSql = """
                UPDATE u
                   SET u.[activo] = 1,
                       u.[versionSesion] =
                           CASE
                               WHEN ISNULL(
                                   u.[versionSesion],
                                   0) < 1
                                   THEN 1
                               ELSE u.[versionSesion] + 1
                           END
                  FROM [dbo].[usuario] AS u
                  INNER JOIN [dbo].[Rol] AS r
                          ON r.[rolId] = u.[rolId]
                 WHERE u.[activo] = 0
                   AND UPPER(
                       LTRIM(
                           RTRIM(
                               ISNULL(
                                   r.[nombreRol],
                                   N'')))) =
                       N'ADMINISTRADOR';
                """;

            await db.Database.ExecuteSqlRawAsync(
                restaurarAdministradorSql,
                cancellationToken);

            /*
             * La tabla sesionActiva reemplaza el ConcurrentDictionary que
             * existía dentro de cada proceso. Todos los nodos del backend
             * consultan la misma información y las sesiones sobreviven a los
             * reciclajes de IIS.
             */
            const string crearSesionesSql = """
                IF OBJECT_ID(N'[dbo].[sesionActiva]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[sesionActiva]
                    (
                        [SesionId] NVARCHAR(64) NOT NULL,
                        [UsuarioId] INT NOT NULL,
                        [VersionSesion] INT NOT NULL,
                        [CreadaUtc] DATETIME2(3) NOT NULL,
                        [UltimaActividadUtc] DATETIME2(3) NOT NULL,
                        [ExpiraUtc] DATETIME2(3) NOT NULL,
                        [Revocada] BIT NOT NULL
                            CONSTRAINT [DF_sesionActiva_revocada]
                            DEFAULT (0),
                        [FechaRevocacionUtc] DATETIME2(3) NULL,
                        [MotivoRevocacion] NVARCHAR(100) NULL,
                        [UltimaActualizacionUtc] DATETIME2(3) NOT NULL,
                        CONSTRAINT [PK_sesionActiva]
                            PRIMARY KEY CLUSTERED ([SesionId]),
                        CONSTRAINT [FK_sesionActiva_usuario]
                            FOREIGN KEY ([UsuarioId])
                            REFERENCES [dbo].[usuario]([UsuarioId])
                    );
                END;
                """;

            await db.Database.ExecuteSqlRawAsync(
                crearSesionesSql,
                cancellationToken);

            const string crearIndicesSesionesSql = """
                IF NOT EXISTS
                (
                    SELECT 1
                      FROM sys.indexes
                     WHERE [object_id] =
                           OBJECT_ID(N'[dbo].[sesionActiva]')
                       AND [name] =
                           N'IX_sesionActiva_usuario_revocada'
                )
                BEGIN
                    CREATE INDEX [IX_sesionActiva_usuario_revocada]
                        ON [dbo].[sesionActiva]
                        (
                            [UsuarioId],
                            [Revocada]
                        )
                        INCLUDE
                        (
                            [VersionSesion],
                            [UltimaActividadUtc],
                            [ExpiraUtc]
                        );
                END;

                IF NOT EXISTS
                (
                    SELECT 1
                      FROM sys.indexes
                     WHERE [object_id] =
                           OBJECT_ID(N'[dbo].[sesionActiva]')
                       AND [name] =
                           N'IX_sesionActiva_expiracion'
                )
                BEGIN
                    CREATE INDEX [IX_sesionActiva_expiracion]
                        ON [dbo].[sesionActiva]
                        (
                            [ExpiraUtc],
                            [Revocada]
                        );
                END;
                """;

            await db.Database.ExecuteSqlRawAsync(
                crearIndicesSesionesSql,
                cancellationToken);

            const string limpiarSesionesAntiguasSql = """
                DELETE FROM [dbo].[sesionActiva]
                 WHERE [ExpiraUtc] <
                       DATEADD(DAY, -1, SYSUTCDATETIME())
                    OR
                       (
                           [Revocada] = 1
                           AND [FechaRevocacionUtc] <
                               DATEADD(DAY, -1, SYSUTCDATETIME())
                       );
                """;

            await db.Database.ExecuteSqlRawAsync(
                limpiarSesionesAntiguasSql,
                cancellationToken);

            logger.LogInformation(
                "La estructura de seguridad y sesiones persistentes fue inicializada correctamente.");
        }
    }
}
