using Microsoft.AspNetCore.DataProtection;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace CONATRADEC_API.Infrastructure
{
    /// <summary>
    /// Genera permisos de descarga cifrados y con caducidad. El mismo permiso
    /// puede reutilizarse durante su vigencia para solicitudes HTTP Range.
    /// </summary>
    public static class ActualizacionDescargaTokenService
    {
        public const string ItemOperacionId =
            "ActualizacionDescarga.OperacionId";

        public const string ItemLlaveId =
            "ActualizacionDescarga.LlaveId";

        private const string Proposito =
            "CONATRADEC.Actualizaciones.DescargaProtegida.v1";

        private const string PrefijoCookie =
            "cntr_descarga_permiso_";

        private static readonly ConcurrentDictionary<string, ITimeLimitedDataProtector>
            Protectores = new(StringComparer.OrdinalIgnoreCase);

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        public static string Crear(
            IWebHostEnvironment environment,
            int actualizacionId,
            int? llaveId,
            string operacionId,
            TimeSpan vigencia)
        {
            var payload = new PermisoDescargaPayload
            {
                ActualizacionAplicacionId = actualizacionId,
                ActualizacionLlaveDescargaId = llaveId,
                OperacionId = operacionId
            };

            string contenido = JsonSerializer.Serialize(
                payload,
                JsonOptions);

            return ObtenerProtector(environment)
                .Protect(contenido, vigencia);
        }

        public static bool TryValidar(
            IWebHostEnvironment environment,
            string? token,
            int actualizacionEsperadaId,
            out PermisoDescargaPayload payload)
        {
            payload = new PermisoDescargaPayload();

            if (string.IsNullOrWhiteSpace(token))
                return false;

            try
            {
                string contenido = ObtenerProtector(environment)
                    .Unprotect(token);

                PermisoDescargaPayload? resultado =
                    JsonSerializer.Deserialize<PermisoDescargaPayload>(
                        contenido,
                        JsonOptions);

                if (resultado == null ||
                    resultado.ActualizacionAplicacionId !=
                        actualizacionEsperadaId ||
                    string.IsNullOrWhiteSpace(resultado.OperacionId))
                {
                    return false;
                }

                payload = resultado;
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static string ObtenerNombreCookie(int actualizacionId) =>
            PrefijoCookie + actualizacionId;

        public static string NuevaOperacionId() =>
            Convert.ToHexString(
                    RandomNumberGenerator.GetBytes(24))
                .ToLowerInvariant();

        private static ITimeLimitedDataProtector ObtenerProtector(
            IWebHostEnvironment environment)
        {
            string carpeta = Path.Combine(
                environment.ContentRootPath,
                "resources",
                "security",
                "actualizaciones-data-protection");

            Directory.CreateDirectory(carpeta);

            return Protectores.GetOrAdd(
                carpeta,
                static ruta =>
                {
                    IDataProtectionProvider provider =
                        DataProtectionProvider.Create(
                            new DirectoryInfo(ruta),
                            configuration =>
                                configuration.SetApplicationName(
                                    "CONATRADEC_API"));

                    return provider
                        .CreateProtector(Proposito)
                        .ToTimeLimitedDataProtector();
                });
        }
    }

    public sealed class PermisoDescargaPayload
    {
        public int ActualizacionAplicacionId { get; set; }
        public int? ActualizacionLlaveDescargaId { get; set; }
        public string OperacionId { get; set; } = string.Empty;
    }
}
