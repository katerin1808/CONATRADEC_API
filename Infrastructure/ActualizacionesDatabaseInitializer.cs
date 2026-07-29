using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Inicializa la tabla de versiones y los permisos del portal y de la app
    /// sin requerir migraciones ni archivos SQL externos.
    /// </summary>
    public sealed class ActualizacionesDatabaseInitializer
    {
        // Se conserva este nombre porque el controlador administrativo lo usa.
        public const string CodigoInterfaz =
            "GestionActualizacionesWeb";

        public const string CodigoInterfazAplicacion =
            "ActualizacionAplicacionPage";

        private const string NombreAmigablePortal =
            "Gestión de actualizaciones";

        private const string DescripcionPortal =
            "Publica versiones Android y Windows desde el portal web.";

        private const string NombreAmigableAplicacion =
            "Actualizaciones de la aplicación";

        private const string DescripcionAplicacion =
            "Permite buscar, descargar e instalar nuevas versiones desde la aplicación.";

        private readonly ActualizacionesDbContext actualizacionesDb;
        private readonly DBContext db;
        private readonly ILogger<ActualizacionesDatabaseInitializer> logger;

        public ActualizacionesDatabaseInitializer(
            ActualizacionesDbContext actualizacionesDb,
            DBContext db,
            ILogger<ActualizacionesDatabaseInitializer> logger)
        {
            this.actualizacionesDb = actualizacionesDb;
            this.db = db;
            this.logger = logger;
        }

        public async Task InicializarAsync(
            CancellationToken cancellationToken = default)
        {
            const string sqlEstructura = """
                IF OBJECT_ID(N'[dbo].[actualizacionAplicacion]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[actualizacionAplicacion]
                    (
                        [ActualizacionAplicacionId] INT IDENTITY(1,1) NOT NULL,
                        [Plataforma] NVARCHAR(20) NOT NULL,
                        [Canal] NVARCHAR(20) NOT NULL,
                        [VersionNombre] NVARCHAR(30) NOT NULL,
                        [VersionCodigo] BIGINT NOT NULL,
                        [NotasVersion] NVARCHAR(4000) NOT NULL
                            CONSTRAINT [DF_actualizacion_notas] DEFAULT(N''),
                        [Obligatoria] BIT NOT NULL
                            CONSTRAINT [DF_actualizacion_obligatoria] DEFAULT(0),
                        [VersionMinimaCodigo] BIGINT NULL,
                        [Estado] NVARCHAR(20) NOT NULL,
                        [NombreArchivo] NVARCHAR(260) NOT NULL,
                        [NombreArchivoAlmacenado] NVARCHAR(260) NOT NULL,
                        [RutaArchivo] NVARCHAR(700) NOT NULL,
                        [TipoContenido] NVARCHAR(150) NOT NULL,
                        [TamanoBytes] BIGINT NOT NULL,
                        [HashSha256] NVARCHAR(64) NOT NULL,
                        [UsuarioCreacionId] INT NOT NULL,
                        [UsuarioUltimaModificacionId] INT NOT NULL,
                        [FechaCreacionUtc] DATETIME2(0) NOT NULL,
                        [FechaUltimaModificacionUtc] DATETIME2(0) NOT NULL,
                        [FechaPublicacionUtc] DATETIME2(0) NULL,
                        [Activo] BIT NOT NULL
                            CONSTRAINT [DF_actualizacion_activo] DEFAULT(1),
                        CONSTRAINT [PK_actualizacionAplicacion]
                            PRIMARY KEY CLUSTERED ([ActualizacionAplicacionId]),
                        CONSTRAINT [FK_actualizacion_usuarioCreacion]
                            FOREIGN KEY ([UsuarioCreacionId])
                            REFERENCES [dbo].[usuario]([UsuarioId]),
                        CONSTRAINT [FK_actualizacion_usuarioModificacion]
                            FOREIGN KEY ([UsuarioUltimaModificacionId])
                            REFERENCES [dbo].[usuario]([UsuarioId])
                    );
                END;
                """;

            const string sqlIndices = """
                IF NOT EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] = N'UX_actualizacion_plataforma_canal_codigo'
                      AND [object_id] =
                          OBJECT_ID(N'[dbo].[actualizacionAplicacion]')
                )
                BEGIN
                    CREATE UNIQUE INDEX
                        [UX_actualizacion_plataforma_canal_codigo]
                    ON [dbo].[actualizacionAplicacion]
                       ([Plataforma], [Canal], [VersionCodigo]);
                END;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [name] = N'IX_actualizacion_busqueda_publicada'
                      AND [object_id] =
                          OBJECT_ID(N'[dbo].[actualizacionAplicacion]')
                )
                BEGIN
                    CREATE INDEX
                        [IX_actualizacion_busqueda_publicada]
                    ON [dbo].[actualizacionAplicacion]
                       ([Plataforma], [Canal], [Estado], [Activo], [VersionCodigo]);
                END;
                """;

            await actualizacionesDb.Database.ExecuteSqlRawAsync(
                sqlEstructura,
                cancellationToken);

            await actualizacionesDb.Database.ExecuteSqlRawAsync(
                sqlIndices,
                cancellationToken);

            await CrearPermisoAdministrativoAsync(
                CodigoInterfaz,
                NombreAmigablePortal,
                DescripcionPortal,
                permitirAdministracionCompleta: true,
                cancellationToken);

            await CrearPermisoAdministrativoAsync(
                CodigoInterfazAplicacion,
                NombreAmigableAplicacion,
                DescripcionAplicacion,
                permitirAdministracionCompleta: false,
                cancellationToken);

            logger.LogInformation(
                "Módulo de actualizaciones y permisos inicializado correctamente.");
        }

        private async Task CrearPermisoAdministrativoAsync(
            string codigoInterfaz,
            string nombreAmigable,
            string descripcion,
            bool permitirAdministracionCompleta,
            CancellationToken cancellationToken)
        {
            Interfaz? interfaz = await db.Interfaz
                .FirstOrDefaultAsync(
                    x => x.nombreInterfaz == codigoInterfaz,
                    cancellationToken);

            bool guardar = false;

            if (interfaz == null)
            {
                interfaz = new Interfaz
                {
                    nombreInterfaz = codigoInterfaz,
                    nombreAmigableInterfaz = nombreAmigable,
                    descripcionInterfaz = descripcion,
                    activo = true
                };

                db.Interfaz.Add(interfaz);
                guardar = true;
            }
            else
            {
                if (!string.Equals(
                        interfaz.nombreAmigableInterfaz,
                        nombreAmigable,
                        StringComparison.Ordinal))
                {
                    interfaz.nombreAmigableInterfaz = nombreAmigable;
                    guardar = true;
                }

                if (!string.Equals(
                        interfaz.descripcionInterfaz,
                        descripcion,
                        StringComparison.Ordinal))
                {
                    interfaz.descripcionInterfaz = descripcion;
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
                RolInterfaz? relacion = await db.RolInterfaz
                    .FirstOrDefaultAsync(
                        x =>
                            x.rolId == rolId &&
                            x.interfazId == interfaz.interfazId,
                        cancellationToken);

                if (relacion == null)
                {
                    relacion = new RolInterfaz
                    {
                        rolId = rolId,
                        interfazId = interfaz.interfazId
                    };

                    db.RolInterfaz.Add(relacion);
                }

                relacion.leer = true;
                relacion.agregar = permitirAdministracionCompleta;
                relacion.actualizar = permitirAdministracionCompleta;
                relacion.eliminar = permitirAdministracionCompleta;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
