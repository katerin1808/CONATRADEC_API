using CONATRADEC_API.Security;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/sesion")]
    public sealed class SesionController : ControllerBase
    {
        private readonly SesionActivaService sesionActivaService;

        public SesionController(
            SesionActivaService sesionActivaService)
        {
            this.sesionActivaService = sesionActivaService;
        }

        /// <summary>
        /// El middleware valida el JWT, la versión de sesión y la inactividad.
        /// </summary>
        [HttpGet("validar")]
        public IActionResult Validar() => NoContent();

        /// <summary>
        /// Revoca inmediatamente la sesión representada por el JWT actual.
        /// </summary>
        [HttpPost("cerrar")]
        public IActionResult Cerrar()
        {
            string? sesionId = User.FindFirst(
                JwtRegisteredClaimNames.Jti)?.Value;

            if (!string.IsNullOrWhiteSpace(sesionId))
                sesionActivaService.Revocar(sesionId);

            return NoContent();
        }
    }
}
