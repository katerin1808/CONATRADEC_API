using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure;

public sealed class PortalWebDatabaseInitializer
{
    private const int LongitudMaximaDescripcionCompatible =
        100;

    public const string AccesoPortal =
        "PortalAdministrativoWeb";

    public const string RolesWeb =
        "AdministrarRolesWeb";

    public const string MatrizWeb =
        "AdministrarMatrizPermisosWeb";

    public const string AlertasWeb =
        "CentroAlertasWeb";

    public const string AuditoriaAnalisisWeb =
        "auditoriaAnalisisPage";

    public const string MapaRelacionesAnalisisWeb =
        "MapaRelacionesAnalisisWeb";

    public const string UnidadesConversionesWeb =
        "unidadesConversionesPage";

    public const string ReportesWeb =
        "reportesPage";

    public const string FotosTerrenoWeb =
        "fotosTerrenoPage";

    private readonly DBContext db;
    private readonly ILogger<PortalWebDatabaseInitializer> logger;

    public PortalWebDatabaseInitializer(
        DBContext db,
        ILogger<PortalWebDatabaseInitializer> logger)
    {
        this.db = db;
        this.logger = logger;
    }

    public async Task InicializarAsync(
        CancellationToken cancellationToken = default)
    {
        var permisos = new[]
        {
            new Definicion(
                AccesoPortal,
                "Acceso al portal web",
                "Permite iniciar sesión y navegar en el portal web."),

            new Definicion(
                RolesWeb,
                "Administración de roles",
                "Permite administrar los roles desde la web."),

            new Definicion(
                MatrizWeb,
                "Matriz de permisos",
                "Permite administrar los permisos de cada rol."),

            new Definicion(
                AlertasWeb,
                "Centro de alertas agrícolas",
                "Permite consultar y administrar alertas agrícolas."),

            new Definicion(
                AuditoriaAnalisisWeb,
                "Control de análisis de suelo",
                "Permite administrar y auditar análisis de suelo."),

            new Definicion(
                MapaRelacionesAnalisisWeb,
                "Mapa de relaciones de análisis",
                "Consulta las relaciones del análisis de suelo."),

            new Definicion(
                UnidadesConversionesWeb,
                "Unidades y conversiones",
                "Administra unidades y fórmulas de conversión."),

            new Definicion(
                ReportesWeb,
                "Centro de reportes",
                "Consulta indicadores y exporta reportes administrativos."),

            new Definicion(
                FotosTerrenoWeb,
                "Fotografías de terrenos",
                "Administra fotografías, portadas y metadatos de terrenos.")
        };

        foreach (Definicion permiso in permisos)
        {
            Interfaz? interfaz =
                await db.Interfaz.FirstOrDefaultAsync(
                    item =>
                        item.nombreInterfaz ==
                        permiso.Codigo,
                    cancellationToken);

            string descripcionSegura =
                AjustarDescripcion(
                    permiso.Descripcion);

            if (interfaz is null)
            {
                db.Interfaz.Add(
                    new Interfaz
                    {
                        nombreInterfaz =
                            permiso.Codigo,
                        nombreAmigableInterfaz =
                            permiso.Nombre,
                        descripcionInterfaz =
                            descripcionSegura,
                        activo = true
                    });
            }
            else
            {
                interfaz.nombreAmigableInterfaz =
                    permiso.Nombre;
                interfaz.descripcionInterfaz =
                    descripcionSegura;
                interfaz.activo = true;
            }
        }

        await db.SaveChangesAsync(
            cancellationToken);

        List<int> rolesAdministradores =
            await db.Roles
                .AsNoTracking()
                .Where(item =>
                    item.activo &&
                    item.nombreRol
                        .Trim()
                        .ToUpper() ==
                    "ADMINISTRADOR")
                .Select(item => item.rolId)
                .ToListAsync(cancellationToken);

        string[] codigosPermisos =
            permisos
                .Select(item => item.Codigo)
                .ToArray();

        List<Interfaz> interfaces =
            await db.Interfaz
                .Where(item =>
                    codigosPermisos.Contains(
                        item.nombreInterfaz))
                .ToListAsync(cancellationToken);

        foreach (int rolId in rolesAdministradores)
        {
            foreach (Interfaz interfaz in interfaces)
            {
                RolInterfaz? relacion =
                    await db.RolInterfaz
                        .FirstOrDefaultAsync(
                            item =>
                                item.rolId == rolId &&
                                item.interfazId ==
                                    interfaz.interfazId,
                            cancellationToken);

                if (relacion is null)
                {
                    db.RolInterfaz.Add(
                        new RolInterfaz
                        {
                            rolId = rolId,
                            interfazId =
                                interfaz.interfazId,
                            leer = true,
                            agregar = true,
                            actualizar = true,
                            eliminar = true
                        });
                }
                else
                {
                    relacion.leer = true;
                    relacion.agregar = true;
                    relacion.actualizar = true;
                    relacion.eliminar = true;
                }
            }
        }

        await db.SaveChangesAsync(
            cancellationToken);

        await InicializarEstructuraFotosTerrenoAsync(
            cancellationToken);

        logger.LogInformation(
            "Permisos y estructura del portal web inicializados correctamente.");
    }

    private async Task InicializarEstructuraFotosTerrenoAsync(
        CancellationToken cancellationToken)
    {
        /*
         * SQL Server compila cada lote completo antes de ejecutarlo.
         * Por ese motivo, las instrucciones que utilizan columnas nuevas no
         * pueden estar en el mismo lote que los ALTER TABLE que las crean.
         *
         * Primero garantizamos la estructura y, en una segunda ejecución,
         * normalizamos portadas y creamos el índice.
         */
        const string sqlCrearColumnas =
            """
            IF OBJECT_ID(N'[dbo].[FotoTerreno]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH('dbo.FotoTerreno', 'tituloFotoTerreno') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[FotoTerreno]
                    ADD [tituloFotoTerreno] nvarchar(150) NOT NULL
                        CONSTRAINT [DF_FotoTerreno_Titulo]
                        DEFAULT(N'') WITH VALUES;
                END;

                IF COL_LENGTH('dbo.FotoTerreno', 'descripcionFotoTerreno') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[FotoTerreno]
                    ADD [descripcionFotoTerreno] nvarchar(600) NOT NULL
                        CONSTRAINT [DF_FotoTerreno_Descripcion]
                        DEFAULT(N'') WITH VALUES;
                END;

                IF COL_LENGTH('dbo.FotoTerreno', 'nombreArchivoOriginal') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[FotoTerreno]
                    ADD [nombreArchivoOriginal] nvarchar(255) NOT NULL
                        CONSTRAINT [DF_FotoTerreno_ArchivoOriginal]
                        DEFAULT(N'') WITH VALUES;
                END;

                IF COL_LENGTH('dbo.FotoTerreno', 'fechaRegistroUtc') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[FotoTerreno]
                    ADD [fechaRegistroUtc] datetime2 NOT NULL
                        CONSTRAINT [DF_FotoTerreno_FechaRegistro]
                        DEFAULT(SYSUTCDATETIME()) WITH VALUES;
                END;

                IF COL_LENGTH('dbo.FotoTerreno', 'fechaCaptura') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[FotoTerreno]
                    ADD [fechaCaptura] date NULL;
                END;

                IF COL_LENGTH('dbo.FotoTerreno', 'esPortada') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[FotoTerreno]
                    ADD [esPortada] bit NOT NULL
                        CONSTRAINT [DF_FotoTerreno_EsPortada]
                        DEFAULT(0) WITH VALUES;
                END;
            END;
            """;

        await db.Database.ExecuteSqlRawAsync(
            sqlCrearColumnas,
            cancellationToken);

        const string sqlNormalizarDatosEIndices =
            """
            IF OBJECT_ID(N'[dbo].[FotoTerreno]', N'U') IS NOT NULL
               AND COL_LENGTH('dbo.FotoTerreno', 'fechaRegistroUtc') IS NOT NULL
               AND COL_LENGTH('dbo.FotoTerreno', 'esPortada') IS NOT NULL
            BEGIN
                UPDATE [dbo].[FotoTerreno]
                SET [esPortada] = 0
                WHERE [activo] = 0
                  AND [esPortada] = 1;

                ;WITH PortadasActivas AS
                (
                    SELECT
                        fotoTerrenoId,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY terrenoId
                            ORDER BY fotoTerrenoId
                        ) AS numero
                    FROM [dbo].[FotoTerreno]
                    WHERE activo = 1
                      AND esPortada = 1
                )
                UPDATE foto
                SET esPortada = 0
                FROM [dbo].[FotoTerreno] foto
                INNER JOIN PortadasActivas portada
                    ON portada.fotoTerrenoId = foto.fotoTerrenoId
                WHERE portada.numero > 1;

                ;WITH PrimeraFoto AS
                (
                    SELECT
                        fotoTerrenoId,
                        terrenoId,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY terrenoId
                            ORDER BY fotoTerrenoId
                        ) AS numero
                    FROM [dbo].[FotoTerreno]
                    WHERE activo = 1
                )
                UPDATE foto
                SET esPortada = 1
                FROM [dbo].[FotoTerreno] foto
                INNER JOIN PrimeraFoto primera
                    ON primera.fotoTerrenoId = foto.fotoTerrenoId
                WHERE primera.numero = 1
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM [dbo].[FotoTerreno] portada
                      WHERE portada.terrenoId = foto.terrenoId
                        AND portada.activo = 1
                        AND portada.esPortada = 1
                  );

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_FotoTerreno_TerrenoActivoFecha'
                      AND object_id = OBJECT_ID(N'[dbo].[FotoTerreno]')
                )
                BEGIN
                    CREATE INDEX [IX_FotoTerreno_TerrenoActivoFecha]
                    ON [dbo].[FotoTerreno]
                    (
                        [terrenoId],
                        [activo],
                        [fechaRegistroUtc] DESC
                    )
                    INCLUDE ([esPortada]);
                END;
            END;
            """;

        await db.Database.ExecuteSqlRawAsync(
            sqlNormalizarDatosEIndices,
            cancellationToken);
    }

    private static string AjustarDescripcion(
        string descripcion)
    {
        string valor =
            string.IsNullOrWhiteSpace(descripcion)
                ? string.Empty
                : descripcion.Trim();

        return valor.Length <=
               LongitudMaximaDescripcionCompatible
            ? valor
            : valor[..LongitudMaximaDescripcionCompatible];
    }

    private sealed record Definicion(
        string Codigo,
        string Nombre,
        string Descripcion);
}
