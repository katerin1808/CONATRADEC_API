using CONATRADEC_API.Reportes;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/reportes/analisis")]
    public sealed class ReporteAnalisisController : ControllerBase
    {
        private readonly AnalisisReporteHistoricoService historial;
        private readonly ILogger<ReporteAnalisisController> logger;

        public ReporteAnalisisController(
            AnalisisReporteHistoricoService historial,
            ILogger<ReporteAnalisisController> logger)
        {
            this.historial = historial;
            this.logger = logger;
        }

        /// <summary>
        /// Devuelve la versión vigente e inmutable del reporte.
        /// </summary>
        [HttpGet("{analisisSueloCalculoId:int}/datos")]
        [Produces("application/json")]
        public async Task<IActionResult> ObtenerDatos(
            int analisisSueloCalculoId,
            CancellationToken cancellationToken)
        {
            if (analisisSueloCalculoId <= 0)
                return IdentificadorInvalido();

            AnalisisReporte? reporte =
                await historial.ObtenerReporteAsync(
                    analisisSueloCalculoId,
                    versionRegistro: null,
                    cancellationToken);

            if (reporte == null)
                return NoEncontrado();

            await AgregarEncabezadosAsync(
                analisisSueloCalculoId,
                cancellationToken);

            return Ok(reporte);
        }

        /// <summary>
        /// Genera el PDF a partir del snapshot vigente, sin recalcular.
        /// </summary>
        [HttpGet("{analisisSueloCalculoId:int}/pdf")]
        [Produces("application/pdf")]
        public async Task<IActionResult> DescargarPdf(
            int analisisSueloCalculoId,
            CancellationToken cancellationToken)
        {
            if (analisisSueloCalculoId <= 0)
                return IdentificadorInvalido();

            return await GenerarPdfAsync(
                analisisSueloCalculoId,
                versionRegistro: null,
                cancellationToken);
        }

        /// <summary>
        /// Metadatos utilizados para control de concurrencia optimista.
        /// </summary>
        [HttpGet("{analisisSueloCalculoId:int}/control")]
        public async Task<IActionResult> ObtenerControl(
            int analisisSueloCalculoId,
            CancellationToken cancellationToken)
        {
            if (analisisSueloCalculoId <= 0)
                return IdentificadorInvalido();

            AnalisisControlHistorialDto? control =
                await historial.ObtenerControlAsync(
                    analisisSueloCalculoId,
                    cancellationToken);

            if (control == null)
                return NoEncontrado();

            AgregarEncabezados(control);

            return Ok(new
            {
                success = true,
                data = new
                {
                    control.AnalisisSueloId,
                    control.AnalisisSueloCalculoId,
                    control.VersionRegistro,
                    control.FechaCreacionClienteUtc,
                    control.FechaUltimaModificacionUtc,
                    control.FechaCreacionServidor,
                    control.OrigenRegistro,
                    control.ETag
                }
            });
        }

        /// <summary>
        /// Lista todas las versiones inmutables disponibles del análisis.
        /// </summary>
        [HttpGet("{analisisSueloCalculoId:int}/versiones")]
        public async Task<IActionResult> ListarVersiones(
            int analisisSueloCalculoId,
            CancellationToken cancellationToken)
        {
            if (analisisSueloCalculoId <= 0)
                return IdentificadorInvalido();

            AnalisisControlHistorialDto? control =
                await historial.ObtenerControlAsync(
                    analisisSueloCalculoId,
                    cancellationToken);

            if (control == null)
                return NoEncontrado();

            /*
             * Para análisis anteriores a esta mejora, la primera consulta de
             * versiones congela la información disponible bajo el mismo bloqueo
             * utilizado por el PDF y los datos.
             */
            _ = await historial.ObtenerReporteAsync(
                analisisSueloCalculoId,
                versionRegistro: null,
                cancellationToken);

            List<AnalisisVersionHistorialDto> versiones =
                await historial.ListarVersionesAsync(
                    analisisSueloCalculoId,
                    cancellationToken);

            AgregarEncabezados(control);

            return Ok(new
            {
                success = true,
                data = new
                {
                    analisisSueloCalculoId,
                    versionActual = control.VersionRegistro,
                    etagActual = control.ETag,
                    versiones
                }
            });
        }

        /// <summary>
        /// Devuelve una versión específica exactamente como quedó guardada.
        /// </summary>
        [HttpGet("{analisisSueloCalculoId:int}/versiones/{versionRegistro:int}/datos")]
        public async Task<IActionResult> ObtenerDatosVersion(
            int analisisSueloCalculoId,
            int versionRegistro,
            CancellationToken cancellationToken)
        {
            if (analisisSueloCalculoId <= 0 || versionRegistro <= 0)
                return IdentificadorInvalido();

            AnalisisReporte? reporte =
                await historial.ObtenerReporteAsync(
                    analisisSueloCalculoId,
                    versionRegistro,
                    cancellationToken);

            if (reporte == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontró la versión solicitada del análisis."
                });
            }

            Response.Headers["X-Version-Registro"] =
                versionRegistro.ToString();

            return Ok(reporte);
        }

        /// <summary>
        /// Genera el PDF de una versión específica, incluso si el análisis fue
        /// posteriormente editado o eliminado lógicamente.
        /// </summary>
        [HttpGet("{analisisSueloCalculoId:int}/versiones/{versionRegistro:int}/pdf")]
        [Produces("application/pdf")]
        public async Task<IActionResult> DescargarPdfVersion(
            int analisisSueloCalculoId,
            int versionRegistro,
            CancellationToken cancellationToken)
        {
            if (analisisSueloCalculoId <= 0 || versionRegistro <= 0)
                return IdentificadorInvalido();

            return await GenerarPdfAsync(
                analisisSueloCalculoId,
                versionRegistro,
                cancellationToken);
        }

        private async Task<IActionResult> GenerarPdfAsync(
            int analisisSueloCalculoId,
            int? versionRegistro,
            CancellationToken cancellationToken)
        {
            try
            {
                AnalisisReporte? reporte =
                    await historial.ObtenerReporteAsync(
                        analisisSueloCalculoId,
                        versionRegistro,
                        cancellationToken);

                if (reporte == null)
                    return NoEncontrado();

                byte[] pdf = AnalisisReportePdf.Generar(reporte);

                if (versionRegistro.HasValue)
                {
                    Response.Headers["X-Version-Registro"] =
                        versionRegistro.Value.ToString();
                }
                else
                {
                    await AgregarEncabezadosAsync(
                        analisisSueloCalculoId,
                        cancellationToken);
                }

                string sufijo = versionRegistro.HasValue
                    ? $"_V{versionRegistro.Value}"
                    : string.Empty;

                return File(
                    pdf,
                    "application/pdf",
                    $"{reporte.NombreArchivoBase}{sufijo}.pdf");
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error al generar el PDF histórico del cálculo {CalculoId}, versión {Version}.",
                    analisisSueloCalculoId,
                    versionRegistro);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message =
                            "No fue posible generar el reporte PDF histórico."
                    });
            }
        }

        private async Task AgregarEncabezadosAsync(
            int analisisSueloCalculoId,
            CancellationToken cancellationToken)
        {
            AnalisisControlHistorialDto? control =
                await historial.ObtenerControlAsync(
                    analisisSueloCalculoId,
                    cancellationToken);

            if (control != null)
                AgregarEncabezados(control);
        }

        private void AgregarEncabezados(
            AnalisisControlHistorialDto control)
        {
            Response.Headers["ETag"] = control.ETag;
            Response.Headers["X-Version-Registro"] =
                control.VersionRegistro.ToString();

            if (control.FechaUltimaModificacionUtc.HasValue)
            {
                Response.Headers["Last-Modified"] =
                    control.FechaUltimaModificacionUtc.Value.ToString("R");
            }
        }

        private static BadRequestObjectResult IdentificadorInvalido() =>
            new(new
            {
                success = false,
                message = "El identificador o la versión no son válidos."
            });

        private static NotFoundObjectResult NoEncontrado() =>
            new(new
            {
                success = false,
                message = "No se encontró el análisis solicitado."
            });
    }
}
