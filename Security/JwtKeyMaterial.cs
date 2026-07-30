using Microsoft.IdentityModel.Tokens;

namespace CONATRADEC_API.Security
{
    /// <summary>
    /// Clave utilizada tanto para firmar como para validar los JWT.
    /// </summary>
    public sealed class JwtKeyMaterial
    {
        public JwtKeyMaterial(
            byte[] keyBytes,
            bool esEfimera)
        {
            ArgumentNullException.ThrowIfNull(keyBytes);

            SecurityKey =
                new SymmetricSecurityKey(keyBytes);

            SigningCredentials =
                new SigningCredentials(
                    SecurityKey,
                    SecurityAlgorithms.HmacSha256);

            EsEfimera = esEfimera;
        }

        public SymmetricSecurityKey SecurityKey { get; }

        public SigningCredentials SigningCredentials { get; }

        public bool EsEfimera { get; }
    }
}
