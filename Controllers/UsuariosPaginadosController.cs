using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Listado paginado para la administración web.
    ///
    /// El endpoint histórico GET api/usuarios/listar se conserva sin cambios
    /// para no afectar las versiones instaladas de Android y Windows.
    /// </summary>
    [ApiController]
    [Route("api/usuarios")]
    public sealed class UsuariosPaginadosController : ControllerBase
    {
        private const string ProcedenciaInterna = "Interno";
        private readonly DBContext db;

        public UsuariosPaginadosController(DBContext db)
        {
            this.db = db;
        }

        [HttpGet("paginado")]
        public async Task<ActionResult<ResultadoPaginadoDto<UsuarioReadDto>>>
            ListarPaginado(
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 12,
                [FromQuery] string? buscar = null,
                CancellationToken cancellationToken = default)
        {
            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 6, 100);
            string texto = (buscar ?? string.Empty).Trim();

            var consulta =
                from usuario in db.Usuarios.AsNoTracking()
                join rolConsulta in db.Roles.AsNoTracking()
                    on usuario.rolId equals rolConsulta.rolId
                    into roles
                from rol in roles.DefaultIfEmpty()
                join procedenciaConsulta in db.Procedencia.AsNoTracking()
                    on usuario.procedenciaId equals
                        procedenciaConsulta.procedenciaId
                    into procedencias
                from procedencia in procedencias.DefaultIfEmpty()
                where usuario.activo
                select new
                {
                    Usuario = usuario,
                    RolNombre = rol != null
                        ? rol.nombreRol
                        : string.Empty,
                    ProcedenciaNombre = procedencia != null
                        ? procedencia.nombreProcedencia
                        : string.Empty
                };

            if (!string.IsNullOrWhiteSpace(texto))
            {
                consulta = consulta.Where(item =>
                    item.Usuario.nombreCompletoUsuario.Contains(texto) ||
                    item.Usuario.nombreUsuario.Contains(texto) ||
                    item.Usuario.correoUsuario.Contains(texto) ||
                    item.Usuario.identificacionUsuario.Contains(texto) ||
                    item.RolNombre.Contains(texto) ||
                    item.ProcedenciaNombre.Contains(texto));
            }

            int totalRegistros =
                await consulta.CountAsync(cancellationToken);

            int totalPaginas = totalRegistros == 0
                ? 0
                : (int)Math.Ceiling(
                    totalRegistros / (double)tamanoPagina);

            if (totalPaginas > 0 && pagina > totalPaginas)
                pagina = totalPaginas;

            List<UsuarioReadDto> items =
                await consulta
                    .OrderBy(item =>
                        item.Usuario.nombreCompletoUsuario)
                    .ThenBy(item =>
                        item.Usuario.UsuarioId)
                    .Skip((pagina - 1) * tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(item =>
                        new UsuarioReadDto
                        {
                            UsuarioId =
                                item.Usuario.UsuarioId,

                            nombreUsuario =
                                item.Usuario.nombreUsuario,

                            nombreCompletoUsuario =
                                item.Usuario.nombreCompletoUsuario,

                            correoUsuario =
                                item.Usuario.correoUsuario,

                            telefonoUsuario =
                                item.Usuario.telefonoUsuario,

                            fechaNacimientoUsuario =
                                item.Usuario.fechaNacimientoUsuario,

                            identificacionUsuario =
                                item.Usuario.identificacionUsuario,

                            rolId =
                                item.Usuario.rolId,

                            procedenciaId =
                                item.Usuario.procedenciaId,

                            municipioId =
                                item.Usuario.municipioId,

                            rolNombre =
                                item.RolNombre,

                            procedenciaNombre =
                                item.ProcedenciaNombre,

                            esInterno =
                                item.ProcedenciaNombre ==
                                ProcedenciaInterna,

                            urlImagenUsuario =
                                item.Usuario.urlImagenUsuario ??
                                string.Empty
                        })
                    .ToListAsync(cancellationToken);

            return Ok(
                new ResultadoPaginadoDto<UsuarioReadDto>
                {
                    Items = items,
                    Pagina = pagina,
                    TamanoPagina = tamanoPagina,
                    TotalRegistros = totalRegistros,
                    TotalPaginas = totalPaginas
                });
        }
    }
}
