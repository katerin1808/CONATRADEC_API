using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
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

        private const string INTERFAZ_ROLES =
            "rolPage";

        private readonly DBContext context;
        private readonly PermisoApiService permisoApiService;
        private readonly ILogger<RolController> logger;

        public RolController(
            DBContext context,
            PermisoApiService permisoApiService,
            ILogger<RolController> logger)
        {
            this.context = context;
            this.permisoApiService = permisoApiService;
            this.logger = logger;
        }

        // ==========================================================
        // ENDPOINTS HISTÓRICOS
        // ==========================================================
        // Se conservan para no romper versiones instaladas o consumidores
        // existentes. La aplicación actual utiliza los endpoints administrativos
        // definidos más abajo, que aplican permisos y paginación de servidor.

        [HttpPost("crearRol")]
        public async Task<IActionResult> CrearRol(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromBody] RolCreateDto? dto,
            CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

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
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarLecturaCatalogoRolesAsync(
                    usuarioSesionId,
                    cancellationToken);

            if (acceso != null)
                return acceso;

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
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

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
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

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
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarLecturaCatalogoRolesAsync(
                    usuarioSesionId,
                    cancellationToken);

            if (acceso != null)
                return acceso;

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
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromBody] RolUpdateDto? dto,
            CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

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
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Eliminar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

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

        // ==========================================================
        // ENDPOINTS ADMINISTRATIVOS ACTUALES
        // ==========================================================
        // Estos endpoints son utilizados por Android/Windows para aplicar las
        // reglas actuales: autorización backend, paginación real y DTO completo
        // después de Crear/Editar/Reactivar.

        [HttpGet("administracion/paginado")]
        public async Task<ActionResult<RolAdministracionPaginaResponse>>
            ListarAdministracionPaginado(
                [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
                [FromQuery] string? buscar = null,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 20,
                CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            return await ConstruirPaginaAsync(
                activos: true,
                buscar,
                pagina,
                tamanoPagina,
                cancellationToken);
        }

        [HttpGet("administracion/inactivos/paginado")]
        public async Task<ActionResult<RolAdministracionPaginaResponse>>
            ListarInactivosAdministracionPaginado(
                [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
                [FromQuery] string? buscar = null,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 20,
                CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            return await ConstruirPaginaAsync(
                activos: false,
                buscar,
                pagina,
                tamanoPagina,
                cancellationToken);
        }

        [HttpPost("administracion/crear")]
        public async Task<IActionResult> CrearRolAdministracion(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromQuery] bool crearNuevoSiExisteInactivo = false,
            [FromBody] RolCreateDto? dto = null,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (dto == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "No se recibieron los datos del rol."
                });
            }

            string nombre =
                NormalizarNombre(dto.nombreRol);

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

            Rol? activo =
                await context.Roles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        item =>
                            item.activo &&
                            EF.Functions.Collate(
                                item.nombreRol,
                                "Modern_Spanish_CI_AI") == nombre,
                        cancellationToken);

            if (activo != null)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe un rol activo con ese nombre."
                });
            }

            Rol? inactivo =
                await context.Roles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        item =>
                            !item.activo &&
                            EF.Functions.Collate(
                                item.nombreRol,
                                "Modern_Spanish_CI_AI") == nombre,
                        cancellationToken);

            if (inactivo != null &&
                !crearNuevoSiExisteInactivo)
            {
                RolAdministracionDto? dtoInactivo =
                    await ConstruirDtoAsync(
                        inactivo.rolId,
                        cancellationToken);

                return Conflict(new
                {
                    success = false,
                    code = "ROL_INACTIVO_EXISTENTE",
                    message =
                        "Ya existe un rol eliminado con ese nombre.",
                    data = dtoInactivo
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

                RolAdministracionDto? creado =
                    await ConstruirDtoAsync(
                        nuevoRol.rolId,
                        cancellationToken);

                return StatusCode(
                    StatusCodes.Status201Created,
                    new
                    {
                        success = true,
                        message = "Rol creado correctamente.",
                        data = creado
                    });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al crear el rol administrativo {NombreRol}.",
                    nombre);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible crear el rol porque existe un registro con el mismo nombre."
                });
            }
        }

        [HttpPut("administracion/{id:int}")]
        public async Task<IActionResult> EditarRolAdministracion(
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromBody] RolUpdateDto? dto,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0 || dto == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "No se recibieron datos válidos del rol."
                });
            }

            Rol? rol =
                await context.Roles
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

            string nombre =
                NormalizarNombre(dto.nombreRol);

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
                    message =
                        "Ya existe otro rol activo con ese nombre."
                });
            }

            rol.nombreRol = nombre;
            rol.descripcionRol = descripcion;

            try
            {
                await context.SaveChangesAsync(cancellationToken);

                RolAdministracionDto? actualizado =
                    await ConstruirDtoAsync(
                        rol.rolId,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Rol actualizado correctamente.",
                    data = actualizado
                });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al actualizar el rol administrativo {RolId}.",
                    id);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible actualizar el rol porque existe otro registro con el mismo nombre."
                });
            }
        }

        [HttpDelete("administracion/{id:int}")]
        public async Task<IActionResult> EliminarRolAdministracion(
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Eliminar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El identificador del rol no es válido."
                });
            }

            Rol? rol =
                await context.Roles
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

        [HttpPut("administracion/{id:int}/reactivar")]
        public async Task<IActionResult> ReactivarRolAdministracion(
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            [FromBody] RolUpdateDto? dto,
            CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0 || dto == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "No se recibieron datos válidos del rol."
                });
            }

            Rol? rol =
                await context.Roles
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

            string nombre =
                NormalizarNombre(dto.nombreRol);

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
                    message =
                        "No se puede reactivar porque ya existe otro rol activo con el mismo nombre."
                });
            }

            rol.nombreRol = nombre;
            rol.descripcionRol = descripcion;
            rol.activo = true;

            try
            {
                await context.SaveChangesAsync(cancellationToken);

                RolAdministracionDto? reactivado =
                    await ConstruirDtoAsync(
                        rol.rolId,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Rol reactivado correctamente.",
                    data = reactivado
                });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al reactivar el rol administrativo {RolId}.",
                    id);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible reactivar el rol porque existe otro registro activo con el mismo nombre."
                });
            }
        }

        /// <summary>
        /// El catálogo histórico de roles también alimenta el selector del
        /// formulario de Usuarios. Se permite esa lectura cuando el usuario
        /// puede administrar Usuarios, sin otorgarle acceso administrativo a
        /// la pantalla de Roles ni a sus operaciones de escritura.
        /// </summary>
        private async Task<ActionResult?> ValidarLecturaCatalogoRolesAsync(
            int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi rol =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    INTERFAZ_ROLES,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (rol.Permitido)
                return null;

            ResultadoPermisoApi usuarioLectura =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    "userPage",
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (usuarioLectura.Permitido)
                return null;

            ResultadoPermisoApi usuarioGestion =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    "userPage",
                    TipoPermisoApi.AgregarOActualizar,
                    cancellationToken);

            if (usuarioGestion.Permitido)
                return null;

            ResultadoPermisoApi denegado =
                rol.CodigoEstado == StatusCodes.Status401Unauthorized
                    ? rol
                    : usuarioGestion;

            return StatusCode(
                denegado.CodigoEstado,
                new
                {
                    success = false,
                    message = denegado.Mensaje
                });
        }

        private async Task<ActionResult?> ValidarAccesoAsync(
            int? usuarioSesionId,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    INTERFAZ_ROLES,
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

        private async Task<RolAdministracionPaginaResponse>
            ConstruirPaginaAsync(
                bool activos,
                string? buscar,
                int pagina,
                int tamanoPagina,
                CancellationToken cancellationToken)
        {
            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);

            IQueryable<Rol> consulta =
                context.Roles
                    .AsNoTracking()
                    .Where(item => item.activo == activos);

            string texto =
                NormalizarBusqueda(buscar);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                consulta = consulta.Where(item =>
                    item.nombreRol.Contains(texto) ||
                    item.descripcionRol.Contains(texto));
            }

            int totalRegistros =
                await consulta.CountAsync(cancellationToken);

            int totalPaginas =
                totalRegistros == 0
                    ? 1
                    : (int)Math.Ceiling(
                        totalRegistros /
                        (double)tamanoPagina);

            if (pagina > totalPaginas)
                pagina = totalPaginas;

            List<RolAdministracionDto> items =
                await consulta
                    .OrderBy(item => item.nombreRol)
                    .ThenBy(item => item.rolId)
                    .Skip((pagina - 1) * tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(item => new RolAdministracionDto
                    {
                        RolId = item.rolId,
                        NombreRol = item.nombreRol,
                        DescripcionRol = item.descripcionRol,
                        CantidadUsuarios =
                            context.Usuarios.Count(usuario =>
                                usuario.activo &&
                                usuario.rolId == item.rolId),
                        CantidadInterfaces =
                            context.RolInterfaz.Count(relacion =>
                                relacion.rolId == item.rolId &&
                                (relacion.leer == true ||
                                 relacion.agregar == true ||
                                 relacion.actualizar == true ||
                                 relacion.eliminar == true))
                    })
                    .ToListAsync(cancellationToken);

            return new RolAdministracionPaginaResponse
            {
                Items = items,
                PaginaActual = pagina,
                TamanoPagina = tamanoPagina,
                TotalRegistros = totalRegistros,
                TotalPaginas = totalPaginas
            };
        }

        private async Task<RolAdministracionDto?>
            ConstruirDtoAsync(
                int rolId,
                CancellationToken cancellationToken)
        {
            return await context.Roles
                .AsNoTracking()
                .Where(item => item.rolId == rolId)
                .Select(item => new RolAdministracionDto
                {
                    RolId = item.rolId,
                    NombreRol = item.nombreRol,
                    DescripcionRol = item.descripcionRol,
                    CantidadUsuarios =
                        context.Usuarios.Count(usuario =>
                            usuario.activo &&
                            usuario.rolId == item.rolId),
                    CantidadInterfaces =
                        context.RolInterfaz.Count(relacion =>
                            relacion.rolId == item.rolId &&
                            (relacion.leer == true ||
                             relacion.agregar == true ||
                             relacion.actualizar == true ||
                             relacion.eliminar == true))
                })
                .SingleOrDefaultAsync(cancellationToken);
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

        private static string NormalizarBusqueda(string? valor)
        {
            string texto =
                (valor ?? string.Empty)
                    .ReplaceLineEndings(" ")
                    .Trim();

            return texto.Length <= 150
                ? texto
                : texto[..150];
        }
    }
}
