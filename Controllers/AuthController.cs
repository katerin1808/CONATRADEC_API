using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{

    /// Endpoints de autenticación (login).
    [ApiController]
    [Route("api/auth")]

    public class AuthController : Controller
    {

        private readonly DBContext _db;
        public AuthController(DBContext db) => _db = db;

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.nombreUsuario) || string.IsNullOrWhiteSpace(req.clavePlano))
                return BadRequest(new { mensaje = "Debe proporcionar usuario y contraseña." });

            // Buscar usuario activo por nombre
            var usuario = await _db.Usuarios
                .AsNoTracking()
                .Include(u => u.Rol)
                .FirstOrDefaultAsync(u => u.nombreUsuario == req.nombreUsuario.Trim() && u.activo);

            if (usuario is null)
                return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });

            // Verificar hash de la contraseña
            bool ok = Pbkdf2PasswordHasher.VerifyFromString(req.clavePlano, usuario.claveHashUsuario);

            if (!ok)
                return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });

            // ✅ Si todo bien:
            return Ok(new
            {
                mensaje = "Inicio de sesión exitoso",
                usuario = new
                {
                    usuario.UsuarioId,
                    usuario.nombreUsuario,
                    rol = usuario.Rol?.nombreRol ?? "(sin rol)"
                }
            });
        }
}
}
