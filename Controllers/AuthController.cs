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
        // POST: api/Auth/login
        [HttpPost("login")]
        public async Task<ActionResult<UsuarioLoginResponseDto>> Login([FromBody] UsuarioLoginDto req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var u = await _db.Usuarios
                .Include(x => x.Rol)
                .Include(x => x.Procedencia)
                .FirstOrDefaultAsync(x =>
                    x.nombreUsuario == req.usuarioOEmail || x.correoUsuario == req.usuarioOEmail);

            if (u is null) return Unauthorized("Usuario o contraseña inválidos.");
            if (!u.activo) return Unauthorized("Usuario inactivo.");
            if (!VerifyHash(req.clave, u.claveHashUsuario)) return Unauthorized("Usuario o contraseña inválidos.");

            // 🔹 Traer la matriz de permisos del rol del usuario
            // Ajusta nombres de DbSet si en tu DbContext difieren (_db.RolInteraz / _db.Interfaz)
            var permisos = await _db.RolInteraz
                .Where(ri => ri.rolId == u.rolId)
                .Join(
                    _db.Interfaz.Where(i => i.activo),                      // filtra interfaces activas (si tienes esa columna)
                    ri => ri.interfazId,
                    i => i.interfazId,
                    (ri, i) => new PermisoInterfazDto
                    {
                        interfazId = i.interfazId,
                        nombreInterfaz = i.nombreInterfaz,
                        leer = ri.leer,
                        agregar = ri.agregar,
                        actualizar = ri.actualizar,
                        eliminar = ri.eliminar
                    }
                )
                .OrderBy(p => p.nombreInterfaz)
                .ToListAsync();

            // 🔹 Construir respuesta
            var resp = new UsuarioLoginResponseDto
            {
                UsuarioId = u.UsuarioId,
                nombreUsuario = u.nombreUsuario,
                nombreCompletoUsuario = u.nombreCompletoUsuario,
                correoUsuario = u.correoUsuario,
                activo = u.activo,

                rolId = u.rolId,
                rolNombre = u.Rol.nombreRol,

                procedenciaId = u.procedenciaId,
                procedenciaNombre = u.Procedencia.nombreProcedencia,
                esInterno = u.Procedencia.nombreProcedencia.Equals("Interno", StringComparison.OrdinalIgnoreCase),
                urlImagenUsuario = u.urlImagenUsuario,
                permisos = permisos
            };

            return Ok(resp);
        }
    }
}

