using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Expone únicamente una vista segura y una prueba de conexión del
    /// proveedor configurado en el backend. La configuración ya no se guarda
    /// desde MAUI ni depende de tablas adicionales en la base de datos.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/inspecciones-fitosanitarias/proveedor-ia")]
    public sealed class ProveedorIAConfiguracionController : ControllerBase
    {
        private readonly DBContext db;
        private readonly PermisoApiService permisos;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly IConfiguration configuration;
        private readonly ImageStoragePathService storage;
        private readonly ILogger<ProveedorIAConfiguracionController> logger;

        public ProveedorIAConfiguracionController(
            DBContext db,
            PermisoApiService permisos,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ImageStoragePathService storage,
            ILogger<ProveedorIAConfiguracionController> logger)
        {
            this.db = db;
            this.permisos = permisos;
            this.httpClientFactory = httpClientFactory;
            this.configuration = configuration;
            this.storage = storage;
            this.logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Obtener(
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            try
            {
                ProveedorIAConfiguracionDto data =
                    await CrearService().ObtenerDtoAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Configuración activa del proveedor IA obtenida desde el backend.",
                    data
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "No fue posible leer la configuración del proveedor IA.");

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message =
                            "La configuración del proveedor IA del backend no es válida. Revise appsettings o las variables de entorno."
                    });
            }
        }

        [HttpPut]
        public async Task<IActionResult> Actualizar(
            [FromBody] ProveedorIAConfiguracionActualizarRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            return Conflict(new
            {
                success = false,
                message =
                    "El proveedor de IA se configura directamente en el backend. Modifique la sección ProveedorIA de appsettings o las variables de entorno y vuelva a publicar la API."
            });
        }

        [HttpPost("probar")]
        public async Task<IActionResult> Probar(
            [FromBody] ProveedorIAConfiguracionActualizarRequest? request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            try
            {
                ProveedorIAPruebaDto data = await CrearService().ProbarAsync(
                    request,
                    cancellationToken);

                return data.Exitoso
                    ? Ok(new
                    {
                        success = true,
                        message = data.Mensaje,
                        data
                    })
                    : StatusCode(
                        data.CodigoHttp is >= 400 and <= 599
                            ? data.CodigoHttp
                            : StatusCodes.Status502BadGateway,
                        new
                        {
                            success = false,
                            message = data.Mensaje,
                            data
                        });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "No fue posible probar la configuración del proveedor IA.");

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message =
                            "No fue posible probar el proveedor configurado en el backend."
                    });
            }
        }

        private ProveedorIAClienteService CrearService() =>
            new(
                httpClientFactory,
                configuration,
                storage,
                db,
                logger);

        private async Task<IActionResult?> ValidarPermisoAsync(
            int? usuarioId,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazConfiguracion,
                tipo,
                cancellationToken);

            return resultado.Permitido
                ? null
                : StatusCode(
                    resultado.CodigoEstado,
                    new
                    {
                        success = false,
                        message = resultado.Mensaje
                    });
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int usuarioId)
                ? usuarioId
                : null;
        }
    }
}
