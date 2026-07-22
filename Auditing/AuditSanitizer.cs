using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CONATRADEC_API.Auditing
{
    public static class AuditSanitizer
    {
        private static readonly string[] PalabrasSensibles =
        {
            "clave",
            "password",
            "contrasena",
            "contraseña",
            "hash",
            "token",
            "jwt",
            "secret",
            "secreto",
            "salt",
            "authorization",
            "cookie",
            "session",
            "credential",
            "credencial",
            "apikey",
            "api-key",
            "accesskey",
            "privatekey",
            "refresh"
        };

        private static readonly Regex PatronAsignacionSensible =
            new(
                @"(?ix)
                \b(
                    password|pwd|clave|contrasena|contraseña|
                    token|jwt|secret|secreto|authorization|
                    api[-_]?key|access[-_]?key|private[-_]?key|
                    refresh[-_]?token
                )\b
                \s*[:=]\s*
                (""[^""]*""|'[^']*'|[^;,\s]+)",
                RegexOptions.Compiled |
                RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));

        public static bool EsSensible(
            string? nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                return false;

            return PalabrasSensibles.Any(x =>
                nombre.Contains(
                    x,
                    StringComparison.OrdinalIgnoreCase));
        }

        public static string SanitizarJson(
            string? json,
            int maximo = 16000)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            try
            {
                JsonNode? node = JsonNode.Parse(json);

                if (node == null)
                    return SanitizarTexto(json, maximo);

                SanitizarNodo(node);

                return Truncar(
                    node.ToJsonString(
                        new JsonSerializerOptions
                        {
                            WriteIndented = false
                        }),
                    maximo);
            }
            catch
            {
                return SanitizarTexto(
                    json,
                    maximo);
            }
        }

        /// <summary>
        /// Aplica una protección básica a mensajes que no son JSON,
        /// por ejemplo errores de texto o mensajes de excepciones.
        /// </summary>
        public static string SanitizarTexto(
            string? texto,
            int maximo = 16000)
        {
            if (string.IsNullOrWhiteSpace(texto))
                return string.Empty;

            string protegido;

            try
            {
                protegido = PatronAsignacionSensible.Replace(
                    texto,
                    coincidencia =>
                        $"{coincidencia.Groups[1].Value}=" +
                        "***PROTEGIDO***");
            }
            catch (RegexMatchTimeoutException)
            {
                protegido = texto;
            }

            return Truncar(
                protegido,
                maximo);
        }

        public static string Truncar(
            string? valor,
            int maximo)
        {
            if (string.IsNullOrEmpty(valor) ||
                valor.Length <= maximo)
            {
                return valor ?? string.Empty;
            }

            return valor[..maximo] + "…";
        }

        private static void SanitizarNodo(
            JsonNode node)
        {
            if (node is JsonObject objeto)
            {
                foreach (string nombre
                         in objeto.Select(x => x.Key).ToList())
                {
                    JsonNode? valor = objeto[nombre];

                    if (EsSensible(nombre))
                    {
                        objeto[nombre] =
                            "***PROTEGIDO***";

                        continue;
                    }

                    if (valor != null)
                        SanitizarNodo(valor);
                }
            }
            else if (node is JsonArray arreglo)
            {
                foreach (JsonNode? elemento in arreglo)
                {
                    if (elemento != null)
                        SanitizarNodo(elemento);
                }
            }
        }
    }
}
