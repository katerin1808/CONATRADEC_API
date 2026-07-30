using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

namespace CONATRADEC_API.Security
{
    public static class JwtSecurityExtensions
    {
        public static IServiceCollection AddConatradecJwt(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            IConfigurationSection section =
                configuration.GetSection(
                    JwtOptions.Seccion);

            JwtOptions values =
                section.Get<JwtOptions>() ??
                new JwtOptions();

            values.Issuer =
                string.IsNullOrWhiteSpace(values.Issuer)
                    ? "CONATRADEC_API"
                    : values.Issuer.Trim();

            values.Audience =
                string.IsNullOrWhiteSpace(values.Audience)
                    ? "CONATRADEC_CLIENTS"
                    : values.Audience.Trim();

            string secret =
                values.Secret?.Trim() ??
                string.Empty;

            bool esEfimera =
                Encoding.UTF8.GetByteCount(secret) < 32;

            byte[] keyBytes =
                esEfimera
                    ? RandomNumberGenerator.GetBytes(64)
                    : Encoding.UTF8.GetBytes(secret);

            var keyMaterial =
                new JwtKeyMaterial(
                    keyBytes,
                    esEfimera);

            services.AddSingleton(keyMaterial);

            services
                .AddOptions<JwtOptions>()
                .Bind(section);

            services.PostConfigure<JwtOptions>(
                options =>
                {
                    options.Issuer =
                        string.IsNullOrWhiteSpace(options.Issuer)
                            ? values.Issuer
                            : options.Issuer.Trim();

                    options.Audience =
                        string.IsNullOrWhiteSpace(options.Audience)
                            ? values.Audience
                            : options.Audience.Trim();

                    options.ExpirationHours =
                        Math.Clamp(
                            options.ExpirationHours,
                            1,
                            168);

                    options.InactivityMinutes =
                        Math.Clamp(
                            options.InactivityMinutes,
                            1,
                            1440);

                    options.ClockSkewSeconds =
                        Math.Clamp(
                            options.ClockSkewSeconds,
                            0,
                            300);
                });

            services
                .AddAuthentication(
                    JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(
                    options =>
                    {
                        options.MapInboundClaims = false;

                        options.TokenValidationParameters =
                            new TokenValidationParameters
                            {
                                ValidateIssuer = true,
                                ValidIssuer = values.Issuer,
                                ValidateAudience = true,
                                ValidAudience = values.Audience,
                                ValidateIssuerSigningKey = true,
                                IssuerSigningKey =
                                    keyMaterial.SecurityKey,
                                ValidateLifetime = true,
                                RequireExpirationTime = true,
                                ClockSkew =
                                    TimeSpan.FromSeconds(
                                        Math.Clamp(
                                            values.ClockSkewSeconds,
                                            0,
                                            300)),
                                NameClaimType = "name",
                                RoleClaimType = "role"
                            };

                        options.Events =
                            new JwtBearerEvents
                            {
                                OnAuthenticationFailed =
                                    context =>
                                    {
                                        context.HttpContext.Items[
                                            JwtSessionMiddleware
                                                .ItemAuthenticationError] =
                                            context.Exception
                                                is SecurityTokenExpiredException
                                                    ? "SESSION_TOKEN_EXPIRED"
                                                    : "AUTH_TOKEN_INVALID";

                                        return Task.CompletedTask;
                                    }
                            };
                    });

            services.AddAuthorization();
            services.AddSingleton<SesionActivaService>();
            services.AddSingleton<JwtTokenService>();

            return services;
        }
    }
}
