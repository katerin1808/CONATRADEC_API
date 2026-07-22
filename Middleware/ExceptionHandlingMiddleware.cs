using CONATRADEC_API.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CONATRADEC_API.Middleware
{
    /// <summary>
    /// Evita que las excepciones no controladas lleguen al cliente
    /// como HTML, texto técnico o una respuesta vacía.
    /// </summary>
    public sealed class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException)
                when (context.RequestAborted.IsCancellationRequested)
            {
                // El cliente canceló la solicitud. No se intenta escribir
                // una respuesta nueva sobre una conexión ya cerrada.
            }
            catch (Exception exception)
            {
                if (context.Response.HasStarted)
                    throw;

                (int statusCode, string message, string code) =
                    ResolveException(exception);

                if (statusCode >= StatusCodes.Status500InternalServerError)
                {
                    _logger.LogError(
                        exception,
                        "Error no controlado. TraceId: {TraceId}",
                        context.TraceIdentifier);
                }
                else
                {
                    _logger.LogWarning(
                        exception,
                        "Solicitud rechazada con estado {StatusCode}. TraceId: {TraceId}",
                        statusCode,
                        context.TraceIdentifier);
                }

                context.Response.Clear();
                context.Response.StatusCode = statusCode;
                context.Response.ContentType =
                    "application/json; charset=utf-8";

                var response = ApiErrorResponseFactory.Create(
                    context,
                    statusCode,
                    message: message,
                    code: code);

                await context.Response.WriteAsJsonAsync(response);
            }
        }

        private static (
            int StatusCode,
            string Message,
            string Code)
            ResolveException(Exception exception)
        {
            if (exception is JsonException or BadHttpRequestException)
            {
                return (
                    StatusCodes.Status400BadRequest,
                    "El JSON enviado está vacío, incompleto o tiene un formato inválido.",
                    "INVALID_JSON");
            }

            if (exception is DbUpdateConcurrencyException)
            {
                return (
                    StatusCodes.Status409Conflict,
                    "El registro fue modificado o eliminado por otro proceso. Recargue la información e intente nuevamente.",
                    "CONCURRENCY_CONFLICT");
            }

            SqlException? sqlException = FindSqlException(exception);

            if (sqlException is not null)
                return ResolveSqlException(sqlException);

            if (exception is KeyNotFoundException)
            {
                return (
                    StatusCodes.Status404NotFound,
                    "No se encontró el registro solicitado.",
                    "NOT_FOUND");
            }

            if (exception is UnauthorizedAccessException)
            {
                return (
                    StatusCodes.Status403Forbidden,
                    "No tiene permisos para realizar esta operación.",
                    "FORBIDDEN");
            }

            if (exception is ArgumentException)
            {
                return (
                    StatusCodes.Status400BadRequest,
                    "La solicitud contiene datos inválidos.",
                    "VALIDATION_ERROR");
            }

            if (exception is TimeoutException)
            {
                return (
                    StatusCodes.Status503ServiceUnavailable,
                    "El servidor tardó demasiado en procesar la solicitud. Intente nuevamente.",
                    "SERVICE_TIMEOUT");
            }

            if (exception is DbUpdateException)
            {
                return (
                    StatusCodes.Status409Conflict,
                    "No fue posible guardar los cambios porque la información entra en conflicto con otros registros.",
                    "DATABASE_CONFLICT");
            }

            return (
                StatusCodes.Status500InternalServerError,
                "Ocurrió un error interno al procesar la solicitud. Intente nuevamente.",
                "INTERNAL_ERROR");
        }

        private static (
            int StatusCode,
            string Message,
            string Code)
            ResolveSqlException(SqlException exception) =>
            exception.Number switch
            {
                2601 or 2627 => (
                    StatusCodes.Status409Conflict,
                    "Ya existe un registro con la información ingresada. Revise los campos que deben ser únicos.",
                    "DUPLICATE_RECORD"),

                547 => (
                    StatusCodes.Status409Conflict,
                    "No se puede completar la operación porque el registro está relacionado con otra información.",
                    "RELATED_RECORD"),

                515 => (
                    StatusCodes.Status400BadRequest,
                    "No fue posible guardar porque falta un dato obligatorio.",
                    "REQUIRED_VALUE"),

                8152 or 2628 => (
                    StatusCodes.Status400BadRequest,
                    "Uno de los valores enviados supera la longitud permitida.",
                    "VALUE_TOO_LONG"),

                -2 => (
                    StatusCodes.Status503ServiceUnavailable,
                    "La base de datos tardó demasiado en responder. Intente nuevamente.",
                    "DATABASE_TIMEOUT"),

                _ => (
                    StatusCodes.Status500InternalServerError,
                    "Ocurrió un error al procesar la información en la base de datos.",
                    "DATABASE_ERROR")
            };

        private static SqlException? FindSqlException(Exception exception)
        {
            Exception? current = exception;

            while (current is not null)
            {
                if (current is SqlException sqlException)
                    return sqlException;

                current = current.InnerException;
            }

            return null;
        }
    }
}
