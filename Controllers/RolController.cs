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
        private const string NOMBRE_ROL_ADMINISTRADOR =
            "Administrador";

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
            string descripcion =
                NormalizarDescripcion(dto.descripcionRol);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El nombre del rol es obligatorio."
                });
            }

            Rol? existente = await context.Roles
                .FirstOrDefaultAsync(
                    item =>
                        EF.Functions.Collate(
                            item.nombreRol,
                            "Modern_Spanish_CI_AI") == nombre,
                    cancellationToken);

            if (existente is not null)
            {
                if (existente.activo)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "Ya existe un rol activo con ese nombre."
                    });
                }

                return Conflict(new
                {
                    success = false,
                    code = "ROL_INACTIVO_EXISTENTE",
                    message =
                        "Ya existe un rol inactivo con ese nombre. Puedes reactivarlo desde la lista de roles inactivos.",
                    data = new
                    {
                        existente.rolId,
                        existente.nombreRol,
                        existente.descripcionRol
                    }
                });
            }

            var nuevoRol = new Rol
            {
                nombreRol = nombre,
                descripcionRol = descripcion,
                activo = true
            };

            try
            {
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
                            nuevoRol.descripcionRol,
                            nuevoRol.activo
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
            var roles = await context.Roles
                .AsNoTracking()
                .Where(item => item.activo)
                .OrderBy(item => item.nombreRol)
                .Select(item => new
                {
                    item.rolId,
                    item.nombreRol,
                    item.descripcionRol,
                    item.activo,
                    esAdministrador =
                        item.nombreRol.Trim().ToUpper() ==
                        "ADMINISTRADOR"
                })
                .ToListAsync(cancellationToken);

            return Ok(roles);
        }

        [HttpGet("listarRolesInactivos")]
        public async Task<IActionResult> ListarRolesInactivos(
            CancellationToken cancellationToken)
        {
            var roles = await context.Roles
                .AsNoTracking()
                .Where(item => !item.activo)
                .OrderBy(item => item.nombreRol)
                .Select(item => new
                {
                    item.rolId,
                    item.nombreRol,
                    item.descripcionRol,
                    item.activo,
                    esAdministrador =
                        item.nombreRol.Trim().ToUpper() ==
                        "ADMINISTRADOR"
                })
                .ToListAsync(cancellationToken);

            return Ok(roles);
        }

        [HttpPut("reactivarRol/{id:int}")]
        public async Task<IActionResult> ReactivarRol(
            int id,
            CancellationToken cancellationToken)
        {
            Rol? rol = await context.Roles
                .FirstOrDefaultAsync(
                    item => item.rolId == id,
                    cancellationToken);

            if (rol == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "No se encontró el rol."
                });
            }

            if (rol.activo)
            {
                return Conflict(new
                {
                    success = false,
                    message = "El rol ya se encuentra activo."
                });
            }

            bool existeActivoMismoNombre =
                await context.Roles
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.rolId != rol.rolId &&
                            item.activo &&
                            EF.Functions.Collate(
                                item.nombreRol,
                                "Modern_Spanish_CI_AI") ==
                            rol.nombreRol,
                        cancellationToken);

            if (existeActivoMismoNombre)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede reactivar porque ya existe otro rol activo con el mismo nombre."
                });
            }

            rol.activo = true;
            await context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Rol reactivado correctamente.",
                data = new
                {
                    rol.rolId,
                    rol.nombreRol,
                    rol.descripcionRol,
                    rol.activo
                }
            });
        }

        [HttpGet("buscarRol/{id:int}")]
        public async Task<IActionResult> BuscarRol(
            int id,
            CancellationToken cancellationToken)
        {
            var rol = await context.Roles
                .AsNoTracking()
                .Where(item =>
                    item.rolId == id &&
                    item.activo)
                .Select(item => new
                {
                    item.rolId,
                    item.nombreRol,
                    item.descripcionRol,
                    item.activo,
                    esAdministrador =
                        item.nombreRol.Trim().ToUpper() ==
                        "ADMINISTRADOR"
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (rol == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontró un rol activo con ese identificador."
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

            Rol? rol = await context.Roles
                .FirstOrDefaultAsync(
                    item =>
                        item.rolId == id &&
                        item.activo,
                    cancellationToken);

            if (rol == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontró un rol activo con ese identificador."
                });
            }

            if (EsAdministrador(rol.nombreRol))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "El rol Administrador está protegido y no puede editarse."
                });
            }

            string nombre = NormalizarNombre(dto.nombreRol);
            string descripcion =
                NormalizarDescripcion(dto.descripcionRol);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El nombre del rol es obligatorio."
                });
            }

            bool duplicado = await context.Roles
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
                    message =
                        "Ya existe otro rol activo con ese nombre."
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
                        rol.descripcionRol,
                        rol.activo
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
            Rol? rol = await context.Roles
                .FirstOrDefaultAsync(
                    item =>
                        item.rolId == id &&
                        item.activo,
                    cancellationToken);

            if (rol == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El rol no existe o ya está desactivado."
                });
            }

            if (EsAdministrador(rol.nombreRol))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "El rol Administrador está protegido y no puede desactivarse."
                });
            }

            bool tieneUsuariosActivos =
                await context.Usuarios
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.rolId == id &&
                            item.activo,
                        cancellationToken);

            if (tieneUsuariosActivos)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede desactivar el rol porque tiene usuarios activos asignados.",
                    usadoEn = new[] { "usuarios activos" }
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

        private static bool EsAdministrador(string? nombreRol) =>
            string.Equals(
                nombreRol?.Trim(),
                NOMBRE_ROL_ADMINISTRADOR,
                StringComparison.OrdinalIgnoreCase);

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
