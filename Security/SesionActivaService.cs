using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace CONATRADEC_API.Security
{
    public enum EstadoSesionToken
    {
        Valida,
        NoRegistrada,
        NoCoincide,
        Inactiva,
        Expirada
    }

    /// <summary>
    /// Registro en memoria de las sesiones emitidas por esta instancia.
    /// Solamente una interacción real reportada por Android, Windows o la web
    /// renueva la última actividad; los procesos automáticos no la renuevan.
    /// </summary>
    public sealed class SesionActivaService
    {
        private readonly ConcurrentDictionary<
            string,
            SesionActiva> sesiones = new();

        private readonly IOptions<JwtOptions> options;
        private long registrosCreados;

        public SesionActivaService(
            IOptions<JwtOptions> options)
        {
            this.options = options;
        }

        public void Registrar(
            string sesionId,
            int usuarioId,
            int versionSesion,
            DateTime expiraUtc)
        {
            DateTime ahoraUtc = DateTime.UtcNow;

            sesiones[sesionId] =
                new SesionActiva(
                    usuarioId,
                    versionSesion,
                    ahoraUtc,
                    expiraUtc);

            if (Interlocked.Increment(
                    ref registrosCreados) % 100 == 0)
            {
                LimpiarExpiradas(ahoraUtc);
            }
        }

        public EstadoSesionToken ValidarYRegistrarActividad(
            string sesionId,
            int usuarioId,
            int versionSesion,
            bool registrarActividad)
        {
            while (true)
            {
                if (!sesiones.TryGetValue(
                        sesionId,
                        out SesionActiva? sesion))
                {
                    return EstadoSesionToken.NoRegistrada;
                }

                if (sesion.UsuarioId != usuarioId ||
                    sesion.VersionSesion != versionSesion)
                {
                    sesiones.TryRemove(
                        sesionId,
                        out _);

                    return EstadoSesionToken.NoCoincide;
                }

                DateTime ahoraUtc = DateTime.UtcNow;

                if (ahoraUtc >= sesion.ExpiraUtc)
                {
                    sesiones.TryRemove(
                        sesionId,
                        out _);

                    return EstadoSesionToken.Expirada;
                }

                int minutosInactividad =
                    Math.Clamp(
                        options.Value.InactivityMinutes,
                        1,
                        1440);

                if (ahoraUtc - sesion.UltimaActividadUtc >=
                    TimeSpan.FromMinutes(
                        minutosInactividad))
                {
                    sesiones.TryRemove(
                        sesionId,
                        out _);

                    return EstadoSesionToken.Inactiva;
                }

                if (!registrarActividad)
                    return EstadoSesionToken.Valida;

                SesionActiva actualizada =
                    sesion with
                    {
                        UltimaActividadUtc = ahoraUtc
                    };

                if (sesiones.TryUpdate(
                        sesionId,
                        actualizada,
                        sesion))
                {
                    return EstadoSesionToken.Valida;
                }
            }
        }

        public void Revocar(string sesionId)
        {
            if (string.IsNullOrWhiteSpace(sesionId))
                return;

            sesiones.TryRemove(
                sesionId,
                out _);
        }

        private void LimpiarExpiradas(
            DateTime ahoraUtc)
        {
            foreach (KeyValuePair<
                         string,
                         SesionActiva> item
                     in sesiones)
            {
                if (ahoraUtc >= item.Value.ExpiraUtc)
                {
                    sesiones.TryRemove(
                        item.Key,
                        out _);
                }
            }
        }

        private sealed record SesionActiva(
            int UsuarioId,
            int VersionSesion,
            DateTime UltimaActividadUtc,
            DateTime ExpiraUtc);
    }
}
