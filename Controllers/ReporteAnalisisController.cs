using CONATRADEC_API.Reportes;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/reportes/analisis")]
    public sealed class ReporteAnalisisController : ControllerBase
    {
        private readonly AnalisisReporteDatosService _datosService;
        private readonly ILogger<ReporteAnalisisController> _logger;

        public ReporteAnalisisController(
            AnalisisReporteDatosService datosService,
            ILogger<ReporteAnalisisController> logger)
        {
            _datosService = datosService;
            _logger = logger;
        }

        /// <summary>
        /// Genera el reporte PDF de un análisis previamente guardado.
        /// </summary>
        [HttpGet("{analisisSueloCalculoId:int}/pdf")]
        [Produces("application/pdf")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DescargarPdf(
            int analisisSueloCalculoId,
            CancellationToken cancellationToken)
        {
            if (analisisSueloCalculoId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El identificador del cálculo no es válido."
                });
            }

            try
            {
                AnalisisReporte? reporte = await _datosService.ObtenerAsync(
                    analisisSueloCalculoId,
                    cancellationToken);

                if (reporte == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        message = "No se encontró el análisis solicitado."
                    });
                }

                byte[] pdf = AnalisisReportePdf.Generar(reporte);

                return File(
                    pdf,
                    "application/pdf",
                    $"{reporte.NombreArchivoBase}.pdf");
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error al generar el reporte PDF del cálculo {AnalisisSueloCalculoId}.",
                    analisisSueloCalculoId);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message = "No fue posible generar el reporte PDF."
                    });
            }
        }
    }
}
