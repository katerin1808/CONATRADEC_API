using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using CONATRADEC_API.DTOs;

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

        public async Task<IActionResult> CreateRol([FromBody] RolCreateDto rolCreateDto)
        {

            // Validaciones básicas
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Evitar duplicados
            if (_context.Roles.Any(r => r.nombreRol == rolCreateDto.nombreRol))
                return BadRequest(new { mensaje = "Ya existe un rol con ese nombre." });

            // Crear el objeto Rol completo
            var rol = new Rol
            {
                nombreRol = rolCreateDto.nombreRol,
                descripcionRol= rolCreateDto.descripcionRol,
                activo = true // Se asigna automáticamente
            };
            //guardar el producto en la base de datos
            await _context.Roles.AddAsync(rol);
            await _context.SaveChangesAsync();

            //devolver un mensaje de exito
            return Ok(new
            {
                mensaje = "Rol creado correctamente",
                datos = new
                {
                    rol.rolId,
                    rol.nombreRol,
                    rol.descripcionRol,
                    rol.activo
                }
            });
        }

    }
}
