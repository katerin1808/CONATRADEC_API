using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using CONATRADEC_API.DTOs;
using Microsoft.EntityFrameworkCore;

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
                rol= new

                {
                    rol.rolId,
                    rol.nombreRol,
                    rol.descripcionRol,
                    rol.activo
                }
            });
        }

        [HttpGet]
        [Route("listaRol")]
        public async Task<ActionResult<IEnumerable<RolCreateDto>>> GetRol()
        {
            //Obten la lista de productos de la base de datos
            var rol = await _context.Roles.ToListAsync();

            //devuelve una lista de productos
            return Ok(rol);
        }


        [HttpGet("verRol")]
        public async Task<IActionResult> GetRol(int id)
        {
            //obtener el producto de la base de datos
            var rol = await _context.Roles.FindAsync(id);


            //devolver el producto
            if (rol == null)
            {
                return NotFound(new { mensaje = "Rol no encontrado." });
            }

            return Ok(rol);
        }

        // GET /roles/editarRol/{id} → Carga datos existentes para el formulario
        [HttpGet("editarRol/{id:int}")]
        public async Task<IActionResult> GetRolForEdit(int id)
        {
            var rol = await _context.Roles.FindAsync(id);
            if (rol == null) return NotFound($"No se encontró un rol con ID {id}");
            return Ok(rol); // ← lo usas para prellenar el formulario
        }

        // PUT /roles/editarRol/{id} → Guarda los cambios del formulario
        [HttpPut("editarRol/{id:int}")]
        public async Task<IActionResult> SaveRolEdit(int id, [FromBody] Rol dto)
        {
            var rol = await _context.Roles.FindAsync(id);
            if (rol == null) return NotFound($"No se encontró un rol con ID {id}");

            rol.nombreRol = dto.nombreRol;
            rol.descripcionRol = dto.descripcionRol;
            rol.activo = dto.activo;

            await _context.SaveChangesAsync();
            return Ok (new
         {
                message = "Rol actualizado correctamente",
                rol = rol
            }); // ← devuelve el rol actualizado
        }


        // DELETE /eliminarRol/{id} → Elimina un rol por ID
        [HttpDelete]
        [Route("eliminarRol/{id:int}")]
        public async Task<IActionResult> DeleteRol(int id)
        {
            // Buscar el rol por ID
            var rolBorrado = await _context.Roles.FindAsync(id);

            // Si no existe, devolver 404
            if (rolBorrado == null)
                return NotFound($"No se encontró un rol con ID {id}");

            // Eliminar el rol de la base de datos
            _context.Roles.Remove(rolBorrado);

            // Guardar los cambios
            await _context.SaveChangesAsync();

            // ✅ Devolver mensaje de éxito
            return Ok(new { message = "Rol eliminado correctamente" });
        }
    }
}
