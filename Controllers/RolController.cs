using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class RolController : ControllerBase
    {
        private readonly DBContext context;
        private readonly ILogger<RolController> logger;

        public RolController(
            DBContext context,
            ILogger<RolController> logger)
        {
            this.context = context;
            this.logger = logger;
        }

        [HttpPost("crearRol")]
        public async Task<IActionResult> CrearRol(
            [FromBody] RolCreateDto? dto,
            CancellationToken cancellationToken)
        {
            if (dto == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "No se recibieron los datos del rol."
                });
            }

            string nombre = NormalizarNombre(dto.nombreRol);
            string descripcion = NormalizarDescripcion(dto.descripcionRol);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El nombre del rol es obligatorio."
                });
            }

            bool existeActivo =
                await context.Roles
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.activo &&
                            EF.Functions.Collate(
                                item.nombreRol,
                                "Modern_Spanish_CI_AI") == nombre,
                        cancellationToken);

            if (existeActivo)
            {
                return Conflict(new
                {
                    success = false,
                    message = "Ya existe un rol activo con ese nombre."
                });
            }

            Rol? inactivo =
                await context.Roles
                    .FirstOrDefaultAsync(
                        item =>
                            !item.activo &&
                            EF.Functions.Collate(
                                item.nombreRol,
                                "Modern_Spanish_CI_AI") == nombre,
                        cancellationToken);

            try
            {
                if (inactivo != null)
                {
                    inactivo.nombreRol = nombre;
                    inactivo.descripcionRol = descripcion;
                    inactivo.activo = true;

                    await context.SaveChangesAsync(cancellationToken);

                    return Ok(new
                    {
                        success = true,
                        message = "Rol reactivado correctamente.",
                        data = new
                        {
                            inactivo.rolId,
                            inactivo.nombreRol,
                            inactivo.descripcionRol
                        }
                    });
                }

                var nuevoRol = new Rol
                {
                    nombreRol = nombre,
                    descripcionRol = descripcion,
                    activo = true
                };

                context.Roles.Add(nuevoRol);
                await context.SaveChangesAsync(cancellationToken);

                return CreatedAtAction(
                    nameof(BuscarRol),
                    new { id = nuevoRol.rolId },
                    new
                    {
                        success = true,
                        message = "Rol creado correctamente.",
                        data = new
                        {
                            nuevoRol.rolId,
                            nuevoRol.nombreRol,
                            nuevoRol.descripcionRol
                        }
                    });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al crear el rol {NombreRol}.",
                    nombre);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible crear el rol porque existe un registro con el mismo nombre."
                });
            }
        }

        [HttpGet("listarRoles")]
        public async Task<IActionResult> ListarRoles(
            CancellationToken cancellationToken)
        {
            var roles =
                await context.Roles
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .OrderBy(item => item.nombreRol)
                    .Select(item => new
                    {
                        item.rolId,
                        item.nombreRol,
                        item.descripcionRol
                    })
                    .ToListAsync(cancellationToken);

            return Ok(roles);
        }

        [HttpGet("buscarRol/{id:int}")]
        public async Task<IActionResult> BuscarRol(
            int id,
            CancellationToken cancellationToken)
        {
            var rol =
                await context.Roles
                    .AsNoTracking()
                    .Where(item => item.rolId == id && item.activo)
                    .Select(item => new
                    {
                        item.rolId,
                        item.nombreRol,
                        item.descripcionRol
                    })
                    .SingleOrDefaultAsync(cancellationToken);

            if (rol == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "No se encontró un rol activo con ese identificador."
                });
            }

            return Ok(rol);
        }

        [HttpPut("editarRol/{id:int}")]
        public async Task<IActionResult> UpdateRol(
            int id,
            [FromBody] RolUpdateDto? dto,
            CancellationToken cancellationToken)
        {
            if (dto == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "No se recibieron los datos del rol."
                });
            }

            Rol? rol =
                await context.Roles
                    .FirstOrDefaultAsync(
                        item => item.rolId == id && item.activo,
                        cancellationToken);

            if (rol == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "No se encontró un rol activo con ese identificador."
                });
            }

            string nombre = NormalizarNombre(dto.nombreRol);
            string descripcion = NormalizarDescripcion(dto.descripcionRol);

            bool duplicado =
                await context.Roles
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.rolId != id &&
                            item.activo &&
                            EF.Functions.Collate(
                                item.nombreRol,
                                "Modern_Spanish_CI_AI") == nombre,
                        cancellationToken);

            if (duplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message = "Ya existe otro rol activo con ese nombre."
                });
            }

            rol.nombreRol = nombre;
            rol.descripcionRol = descripcion;

            try
            {
                await context.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Rol actualizado correctamente.",
                    data = new
                    {
                        rol.rolId,
                        rol.nombreRol,
                        rol.descripcionRol
                    }
                });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al actualizar el rol {RolId}.",
                    id);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible actualizar el rol porque existe otro registro con el mismo nombre."
                });
            }
        }

        [HttpDelete("eliminarRol/{id:int}")]
        public async Task<IActionResult> DeleteRol(
            int id,
            CancellationToken cancellationToken)
        {
            Rol? rol =
                await context.Roles
                    .FirstOrDefaultAsync(
                        item => item.rolId == id && item.activo,
                        cancellationToken);

            if (rol == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El rol no existe o ya está desactivado."
                });
            }

            var dependencias = new List<string>();

            if (await context.Usuarios
                    .AsNoTracking()
                    .AnyAsync(
                        item => item.rolId == id && item.activo,
                        cancellationToken))
            {
                dependencias.Add("usuarios activos");
            }

            if (await context.RolInterfaz
                    .AsNoTracking()
                    .AnyAsync(
                        item => item.rolId == id,
                        cancellationToken))
            {
                dependencias.Add("permisos de interfaces");
            }

            if (dependencias.Count > 0)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede eliminar el rol porque está siendo utilizado.",
                    usadoEn = dependencias
                });
            }

            rol.activo = false;
            await context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Rol desactivado correctamente."
            });
        }

        private static string NormalizarNombre(string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();

        private static string NormalizarDescripcion(string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim();
    }
}
