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
                "Consulta indicadores y exporta reportes administrativos.")
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

        logger.LogInformation(
            "Permisos del portal web inicializados correctamente.");
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
