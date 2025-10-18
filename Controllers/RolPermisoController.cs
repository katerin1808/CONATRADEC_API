using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace CONATRADEC_API.Controllers
{

    [ApiController]
    [Route("api/rol-permisos")]
    public class RolPermisoController : Controller
    {
        private readonly RolContext _db;
        public RolPermisoController(RolContext db) => _db = db;

        // ---------------------------------------------
        // CREATE (por nombres)
        // POST /api/rol-permisos/by-name
        // ---------------------------------------------
        [HttpPost("Crear", Name = "AgregarRolPermisoPorNombre")]
        public async Task<ActionResult<RolPermisoReadDto>> AgregarRolPermisoPorNombre([FromBody] RolPermisoCreateDto dto)
        {
            var nombreRol = dto.nombreRol?.Trim();
            var nombrePermiso = dto.nombrePermiso?.Trim();
            if (string.IsNullOrWhiteSpace(nombreRol) || string.IsNullOrWhiteSpace(nombrePermiso))
                return BadRequest("nombreRol y nombrePermiso son requeridos.");

            // Lookups internos (no se crean aquí)
            var rol = await _db.Roles.FirstOrDefaultAsync(r => r.nombreRol == nombreRol);
            if (rol is null) return NotFound($"No existe el rol '{nombreRol}'.");

            var permiso = await _db.Permisos.FirstOrDefaultAsync(p => p.nombrePermiso == nombrePermiso);
            if (permiso is null) return NotFound($"No existe el permiso/interfaz '{nombrePermiso}'.");

            // Evitar duplicados (PK compuesta)
            var existe = await _db.RolPermisos.AnyAsync(x => x.rolId == rol.rolId && x.permisoId == permiso.permisoId);
            if (existe) return Conflict("Ya existe la relación rol-permiso.");

            var nuevo = new RolPermiso
            {
                rolId = rol.rolId,
                permisoId = permiso.permisoId,
                leer = dto.leer,
                agregar = dto.agregar,
                actualizar = dto.actualizar,
                eliminar = dto.eliminar
            };

            _db.RolPermisos.Add(nuevo);
            await _db.SaveChangesAsync();

            var read = new RolPermisoReadDto
            {
                rolId = rol.rolId,
                permisoId = permiso.permisoId,
                nombreRol = rol.nombreRol,
                nombrePermiso = permiso.nombrePermiso,
                leer = nuevo.leer,
                agregar = nuevo.agregar,
                actualizar = nuevo.actualizar,
                eliminar = nuevo.eliminar
            };

            // Para coherencia, la "key" de ubicación puede ir por nombres
            return CreatedAtAction(nameof(ObtenerRolPermisoPorNombre),
                new { nombreRol = rol.nombreRol, nombrePermiso = permiso.nombrePermiso }, read);
        }

        // ---------------------------------------------
        // READ uno (por nombres)
        // GET /api/rol-permisos/by-name?nombreRol=...&nombrePermiso=...
        // ---------------------------------------------
        [HttpGet("Buscar", Name = "ObtenerRolPermisoPorNombre")]
        public async Task<ActionResult<RolPermisoReadDto>> ObtenerRolPermisoPorNombre(
            [FromQuery] string nombreRol,
            [FromQuery] string nombrePermiso)
        {
            var rol = await _db.Roles.FirstOrDefaultAsync(r => r.nombreRol == nombreRol.Trim());
            if (rol is null) return NotFound($"No existe el rol '{nombreRol}'.");

            var permiso = await _db.Permisos.FirstOrDefaultAsync(p => p.nombrePermiso == nombrePermiso.Trim());
            if (permiso is null) return NotFound($"No existe el permiso/interfaz '{nombrePermiso}'.");

            var rp = await _db.RolPermisos
                .Where(x => x.rolId == rol.rolId && x.permisoId == permiso.permisoId)
                .Select(x => new RolPermisoReadDto
                {
                    rolId = x.rolId,
                    permisoId = x.permisoId,
                    nombreRol = rol.nombreRol,
                    nombrePermiso = permiso.nombrePermiso,
                    leer = x.leer,
                    agregar = x.agregar,
                    actualizar = x.actualizar,
                    eliminar = x.eliminar
                })
                .FirstOrDefaultAsync();

            if (rp is null) return NotFound();
            return Ok(rp);
        }

        // ---------------------------------------------
        // READ lista (por nombre de rol)
        // GET /api/rol-permisos/por-rol-nombre/{nombreRol}
        // ---------------------------------------------
        [HttpGet("Listar/{nombreRol}", Name = "ListarPermisosPorNombreRol")]
        public async Task<ActionResult<IEnumerable<RolPermisoReadDto>>> ListarPermisosPorNombreRol(string nombreRol)
        {
            var rol = await _db.Roles.FirstOrDefaultAsync(r => r.nombreRol == nombreRol.Trim());
            if (rol is null) return NotFound($"No existe el rol '{nombreRol}'.");

            var lista = await _db.RolPermisos
                .Include(x => x.Permiso)
                .Where(x => x.rolId == rol.rolId)
                .OrderBy(x => x.Permiso.nombrePermiso)
                .Select(x => new RolPermisoReadDto
                {
                    rolId = x.rolId,
                    permisoId = x.permisoId,
                    nombreRol = rol.nombreRol,
                    nombrePermiso = x.Permiso.nombrePermiso,
                    leer = x.leer,
                    agregar = x.agregar,
                    actualizar = x.actualizar,
                    eliminar = x.eliminar
                })
                .ToListAsync();

            return Ok(lista);
        }

        // ---------------------------------------------
        // UPDATE (por nombres)
        // PUT /api/rol-permisos/by-name
        // ---------------------------------------------
        [HttpPut("Editar", Name = "ActualizarRolPermisoPorNombre")]
        public async Task<IActionResult> ActualizarRolPermisoPorNombre([FromBody] RolPermisoUpdateDto dto)
        {
            var nombreRol = dto.nombreRol?.Trim();
            var nombrePermiso = dto.nombrePermiso?.Trim();
            if (string.IsNullOrWhiteSpace(nombreRol) || string.IsNullOrWhiteSpace(nombrePermiso))
                return BadRequest("nombreRol y nombrePermiso son requeridos.");

            var rol = await _db.Roles.FirstOrDefaultAsync(r => r.nombreRol == nombreRol);
            if (rol is null) return NotFound($"No existe el rol '{nombreRol}'.");

            var permiso = await _db.Permisos.FirstOrDefaultAsync(p => p.nombrePermiso == nombrePermiso);
            if (permiso is null) return NotFound($"No existe el permiso/interfaz '{nombrePermiso}'.");

            var rp = await _db.RolPermisos
                .FirstOrDefaultAsync(x => x.rolId == rol.rolId && x.permisoId == permiso.permisoId);

            if (rp is null) return NotFound("La relación rol-permiso no existe.");

            rp.leer = dto.leer;
            rp.agregar = dto.agregar;
            rp.actualizar = dto.actualizar;
            rp.eliminar = dto.eliminar;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ---------------------------------------------
        // DELETE (por nombres)
        // DELETE /api/rol-permisos/by-name?nombreRol=...&nombrePermiso=...
        // ---------------------------------------------
        [HttpDelete("Eliminar", Name = "EliminarRolPermisoPorNombre")]
        public async Task<IActionResult> EliminarRolPermisoPorNombre(
            [FromQuery] string nombreRol,
            [FromQuery] string nombrePermiso)
        {
            var rol = await _db.Roles.FirstOrDefaultAsync(r => r.nombreRol == nombreRol.Trim());
            if (rol is null) return NotFound($"No existe el rol '{nombreRol}'.");

            var permiso = await _db.Permisos.FirstOrDefaultAsync(p => p.nombrePermiso == nombrePermiso.Trim());
            if (permiso is null) return NotFound($"No existe el permiso/interfaz '{nombrePermiso}'.");

            var rp = await _db.RolPermisos
                .FirstOrDefaultAsync(x => x.rolId == rol.rolId && x.permisoId == permiso.permisoId);

            if (rp is null) return NotFound("La relación rol-permiso no existe.");

            _db.RolPermisos.Remove(rp);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}