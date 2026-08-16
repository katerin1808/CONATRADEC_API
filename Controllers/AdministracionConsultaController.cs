using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
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
        private readonly PermisoApiService permisoApiService;

        public AdministracionConsultaController(
            DBContext db,
            PermisoApiService permisoApiService)
        {
            this.db = db;
            this.permisoApiService = permisoApiService;
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

            /*
             * Orden natural ejecutado completamente en SQL Server antes de
             * Skip/Take. Si el nombre termina en un bloque numérico separado
             * por espacio, dicho bloque se compara como número. De esta forma:
             *
             * Usuario 2, Usuario 3, Usuario 10, Usuario 11...
             *
             * Los nombres que no terminan en número conservan el orden
             * alfabético normal. Nunca se materializa el listado completo.
             */
            var conPosicionNumerica =
                consulta.Select(item => new
                {
                    Usuario = item,
                    PosicionNumero = EF.Functions.PatIndex(
                        "% [0-9]%",
                        item.nombreCompletoUsuario)
                });

            var conSufijo =
                conPosicionNumerica.Select(item => new
                {
                    item.Usuario,
                    Prefijo =
                        item.PosicionNumero > 0
                            ? item.Usuario.nombreCompletoUsuario
                                .Substring(
                                    0,
                                    Convert.ToInt32(item.PosicionNumero) - 1)
                                .Trim()
                            : item.Usuario.nombreCompletoUsuario,
                    Sufijo =
                        item.PosicionNumero > 0
                            ? item.Usuario.nombreCompletoUsuario
                                .Substring(
                                    Convert.ToInt32(item.PosicionNumero))
                                .Trim()
                            : string.Empty
                });

            var consultaOrdenable =
                conSufijo.Select(item => new
                {
                    item.Usuario,
                    item.Prefijo,
                    item.Sufijo,
                    EsSufijoNumerico =
                        item.Sufijo.Length > 0 &&
                        item.Sufijo.Length <= 18 &&
                        EF.Functions.PatIndex(
                            "%[^0-9]%",
                            item.Sufijo) == 0
                });

            List<UsuarioAdministracionDto> items =
                await consultaOrdenable
                    .OrderBy(item =>
                        item.EsSufijoNumerico
                            ? item.Prefijo
                            : item.Usuario.nombreCompletoUsuario)
                    .ThenBy(item =>
                        item.EsSufijoNumerico ? 0 : 1)
                    .ThenBy(item =>
                        item.EsSufijoNumerico
                            ? Convert.ToInt64(item.Sufijo)
                            : 0L)
                    .ThenBy(item =>
                        item.Usuario.nombreCompletoUsuario)
                    .ThenBy(item => item.Usuario.UsuarioId)
                    .Skip((pagina - 1) * tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(item => new UsuarioAdministracionDto
                    {
                        UsuarioId = item.Usuario.UsuarioId,
                        NombreUsuario = item.Usuario.nombreUsuario,
                        IdentificacionUsuario =
                            item.Usuario.identificacionUsuario,
                        NombreCompletoUsuario =
                            item.Usuario.nombreCompletoUsuario,
                        CorreoUsuario = item.Usuario.correoUsuario,
                        TelefonoUsuario =
                            item.Usuario.telefonoUsuario ?? string.Empty,
                        FechaNacimientoUsuario =
                            item.Usuario.fechaNacimientoUsuario,
                        RolId = item.Usuario.rolId,
                        ProcedenciaId = item.Usuario.procedenciaId,
                        MunicipioId = item.Usuario.municipioId,
                        DepartamentoId =
                            item.Usuario.Municipio != null
                                ? item.Usuario.Municipio.DepartamentoId
                                : null,
                        PaisId =
                            item.Usuario.Municipio != null
                                ? item.Usuario.Municipio.Departamento.PaisId
                                : null,
                        RolNombre = item.Usuario.Rol.nombreRol,
                        ProcedenciaNombre =
                            item.Usuario.Procedencia.nombreProcedencia,
                        MunicipioNombre =
                            item.Usuario.Municipio != null
                                ? item.Usuario.Municipio.NombreMunicipio
                                : string.Empty,
                        DepartamentoNombre =
                            item.Usuario.Municipio != null
                                ? item.Usuario.Municipio.Departamento
                                    .NombreDepartamento
                                : string.Empty,
                        PaisNombre =
                            item.Usuario.Municipio != null
                                ? item.Usuario.Municipio.Departamento.Pais
                                    .NombrePais
                                : string.Empty,
                        EsInterno =
                            item.Usuario.Procedencia.nombreProcedencia ==
                                "Interno",
                        UrlImagenUsuario =
                            item.Usuario.urlImagenUsuario ?? string.Empty
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
                [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
                [FromQuery] string? buscar = null,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 20,
                CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarLecturaRolesAsync(
                    usuarioSesionId,
                    cancellationToken);

            if (acceso != null)
                return acceso;

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


        /// <summary>
        /// Este listado también alimenta la Matriz de permisos. La lectura se
        /// autoriza por Roles o por Matriz, sin convertir ninguno de los dos
        /// permisos en acceso de escritura sobre el otro módulo.
        /// </summary>
        private async Task<ActionResult?> ValidarLecturaRolesAsync(
            int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi roles =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    "rolPage",
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (roles.Permitido)
                return null;

            ResultadoPermisoApi matriz =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    "matrizPermisosPage",
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (matriz.Permitido)
                return null;

            ResultadoPermisoApi denegado =
                roles.CodigoEstado == StatusCodes.Status401Unauthorized
                    ? roles
                    : matriz;

            return StatusCode(
                denegado.CodigoEstado,
                new
                {
                    success = false,
                    message = denegado.Mensaje
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
