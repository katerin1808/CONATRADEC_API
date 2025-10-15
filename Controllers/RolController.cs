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

        // GET /listarRoles → Devuelve solo los roles activos
        [HttpGet("listarRoles")]
        public async Task<IActionResult> ListarRoles()
        {
            // Obtener solo los registros donde 'activo' sea true
            var rolesActivos = await _context.Roles
                .Where(r => r.activo)
                .Select(r => new
                {
                    r.rolId,
                    r.nombreRol,
                    r.descripcionRol
                })
                .ToListAsync();

            // Si no hay registros activos
            if (rolesActivos == null || !rolesActivos.Any())
                return NotFound("No hay roles activos registrados");

            // Devolver la lista de roles activos
            return Ok(rolesActivos);
        }

        // GET /buscarRol/{id} → Devuelve el rol solo si está activo
        [HttpGet("buscarRol/{id:int}")]
        public async Task<IActionResult> BuscarRol(int id)
        {
            // Buscar el rol en la base de datos
            var rol = await _context.Roles.FindAsync(id);

            // Verificar si no existe o está inactivo
            if (rol == null || !rol.activo)
                return NotFound("No se encontró un rol activo con ese ID");

            // Devolver solo los datos permitidos
            return Ok(new
            {
                rol.rolId,
                rol.nombreRol,
                rol.descripcionRol
            });
        }



        // PUT /editarRol/{id} → Actualiza los datos de un rol activo
        [HttpPut("editarRol/{id:int}")]
        public async Task<IActionResult> UpdateRol(int id, [FromBody] RolUpdateDto dto)
        {
            // Buscar el rol
            var rol = await _context.Roles.FindAsync(id);

            // Validar que exista y esté activo
            if (rol == null || !rol.activo)
                return NotFound("No se encontró un rol activo con ese ID");

            // Actualizar solo los campos permitidos
            rol.nombreRol = dto.nombreRol;
            rol.descripcionRol = dto.descripcionRol;

            // Guardar los cambios
            await _context.SaveChangesAsync();

            // Devolver mensaje de éxito
            return Ok(new
            {
                mensaje = "Rol actualizado correctamente",
                rol = new

                {
                  
                    rol.nombreRol,
                    rol.descripcionRol,
                  
                }
            });
        }


        // DELETE /eliminarRol/{id} → Elimina un rol por ID
        // DELETE /eliminarRol/{id} → Borrado lógico (activo = false)
        [HttpDelete("eliminarRol/{id:int}")]
        public async Task<IActionResult> DeleteRol(int id)
        {
            var rol = await _context.Roles.FindAsync(id);
            if (rol == null)
                return NotFound($"No se encontró un rol con ID {id}");

            if (!rol.activo)
                return Ok(new { message = "El rol ya estaba inactivo" });

            rol.activo = false;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Rol desactivado (borrado lógico) correctamente" });
        }
    }
}
