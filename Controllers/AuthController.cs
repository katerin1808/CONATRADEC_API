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
    public sealed class AuthController : Controller
    {
        private readonly DBContext db;

        public AuthController(DBContext db)
        {
            this.db = db;
        }

        private static bool VerifyHash(
            string password,
            string stored)
        {
            string[] parts = stored.Split('$');

            if (parts.Length != 4 || parts[0] != "PBKDF2")
                return false;

            if (!int.TryParse(parts[1], out int iter))
                return false;

            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] hash = Convert.FromBase64String(parts[3]);

            using var pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                iter,
                HashAlgorithmName.SHA256);

            byte[] computed = pbkdf2.GetBytes(hash.Length);

            return CryptographicOperations.FixedTimeEquals(
                computed,
                hash);
        }

        [HttpPost("login")]
        public async Task<ActionResult<UsuarioLoginResponseDto>> Login(
            [FromBody] UsuarioLoginDto req,
            CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            Usuario? usuario = await db.Usuarios
                .Include(item => item.Rol)
                .Include(item => item.Procedencia)
                .FirstOrDefaultAsync(
                    item =>
                        item.nombreUsuario == req.usuarioOEmail ||
                        item.correoUsuario == req.usuarioOEmail,
                    cancellationToken);

            if (usuario == null)
                return Unauthorized("Usuario o contraseña inválidos.");

            if (!usuario.activo)
                return Unauthorized("Usuario inactivo.");

            if (!VerifyHash(req.clave, usuario.claveHashUsuario))
                return Unauthorized("Usuario o contraseña inválidos.");

            /*
             * Este aprovisionador puede crear una interfaz o una relación de
             * permisos. Cuando eso sucede, el interceptor aumenta en la base de
             * datos la versionSesion de los usuarios afectados.
             *
             * Como el usuario fue cargado antes, su entidad rastreada puede
             * conservar temporalmente la versión anterior. Si se devuelve esa
             * versión desactualizada, el siguiente request del cliente queda
             * invalidado aunque el usuario apenas haya iniciado sesión.
             */
            await OfflinePermissionProvisioner.AsegurarAsync(
                db,
                cancellationToken);

            // Recupera la versión definitiva que quedó guardada en la BD.
            await db.Entry(usuario).ReloadAsync(cancellationToken);

            /*
             * Protección adicional para instalaciones antiguas o registros que
             * todavía tengan una versión nula/cero por datos heredados.
             */
            if (usuario.versionSesion < 1)
            {
                usuario.versionSesion = 1;
                await db.SaveChangesAsync(cancellationToken);
            }

            List<PermisoInterfazDto> permisos = await db.RolInterfaz
                .AsNoTracking()
                .Where(item => item.rolId == usuario.rolId)
                .Join(
                    db.Interfaz
                        .AsNoTracking()
                        .Where(item => item.activo),
                    relacion => relacion.interfazId,
                    interfaz => interfaz.interfazId,
                    (relacion, interfaz) => new PermisoInterfazDto
                    {
                        interfazId = interfaz.interfazId,
                        nombreInterfaz = interfaz.nombreInterfaz,
                        leer = relacion.leer,
                        agregar = relacion.agregar,
                        actualizar = relacion.actualizar,
                        eliminar = relacion.eliminar
                    })
                .OrderBy(item => item.nombreInterfaz)
                .ToListAsync(cancellationToken);

            var response = new UsuarioLoginResponseDto
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
                esInterno = usuario.Procedencia.nombreProcedencia.Equals(
                    "Interno",
                    StringComparison.OrdinalIgnoreCase),
                urlImagenUsuario = usuario.urlImagenUsuario,
                versionSesion = usuario.versionSesion,
                permisos = permisos
            };

            return Ok(response);
        }
    }
}
