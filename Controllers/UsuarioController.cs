using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/usuarios")]

    public class UsuarioController : Controller
    {


        private readonly RolContext _db;
        public UsuarioController(RolContext db) => _db = db;

       

        /// GET /api/usuarios/obtener
        [HttpGet("{id:int}", Name = "GetUsuarioById")]
        public async Task<ActionResult<UsuarioDto>> Obtener(int id)
        {
            var u = await _db.Usuarios
                .AsNoTracking()
                .Include(x => x.Rol)
                .FirstOrDefaultAsync(x => x.UsuarioId == id);

            if (u is null)
                return NotFound("Usuario no encontrado.");

            return Ok(new UsuarioDto
            {
                UsuarioId = u.UsuarioId,
                nombreUsuario = u.nombreUsuario,
                telefonoUsuario = u.telefonoUsuario,
                correoUsuario = u.correoUsuario,
                rolId = u.rolId,
                nombreRol = u.Rol?.nombreRol ?? "",
                activo = u.activo
            });
        }

        // ===============================================================
        // POST /api/usuarios  (Crear usuario SIEMPRE activo)
        // ===============================================================
        [HttpPost]
        public async Task<IActionResult> Crear([FromBody] UsuarioCreateDto dto)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var login = dto.nombreUsuario.Trim();

            // Verificar duplicado
            if (await _db.Usuarios.AnyAsync(u => u.nombreUsuario == login))
                return Conflict($"El nombre de usuario '{login}' ya existe.");

            // ✅ Verificar rol existente y activo
            var rol = await _db.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.rolId == dto.rolId && r.activo);

            if (rol is null)
                return BadRequest("El rol especificado no existe o está inactivo.");

            // Crear usuario
            var u = new Usuario
            {
                nombreUsuario = login,
                claveHashUsuario = Pbkdf2PasswordHasher.HashToString(dto.clavePlano),
                telefonoUsuario = string.IsNullOrWhiteSpace(dto.telefonoUsuario) ? null : dto.telefonoUsuario.Trim(),
                correoUsuario = string.IsNullOrWhiteSpace(dto.correoUsuario) ? null : dto.correoUsuario.Trim(),
                rolId = rol.rolId,
                activo = true // siempre activo
            };

            _db.Usuarios.Add(u);
            await _db.SaveChangesAsync();

            // ✅ Solo mensaje de éxito
            return Ok(new { mensaje = "Usuario creado correctamente" });
        }

        /// PUT /api/usuarios/{id}
        [HttpPatch("{id:int}/actualizar")]
        public async Task<IActionResult> Actualizar(int id, [FromBody] UsuarioUpdateDto dto)
        {
            var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.UsuarioId == id);
            if (u is null) return NotFound();

            var rolExiste = await _db.Roles.AnyAsync(r => r.rolId == dto.rolId);
            if (!rolExiste) return BadRequest("Rol no válido.");

            u.telefonoUsuario = string.IsNullOrWhiteSpace(dto.telefonoUsuario) ? null : dto.telefonoUsuario.Trim();
            u.correoUsuario = string.IsNullOrWhiteSpace(dto.correoUsuario) ? null : dto.correoUsuario.Trim();
            u.activo = dto.activo;
            u.rolId = dto.rolId;

            _db.Usuarios.Update(u);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// PATCH /api/usuarios/{id}/clave
        [HttpPatch("{id:int}/clave")]
        public async Task<IActionResult> CambiarClave(int id, [FromBody] UsuarioPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.clavePlano))
                return BadRequest("La nueva clave es requerida.");

            var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.UsuarioId == id);
            if (u is null) return NotFound();

            u.claveHashUsuario = Pbkdf2PasswordHasher.HashToString(dto.clavePlano);
            _db.Usuarios.Update(u);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// DELETE /api/usuarios/{id} (lógico)
        [HttpPatch("{id:int}/Eliminar")]
        public async Task<IActionResult> Desactivar(int id)
        {
            var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.UsuarioId == id);
            if (u is null) return NotFound();

            u.activo = false;
            _db.Usuarios.Update(u);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
    }


