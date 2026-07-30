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
    /// Emite un JWT firmado y registra su identificador jti como sesión activa.
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

        public TokenSesionEmitido Crear(
            Usuario usuario)
        {
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

            Claim[] claims =
            [
                new(
                    JwtRegisteredClaimNames.Sub,
                    usuario.UsuarioId.ToString()),
                new(
                    "uid",
                    usuario.UsuarioId.ToString()),
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

            sesionActivaService.Registrar(
                sesionId,
                usuario.UsuarioId,
                usuario.versionSesion,
                expiraUtc);

            return new TokenSesionEmitido(
                accessToken,
                expiraUtc,
                minutosInactividad);
        }
    }
}
