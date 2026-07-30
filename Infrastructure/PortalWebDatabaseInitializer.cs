using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Infrastructure;

public sealed class PortalWebDatabaseInitializer
{
    public const string AccesoPortal = "PortalAdministrativoWeb";
    public const string RolesWeb = "AdministrarRolesWeb";
    public const string MatrizWeb = "AdministrarMatrizPermisosWeb";
    public const string AlertasWeb = "CentroAlertasWeb";
    public const string AuditoriaAnalisisWeb = "auditoriaAnalisisPage";

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
                "Acceso al portal administrativo",
                "Permite iniciar sesión y navegar dentro del portal web."),
            new Definicion(
                RolesWeb,
                "Administración de roles",
                "Permite crear, editar y desactivar roles desde la web."),
            new Definicion(
                MatrizWeb,
                "Matriz de permisos",
                "Permite administrar los permisos de cada rol."),
            new Definicion(
                AlertasWeb,
                "Centro de alertas agrícolas",
                "Permite consultar y administrar las alertas agrícolas."),
            new Definicion(
                AuditoriaAnalisisWeb,
                "Auditoría de análisis de suelo",
                "Permite consultar filtros, inconsistencias e historial de los análisis de suelo.")
        };

        foreach (Definicion permiso in permisos)
        {
            Interfaz? interfaz = await db.Interfaz
                .FirstOrDefaultAsync(
                    x => x.nombreInterfaz == permiso.Codigo,
                    cancellationToken);

            if (interfaz is null)
            {
                db.Interfaz.Add(new Interfaz
                {
                    nombreInterfaz = permiso.Codigo,
                    nombreAmigableInterfaz = permiso.Nombre,
                    descripcionInterfaz = permiso.Descripcion,
                    activo = true
                });
            }
            else
            {
                interfaz.nombreAmigableInterfaz = permiso.Nombre;
                interfaz.descripcionInterfaz = permiso.Descripcion;
                interfaz.activo = true;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        List<int> rolesAdministradores = await db.Roles
            .AsNoTracking()
            .Where(x =>
                x.activo &&
                x.nombreRol.Trim().ToUpper() == "ADMINISTRADOR")
            .Select(x => x.rolId)
            .ToListAsync(cancellationToken);

        List<Interfaz> interfaces = await db.Interfaz
            .Where(x => permisos.Select(p => p.Codigo)
                .Contains(x.nombreInterfaz))
            .ToListAsync(cancellationToken);

        foreach (int rolId in rolesAdministradores)
        {
            foreach (Interfaz interfaz in interfaces)
            {
                RolInterfaz? relacion = await db.RolInterfaz
                    .FirstOrDefaultAsync(
                        x => x.rolId == rolId &&
                             x.interfazId == interfaz.interfazId,
                        cancellationToken);

                if (relacion is null)
                {
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

    private sealed record Definicion(
        string Codigo,
        string Nombre,
        string Descripcion);
}
