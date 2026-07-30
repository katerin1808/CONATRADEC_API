namespace CONATRADEC_API.Security
{
    /// <summary>
    /// Configuración global de la seguridad de las sesiones.
    /// Puede administrarse con variables de entorno Jwt__*.
    /// </summary>
    public sealed class JwtOptions
    {
        public const string Seccion = "Jwt";

        public string Issuer { get; set; } =
            "CONATRADEC_API";

        public string Audience { get; set; } =
            "CONATRADEC_CLIENTS";

        /// <summary>
        /// Se recomienda configurarlo con Jwt__Secret.
        /// Si queda vacío, la API genera una clave segura temporal al arrancar.
        /// </summary>
        public string Secret { get; set; } =
            string.Empty;

        public int ExpirationHours { get; set; } = 12;

        public int InactivityMinutes { get; set; } = 15;

        public int ClockSkewSeconds { get; set; } = 30;
    }
}
