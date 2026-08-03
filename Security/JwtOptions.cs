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
        /// Clave privada utilizada para firmar los JWT.
        /// Debe contener como mínimo 32 bytes y permanecer estable entre
        /// publicaciones, reinicios y múltiples instancias del backend.
        /// </summary>
        public string Secret { get; set; } =
            string.Empty;

        public int ExpirationHours { get; set; } = 12;

        public int InactivityMinutes { get; set; } = 15;

        public int ClockSkewSeconds { get; set; } = 30;

        /// <summary>
        /// Evita escribir en SQL Server en cada solicitud. Aunque el cliente
        /// reporte actividad real, la fecha se persiste como máximo una vez
        /// dentro de este intervalo por sesión.
        /// </summary>
        public int ActivityUpdateSeconds { get; set; } = 60;

        /// <summary>
        /// Tiempo máximo durante el cual una sesión validada puede reutilizarse
        /// desde la caché local del proceso antes de consultar nuevamente SQL.
        ///
        /// SQL Server continúa siendo la fuente definitiva. Esta caché solo
        /// reduce la presión causada por múltiples solicitudes consecutivas.
        /// </summary>
        public int SessionCacheSeconds { get; set; } = 15;
    }
}
