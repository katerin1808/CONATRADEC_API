using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Services
{
    /// <summary>
    /// Garantiza que la opción Datos sin conexión exista en la matriz.
    ///
    /// Se ejecuta durante el login, sin migraciones ni scripts manuales.
    /// Los roles administrativos reciben lectura solamente cuando todavía no
    /// existe una relación explícita. Después, la matriz controla el permiso.
    /// </summary>
    public static class OfflinePermissionProvisioner
    {
        public const string CodigoInterfaz =
            "datosSinConexionPage";

        private const string NombreAmigable =
            "Datos sin conexión";

        /*
         * La columna descripcionInterfaz admite 80 caracteres.
         * Este texto debe mantenerse por debajo de ese límite.
         */
        private const string Descripcion =
            "Descarga y uso de datos guardados para trabajar sin conexión.";

        public static async Task AsegurarAsync(
            DBContext db,
            CancellationToken cancellationToken =
                default)
        {
            ArgumentNullException.ThrowIfNull(db);

            Interfaz? interfaz =
                await db.Interfaz
                    .FirstOrDefaultAsync(
                        item =>
                            item.nombreInterfaz ==
                            CodigoInterfaz,
                        cancellationToken);

            bool guardar = false;

            if (interfaz == null)
            {
                interfaz =
                    new Interfaz
                    {
                        nombreInterfaz =
                            CodigoInterfaz,
                        nombreAmigableInterfaz =
                            NombreAmigable,
                        descripcionInterfaz =
                            Descripcion,
                        activo = true
                    };

                db.Interfaz.Add(interfaz);
                guardar = true;
            }
            else
            {
                if (!string.Equals(
                        interfaz
                            .nombreAmigableInterfaz,
                        NombreAmigable,
                        StringComparison.Ordinal))
                {
                    interfaz
                        .nombreAmigableInterfaz =
                        NombreAmigable;

                    guardar = true;
                }

                if (!string.Equals(
                        interfaz
                            .descripcionInterfaz,
                        Descripcion,
                        StringComparison.Ordinal))
                {
                    interfaz
                        .descripcionInterfaz =
                        Descripcion;

                    guardar = true;
                }

                if (!interfaz.activo)
                {
                    interfaz.activo = true;
                    guardar = true;
                }
            }

            if (guardar)
            {
                await db.SaveChangesAsync(
                    cancellationToken);
            }

            List<int> rolesAdministradores =
                await db.Roles
                    .AsNoTracking()
                    .Where(rol =>
                        rol.activo &&
                        EF.Functions.Like(
                            rol.nombreRol,
                            "%ADMIN%"))
                    .Select(rol =>
                        rol.rolId)
                    .ToListAsync(
                        cancellationToken);

            if (rolesAdministradores.Count == 0)
                return;

            List<int> relacionesExistentes =
                await db.RolInterfaz
                    .AsNoTracking()
                    .Where(item =>
                        item.interfazId ==
                            interfaz.interfazId &&
                        rolesAdministradores
                            .Contains(
                                item.rolId))
                    .Select(item =>
                        item.rolId)
                    .ToListAsync(
                        cancellationToken);

            foreach (int rolId
                     in rolesAdministradores)
            {
                if (relacionesExistentes
                    .Contains(rolId))
                {
                    continue;
                }

                db.RolInterfaz.Add(
                    new RolInterfaz
                    {
                        rolId = rolId,
                        interfazId =
                            interfaz.interfazId,
                        leer = true,
                        agregar = false,
                        actualizar = false,
                        eliminar = false
                    });
            }

            await db.SaveChangesAsync(
                cancellationToken);
        }
    }
}
