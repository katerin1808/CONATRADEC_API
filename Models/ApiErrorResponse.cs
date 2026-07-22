namespace CONATRADEC_API.Models
{
    /// <summary>
    /// Formato único utilizado por la API para devolver cualquier error HTTP.
    /// </summary>
    public sealed class ApiErrorResponse
    {
        public bool Success { get; init; } = false;

        public string Message { get; init; } = string.Empty;

        public string Code { get; init; } = string.Empty;

        public IDictionary<string, string[]>? Errors { get; init; }

        /// <summary>
        /// Conserva información adicional que ya devolvía el endpoint,
        /// por ejemplo las dependencias que impiden eliminar un registro.
        /// </summary>
        public object? Details { get; init; }

        public string TraceId { get; init; } = string.Empty;
    }
}
