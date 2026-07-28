using Microsoft.AspNetCore.Mvc;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/sesion")]
    public sealed class SesionController : ControllerBase
    {
        /// <summary>
        /// El middleware valida las cabeceras X-Usuario-Id y X-Version-Sesion.
        /// Si ambas siguen vigentes, se devuelve 204.
        /// </summary>
        [HttpGet("validar")]
        public IActionResult Validar() => NoContent();
    }
}
