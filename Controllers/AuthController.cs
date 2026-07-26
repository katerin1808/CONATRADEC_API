using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Security;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using static CONATRADEC_API.DTOs.AuthDtos;
using static CONATRADEC_API.Models.Usuario;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Endpoints de autenticación.
    /// </summary>
    [ApiController]
    [Route("api/auth")]
    public sealed class AuthController :
        Controller
    {
        private readonly DBContext db;

        public AuthController(
            DBContext db)
        {
            this.db = db;
        }

        private static bool VerifyHash(
            string password,
            string stored)
        {
            string[] parts =
                stored.Split('$');

            if (parts.Length != 4 ||
                parts[0] != "PBKDF2")
            {
                return false;
            }

            if (!int.TryParse(
                    parts[1],
                    out int iter))
            {
                return false;
            }

            byte[] salt =
                Convert.FromBase64String(
                    parts[2]);

            byte[] hash =
                Convert.FromBase64String(
                    parts[3]);

            using var pbkdf2 =
                new Rfc2898DeriveBytes(
                    password,
                    salt,
                    iter,
                    HashAlgorithmName.SHA256);

            byte[] computed =
                pbkdf2.GetBytes(
                    hash.Length);

            return CryptographicOperations
                .FixedTimeEquals(
                    computed,
                    hash);
        }

        [HttpPost("login")]
        public async Task<
            ActionResult<UsuarioLoginResponseDto>>
            Login(
                [FromBody] UsuarioLoginDto req,
                CancellationToken cancellationToken =
                    default)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            Usuario? usuario =
                await db.Usuarios
                    .Include(item =>
                        item.Rol)
                    .Include(item =>
                        item.Procedencia)
                    .FirstOrDefaultAsync(
                        item =>
                            item.nombreUsuario ==
                                req.usuarioOEmail ||
                            item.correoUsuario ==
                                req.usuarioOEmail,
                        cancellationToken);

            if (usuario == null)
            {
                return Unauthorized(
                    "Usuario o contraseña inválidos.");
            }

            if (!usuario.activo)
            {
                return Unauthorized(
                    "Usuario inactivo.");
            }

            if (!VerifyHash(
                    req.clave,
                    usuario.claveHashUsuario))
            {
                return Unauthorized(
                    "Usuario o contraseña inválidos.");
            }

            /*
             * Crea la nueva opción de la matriz de forma idempotente antes de
             * devolver los permisos. No requiere ejecutar scripts.
             */
            await OfflinePermissionProvisioner
                .AsegurarAsync(
                    db,
                    cancellationToken);

            List<PermisoInterfazDto> permisos =
                await db.RolInterfaz
                    .AsNoTracking()
                    .Where(item =>
                        item.rolId ==
                            usuario.rolId)
                    .Join(
                        db.Interfaz
                            .AsNoTracking()
                            .Where(item =>
                                item.activo),
                        relacion =>
                            relacion.interfazId,
                        interfaz =>
                            interfaz.interfazId,
                        (relacion, interfaz) =>
                            new PermisoInterfazDto
                            {
                                interfazId =
                                    interfaz.interfazId,
                                nombreInterfaz =
                                    interfaz.nombreInterfaz,
                                leer =
                                    relacion.leer,
                                agregar =
                                    relacion.agregar,
                                actualizar =
                                    relacion.actualizar,
                                eliminar =
                                    relacion.eliminar
                            })
                    .OrderBy(item =>
                        item.nombreInterfaz)
                    .ToListAsync(
                        cancellationToken);

            var response =
                new UsuarioLoginResponseDto
                {
                    UsuarioId =
                        usuario.UsuarioId,
                    nombreUsuario =
                        usuario.nombreUsuario,
                    nombreCompletoUsuario =
                        usuario
                            .nombreCompletoUsuario,
                    correoUsuario =
                        usuario.correoUsuario,
                    activo =
                        usuario.activo,
                    rolId =
                        usuario.rolId,
                    rolNombre =
                        usuario.Rol.nombreRol,
                    procedenciaId =
                        usuario.procedenciaId,
                    procedenciaNombre =
                        usuario
                            .Procedencia
                            .nombreProcedencia,
                    esInterno =
                        usuario
                            .Procedencia
                            .nombreProcedencia
                            .Equals(
                                "Interno",
                                StringComparison
                                    .OrdinalIgnoreCase),
                    urlImagenUsuario =
                        usuario.urlImagenUsuario,
                    permisos =
                        permisos
                };

            return Ok(response);
        }
    }
}
