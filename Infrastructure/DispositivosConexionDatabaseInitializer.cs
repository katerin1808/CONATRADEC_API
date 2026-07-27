using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Crea de forma idempotente la tabla, sus índices y la interfaz de
    /// permisos. No requiere migración ni script SQL manual.
    /// </summary>
    public sealed class DispositivosConexionDatabaseInitializer
    {
        public const string CodigoInterfaz =
            "dispositivosConectadosPage";

        private readonly DispositivosConexionDbContext dispositivosDb;
        private readonly DBContext db;
        private readonly ILogger<DispositivosConexionDatabaseInitializer>
            logger;

        public DispositivosConexionDatabaseInitializer(
            DispositivosConexionDbContext dispositivosDb,
            DBContext db,
            ILogger<DispositivosConexionDatabaseInitializer> logger)
        {
            this.dispositivosDb = dispositivosDb;
            this.db = db;
            this.logger = logger;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            const string sql = """
                IF OBJECT_ID(N'[dbo].[dispositivoConexion]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[dispositivoConexion]
                    (
                        [DispositivoConexionId] INT IDENTITY(1,1) NOT NULL,
                        [InstalacionId] NVARCHAR(64) NOT NULL,
                        [SesionId] NVARCHAR(64) NOT NULL,
                        [UsuarioId] INT NOT NULL,
                        [UsuarioNombre] NVARCHAR(150) NOT NULL,
                        [CorreoUsuario] NVARCHAR(150) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_correo]
                            DEFAULT(N''),
                        [RolNombre] NVARCHAR(100) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_rol]
                            DEFAULT(N''),
                        [Plataforma] NVARCHAR(30) NOT NULL,
                        [TipoDispositivo] NVARCHAR(30) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_tipo]
                            DEFAULT(N''),
                        [Fabricante] NVARCHAR(100) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_fabricante]
                            DEFAULT(N''),
                        [Modelo] NVARCHAR(150) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_modelo]
                            DEFAULT(N''),
                        [NombreDispositivo] NVARCHAR(150) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_nombre]
                            DEFAULT(N''),
                        [SistemaOperativo] NVARCHAR(100) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_so]
                            DEFAULT(N''),
                        [VersionSistema] NVARCHAR(50) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_versionSo]
                            DEFAULT(N''),
                        [VersionApp] NVARCHAR(50) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_versionApp]
                            DEFAULT(N''),
                        [BuildApp] NVARCHAR(50) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_build]
                            DEFAULT(N''),
                        [Idioma] NVARCHAR(20) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_idioma]
                            DEFAULT(N''),
                        [TipoConexion] NVARCHAR(100) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_conexion]
                            DEFAULT(N''),
                        [PaginaActual] NVARCHAR(500) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_pagina]
                            DEFAULT(N''),
                        [DireccionIp] NVARCHAR(100) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_ip]
                            DEFAULT(N''),
                        [UserAgent] NVARCHAR(500) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_userAgent]
                            DEFAULT(N''),
                        [FechaRegistroUtc] DATETIME2(0) NOT NULL,
                        [FechaInicioSesionUtc] DATETIME2(0) NOT NULL,
                        [UltimoLatidoUtc] DATETIME2(0) NOT NULL,
                        [FechaDesconexionUtc] DATETIME2(0) NULL,
                        [ConectadoReportado] BIT NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_conectado]
                            DEFAULT(0),
                        [CantidadSesiones] INT NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_sesiones]
                            DEFAULT(1),
                        [Activo] BIT NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_activo]
                            DEFAULT(1),
                        CONSTRAINT [PK_dispositivoConexion]
                            PRIMARY KEY CLUSTERED ([DispositivoConexionId]),
                        CONSTRAINT [FK_dispositivoConexion_usuario]
                            FOREIGN KEY ([UsuarioId])
                            REFERENCES [dbo].[usuario]([UsuarioId])
                    );
                END;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] = N'UX_dispositivoConexion_instalacionId'
                      AND [object_id] =
                          OBJECT_ID(N'[dbo].[dispositivoConexion]')
                )
                BEGIN
                    CREATE UNIQUE INDEX
                        [UX_dispositivoConexion_instalacionId]
                    ON [dbo].[dispositivoConexion]([InstalacionId]);
                END;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] = N'IX_dispositivoConexion_ultimoLatidoUtc'
                      AND [object_id] =
                          OBJECT_ID(N'[dbo].[dispositivoConexion]')
                )
                BEGIN
                    CREATE INDEX
                        [IX_dispositivoConexion_ultimoLatidoUtc]
                    ON [dbo].[dispositivoConexion]([UltimoLatidoUtc]);
                END;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] =
                        N'IX_dispositivoConexion_usuario_ultimoLatido'
                      AND [object_id] =
                          OBJECT_ID(N'[dbo].[dispositivoConexion]')
                )
                BEGIN
                    CREATE INDEX
                        [IX_dispositivoConexion_usuario_ultimoLatido]
                    ON [dbo].[dispositivoConexion]
                       ([UsuarioId], [UltimoLatidoUtc]);
                END;
                """;

            await dispositivosDb.Database.ExecuteSqlRawAsync(
                sql,
                cancellationToken);

            await CrearPermisoAdministrativoAsync(cancellationToken);

            logger.LogInformation(
                "Módulo de dispositivos conectados inicializado correctamente.");
        }

        private async Task CrearPermisoAdministrativoAsync(
            CancellationToken cancellationToken)
        {
            Interfaz? interfaz = await db.Interfaz
                .FirstOrDefaultAsync(
                    x => x.nombreInterfaz == CodigoInterfaz,
                    cancellationToken);

            if (interfaz == null)
            {
                interfaz = new Interfaz
                {
                    nombreInterfaz = CodigoInterfaz,
                    nombreAmigableInterfaz =
                        "Dispositivos conectados",
                    descripcionInterfaz =
                        "Consulta dispositivos Android y Windows conectados a la API.",
                    activo = true
                };

                db.Interfaz.Add(interfaz);
                await db.SaveChangesAsync(cancellationToken);
            }
            else if (!interfaz.activo)
            {
                interfaz.activo = true;
                await db.SaveChangesAsync(cancellationToken);
            }

            List<int> rolesAdministradores = await db.Roles
                .AsNoTracking()
                .Where(x =>
                    x.activo &&
                    x.nombreRol.ToUpper() == "ADMINISTRADOR")
                .Select(x => x.rolId)
                .ToListAsync(cancellationToken);

            foreach (int rolId in rolesAdministradores)
            {
                bool existe = await db.RolInterfaz.AnyAsync(
                    x =>
                        x.rolId == rolId &&
                        x.interfazId == interfaz.interfazId,
                    cancellationToken);

                if (existe)
                    continue;

                db.RolInterfaz.Add(new RolInterfaz
                {
                    rolId = rolId,
                    interfazId = interfaz.interfazId,
                    leer = true,
                    agregar = true,
                    actualizar = true,
                    eliminar = true
                });
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
