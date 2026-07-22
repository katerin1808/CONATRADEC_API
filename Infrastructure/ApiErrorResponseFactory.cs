using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CONATRADEC_API.Infrastructure
{
    public static class ApiErrorResponseFactory
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };

        public static ApiErrorResponse Create(
            HttpContext httpContext,
            int statusCode,
            object? originalValue = null,
            string? message = null,
            IDictionary<string, string[]>? errors = null,
            string? code = null)
        {
            if (originalValue is ApiErrorResponse existing)
                return existing;

            IDictionary<string, string[]>? normalizedErrors =
                errors ?? ExtractErrors(originalValue);

            string finalMessage =
                CleanMessage(message)
                ?? ExtractMessage(originalValue)
                ?? (normalizedErrors is { Count: > 0 }
                    ? "Revise los campos indicados e intente nuevamente."
                    : GetDefaultMessage(statusCode));

            return new ApiErrorResponse
            {
                Success = false,
                Message = finalMessage,
                Code = string.IsNullOrWhiteSpace(code)
                    ? GetDefaultCode(statusCode)
                    : code.Trim(),
                Errors = normalizedErrors is { Count: > 0 }
                    ? normalizedErrors
                    : null,
                Details = ShouldPreserveDetails(originalValue)
                    ? originalValue
                    : null,
                TraceId = httpContext.TraceIdentifier
            };
        }

        public static IDictionary<string, string[]> FromModelState(
            ModelStateDictionary modelState)
        {
            var result = new Dictionary<string, string[]>(
                StringComparer.OrdinalIgnoreCase);

            foreach ((string key, ModelStateEntry? value) in modelState)
            {
                if (value?.Errors.Count is null or 0)
                    continue;

                string fieldName = NormalizeFieldName(key);

                string[] messages = value.Errors
                    .Select(error =>
                        TranslateValidationError(
                            error.ErrorMessage,
                            error.Exception,
                            fieldName))
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (messages.Length > 0)
                    result[fieldName] = messages;
            }

            return result;
        }

        public static string GetDefaultMessage(int statusCode) =>
            statusCode switch
            {
                StatusCodes.Status400BadRequest =>
                    "La solicitud contiene datos inválidos.",
                StatusCodes.Status401Unauthorized =>
                    "La sesión no está autorizada. Inicie sesión nuevamente.",
                StatusCodes.Status403Forbidden =>
                    "No tiene permisos para realizar esta operación.",
                StatusCodes.Status404NotFound =>
                    "No se encontró el registro solicitado.",
                StatusCodes.Status409Conflict =>
                    "La operación entra en conflicto con información existente.",
                StatusCodes.Status413PayloadTooLarge =>
                    "El archivo o contenido enviado supera el tamaño permitido.",
                StatusCodes.Status415UnsupportedMediaType =>
                    "El formato del contenido enviado no es compatible.",
                StatusCodes.Status422UnprocessableEntity =>
                    "No fue posible procesar los datos enviados.",
                StatusCodes.Status429TooManyRequests =>
                    "Se realizaron demasiadas solicitudes. Intente nuevamente.",
                StatusCodes.Status502BadGateway or
                StatusCodes.Status503ServiceUnavailable or
                StatusCodes.Status504GatewayTimeout =>
                    "El servicio no está disponible temporalmente. Intente nuevamente.",
                _ =>
                    "Ocurrió un error interno al procesar la solicitud."
            };

        public static string GetDefaultCode(int statusCode) =>
            statusCode switch
            {
                StatusCodes.Status400BadRequest => "VALIDATION_ERROR",
                StatusCodes.Status401Unauthorized => "UNAUTHORIZED",
                StatusCodes.Status403Forbidden => "FORBIDDEN",
                StatusCodes.Status404NotFound => "NOT_FOUND",
                StatusCodes.Status409Conflict => "CONFLICT",
                StatusCodes.Status413PayloadTooLarge => "PAYLOAD_TOO_LARGE",
                StatusCodes.Status415UnsupportedMediaType => "UNSUPPORTED_MEDIA_TYPE",
                StatusCodes.Status422UnprocessableEntity => "UNPROCESSABLE_ENTITY",
                StatusCodes.Status429TooManyRequests => "TOO_MANY_REQUESTS",
                StatusCodes.Status502BadGateway => "BAD_GATEWAY",
                StatusCodes.Status503ServiceUnavailable => "SERVICE_UNAVAILABLE",
                StatusCodes.Status504GatewayTimeout => "GATEWAY_TIMEOUT",
                _ => "INTERNAL_ERROR"
            };

        private static string? ExtractMessage(object? value)
        {
            if (value is null)
                return null;

            if (value is string text)
                return CleanMessage(text);

            if (value is ProblemDetails problem)
            {
                return CleanMessage(problem.Detail)
                    ?? CleanMessage(problem.Title);
            }

            try
            {
                JsonElement root = JsonSerializer.SerializeToElement(
                    value,
                    value.GetType(),
                    JsonOptions);

                return FindMessage(root, 0);
            }
            catch
            {
                return null;
            }
        }

        private static IDictionary<string, string[]>? ExtractErrors(
            object? value)
        {
            if (value is ValidationProblemDetails validation)
            {
                return validation.Errors.ToDictionary(
                    item => NormalizeFieldName(item.Key),
                    item => item.Value
                        .Select(message =>
                            TranslateValidationError(
                                message,
                                null,
                                NormalizeFieldName(item.Key)))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            }

            if (value is null || value is string)
                return null;

            try
            {
                JsonElement root = JsonSerializer.SerializeToElement(
                    value,
                    value.GetType(),
                    JsonOptions);

                if (!TryGetPropertyIgnoreCase(
                        root,
                        "errors",
                        out JsonElement errorsElement) ||
                    errorsElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var result = new Dictionary<string, string[]>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (JsonProperty property in errorsElement.EnumerateObject())
                {
                    var messages = new List<string>();

                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                string? message = item.GetString();

                                if (!string.IsNullOrWhiteSpace(message))
                                {
                                    messages.Add(
                                        TranslateValidationError(
                                            message,
                                            null,
                                            NormalizeFieldName(property.Name)));
                                }
                            }
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        string? message = property.Value.GetString();

                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            messages.Add(
                                TranslateValidationError(
                                    message,
                                    null,
                                    NormalizeFieldName(property.Name)));
                        }
                    }

                    if (messages.Count > 0)
                    {
                        result[NormalizeFieldName(property.Name)] =
                            messages
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray();
                    }
                }

                return result.Count > 0 ? result : null;
            }
            catch
            {
                return null;
            }
        }

        private static string? FindMessage(
            JsonElement element,
            int depth)
        {
            if (depth > 3)
                return null;

            if (element.ValueKind == JsonValueKind.String)
                return CleanMessage(element.GetString());

            if (element.ValueKind != JsonValueKind.Object)
                return null;

            foreach (string propertyName in new[]
                     {
                         "message",
                         "mensaje",
                         "detail",
                         "descripcion",
                         "description"
                     })
            {
                if (TryGetPropertyIgnoreCase(
                        element,
                        propertyName,
                        out JsonElement propertyValue) &&
                    propertyValue.ValueKind == JsonValueKind.String)
                {
                    string? message = CleanMessage(propertyValue.GetString());

                    if (!string.IsNullOrWhiteSpace(message))
                        return message;
                }
            }

            foreach (string nestedProperty in new[]
                     {
                         "error",
                         "details",
                         "detalle"
                     })
            {
                if (TryGetPropertyIgnoreCase(
                        element,
                        nestedProperty,
                        out JsonElement nested))
                {
                    string? nestedMessage = FindMessage(nested, depth + 1);

                    if (!string.IsNullOrWhiteSpace(nestedMessage))
                        return nestedMessage;
                }
            }

            if (TryGetPropertyIgnoreCase(
                    element,
                    "title",
                    out JsonElement title) &&
                title.ValueKind == JsonValueKind.String)
            {
                return CleanMessage(title.GetString());
            }

            return null;
        }

        private static bool TryGetPropertyIgnoreCase(
            JsonElement element,
            string propertyName,
            out JsonElement value)
        {
            value = default;

            if (element.ValueKind != JsonValueKind.Object)
                return false;

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            return false;
        }

        private static string TranslateValidationError(
            string? errorMessage,
            Exception? exception,
            string fieldName)
        {
            string message =
                errorMessage?.Trim()
                ?? exception?.Message?.Trim()
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(message))
                return $"El campo {fieldName} contiene un valor inválido.";

            if (message.Contains(
                    "A non-empty request body is required",
                    StringComparison.OrdinalIgnoreCase) ||
                message.Contains(
                    "does not contain any JSON tokens",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "El cuerpo de la solicitud está vacío o no contiene un JSON válido.";
            }

            Match requiredMatch = Regex.Match(
                message,
                @"^The (?<field>.+?) field is required\.?$",
                RegexOptions.IgnoreCase);

            if (requiredMatch.Success)
            {
                string requiredField =
                    NormalizeFieldName(requiredMatch.Groups["field"].Value);

                return $"El campo {requiredField} es obligatorio.";
            }

            if (message.Contains(
                    "could not be converted",
                    StringComparison.OrdinalIgnoreCase) ||
                message.Contains(
                    "is not valid",
                    StringComparison.OrdinalIgnoreCase) ||
                message.Contains(
                    "invalid JSON",
                    StringComparison.OrdinalIgnoreCase))
            {
                return $"El valor enviado para {fieldName} no tiene el formato esperado.";
            }

            if (message.StartsWith(
                    "JSON deserialization",
                    StringComparison.OrdinalIgnoreCase) ||
                message.Contains(
                    "System.Text.Json",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "El JSON enviado no tiene el formato esperado.";
            }

            return message;
        }

        private static string NormalizeFieldName(string? fieldName)
        {
            string value = fieldName?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(value) || value == "$")
                return "solicitud";

            value = value.TrimStart('$', '.');

            int lastDot = value.LastIndexOf('.');
            if (lastDot >= 0 && lastDot < value.Length - 1)
                value = value[(lastDot + 1)..];

            return string.IsNullOrWhiteSpace(value)
                ? "solicitud"
                : value;
        }

        private static string? CleanMessage(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            string value = Regex.Replace(
                    message.ReplaceLineEndings(" "),
                    @"\s+",
                    " ")
                .Trim()
                .Trim('"');

            return string.IsNullOrWhiteSpace(value)
                ? null
                : value;
        }

        private static bool ShouldPreserveDetails(object? value) =>
            value is not null &&
            value is not string &&
            value is not ProblemDetails &&
            value is not ApiErrorResponse;
    }
}
