using System.Text.Json;
using System.Text.Json.Nodes;

namespace CONATRADEC_API.Auditing
{
    public static class AuditSanitizer
    {
        private static readonly string[] PalabrasSensibles =
        {
            "clave", "password", "contrasena", "contraseña", "hash",
            "token", "secret", "secreto", "salt", "authorization"
        };

        public static bool EsSensible(string? nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                return false;

            return PalabrasSensibles.Any(x =>
                nombre.Contains(x, StringComparison.OrdinalIgnoreCase));
        }

        public static string SanitizarJson(string? json, int maximo = 16000)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            try
            {
                JsonNode? node = JsonNode.Parse(json);
                if (node == null)
                    return Truncar(json, maximo);

                SanitizarNodo(node);

                return Truncar(
                    node.ToJsonString(new JsonSerializerOptions
                    {
                        WriteIndented = false
                    }),
                    maximo);
            }
            catch
            {
                return Truncar(json, maximo);
            }
        }

        public static string Truncar(string? valor, int maximo)
        {
            if (string.IsNullOrEmpty(valor) || valor.Length <= maximo)
                return valor ?? string.Empty;

            return valor[..maximo] + "…";
        }

        private static void SanitizarNodo(JsonNode node)
        {
            if (node is JsonObject objeto)
            {
                foreach (string nombre in objeto.Select(x => x.Key).ToList())
                {
                    JsonNode? valor = objeto[nombre];

                    if (EsSensible(nombre))
                    {
                        objeto[nombre] = "***PROTEGIDO***";
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
