using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using static CONATRADEC_API.DTOs.AuthDtos;
using static CONATRADEC_API.Models.Usuario;

namespace CONATRADEC_API.Controllers
{

    /// Endpoints de autenticación (login).
    [ApiController]
    [Route("api/auth")]

    public class AuthController : Controller
    {
        private readonly DBContext _db;
        public AuthController(DBContext db) => _db = db;

        // PBKDF2 verifier
        private static bool VerifyHash(string password, string stored)
        {
            var parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != "PBKDF2") return false;
            if (!int.TryParse(parts[1], out var iter)) return false;

            var salt = Convert.FromBase64String(parts[2]);
            var hash = Convert.FromBase64String(parts[3]);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iter, HashAlgorithmName.SHA256);
            var computed = pbkdf2.GetBytes(hash.Length);
            return CryptographicOperations.FixedTimeEquals(computed, hash);
        }

        // ==========================
        // LOGIN
        // POST: api/Auth/login
        // ==========================
        [HttpPost("login")]
        public async Task<ActionResult<UsuarioLoginResponseDto>> Login([FromBody] UsuarioLoginDto req)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var usuario = await _db.Usuarios
                .Include(u => u.Rol)
                .Include(u => u.Procedencia)
                .FirstOrDefaultAsync(u =>
                    u.nombreUsuario == req.usuarioOEmail || u.correoUsuario == req.usuarioOEmail);

            if (usuario is null)
                return Unauthorized("Usuario o contraseña inválidos.");

            if (!usuario.activo)
                return Unauthorized("Usuario inactivo.");

            if (!VerifyHash(req.clave, usuario.claveHashUsuario))
                return Unauthorized("Usuario o contraseña inválidos.");

            var respuesta = new UsuarioLoginResponseDto
            {
                UsuarioId = usuario.UsuarioId,
                nombreUsuario = usuario.nombreUsuario,
                nombreCompletoUsuario = usuario.nombreCompletoUsuario,
                correoUsuario = usuario.correoUsuario,
                activo = usuario.activo,
                rolId = usuario.rolId,
                rolNombre = usuario.Rol.nombreRol,
                procedenciaId = usuario.procedenciaId,
                procedenciaNombre = usuario.Procedencia.nombreProcedencia,
                esInterno = usuario.Procedencia.nombreProcedencia.Equals("Interno", StringComparison.OrdinalIgnoreCase)
            };

            return Ok(respuesta);

        }
}
}

