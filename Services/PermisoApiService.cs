using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Services
{
    public enum TipoPermisoApi
    {
        Leer,
        Agregar,
        Actualizar,
        Eliminar,
        AgregarOActualizar,
        Administrar
    }

    public sealed class ResultadoPermisoApi
    {
        public bool Permitido { get; init; }
        public int CodigoEstado { get; init; }
        public string Mensaje { get; init; } = string.Empty;

        public static ResultadoPermisoApi Ok() =>
            new()
            {
                Permitido = true,
                CodigoEstado = StatusCodes.Status200OK
            };

        public static ResultadoPermisoApi Denegado(
            int codigoEstado,
            string mensaje) =>
            new()
            {
                Permitido = false,
                CodigoEstado = codigoEstado,
                Mensaje = mensaje
            };
    }

    public sealed class PermisoApiService
    {
        private readonly DBContext db;

        public PermisoApiService(DBContext db)
        {
            this.db = db;
        }

        public async Task<ResultadoPermisoApi> ValidarAsync(
            int? usuarioId,
            string nombreInterfaz,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken = default)
        {
            if (!usuarioId.HasValue || usuarioId.Value <= 0)
            {
                return ResultadoPermisoApi.Denegado(
                    StatusCodes.Status401Unauthorized,
                    "No se encontró el usuario autenticado. Cierre sesión e ingrese nuevamente.");
            }

            var datos = await (
                from usuario in db.Usuarios.AsNoTracking()
                join rolInterfaz in db.RolInterfaz.AsNoTracking()
                    on usuario.rolId equals rolInterfaz.rolId
                join interfaz in db.Interfaz.AsNoTracking()
                    on rolInterfaz.interfazId equals interfaz.interfazId
                where usuario.UsuarioId == usuarioId.Value
                      && usuario.activo
                      && interfaz.activo
                      && interfaz.nombreInterfaz == nombreInterfaz
                select new
                {
                    rolInterfaz.leer,
                    rolInterfaz.agregar,
                    rolInterfaz.actualizar,
                    rolInterfaz.eliminar
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (datos == null)
            {
                bool usuarioActivo = await db.Usuarios
                    .AsNoTracking()
                    .AnyAsync(
                        x => x.UsuarioId == usuarioId.Value && x.activo,
                        cancellationToken);

                return ResultadoPermisoApi.Denegado(
                    usuarioActivo
                        ? StatusCodes.Status403Forbidden
                        : StatusCodes.Status401Unauthorized,
                    usuarioActivo
                        ? "Su rol no tiene acceso al módulo de noticias."
                        : "El usuario autenticado no existe o se encuentra inactivo.");
            }

            bool permitido = permiso switch
            {
                TipoPermisoApi.Leer => datos.leer == true,
                TipoPermisoApi.Agregar => datos.agregar == true,
                TipoPermisoApi.Actualizar => datos.actualizar == true,
                TipoPermisoApi.Eliminar => datos.eliminar == true,
                TipoPermisoApi.AgregarOActualizar =>
                    datos.agregar == true || datos.actualizar == true,
                TipoPermisoApi.Administrar =>
                    datos.agregar == true ||
                    datos.actualizar == true ||
                    datos.eliminar == true,
                _ => false
            };

            return permitido
                ? ResultadoPermisoApi.Ok()
                : ResultadoPermisoApi.Denegado(
                    StatusCodes.Status403Forbidden,
                    "No tiene permiso para realizar esta operación en el módulo de noticias.");
        }
    }
}
