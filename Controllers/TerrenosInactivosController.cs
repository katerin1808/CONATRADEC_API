using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Listado paginado exclusivo de terrenos eliminados.
    ///
    /// El endpoint histórico de CatalogosEliminadosController permanece sin
    /// cambios para conservar compatibilidad con consumidores existentes.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/administracion/terrenos/inactivos")]
    public sealed class TerrenosInactivosController : ControllerBase
    {
        private const string InterfazTerrenos = "terrenoPage";

        private readonly DBContext db;
        private readonly PermisoApiService permisos;

        public TerrenosInactivosController(
            DBContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;
        }

        [HttpGet]
        public async Task<ActionResult> Listar(
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 20,
            [FromQuery] string? buscar = null,
            CancellationToken cancellationToken = default)
        {
            ResultadoPermisoApi permiso =
                await permisos.ValidarAsync(
                    ObtenerUsuarioId(),
                    InterfazTerrenos,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    new
                    {
                        success = false,
                        message = permiso.Mensaje
                    });
            }

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);
            string texto = (buscar ?? string.Empty).Trim();

            IQueryable<Terreno> consulta =
                db.Terreno
                    .AsNoTracking()
                    .Where(x => !x.activo);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                consulta = consulta.Where(x =>
                    x.codigoTerreno.Contains(texto) ||
                    x.direccionTerreno.Contains(texto) ||
                    x.Municipio.NombreMunicipio.Contains(texto) ||
                    x.Municipio.Departamento.NombreDepartamento.Contains(texto) ||
                    x.RelacionesPropietario.Any(relacion =>
                        relacion.Propietario.nombreCompleto.Contains(texto) ||
                        relacion.Propietario.identificacion.Contains(texto)));
            }

            int totalRegistros =
                await consulta.CountAsync(cancellationToken);

            int totalPaginas = totalRegistros == 0
                ? 0
                : (int)Math.Ceiling(
                    totalRegistros / (double)tamanoPagina);

            if (totalPaginas > 0 && pagina > totalPaginas)
                pagina = totalPaginas;

            List<TerrenoInactivoItemDto> items =
                totalRegistros == 0
                    ? new List<TerrenoInactivoItemDto>()
                    : await consulta
                        .OrderBy(x => x.codigoTerreno)
                        .ThenBy(x => x.terrenoId)
                        .Skip((pagina - 1) * tamanoPagina)
                        .Take(tamanoPagina)
                        .Select(x => new TerrenoInactivoItemDto
                        {
                            Id = x.terrenoId,
                            Catalogo = "terreno",
                            Titulo = x.codigoTerreno,
                            Subtitulo =
                                x.RelacionesPropietario
                                    .OrderByDescending(relacion =>
                                        relacion.fechaAsignacionUtc)
                                    .Select(relacion =>
                                        relacion.Propietario.nombreCompleto)
                                    .FirstOrDefault() ??
                                "Sin propietario",
                            Detalle =
                                x.Municipio.NombreMunicipio +
                                " · " +
                                x.Municipio.Departamento.NombreDepartamento,
                            Codigo =
                                x.RelacionesPropietario
                                    .OrderByDescending(relacion =>
                                        relacion.fechaAsignacionUtc)
                                    .Select(relacion =>
                                        relacion.Propietario.identificacion)
                                    .FirstOrDefault() ??
                                string.Empty,
                            Activo = x.activo
                        })
                        .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Terrenos eliminados cargados correctamente.",
                data = new
                {
                    items,
                    paginaActual = pagina,
                    tamanoPagina,
                    totalRegistros,
                    totalPaginas
                }
            });
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int usuarioId) && usuarioId > 0
                ? usuarioId
                : null;
        }

        private sealed class TerrenoInactivoItemDto
        {
            public int Id { get; init; }
            public string Catalogo { get; init; } = string.Empty;
            public string Titulo { get; init; } = string.Empty;
            public string Subtitulo { get; init; } = string.Empty;
            public string Detalle { get; init; } = string.Empty;
            public string Codigo { get; init; } = string.Empty;
            public bool Activo { get; init; }
        }
    }
}
