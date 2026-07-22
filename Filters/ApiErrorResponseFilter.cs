using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace CONATRADEC_API.Filters
{
    /// <summary>
    /// Convierte en ApiErrorResponse cualquier respuesta 4xx o 5xx
    /// devuelta manualmente por los controladores.
    /// </summary>
    public sealed class ApiErrorResponseFilter : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(
            ResultExecutingContext context,
            ResultExecutionDelegate next)
        {
            switch (context.Result)
            {
                case ObjectResult objectResult:
                {
                    int statusCode =
                        objectResult.StatusCode
                        ?? (objectResult.Value as ProblemDetails)?.Status
                        ?? StatusCodes.Status200OK;

                    if (statusCode >= StatusCodes.Status400BadRequest &&
                        objectResult.Value is not ApiErrorResponse)
                    {
                        context.Result = new ObjectResult(
                            ApiErrorResponseFactory.Create(
                                context.HttpContext,
                                statusCode,
                                objectResult.Value))
                        {
                            StatusCode = statusCode
                        };
                    }

                    break;
                }

                case JsonResult jsonResult:
                {
                    int statusCode =
                        jsonResult.StatusCode
                        ?? (jsonResult.Value as ProblemDetails)?.Status
                        ?? StatusCodes.Status200OK;

                    if (statusCode >= StatusCodes.Status400BadRequest &&
                        jsonResult.Value is not ApiErrorResponse)
                    {
                        jsonResult.Value = ApiErrorResponseFactory.Create(
                            context.HttpContext,
                            statusCode,
                            jsonResult.Value);
                        jsonResult.StatusCode = statusCode;
                    }

                    break;
                }

                case ContentResult contentResult
                    when contentResult.StatusCode is >=
                        StatusCodes.Status400BadRequest:
                {
                    int statusCode = contentResult.StatusCode.Value;

                    context.Result = new ObjectResult(
                        ApiErrorResponseFactory.Create(
                            context.HttpContext,
                            statusCode,
                            contentResult.Content))
                    {
                        StatusCode = statusCode
                    };

                    break;
                }

                case StatusCodeResult statusCodeResult
                    when statusCodeResult.StatusCode >=
                        StatusCodes.Status400BadRequest:
                {
                    context.Result = new ObjectResult(
                        ApiErrorResponseFactory.Create(
                            context.HttpContext,
                            statusCodeResult.StatusCode))
                    {
                        StatusCode = statusCodeResult.StatusCode
                    };

                    break;
                }

                case IStatusCodeActionResult statusResult
                    when statusResult.StatusCode is >=
                        StatusCodes.Status400BadRequest:
                {
                    int statusCode = statusResult.StatusCode.Value;

                    context.Result = new ObjectResult(
                        ApiErrorResponseFactory.Create(
                            context.HttpContext,
                            statusCode))
                    {
                        StatusCode = statusCode
                    };

                    break;
                }
            }

            await next();
        }
    }
}
