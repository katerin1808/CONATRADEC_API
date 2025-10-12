using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CONATRADEC_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RolController : ControllerBase
    {

        private readonly RolContext _context;

        public RolController(RolContext context)
        {
            _context = context;
        }

        [HttpPost]
        [Route("crearRol")]

        public async Task<IActionResult> CreateRol(Rol roles)
        {
            //guardar el producto en la base de datos
            await _context.Roles.AddAsync(roles);
            await _context.SaveChangesAsync();

            //devolver un mensaje de exito
            return Ok();
        }

    }
}
