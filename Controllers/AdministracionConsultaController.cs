using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Consultas paginadas para las pantallas administrativas.
    /// No sustituye los controladores CRUD existentes.
    /// </summary>
    [ApiController]
    [Route("api/administracion")]
    public sealed class AdministracionConsultaController : ControllerBase
    {
        private readonly DBContext db;

        public AdministracionConsultaController(DBContext db)
        {
            this.db = db;
        }

        [HttpGet("usuarios/buscar")]
        public async Task<ActionResult<UsuarioAdministracionPaginaResponse>>
            BuscarUsuarios(
                [FromQuery] string? buscar = null,
                [FromQuery] int? rolId = null,
                [FromQuery] string? procedencia = null,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 20,
                CancellationToken cancellationToken = default)
        {
            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);

            IQueryable<Usuario> consulta =
                db.Usuarios
                    .AsNoTracking()
                    .Where(item => item.activo);

            string texto = NormalizarBusqueda(buscar);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                consulta = consulta.Where(item =>
                    item.nombreUsuario.Contains(texto) ||
                    item.nombreCompletoUsuario.Contains(texto) ||
                    item.correoUsuario.Contains(texto) ||
                    item.identificacionUsuario.Contains(texto) ||
                    (item.telefonoUsuario != null &&
                     item.telefonoUsuario.Contains(texto)));
            }

            if (rolId is > 0)
            {
                consulta = consulta.Where(item => item.rolId == rolId.Value);
            }

            string procedenciaNormalizada =
                (procedencia ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(procedenciaNormalizada))
            {
                consulta = consulta.Where(item =>
                    item.Procedencia.nombreProcedencia ==
                        procedenciaNormalizada);
            }

            int totalRegistros =
                await consulta.CountAsync(cancellationToken);

            List<UsuarioAdministracionDto> items =
                await consulta
                    .OrderBy(item => item.nombreCompletoUsuario)
                    .ThenBy(item => item.UsuarioId)
                    .Skip((pagina - 1) * tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(item => new UsuarioAdministracionDto
                    {
                        UsuarioId = item.UsuarioId,
                        NombreUsuario = item.nombreUsuario,
                        IdentificacionUsuario = item.identificacionUsuario,
                        NombreCompletoUsuario = item.nombreCompletoUsuario,
                        CorreoUsuario = item.correoUsuario,
                        TelefonoUsuario = item.telefonoUsuario ?? string.Empty,
                        FechaNacimientoUsuario = item.fechaNacimientoUsuario,
                        RolId = item.rolId,
                        ProcedenciaId = item.procedenciaId,
                        MunicipioId = item.municipioId,
                        RolNombre = item.Rol.nombreRol,
                        ProcedenciaNombre =
                            item.Procedencia.nombreProcedencia,
                        EsInterno =
                            item.Procedencia.nombreProcedencia == "Interno",
                        UrlImagenUsuario = item.urlImagenUsuario ?? string.Empty
                    })
                    .ToListAsync(cancellationToken);

            return Ok(new UsuarioAdministracionPaginaResponse
            {
                Items = items,
                PaginaActual = pagina,
                TamanoPagina = tamanoPagina,
                TotalRegistros = totalRegistros,
                TotalPaginas = CalcularPaginas(totalRegistros, tamanoPagina)
            });
        }

        [HttpGet("roles/buscar")]
        public async Task<ActionResult<RolAdministracionPaginaResponse>>
            BuscarRoles(
                [FromQuery] string? buscar = null,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 20,
                CancellationToken cancellationToken = default)
        {
            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);

            IQueryable<Rol> consulta =
                db.Roles
                    .AsNoTracking()
                    .Where(item => item.activo);

            string texto = NormalizarBusqueda(buscar);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                consulta = consulta.Where(item =>
                    item.nombreRol.Contains(texto) ||
                    item.descripcionRol.Contains(texto));
            }

            int totalRegistros =
                await consulta.CountAsync(cancellationToken);

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
                            db.Usuarios.Count(usuario =>
                                usuario.activo &&
                                usuario.rolId == item.rolId),
                        CantidadInterfaces =
                            db.RolInterfaz.Count(relacion =>
                                relacion.rolId == item.rolId &&
                                (relacion.leer == true ||
                                 relacion.agregar == true ||
                                 relacion.actualizar == true ||
                                 relacion.eliminar == true))
                    })
                    .ToListAsync(cancellationToken);

            return Ok(new RolAdministracionPaginaResponse
            {
                Items = items,
                PaginaActual = pagina,
                TamanoPagina = tamanoPagina,
                TotalRegistros = totalRegistros,
                TotalPaginas = CalcularPaginas(totalRegistros, tamanoPagina)
            });
        }

        [HttpGet("permisos/rol/{rolId:int}")]
        public async Task<ActionResult<RolConPermisosDto>>
            ObtenerPermisosPorRol(
                int rolId,
                CancellationToken cancellationToken = default)
        {
            if (rolId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El identificador del rol no es válido."
                });
            }

            RolLiteDto? rol =
                await db.Roles
                    .AsNoTracking()
                    .Where(item => item.activo && item.rolId == rolId)
                    .Select(item => new RolLiteDto
                    {
                        rolId = item.rolId,
                        nombreRol = item.nombreRol
                    })
                    .SingleOrDefaultAsync(cancellationToken);

            if (rol == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El rol no existe o está inactivo."
                });
            }

            List<InterfazPermisoDto> interfaces =
                await (
                    from interfaz in db.Interfaz.AsNoTracking()
                    where interfaz.activo
                    join relacion in db.RolInterfaz.AsNoTracking()
                        .Where(item => item.rolId == rolId)
                    on interfaz.interfazId equals relacion.interfazId
                    into relaciones
                    from relacion in relaciones.DefaultIfEmpty()
                    orderby
                        interfaz.nombreAmigableInterfaz,
                        interfaz.nombreInterfaz
                    select new InterfazPermisoDto
                    {
                        interfazId = interfaz.interfazId,
                        nombreInterfaz = interfaz.nombreInterfaz,
                        nombreAmigableInterfaz =
                            interfaz.nombreAmigableInterfaz,
                        leer = relacion != null && relacion.leer == true,
                        agregar = relacion != null && relacion.agregar == true,
                        actualizar =
                            relacion != null && relacion.actualizar == true,
                        eliminar =
                            relacion != null && relacion.eliminar == true
                    })
                    .ToListAsync(cancellationToken);

            return Ok(new RolConPermisosDto
            {
                rol = rol,
                interfaz = interfaces
            });
        }

        private static int CalcularPaginas(
            int total,
            int tamano) =>
            total == 0
                ? 1
                : (int)Math.Ceiling(total / (double)tamano);

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
