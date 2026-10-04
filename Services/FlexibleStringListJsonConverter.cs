using System.Text.Json;
using System.Text.Json.Serialization;

namespace CONATRADEC_API.Services
{
    /// <summary>
    /// Tolera pequeñas variaciones de formato en listas de texto devueltas por
    /// proveedores de IA. Acepta un arreglo JSON, un texto único o null y
    /// siempre expone una lista segura para el resto del flujo fitosanitario.
    /// </summary>
    public sealed class FlexibleStringListJsonConverter :
        JsonConverter<List<string>>
    {
        public override bool HandleNull => true;

        public override List<string>? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return [];

            if (reader.TokenType == JsonTokenType.String)
                return CrearLista(reader.GetString());

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                using JsonDocument valor = JsonDocument.ParseValue(ref reader);
                return CrearLista(ConvertirEscalar(valor.RootElement));
            }

            var resultado = new List<string>();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    break;

                if (reader.TokenType == JsonTokenType.Null)
                    continue;

                if (reader.TokenType == JsonTokenType.String)
                {
                    Agregar(resultado, reader.GetString());
                    continue;
                }

                using JsonDocument valor = JsonDocument.ParseValue(ref reader);
                Agregar(resultado, ConvertirEscalar(valor.RootElement));
            }

            return resultado;
        }

        public override void Write(
            Utf8JsonWriter writer,
            List<string> value,
            JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartArray();

            foreach (string item in value)
                writer.WriteStringValue(item ?? string.Empty);

            writer.WriteEndArray();
        }

        private static List<string> CrearLista(string? valor)
        {
            var resultado = new List<string>();
            Agregar(resultado, valor);
            return resultado;
        }

        private static void Agregar(
            ICollection<string> destino,
            string? valor)
        {
            string texto = (valor ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(texto))
                destino.Add(texto);
        }

        private static string? ConvertirEscalar(JsonElement elemento) =>
            elemento.ValueKind switch
            {
                JsonValueKind.String => elemento.GetString(),
                JsonValueKind.Number => elemento.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
    }
}
