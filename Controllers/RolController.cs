using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;


namespace CONATRADEC_API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RolController : ControllerBase
    {

        private readonly DBContext _context;

        public RolController(DBContext context)
        {
            _context = context;
        }


        // POST /crearRol → crea un nuevo rol (no reactiva)
        [HttpPost("crearRol")]
        public async Task<IActionResult> CrearRol([FromBody] RolCreateDto dto)
        {
            var nombre = dto.nombreRol.Trim();
            var nombreLower = nombre.ToLower();

            // 1️⃣ Verificar si ya existe un rol activo con el mismo nombre
            var existeActivo = await _context.Roles
                .AnyAsync(r => r.activo && r.nombreRol.ToLower() == nombreLower);

            if (existeActivo)
                return Conflict("Ya existe un rol activo con ese nombre.");

            // 2️⃣ No importa si hay uno inactivo, simplemente crea uno nuevo
            var nuevoRol = new Rol
            {
                nombreRol = nombre,
                descripcionRol = dto.descripcionRol,
                activo = true
            };

            _context.Roles.Add(nuevoRol);
            await _context.SaveChangesAsync();

            // 3️⃣ Devolver mensaje y datos del rol creado
            return CreatedAtAction(nameof(BuscarRol), new { id = nuevoRol.rolId }, new
            {
                message = "Rol creado correctamente",
                nuevoRol.rolId,
                nuevoRol.nombreRol,
                nuevoRol.descripcionRol
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

