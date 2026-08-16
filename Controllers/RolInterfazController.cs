using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/rol-permisos")]
    public sealed class RolPermisosController : ControllerBase
    {
        private readonly DBContext db;
        private readonly PermisoApiService permisoApiService;

        public RolPermisosController(
            DBContext db,
            PermisoApiService permisoApiService)
        {
            this.db = db;
            this.permisoApiService = permisoApiService;
        }

        [HttpGet("/api/rol-interfaz/matriz-por-rol")]
        public async Task<ActionResult<IEnumerable<RolConPermisosDto>>>
            ListarMatrizPorRol(
                [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
                CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarMatrizAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

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
                [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
                CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarMatrizAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

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
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarMatrizAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

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

            /*
             * Un cliente histórico podría enviar el mismo interfazId más de
             * una vez. Se conserva el último valor recibido para que el
             * guardado sea determinista y cada interfaz se procese una sola vez.
             */
            List<InterfazPermisoDto> permisosSolicitados =
                dto.interfaz
                    .Where(item => item.interfazId > 0)
                    .GroupBy(item => item.interfazId)
                    .Select(grupo => grupo.Last())
                    .ToList();

            if (permisosSolicitados.Count == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No se recibieron interfaces válidas para actualizar."
                });
            }

            List<int> interfazIds = permisosSolicitados
                .Select(item => item.interfazId)
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

            /*
             * No se usa ToDictionary(interfazId, fila) porque algunas bases
             * históricas contienen relaciones duplicadas. Todas las filas del
             * mismo par rol/interfaz se sincronizan con el mismo valor.
             */
            Dictionary<int, List<RolInterfaz>> mapa =
                existentes
                    .GroupBy(item => item.interfazId)
                    .ToDictionary(
                        grupo => grupo.Key,
                        grupo => grupo.ToList());

            await using var transaccion =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                foreach (InterfazPermisoDto permiso
                         in permisosSolicitados)
                {
                    if (!interfacesValidas.Contains(
                            permiso.interfazId))
                    {
                        continue;
                    }

                    if (mapa.TryGetValue(
                            permiso.interfazId,
                            out List<RolInterfaz>? relaciones))
                    {
                        foreach (RolInterfaz relacion in relaciones)
                        {
                            AplicarPermisos(
                                relacion,
                                permiso.leer,
                                permiso.agregar,
                                permiso.actualizar,
                                permiso.eliminar);
                        }
                    }
                    else
                    {
                        var nueva = new RolInterfaz
                        {
                            rolId = rol.rolId,
                            interfazId = permiso.interfazId
                        };

                        AplicarPermisos(
                            nueva,
                            permiso.leer,
                            permiso.agregar,
                            permiso.actualizar,
                            permiso.eliminar);

                        db.RolInterfaz.Add(nueva);
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
                [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
                CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarMatrizAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

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
                        "Los permisos del rol Administrador están protegidos y no pueden modificarse."
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

            List<RolInterfaz> existentes =
                await db.RolInterfaz
                    .Where(item =>
                        item.rolId == rol.rolId &&
                        item.interfazId == interfaz.interfazId)
                    .ToListAsync(cancellationToken);

            if (existentes.Count == 0)
            {
                var nueva = new RolInterfaz
                {
                    rolId = rol.rolId,
                    interfazId = interfaz.interfazId
                };

                AplicarPermisos(
                    nueva,
                    request.leer,
                    request.agregar,
                    request.actualizar,
                    request.eliminar);

                db.RolInterfaz.Add(nueva);
            }
            else
            {
                foreach (RolInterfaz existente in existentes)
                {
                    AplicarPermisos(
                        existente,
                        request.leer,
                        request.agregar,
                        request.actualizar,
                        request.eliminar);
                }
            }

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
                            "ADMINISTRADOR" ||
                        rol.nombreRol.Trim().ToUpper() ==
                            "ADMIN",

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
                            rolId = grupo.Key.rolId,
                            nombreRol = grupo.Key.nombreRol,
                            esAdministrador =
                                grupo.Key.esAdministrador
                        },

                        interfaz = grupo
                            .GroupBy(item => new
                            {
                                item.interfazId,
                                item.nombreInterfaz,
                                item.nombreAmigableInterfaz
                            })
                            .Select(interfazGrupo =>
                                new InterfazPermisoDto
                                {
                                    interfazId =
                                        interfazGrupo.Key.interfazId,

                                    nombreInterfaz =
                                        interfazGrupo.Key.nombreInterfaz,

                                    nombreAmigableInterfaz =
                                        string.IsNullOrWhiteSpace(
                                            interfazGrupo.Key
                                                .nombreAmigableInterfaz)
                                            ? interfazGrupo.Key
                                                .nombreInterfaz
                                            : interfazGrupo.Key
                                                .nombreAmigableInterfaz,

                                    leer =
                                        interfazGrupo.Any(item =>
                                            item.leer),

                                    agregar =
                                        interfazGrupo.Any(item =>
                                            item.agregar),

                                    actualizar =
                                        interfazGrupo.Any(item =>
                                            item.actualizar),

                                    eliminar =
                                        interfazGrupo.Any(item =>
                                            item.eliminar)
                                })
                            .OrderBy(item =>
                                item.nombreAmigableInterfaz)
                            .ThenBy(item => item.nombreInterfaz)
                            .ToList()
                    })
                .OrderBy(item => item.rol.nombreRol)
                .ToList();
        }

        private async Task<ActionResult?> ValidarMatrizAsync(
            int? usuarioSesionId,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    "matrizPermisosPage",
                    permiso,
                    cancellationToken);

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                new
                {
                    success = false,
                    message = resultado.Mensaje
                });
        }

        private static void AplicarPermisos(
            RolInterfaz relacion,
            bool leer,
            bool agregar,
            bool actualizar,
            bool eliminar)
        {
            relacion.leer = leer;
            relacion.agregar = agregar;
            relacion.actualizar = actualizar;
            relacion.eliminar = eliminar;
        }

        private static bool EsAdministrador(string? nombreRol)
        {
            string nombre =
                (nombreRol ?? string.Empty).Trim();

            return string.Equals(
                       nombre,
                       "ADMINISTRADOR",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       nombre,
                       "ADMIN",
                       StringComparison.OrdinalIgnoreCase);
        }
    }
}
