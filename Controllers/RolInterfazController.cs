using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/rol-permisos")]
    public sealed class RolPermisosController : ControllerBase
    {
        private const string NOMBRE_ROL_ADMINISTRADOR =
            "Administrador";

        private readonly DBContext db;

        public RolPermisosController(DBContext db)
        {
            this.db = db;
        }

        [HttpGet("/api/rol-interfaz/matriz-por-rol")]
        public async Task<ActionResult<IEnumerable<RolConPermisosDto>>>
            ListarMatrizPorRol(
                CancellationToken cancellationToken)
        {
            List<RolConPermisosDto> resultado =
                await ConstruirMatrizAsync(
                    nombreRol: null,
                    cancellationToken);

            return Ok(resultado);
        }

        [HttpGet("/api/rol-interfaz/matriz-por-rol-nombre")]
        public async Task<ActionResult<IEnumerable<RolConPermisosDto>>>
            ListarMatrizPorNombre(
                [FromQuery] string nombreRol,
                CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(nombreRol))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Debe proporcionar el nombre del rol."
                });
            }

            List<RolConPermisosDto> resultado =
                await ConstruirMatrizAsync(
                    nombreRol.Trim(),
                    cancellationToken);

            if (resultado.Count == 0)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        $"No se encontró el rol '{nombreRol}'."
                });
            }

            return Ok(resultado);
        }

        [HttpPut("actualizar-interfaz")]
        public async Task<IActionResult> ActualizarPermisos(
            [FromBody] RolConPermisosDto? dto,
            CancellationToken cancellationToken)
        {
            if (dto?.rol == null ||
                dto.interfaz == null ||
                dto.interfaz.Count == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La matriz recibida está vacía o mal formada."
                });
            }

            Rol? rol = await db.Roles
                .FirstOrDefaultAsync(
                    item =>
                        item.rolId == dto.rol.rolId &&
                        item.activo,
                    cancellationToken);

            if (rol == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El rol no existe o está inactivo."
                });
            }

            if (EsAdministrador(rol.nombreRol))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Los permisos del rol Administrador están protegidos y no pueden modificarse."
                });
            }

            List<int> interfazIds = dto.interfaz
                .Select(item => item.interfazId)
                .Distinct()
                .ToList();

            HashSet<int> interfacesValidas =
                (await db.Interfaz
                    .AsNoTracking()
                    .Where(item =>
                        item.activo &&
                        interfazIds.Contains(item.interfazId))
                    .Select(item => item.interfazId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();

            List<RolInterfaz> existentes =
                await db.RolInterfaz
                    .Where(item =>
                        item.rolId == rol.rolId &&
                        interfazIds.Contains(item.interfazId))
                    .ToListAsync(cancellationToken);

            Dictionary<int, RolInterfaz> mapa =
                existentes.ToDictionary(
                    item => item.interfazId);

            await using var transaccion =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                foreach (InterfazPermisoDto permiso in dto.interfaz)
                {
                    if (!interfacesValidas.Contains(
                            permiso.interfazId))
                    {
                        continue;
                    }

                    if (mapa.TryGetValue(
                            permiso.interfazId,
                            out RolInterfaz? relacion))
                    {
                        relacion.leer = permiso.leer;
                        relacion.agregar = permiso.agregar;
                        relacion.actualizar =
                            permiso.actualizar;
                        relacion.eliminar =
                            permiso.eliminar;
                    }
                    else
                    {
                        db.RolInterfaz.Add(
                            new RolInterfaz
                            {
                                rolId = rol.rolId,
                                interfazId =
                                    permiso.interfazId,
                                leer = permiso.leer,
                                agregar = permiso.agregar,
                                actualizar =
                                    permiso.actualizar,
                                eliminar =
                                    permiso.eliminar
                            });
                    }
                }

                await db.SaveChangesAsync(
                    cancellationToken);

                await transaccion.CommitAsync(
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Permisos actualizados correctamente."
                });
            }
            catch
            {
                await transaccion.RollbackAsync(
                    cancellationToken);

                throw;
            }
        }

        [HttpPost("agregar-interfaz-por-nombre")]
        public async Task<IActionResult>
            AgregarPermisoPorNombre(
                [FromBody]
                AgregarPermisoPorNombreRequest? request,
                CancellationToken cancellationToken)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(
                    request.nombreRol) ||
                string.IsNullOrWhiteSpace(
                    request.nombreInterfaz))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Debe enviar el nombre del rol y de la interfaz."
                });
            }

            Rol? rol = await db.Roles
                .FirstOrDefaultAsync(
                    item =>
                        item.activo &&
                        item.nombreRol ==
                            request.nombreRol.Trim(),
                    cancellationToken);

            if (rol == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontró el rol."
                });
            }

            if (EsAdministrador(rol.nombreRol))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Los permisos del rol Administrador están protegidos."
                });
            }

            Interfaz? interfaz =
                await db.Interfaz
                    .FirstOrDefaultAsync(
                        item =>
                            item.activo &&
                            item.nombreInterfaz ==
                                request.nombreInterfaz
                                    .Trim(),
                        cancellationToken);

            if (interfaz == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "No se encontró la interfaz."
                });
            }

            RolInterfaz? existente =
                await db.RolInterfaz
                    .FirstOrDefaultAsync(
                        item =>
                            item.rolId == rol.rolId &&
                            item.interfazId ==
                                interfaz.interfazId,
                        cancellationToken);

            if (existente == null)
            {
                existente = new RolInterfaz
                {
                    rolId = rol.rolId,
                    interfazId =
                        interfaz.interfazId
                };

                db.RolInterfaz.Add(existente);
            }

            existente.leer = request.leer;
            existente.agregar = request.agregar;
            existente.actualizar =
                request.actualizar;
            existente.eliminar =
                request.eliminar;

            await db.SaveChangesAsync(
                cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Permiso actualizado correctamente."
            });
        }

        private async Task<List<RolConPermisosDto>>
            ConstruirMatrizAsync(
                string? nombreRol,
                CancellationToken cancellationToken)
        {
            IQueryable<Rol> rolesQuery =
                db.Roles
                    .AsNoTracking()
                    .Where(item => item.activo);

            if (!string.IsNullOrWhiteSpace(nombreRol))
            {
                rolesQuery =
                    rolesQuery.Where(
                        item =>
                            item.nombreRol ==
                                nombreRol);
            }

            IQueryable<Interfaz> interfacesQuery =
                db.Interfaz
                    .AsNoTracking()
                    .Where(item => item.activo);

            var filas = await (
                from rol in rolesQuery
                from interfaz in interfacesQuery
                join relacionBase
                    in db.RolInterfaz.AsNoTracking()
                    on new
                    {
                        rol.rolId,
                        interfaz.interfazId
                    }
                    equals new
                    {
                        relacionBase.rolId,
                        relacionBase.interfazId
                    }
                    into relaciones
                from relacion
                    in relaciones.DefaultIfEmpty()
                orderby
                    rol.nombreRol,
                    interfaz.nombreAmigableInterfaz,
                    interfaz.nombreInterfaz
                select new
                {
                    rol.rolId,
                    rol.nombreRol,

                    esAdministrador =
                        rol.nombreRol.Trim().ToUpper() ==
                        "ADMINISTRADOR",

                    interfaz.interfazId,
                    interfaz.nombreInterfaz,
                    interfaz.nombreAmigableInterfaz,

                    leer =
                        relacion != null &&
                        relacion.leer == true,

                    agregar =
                        relacion != null &&
                        relacion.agregar == true,

                    actualizar =
                        relacion != null &&
                        relacion.actualizar == true,

                    eliminar =
                        relacion != null &&
                        relacion.eliminar == true
                })
                .ToListAsync(cancellationToken);

            return filas
                .GroupBy(item => new
                {
                    item.rolId,
                    item.nombreRol,
                    item.esAdministrador
                })
                .Select(grupo =>
                    new RolConPermisosDto
                    {
                        rol = new RolLiteDto
                        {
                            rolId =
                                grupo.Key.rolId,

                            nombreRol =
                                grupo.Key.nombreRol,

                            esAdministrador =
                                grupo.Key.esAdministrador
                        },

                        interfaz = grupo
                            .Select(item =>
                            {
                                bool administrador =
                                    grupo.Key
                                        .esAdministrador;

                                return new
                                    InterfazPermisoDto
                                {
                                    interfazId =
                                        item.interfazId,

                                    nombreInterfaz =
                                        item.nombreInterfaz,

                                    nombreAmigableInterfaz =
                                        string.IsNullOrWhiteSpace(
                                            item.nombreAmigableInterfaz)
                                            ? item.nombreInterfaz
                                            : item.nombreAmigableInterfaz,

                                    leer =
                                        administrador ||
                                        item.leer,

                                    agregar =
                                        administrador ||
                                        item.agregar,

                                    actualizar =
                                        administrador ||
                                        item.actualizar,

                                    eliminar =
                                        administrador ||
                                        item.eliminar
                                };
                            })
                            .OrderBy(item =>
                                item.nombreAmigableInterfaz)
                            .ToList()
                    })
                .OrderBy(item =>
                    item.rol.nombreRol)
                .ToList();
        }

        private static bool EsAdministrador(
            string? nombreRol) =>
            string.Equals(
                nombreRol?.Trim(),
                NOMBRE_ROL_ADMINISTRADOR,
                StringComparison.OrdinalIgnoreCase);
    }
}
