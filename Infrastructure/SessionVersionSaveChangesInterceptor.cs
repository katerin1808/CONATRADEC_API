using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Protege administradores y aumenta la versión de sesión cuando cambia
    /// un rol o la matriz de permisos de ese rol.
    /// </summary>
    public sealed class SessionVersionSaveChangesInterceptor :
        SaveChangesInterceptor
    {
        private readonly HashSet<int> rolesConPermisosModificados = new();

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            if (eventData.Context is DBContext db)
                PrepararCambiosSincrono(db);

            return base.SavingChanges(eventData, result);
        }

        public override async ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (eventData.Context is DBContext db)
            {
                await PrepararCambiosAsync(
                    db,
                    cancellationToken);
            }

            return await base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }

        public override int SavedChanges(
            SaveChangesCompletedEventData eventData,
            int result)
        {
            if (eventData.Context is DBContext db)
                InvalidarUsuariosPorRolesSincrono(db);

            return base.SavedChanges(eventData, result);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is DBContext db)
            {
                await InvalidarUsuariosPorRolesAsync(
                    db,
                    cancellationToken);
            }

            return await base.SavedChangesAsync(
                eventData,
                result,
                cancellationToken);
        }

        public override void SaveChangesFailed(
            DbContextErrorEventData eventData)
        {
            rolesConPermisosModificados.Clear();
            base.SaveChangesFailed(eventData);
        }

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            rolesConPermisosModificados.Clear();
            return base.SaveChangesFailedAsync(
                eventData,
                cancellationToken);
        }

        private void PrepararCambiosSincrono(DBContext db)
        {
            List<EntityEntry<Usuario>> usuarios = db.ChangeTracker
                .Entries<Usuario>()
                .Where(item =>
                    item.State is EntityState.Modified or EntityState.Deleted)
                .ToList();

            HashSet<int> rolIds = ObtenerRolesNecesarios(usuarios);

            Dictionary<int, string> nombresRoles = db.Roles
                .AsNoTracking()
                .Where(item => rolIds.Contains(item.rolId))
                .ToDictionary(
                    item => item.rolId,
                    item => item.nombreRol);

            AplicarProteccionYVersion(
                usuarios,
                nombresRoles);

            CapturarRolesConPermisosModificados(db);
        }

        private async Task PrepararCambiosAsync(
            DBContext db,
            CancellationToken cancellationToken)
        {
            List<EntityEntry<Usuario>> usuarios = db.ChangeTracker
                .Entries<Usuario>()
                .Where(item =>
                    item.State is EntityState.Modified or EntityState.Deleted)
                .ToList();

            HashSet<int> rolIds = ObtenerRolesNecesarios(usuarios);

            Dictionary<int, string> nombresRoles = await db.Roles
                .AsNoTracking()
                .Where(item => rolIds.Contains(item.rolId))
                .ToDictionaryAsync(
                    item => item.rolId,
                    item => item.nombreRol,
                    cancellationToken);

            AplicarProteccionYVersion(
                usuarios,
                nombresRoles);

            CapturarRolesConPermisosModificados(db);
        }

        private static HashSet<int> ObtenerRolesNecesarios(
            IEnumerable<EntityEntry<Usuario>> usuarios)
        {
            var resultado = new HashSet<int>();

            foreach (EntityEntry<Usuario> entry in usuarios)
            {
                resultado.Add(entry.Entity.rolId);

                if (entry.State == EntityState.Modified)
                {
                    resultado.Add(
                        entry.Property(item => item.rolId)
                            .OriginalValue);
                }
            }

            return resultado;
        }

        private static void AplicarProteccionYVersion(
            IEnumerable<EntityEntry<Usuario>> usuarios,
            IReadOnlyDictionary<int, string> nombresRoles)
        {
            foreach (EntityEntry<Usuario> entry in usuarios)
            {
                int rolOriginal = entry.State == EntityState.Modified
                    ? entry.Property(item => item.rolId).OriginalValue
                    : entry.Entity.rolId;

                int rolActual = entry.Entity.rolId;

                bool eraAdministrador = EsAdministrador(
                    ObtenerNombreRol(nombresRoles, rolOriginal));

                bool esAdministrador = EsAdministrador(
                    ObtenerNombreRol(nombresRoles, rolActual));

                bool seEstaDesactivando =
                    entry.State == EntityState.Deleted ||
                    (entry.State == EntityState.Modified &&
                     entry.Property(item => item.activo).IsModified &&
                     !entry.Entity.activo);

                if (seEstaDesactivando &&
                    (eraAdministrador || esAdministrador))
                {
                    throw new UsuarioAdministradorProtegidoException();
                }

                if (entry.State == EntityState.Modified &&
                    entry.Property(item => item.rolId).IsModified &&
                    rolOriginal != rolActual)
                {
                    entry.Entity.versionSesion = Math.Max(
                        1,
                        entry.Entity.versionSesion) + 1;

                    entry.Property(item => item.versionSesion)
                        .IsModified = true;
                }
            }
        }

        private void CapturarRolesConPermisosModificados(DBContext db)
        {
            foreach (EntityEntry<RolInterfaz> entry in db.ChangeTracker
                         .Entries<RolInterfaz>()
                         .Where(item => item.State is
                             EntityState.Added or
                             EntityState.Modified or
                             EntityState.Deleted))
            {
                int rolId = entry.State == EntityState.Deleted
                    ? entry.Property(item => item.rolId).OriginalValue
                    : entry.Entity.rolId;

                if (rolId > 0)
                    rolesConPermisosModificados.Add(rolId);
            }
        }

        private void InvalidarUsuariosPorRolesSincrono(DBContext db)
        {
            try
            {
                foreach (int rolId in rolesConPermisosModificados)
                {
                    db.Database.ExecuteSqlInterpolated($"""
                        UPDATE dbo.usuario
                           SET versionSesion =
                               CASE
                                   WHEN ISNULL(versionSesion, 0) < 1 THEN 2
                                   ELSE versionSesion + 1
                               END
                         WHERE rolId = {rolId}
                           AND activo = 1;
                        """);
                }
            }
            finally
            {
                rolesConPermisosModificados.Clear();
            }
        }

        private async Task InvalidarUsuariosPorRolesAsync(
            DBContext db,
            CancellationToken cancellationToken)
        {
            try
            {
                foreach (int rolId in rolesConPermisosModificados)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE dbo.usuario
                           SET versionSesion =
                               CASE
                                   WHEN ISNULL(versionSesion, 0) < 1 THEN 2
                                   ELSE versionSesion + 1
                               END
                         WHERE rolId = {rolId}
                           AND activo = 1;
                        """,
                        cancellationToken);
                }
            }
            finally
            {
                rolesConPermisosModificados.Clear();
            }
        }

        private static string ObtenerNombreRol(
            IReadOnlyDictionary<int, string> roles,
            int rolId) =>
            roles.TryGetValue(rolId, out string? nombre)
                ? nombre
                : string.Empty;

        private static bool EsAdministrador(string? nombreRol) =>
            string.Equals(
                nombreRol?.Trim(),
                "Administrador",
                StringComparison.OrdinalIgnoreCase);
    }
}
