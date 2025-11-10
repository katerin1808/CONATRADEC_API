using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using static CONATRADEC_API.DTOs.AuthDtos;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/usuarios")]

    public class UsuarioController : Controller
    {
        private readonly DBContext _db;
        public UsuarioController(DBContext db) => _db = db;

        // ==========================
        // Constantes
        // ==========================
        private const string ROL_INVITADO = "Invitado";
        private const string PROC_INTERNO = "Interno";
        private const string PROC_EXTERNO = "Externo";
        private const int PBKDF2_ITER = 100_000;

        // ==========================
        // Helpers de hash (PBKDF2)
        // Formato: PBKDF2$<iter>$<saltB64>$<hashB64>
        // ==========================
        private static string BuildHash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, PBKDF2_ITER, HashAlgorithmName.SHA256);
            var hash = pbkdf2.GetBytes(32);
            return $"PBKDF2${PBKDF2_ITER}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        // ==========================
        // Helpers Procedencia / Rol (sin esInterno)
        // ==========================
        private async Task EnsureProcedenciasAsync()
        {
            if (!await _db.Procedencia.AnyAsync())
            {
                _db.Procedencia.Add(new Procedencia
                {
                    nombreProcedencia = PROC_INTERNO,
                    descripcionProcedencia = "Usuarios internos del sistema",
                    activo = true
                });
                _db.Procedencia.Add(new Procedencia
                {
                    nombreProcedencia = PROC_EXTERNO,
                    descripcionProcedencia = "Usuarios externos o invitados",
                    activo = true
                });
                await _db.SaveChangesAsync();
            }
        }

        private async Task<int> GetProcedenciaIdAsync(bool esInterno)
        {
            var nombre = esInterno ? PROC_INTERNO : PROC_EXTERNO;
            var proc = await _db.Procedencia.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.nombreProcedencia == nombre && p.activo);
            if (proc is null)
                throw new InvalidOperationException($"No se encontró la procedencia '{nombre}'.");
            return proc.procedenciaId;
        }

        private async Task<int> EnsureRolInvitadoAsync()
        {
            var rol = await _db.Roles.FirstOrDefaultAsync(r => r.nombreRol == ROL_INVITADO && r.activo);
            if (rol is null)
            {
                rol = new Rol
                {
                    nombreRol = ROL_INVITADO,
                    descripcionRol = "Rol por defecto para usuarios externos",
                    activo = true
                };
                _db.Roles.Add(rol);
                await _db.SaveChangesAsync();
            }
            return rol.rolId;
        }

        private async Task<(string rolNombre, string procedenciaNombre, bool esInterno)> DescribeUsuarioAsync(Usuario u)
        {
            var rol = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.rolId == u.rolId);
            var proc = await _db.Procedencia.AsNoTracking().FirstOrDefaultAsync(p => p.procedenciaId == u.procedenciaId);

            var rolNombre = rol?.nombreRol ?? string.Empty;
            var procNombre = proc?.nombreProcedencia ?? string.Empty;
            var interno = procNombre.Equals(PROC_INTERNO, StringComparison.OrdinalIgnoreCase);

            return (rolNombre, procNombre, interno);
        }

        private static UsuarioReadDto MapToDto(Usuario u, string rolNombre, string procedenciaNombre, bool esInterno) => new()
        {
            UsuarioId = u.UsuarioId,
            nombreUsuario = u.nombreUsuario,
            nombreCompletoUsuario = u.nombreCompletoUsuario,
            correoUsuario = u.correoUsuario,
            telefonoUsuario = u.telefonoUsuario,
            fechaNacimientoUsuario = u.fechaNacimientoUsuario,
            activo = u.activo,
            rolId = u.rolId,
            procedenciaId = u.procedenciaId,
            municipioId = u.municipioId,
            rolNombre = rolNombre,
            procedenciaNombre = procedenciaNombre,
            esInterno = esInterno
        };

        // ==========================
        // 1) LISTAR
        // ==========================
        [HttpGet("listar")]
        public async Task<ActionResult<IEnumerable<UsuarioReadDto>>> Listar()
        {
            var usuarios = await _db.Usuarios.AsNoTracking().ToListAsync();
            var result = new List<UsuarioReadDto>(usuarios.Count);

            foreach (var u in usuarios)
            {
                var (rolNombre, procNombre, interno) = await DescribeUsuarioAsync(u);
                result.Add(MapToDto(u, rolNombre, procNombre, interno));
            }
            return Ok(result);
        }

        // ==========================
        // 2) BUSCAR POR ID
        // ==========================
        [HttpGet("{id:int}")]
        public async Task<ActionResult<UsuarioReadDto>> BuscarPorId(int id)
        {
            var u = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.UsuarioId == id);
            if (u is null) return NotFound("Usuario no encontrado.");

            var (rolNombre, procNombre, interno) = await DescribeUsuarioAsync(u);
            return Ok(MapToDto(u, rolNombre, procNombre, interno));
        }

        // ==========================
        // 3) CREAR (Interno/Externo en backend)
        // ==========================
        [HttpPost("crear")]
        public async Task<IActionResult> Crear([FromBody] UsuarioCrearDto req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            bool existe = await _db.Usuarios.AnyAsync(u =>
                u.nombreUsuario == req.nombreUsuario || u.correoUsuario == req.correoUsuario);
            if (existe) return Conflict("El usuario o el correo ya existen.");

            await EnsureProcedenciasAsync();
            var procedenciaId = await GetProcedenciaIdAsync(req.esInterno);

            int rolId;
            if (!req.esInterno)
            {
                // Externo -> forzar "Invitado"
                rolId = await EnsureRolInvitadoAsync();
            }
            else
            {
                if (!req.rolId.HasValue)
                    return BadRequest("Para usuarios internos debes especificar un rolId.");
                var rol = await _db.Roles.FirstOrDefaultAsync(r => r.rolId == req.rolId.Value && r.activo);
                if (rol is null) return BadRequest("El rolId indicado no existe o está inactivo.");
                rolId = rol.rolId;
            }

            var usuario = new Usuario
            {
                nombreUsuario = req.nombreUsuario.Trim(),
                nombreCompletoUsuario = req.nombreCompletoUsuario.Trim(),
                correoUsuario = req.correoUsuario.Trim(),
                telefonoUsuario = req.telefonoUsuario,
                fechaNacimientoUsuario = req.fechaNacimientoUsuario,
                identificacionUsuario = req.identificacionUsuario,
                activo = true,
                rolId = rolId,
                procedenciaId = procedenciaId,
                municipioId = req.municipioId,
                claveHashUsuario = BuildHash(req.clave)
            };

            _db.Usuarios.Add(usuario);
            await _db.SaveChangesAsync();

            var (rolNombre, procNombre, interno2) = await DescribeUsuarioAsync(usuario);
            return CreatedAtAction(nameof(BuscarPorId), new { id = usuario.UsuarioId }, MapToDto(usuario, rolNombre, procNombre, interno2));
        }

        // ==========================
        // 4) ACTUALIZAR (misma regla Interno/Externo)
        // ==========================
        [HttpPut("actualizar/{id:int}")]
        public async Task<IActionResult> Actualizar(int id, [FromBody] UsuarioActualizarDto req)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.UsuarioId == id);
            if (u is null) return NotFound("Usuario no encontrado.");

            bool correoTomado = await _db.Usuarios.AnyAsync(x => x.UsuarioId != id && x.correoUsuario == req.correoUsuario);
            if (correoTomado) return Conflict("El correo ya está en uso por otro usuario.");

            u.nombreCompletoUsuario = req.nombreCompletoUsuario.Trim();
            u.correoUsuario = req.correoUsuario.Trim();
            u.telefonoUsuario = req.telefonoUsuario;
            u.fechaNacimientoUsuario = req.fechaNacimientoUsuario;
            u.municipioId = req.municipioId;
            u.identificacionUsuario = req.identificacionUsuario;
            if (req.activo.HasValue) u.activo = req.activo.Value;

            await EnsureProcedenciasAsync();
            u.procedenciaId = await GetProcedenciaIdAsync(req.esInterno);

            if (!req.esInterno)
            {
                u.rolId = await EnsureRolInvitadoAsync();
            }
            else if (req.rolId.HasValue)
            {
                var rol = await _db.Roles.FirstOrDefaultAsync(r => r.rolId == req.rolId.Value && r.activo);
                if (rol is null) return BadRequest("El rolId indicado no existe o está inactivo.");
                u.rolId = rol.rolId;
            }

            if (!string.IsNullOrWhiteSpace(req.nuevaClave))
                u.claveHashUsuario = BuildHash(req.nuevaClave);

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // ==========================
        // 5) ELIMINAR (Soft Delete)
        // ==========================
        [HttpDelete("eliminar/{id:int}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.UsuarioId == id);
            if (u is null) return NotFound("Usuario no encontrado.");

            u.activo = false;
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}

