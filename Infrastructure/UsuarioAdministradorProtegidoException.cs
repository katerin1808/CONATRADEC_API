namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Se lanza cuando se intenta desactivar un usuario administrador.
    /// </summary>
    public sealed class UsuarioAdministradorProtegidoException : Exception
    {
        public UsuarioAdministradorProtegidoException()
            : base("El usuario administrador es un registro protegido y no puede desactivarse.")
        {
        }
    }
}
