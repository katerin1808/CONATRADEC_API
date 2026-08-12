using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Alias de compatibilidad del endpoint experimental utilizado durante las
    /// primeras pruebas de imágenes offline en Windows.
    ///
    /// La conversión real se realiza únicamente en
    /// /imagenes/offline-windows/jpeg-directo, definido en Program.cs.
    /// Mantener este alias evita romper clientes antiguos sin conservar dos
    /// implementaciones distintas de validación y conversión JPEG.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("imagenes/offline-windows")]
    public sealed class ImagenOfflineWindowsController : ControllerBase
    {
        [HttpGet("jpeg")]
        public IActionResult ObtenerJpeg(
            [FromQuery] string ruta,
            [FromQuery] int ancho = 720,
            [FromQuery] int alto = 720,
            [FromQuery] int calidad = 78)
        {
            string destino =
                "/imagenes/offline-windows/jpeg-directo" +
                $"?ruta={Uri.EscapeDataString(ruta ?? string.Empty)}" +
                $"&ancho={ancho}" +
                $"&alto={alto}" +
                $"&calidad={calidad}";

            /*
             * 307 conserva método y query semánticamente, y HttpClient sigue
             * la redirección de forma transparente para clientes anteriores.
             */
            return RedirectPreserveMethod(destino);
        }
    }
}
