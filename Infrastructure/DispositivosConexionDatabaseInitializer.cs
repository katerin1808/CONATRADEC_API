using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Crea o actualiza de forma idempotente la tabla, sus índices y la
    /// interfaz de permisos. No requiere migración ni script SQL manual.
    /// </summary>
    public sealed class DispositivosConexionDatabaseInitializer
    {
        public const string CodigoInterfaz =
            "dispositivosConectadosPage";

        private const string NombreAmigable =
            "Dispositivos conectados";

        // La columna descripcionInterfaz admite 80 caracteres.
        private const string Descripcion =
            "Consulta dispositivos Android y Windows conectados a la API.";

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
            /*
             * SQL Server compila cada lote antes de ejecutarlo. Por eso,
             * cuando una instalación existente todavía no tiene las
             * columnas de ubicación, los índices deben crearse en una
             * segunda ejecución posterior a los ALTER TABLE.
             */
            const string sqlEstructura = """
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
                        [Latitud] DECIMAL(9,6) NULL,
                        [Longitud] DECIMAL(9,6) NULL,
                        [PrecisionMetros] DECIMAL(10,2) NULL,
                        [FechaUbicacionUtc] DATETIME2(0) NULL,
                        [OrigenUbicacion] NVARCHAR(30) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_origenUbicacion]
                            DEFAULT(N''),
                        [EstadoPermisoUbicacion] NVARCHAR(30) NOT NULL
                            CONSTRAINT [DF_dispositivoConexion_permisoUbicacion]
                            DEFAULT(N'NO_REPORTADO'),
                        [UbicacionSimulada] BIT NULL,
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

                IF COL_LENGTH(N'dbo.dispositivoConexion', N'Latitud') IS NULL
                    ALTER TABLE [dbo].[dispositivoConexion]
                    ADD [Latitud] DECIMAL(9,6) NULL;

                IF COL_LENGTH(N'dbo.dispositivoConexion', N'Longitud') IS NULL
                    ALTER TABLE [dbo].[dispositivoConexion]
                    ADD [Longitud] DECIMAL(9,6) NULL;

                IF COL_LENGTH(N'dbo.dispositivoConexion', N'PrecisionMetros') IS NULL
                    ALTER TABLE [dbo].[dispositivoConexion]
                    ADD [PrecisionMetros] DECIMAL(10,2) NULL;

                IF COL_LENGTH(N'dbo.dispositivoConexion', N'FechaUbicacionUtc') IS NULL
                    ALTER TABLE [dbo].[dispositivoConexion]
                    ADD [FechaUbicacionUtc] DATETIME2(0) NULL;

                IF COL_LENGTH(N'dbo.dispositivoConexion', N'OrigenUbicacion') IS NULL
                    ALTER TABLE [dbo].[dispositivoConexion]
                    ADD [OrigenUbicacion] NVARCHAR(30) NOT NULL
                        CONSTRAINT [DF_dispositivoConexion_origenUbicacion]
                        DEFAULT(N'');

                IF COL_LENGTH(N'dbo.dispositivoConexion', N'EstadoPermisoUbicacion') IS NULL
                    ALTER TABLE [dbo].[dispositivoConexion]
                    ADD [EstadoPermisoUbicacion] NVARCHAR(30) NOT NULL
                        CONSTRAINT [DF_dispositivoConexion_permisoUbicacion]
                        DEFAULT(N'NO_REPORTADO');

                IF COL_LENGTH(N'dbo.dispositivoConexion', N'UbicacionSimulada') IS NULL
                    ALTER TABLE [dbo].[dispositivoConexion]
                    ADD [UbicacionSimulada] BIT NULL;
                """;

            const string sqlIndices = """
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

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] =
                        N'IX_dispositivoConexion_fechaUbicacionUtc'
                      AND [object_id] =
                          OBJECT_ID(N'[dbo].[dispositivoConexion]')
                )
                BEGIN
                    CREATE INDEX
                        [IX_dispositivoConexion_fechaUbicacionUtc]
                    ON [dbo].[dispositivoConexion]([FechaUbicacionUtc])
                    WHERE [FechaUbicacionUtc] IS NOT NULL;
                END;
                """;

            await dispositivosDb.Database.ExecuteSqlRawAsync(
                sqlEstructura,
                cancellationToken);

            await dispositivosDb.Database.ExecuteSqlRawAsync(
                sqlIndices,
                cancellationToken);

            await CrearPermisoAdministrativoAsync(cancellationToken);

            logger.LogInformation(
                "Módulo de dispositivos y ubicación inicializado correctamente.");
        }

        private async Task CrearPermisoAdministrativoAsync(
            CancellationToken cancellationToken)
        {
            Interfaz? interfaz = await db.Interfaz
                .FirstOrDefaultAsync(
                    x => x.nombreInterfaz == CodigoInterfaz,
                    cancellationToken);

            bool guardar = false;

            if (interfaz == null)
            {
                interfaz = new Interfaz
                {
                    nombreInterfaz = CodigoInterfaz,
                    nombreAmigableInterfaz = NombreAmigable,
                    descripcionInterfaz = Descripcion,
                    activo = true
                };

                db.Interfaz.Add(interfaz);
                guardar = true;
            }
            else
            {
                if (!string.Equals(
                        interfaz.nombreAmigableInterfaz,
                        NombreAmigable,
                        StringComparison.Ordinal))
                {
                    interfaz.nombreAmigableInterfaz = NombreAmigable;
                    guardar = true;
                }

                if (!string.Equals(
                        interfaz.descripcionInterfaz,
                        Descripcion,
                        StringComparison.Ordinal))
                {
                    interfaz.descripcionInterfaz = Descripcion;
                    guardar = true;
                }

                if (!interfaz.activo)
                {
                    interfaz.activo = true;
                    guardar = true;
                }
            }

            if (guardar)
                await db.SaveChangesAsync(cancellationToken);

            List<int> rolesAdministradores = await db.Roles
                .AsNoTracking()
                .Where(x =>
                    x.activo &&
                    EF.Functions.Like(x.nombreRol, "%ADMIN%"))
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
