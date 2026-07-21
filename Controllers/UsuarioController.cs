using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/usuarios")]
    public class UsuarioController : Controller
    {
        private const string ROL_INVITADO = "Invitado";
        private const string PROC_INTERNO = "Interno";
        private const string PROC_EXTERNO = "Externo";
        private const int PBKDF2_ITER = 100_000;

        private static readonly Regex IdentificacionRegex = new(
            @"^\d{3}-\d{6}-\d{4}[A-Za-z]$",
            RegexOptions.Compiled);

        private readonly DBContext _db;
        private readonly IHostEnvironment _env;

        public UsuarioController(DBContext db, IHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        private static string BuildHash(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);

            using var pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                PBKDF2_ITER,
                HashAlgorithmName.SHA256);

            byte[] hash = pbkdf2.GetBytes(32);

            return $"PBKDF2${PBKDF2_ITER}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        private static bool VerifyHash(string storedHash, string password)
        {
            try
            {
                string[] parts = storedHash.Split('$');

                if (parts.Length != 4 ||
                    !parts[0].Equals("PBKDF2", StringComparison.Ordinal))
                {
                    return false;
                }

                int iteraciones = int.Parse(parts[1]);
                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] hashGuardado = Convert.FromBase64String(parts[3]);

                using var pbkdf2 = new Rfc2898DeriveBytes(
                    password,
                    salt,
                    iteraciones,
                    HashAlgorithmName.SHA256);

                byte[] hashNuevo = pbkdf2.GetBytes(32);

                return CryptographicOperations.FixedTimeEquals(
                    hashGuardado,
                    hashNuevo);
            }
            catch
            {
                return false;
            }
        }

        private static bool EsMayorDeEdad(DateOnly? fechaNacimiento)
        {
            if (!fechaNacimiento.HasValue)
                return false;

            DateOnly hoy = DateOnly.FromDateTime(DateTime.Today);
            int edad = hoy.Year - fechaNacimiento.Value.Year;

            if (hoy < fechaNacimiento.Value.AddYears(edad))
                edad--;

            return edad >= 18;
        }

        private async Task EnsureProcedenciasAsync()
        {
            if (await _db.Procedencia.AnyAsync())
                return;

            _db.Procedencia.AddRange(
                new Procedencia
                {
                    nombreProcedencia = PROC_INTERNO,
                    descripcionProcedencia = "Usuarios internos del sistema",
                    activo = true
                },
                new Procedencia
                {
                    nombreProcedencia = PROC_EXTERNO,
                    descripcionProcedencia = "Usuarios externos o invitados",
                    activo = true
                });

            await _db.SaveChangesAsync();
        }

        private async Task<int> GetProcedenciaIdAsync(bool esInterno)
        {
            string nombre = esInterno
                ? PROC_INTERNO
                : PROC_EXTERNO;

            Procedencia? procedencia = await _db.Procedencia
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    p => p.nombreProcedencia == nombre && p.activo);

            if (procedencia is null)
            {
                throw new InvalidOperationException(
                    $"No se encontró la procedencia '{nombre}'.");
            }

            return procedencia.procedenciaId;
        }

        private async Task<int> EnsureRolInvitadoAsync()
        {
            Rol? rol = await _db.Roles.FirstOrDefaultAsync(
                r => r.nombreRol == ROL_INVITADO && r.activo);

            if (rol is not null)
                return rol.rolId;

            rol = new Rol
            {
                nombreRol = ROL_INVITADO,
                descripcionRol = "Rol por defecto para usuarios externos",
                activo = true
            };

            _db.Roles.Add(rol);
            await _db.SaveChangesAsync();

            return rol.rolId;
        }

        private async Task<(string rolNombre, string procedenciaNombre, bool esInterno)>
            DescribeUsuarioAsync(Usuario usuario)
        {
            string rolNombre = await _db.Roles
                .AsNoTracking()
                .Where(r => r.rolId == usuario.rolId)
                .Select(r => r.nombreRol)
                .FirstOrDefaultAsync() ?? string.Empty;

            string procedenciaNombre = await _db.Procedencia
                .AsNoTracking()
                .Where(p => p.procedenciaId == usuario.procedenciaId)
                .Select(p => p.nombreProcedencia)
                .FirstOrDefaultAsync() ?? string.Empty;

            bool esInterno = procedenciaNombre.Equals(
                PROC_INTERNO,
                StringComparison.OrdinalIgnoreCase);

            return (rolNombre, procedenciaNombre, esInterno);
        }

        private static UsuarioReadDto MapToDto(
            Usuario usuario,
            string rolNombre,
            string procedenciaNombre,
            bool esInterno)
        {
            return new UsuarioReadDto
            {
                UsuarioId = usuario.UsuarioId,
                nombreUsuario = usuario.nombreUsuario,
                nombreCompletoUsuario = usuario.nombreCompletoUsuario,
                correoUsuario = usuario.correoUsuario,
                telefonoUsuario = usuario.telefonoUsuario,
                fechaNacimientoUsuario = usuario.fechaNacimientoUsuario,
                identificacionUsuario = usuario.identificacionUsuario,
                rolId = usuario.rolId,
                procedenciaId = usuario.procedenciaId,
                municipioId = usuario.municipioId,
                rolNombre = rolNombre,
                procedenciaNombre = procedenciaNombre,
                esInterno = esInterno,
                urlImagenUsuario = usuario.urlImagenUsuario ?? string.Empty
            };
        }

        [HttpGet("listar")]
        public async Task<ActionResult<IEnumerable<UsuarioReadDto>>> Listar()
        {
            List<Usuario> usuarios = await _db.Usuarios
                .AsNoTracking()
                .Where(u => u.activo)
                .OrderBy(u => u.nombreCompletoUsuario)
                .ToListAsync();

            var resultado = new List<UsuarioReadDto>(usuarios.Count);

            foreach (Usuario usuario in usuarios)
            {
                var (rolNombre, procedenciaNombre, esInterno) =
                    await DescribeUsuarioAsync(usuario);

                resultado.Add(
                    MapToDto(
                        usuario,
                        rolNombre,
                        procedenciaNombre,
                        esInterno));
            }

            return Ok(resultado);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<UsuarioReadDto>> BuscarPorId(int id)
        {
            Usuario? usuario = await _db.Usuarios
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.UsuarioId == id && x.activo);

            if (usuario is null)
                return NotFound("Usuario no encontrado o inactivo.");

            var (rolNombre, procedenciaNombre, esInterno) =
                await DescribeUsuarioAsync(usuario);

            return Ok(
                MapToDto(
                    usuario,
                    rolNombre,
                    procedenciaNombre,
                    esInterno));
        }

        [HttpPost("crear")]
        public async Task<IActionResult> Crear(
            [FromBody] UsuarioCrearDto req)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            string nombreUsuario =
                req.nombreUsuario.Trim().ToUpperInvariant();

            string correo =
                req.correoUsuario.Trim().ToUpperInvariant();

            string identificacion =
                req.identificacionUsuario.Trim().ToUpperInvariant();

            if (!IdentificacionRegex.IsMatch(identificacion))
            {
                return BadRequest(
                    "La identificación debe tener el formato 001-080701-1050R.");
            }

            if (!EsMayorDeEdad(req.fechaNacimientoUsuario))
            {
                return BadRequest(
                    "El usuario debe tener al menos 18 años.");
            }

            bool nombreTomado = await _db.Usuarios.AnyAsync(
                u => u.nombreUsuario == nombreUsuario);

            if (nombreTomado)
            {
                return Conflict(
                    "El nombre de usuario ya está registrado.");
            }

            bool correoTomado = await _db.Usuarios.AnyAsync(
                u => u.correoUsuario == correo);

            if (correoTomado)
            {
                return Conflict(
                    "El correo electrónico ya está registrado.");
            }

            bool identificacionTomada = await _db.Usuarios.AnyAsync(
                u => u.identificacionUsuario == identificacion);

            if (identificacionTomada)
            {
                return Conflict(
                    "La identificación ya está registrada.");
            }

            await EnsureProcedenciasAsync();

            int procedenciaId =
                await GetProcedenciaIdAsync(req.esInterno);

            int rolId;

            if (!req.esInterno)
            {
                rolId = await EnsureRolInvitadoAsync();
            }
            else
            {
                if (!req.rolId.HasValue)
                {
                    return BadRequest(
                        "Seleccione un rol para el usuario interno.");
                }

                Rol? rol = await _db.Roles.FirstOrDefaultAsync(
                    r => r.rolId == req.rolId.Value && r.activo);

                if (rol is null)
                {
                    return BadRequest(
                        "El rol seleccionado no existe o está inactivo.");
                }

                rolId = rol.rolId;
            }

            var usuario = new Usuario
            {
                nombreUsuario = nombreUsuario,
                nombreCompletoUsuario =
                    req.nombreCompletoUsuario.Trim().ToUpperInvariant(),
                correoUsuario = correo,
                telefonoUsuario = req.telefonoUsuario?.Trim(),
                fechaNacimientoUsuario = req.fechaNacimientoUsuario,
                identificacionUsuario = identificacion,
                activo = true,
                rolId = rolId,
                procedenciaId = procedenciaId,
                municipioId = req.municipioId,
                urlImagenUsuario = string.Empty,
                claveHashUsuario = BuildHash(req.clave)
            };

            _db.Usuarios.Add(usuario);
            await _db.SaveChangesAsync();

            var (rolNombre, procedenciaNombre, esInterno) =
                await DescribeUsuarioAsync(usuario);

            return CreatedAtAction(
                nameof(BuscarPorId),
                new { id = usuario.UsuarioId },
                MapToDto(
                    usuario,
                    rolNombre,
                    procedenciaNombre,
                    esInterno));
        }

        [HttpPost("{usuarioId:int}/SubirImagenUsuario")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> SubirImagenUsuario(
            int usuarioId,
            IFormFile? archivo)
        {
            if (archivo is null || archivo.Length == 0)
            {
                return BadRequest(
                    "Seleccione una imagen para el usuario.");
            }

            Usuario? usuario = await _db.Usuarios
                .FirstOrDefaultAsync(
                    u => u.UsuarioId == usuarioId && u.activo);

            if (usuario is null)
                return NotFound("Usuario no encontrado o inactivo.");

            string[] extensionesPermitidas =
                { ".jpg", ".jpeg", ".png" };

            string extension =
                Path.GetExtension(archivo.FileName).ToLowerInvariant();

            if (!extensionesPermitidas.Contains(extension))
            {
                return BadRequest(
                    "La imagen debe tener formato JPG, JPEG o PNG.");
            }

            if (string.IsNullOrWhiteSpace(archivo.ContentType) ||
                !archivo.ContentType.StartsWith(
                    "image/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    "El archivo seleccionado no corresponde a una imagen.");
            }

            string uploadsRoot = Path.Combine(
                _env.ContentRootPath,
                "resources",
                "uploads",
                "users",
                "img");

            Directory.CreateDirectory(uploadsRoot);

            string nombreArchivo =
                $"user_{usuario.nombreUsuario}_id_{usuario.UsuarioId}{extension}";

            string rutaFisica =
                Path.Combine(uploadsRoot, nombreArchivo);

            if (!string.IsNullOrWhiteSpace(usuario.urlImagenUsuario))
            {
                try
                {
                    var uri = new Uri(usuario.urlImagenUsuario);
                    string nombreAnterior =
                        Path.GetFileName(uri.LocalPath);

                    string rutaAnterior =
                        Path.Combine(uploadsRoot, nombreAnterior);

                    if (System.IO.File.Exists(rutaAnterior))
                        System.IO.File.Delete(rutaAnterior);
                }
                catch
                {
                    // La imagen nueva todavía puede guardarse.
                }
            }

            await using (var stream =
                new FileStream(rutaFisica, FileMode.Create))
            {
                await archivo.CopyToAsync(stream);
            }

            string baseUrl =
                $"{Request.Scheme}://{Request.Host.Value}";

            string urlPublica =
                $"{baseUrl}/resources/uploads/users/img/{nombreArchivo}";

            usuario.urlImagenUsuario = urlPublica;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                usuario.UsuarioId,
                usuario.nombreUsuario,
                urlImagen = urlPublica
            });
        }

        [HttpPut("actualizar/{id:int}")]
        public async Task<IActionResult> Actualizar(
            int id,
            [FromBody] UsuarioActualizarDto req)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            Usuario? usuario = await _db.Usuarios
                .FirstOrDefaultAsync(
                    x => x.UsuarioId == id && x.activo);

            if (usuario is null)
                return NotFound("Usuario no encontrado o inactivo.");

            string correo =
                req.correoUsuario.Trim().ToUpperInvariant();

            string identificacion =
                req.identificacionUsuario.Trim().ToUpperInvariant();

            if (!IdentificacionRegex.IsMatch(identificacion))
            {
                return BadRequest(
                    "La identificación debe tener el formato 001-080701-1050R.");
            }

            if (!EsMayorDeEdad(req.fechaNacimientoUsuario))
            {
                return BadRequest(
                    "El usuario debe tener al menos 18 años.");
            }

            bool correoTomado = await _db.Usuarios.AnyAsync(
                x => x.UsuarioId != id &&
                     x.correoUsuario == correo);

            if (correoTomado)
            {
                return Conflict(
                    "El correo electrónico está registrado por otro usuario.");
            }

            bool identificacionTomada = await _db.Usuarios.AnyAsync(
                x => x.UsuarioId != id &&
                     x.identificacionUsuario == identificacion);

            if (identificacionTomada)
            {
                return Conflict(
                    "La identificación está registrada por otro usuario.");
            }

            usuario.nombreCompletoUsuario =
                req.nombreCompletoUsuario.Trim().ToUpperInvariant();

            usuario.correoUsuario = correo;
            usuario.telefonoUsuario = req.telefonoUsuario?.Trim();
            usuario.fechaNacimientoUsuario =
                req.fechaNacimientoUsuario;

            usuario.municipioId = req.municipioId;
            usuario.identificacionUsuario = identificacion;

            if (req.activo.HasValue)
                usuario.activo = req.activo.Value;

            await EnsureProcedenciasAsync();

            usuario.procedenciaId =
                await GetProcedenciaIdAsync(req.esInterno);

            if (!req.esInterno)
            {
                usuario.rolId = await EnsureRolInvitadoAsync();
            }
            else
            {
                if (!req.rolId.HasValue)
                {
                    return BadRequest(
                        "Seleccione un rol para el usuario interno.");
                }

                Rol? rol = await _db.Roles.FirstOrDefaultAsync(
                    r => r.rolId == req.rolId.Value && r.activo);

                if (rol is null)
                {
                    return BadRequest(
                        "El rol seleccionado no existe o está inactivo.");
                }

                usuario.rolId = rol.rolId;
            }

            // Si el campo llega vacío, se conserva la contraseña actual.
            if (!string.IsNullOrWhiteSpace(req.nuevaClave))
            {
                usuario.claveHashUsuario =
                    BuildHash(req.nuevaClave);
            }

            await _db.SaveChangesAsync();

            var (rolNombre, procedenciaNombre, esInterno) =
                await DescribeUsuarioAsync(usuario);

            return Ok(
                MapToDto(
                    usuario,
                    rolNombre,
                    procedenciaNombre,
                    esInterno));
        }

        [HttpDelete("eliminar/{id:int}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            Usuario? usuario = await _db.Usuarios
                .FirstOrDefaultAsync(
                    x => x.UsuarioId == id && x.activo);

            if (usuario is null)
                return NotFound("Usuario no encontrado o inactivo.");

            usuario.activo = false;
            await _db.SaveChangesAsync();

            return NoContent();
        }

        [HttpPut("{id:int}/actualizar-clave")]
        public async Task<IActionResult> ActualizarClave(
            int id,
            [FromBody] UsuarioActualizarClaveDto req)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            Usuario? usuario = await _db.Usuarios
                .FirstOrDefaultAsync(
                    u => u.UsuarioId == id && u.activo);

            if (usuario is null)
                return NotFound("Usuario no encontrado o inactivo.");

            if (!VerifyHash(
                    usuario.claveHashUsuario,
                    req.claveActual))
            {
                return BadRequest(
                    "La contraseña actual es incorrecta.");
            }

            usuario.claveHashUsuario =
                BuildHash(req.nuevaClave);

            await _db.SaveChangesAsync();

            return Ok(
                "Contraseña actualizada correctamente.");
        }
    }
}
