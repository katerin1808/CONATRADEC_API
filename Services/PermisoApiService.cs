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

            /*
             * La autorización depende exclusivamente del rolId real asociado
             * al usuario y de los permisos persistidos en rolInterfaz.
             *
             * Algunas bases históricas pueden contener más de una relación para
             * el mismo rol e interfaz. No debemos depender de FirstOrDefault(),
             * porque SQL Server no garantiza qué fila devolverá primero y una
             * relación antigua podría negar un permiso que otra relación vigente
             * tiene habilitado. Se consolidan todas las relaciones equivalentes
             * utilizando la misma regla que los clientes: el permiso queda
             * habilitado cuando al menos una relación persistida lo habilita.
             */
            var relaciones = await (
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
                .ToListAsync(cancellationToken);

            if (relaciones.Count == 0)
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
                        ? "Su rol no tiene acceso a este módulo."
                        : "El usuario autenticado no existe o se encuentra inactivo.");
            }

            bool leer = relaciones.Any(item => item.leer == true);
            bool agregar = relaciones.Any(item => item.agregar == true);
            bool actualizar = relaciones.Any(item => item.actualizar == true);
            bool eliminar = relaciones.Any(item => item.eliminar == true);

            bool permitido = permiso switch
            {
                TipoPermisoApi.Leer => leer,
                TipoPermisoApi.Agregar => agregar,
                TipoPermisoApi.Actualizar => actualizar,
                TipoPermisoApi.Eliminar => eliminar,
                TipoPermisoApi.AgregarOActualizar =>
                    agregar || actualizar,

                /*
                 * Consultar pantallas administrativas requiere poder leer la
                 * interfaz y, además, poseer al menos una acción de gestión.
                 * Las mutaciones continúan validando Agregar/Actualizar/Eliminar
                 * de forma independiente en cada endpoint.
                 */
                TipoPermisoApi.Administrar =>
                    leer &&
                    (agregar || actualizar || eliminar),

                _ => false
            };

            return permitido
                ? ResultadoPermisoApi.Ok()
                : ResultadoPermisoApi.Denegado(
                    StatusCodes.Status403Forbidden,
                    "No tiene permiso para realizar esta operación.");
        }
    }
}
