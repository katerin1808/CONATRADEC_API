using CONATRADEC_API.Models;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CONATRADEC_API.Security
{
    public sealed record TokenSesionEmitido(
        string AccessToken,
        DateTime ExpiraUtc,
        int MinutosInactividad);

    /// <summary>
    /// Emite un JWT firmado y registra su identificador jti en SQL Server.
    /// El token solamente se devuelve cuando la sesión quedó persistida.
    /// </summary>
    public sealed class JwtTokenService
    {
        private readonly IOptions<JwtOptions> options;
        private readonly JwtKeyMaterial keyMaterial;
        private readonly SesionActivaService sesionActivaService;

        public JwtTokenService(
            IOptions<JwtOptions> options,
            JwtKeyMaterial keyMaterial,
            SesionActivaService sesionActivaService)
        {
            this.options = options;
            this.keyMaterial = keyMaterial;
            this.sesionActivaService = sesionActivaService;
        }

        public async Task<TokenSesionEmitido> CrearAsync(
            Usuario usuario,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(usuario);

            JwtOptions configuracion =
                options.Value;

            DateTime ahoraUtc =
                DateTime.UtcNow;

            DateTime expiraUtc =
                ahoraUtc.AddHours(
                    Math.Clamp(
                        configuracion.ExpirationHours,
                        1,
                        168));

            int minutosInactividad =
                Math.Clamp(
                    configuracion.InactivityMinutes,
                    1,
                    1440);

            string sesionId =
                Guid.NewGuid().ToString("N");

            string nombre =
                string.IsNullOrWhiteSpace(
                    usuario.nombreCompletoUsuario)
                    ? usuario.nombreUsuario
                    : usuario.nombreCompletoUsuario;

            string rol =
                usuario.Rol?.nombreRol ??
                string.Empty;

            string usuarioId =
                usuario.UsuarioId.ToString();

            /*
             * La API utiliza MapInboundClaims = false. Por eso se mantienen
             * los identificadores JWT actuales (sub y uid) y se agregan los
             * nombres que todavía consumen algunos controladores existentes.
             *
             * Esto no concede permisos ni depende del nombre del rol.
             * La autorización continúa resolviéndose mediante los permisos
             * persistidos para el rol del usuario.
             */
            Claim[] claims =
            [
                new(
                    JwtRegisteredClaimNames.Sub,
                    usuarioId),
                new(
                    "uid",
                    usuarioId),
                new(
                    ClaimTypes.NameIdentifier,
                    usuarioId),
                new(
                    "UsuarioId",
                    usuarioId),
                new(
                    "sv",
                    usuario.versionSesion.ToString()),
                new(
                    JwtRegisteredClaimNames.Jti,
                    sesionId),
                new(
                    "name",
                    nombre),
                new(
                    "username",
                    usuario.nombreUsuario),
                new(
                    "role",
                    rol)
            ];

            var token =
                new JwtSecurityToken(
                    issuer: configuracion.Issuer,
                    audience: configuracion.Audience,
                    claims: claims,
                    notBefore: ahoraUtc,
                    expires: expiraUtc,
                    signingCredentials:
                        keyMaterial.SigningCredentials);

            string accessToken =
                new JwtSecurityTokenHandler()
                    .WriteToken(token);

            await sesionActivaService.RegistrarAsync(
                sesionId,
                usuario.UsuarioId,
                usuario.versionSesion,
                expiraUtc,
                cancellationToken);

            return new TokenSesionEmitido(
                accessToken,
                expiraUtc,
                minutosInactividad);
        }
    }
}
