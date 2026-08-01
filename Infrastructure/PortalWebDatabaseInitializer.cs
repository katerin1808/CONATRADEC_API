using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure;

public sealed class PortalWebDatabaseInitializer
{
    /*
     * La columna descripcionInterfaz de la base de datos de producción
     * utiliza una longitud menor que la definida actualmente en el modelo.
     *
     * Se conserva este límite de compatibilidad para impedir que una
     * descripción extensa detenga el inicio completo de la API.
     */
    private const int LongitudMaximaDescripcionCompatible = 100;

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
                "Consulta las relaciones del análisis de suelo.")
        };

        foreach (Definicion permiso in permisos)
        {
            Interfaz? interfaz = await db.Interfaz
                .FirstOrDefaultAsync(
                    x => x.nombreInterfaz == permiso.Codigo,
                    cancellationToken);

            string descripcionSegura =
                AjustarDescripcion(permiso.Descripcion);

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

        await db.SaveChangesAsync(cancellationToken);

        List<int> rolesAdministradores = await db.Roles
            .AsNoTracking()
            .Where(x =>
                x.activo &&
                x.nombreRol.Trim().ToUpper() ==
                "ADMINISTRADOR")
            .Select(x => x.rolId)
            .ToListAsync(cancellationToken);

        string[] codigosPermisos =
            permisos
                .Select(x => x.Codigo)
                .ToArray();

        List<Interfaz> interfaces = await db.Interfaz
            .Where(x =>
                codigosPermisos.Contains(
                    x.nombreInterfaz))
            .ToListAsync(cancellationToken);

        foreach (int rolId in rolesAdministradores)
        {
            foreach (Interfaz interfaz in interfaces)
            {
                RolInterfaz? relacion =
                    await db.RolInterfaz
                        .FirstOrDefaultAsync(
                            x =>
                                x.rolId == rolId &&
                                x.interfazId ==
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

        await db.SaveChangesAsync(cancellationToken);

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
