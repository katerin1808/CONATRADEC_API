using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Listado paginado de seguimientos para la administración web.
    ///
    /// Los endpoints históricos de seguimiento se conservan para evitar
    /// afectar otros consumidores de la API.
    /// </summary>
    [ApiController]
    [Route("api/seguimiento-alertas-agricolas")]
    public sealed class SeguimientosPaginadosController : ControllerBase
    {
        private static readonly string[] EstadosValidos =
        [
            "PENDIENTE",
            "EN_PROCESO",
            "ATENDIDA",
            "DESCARTADA"
        ];

        private readonly AlertasAgricolasDbContext alertasDb;
        private readonly DBContext db;

        public SeguimientosPaginadosController(
            AlertasAgricolasDbContext alertasDb,
            DBContext db)
        {
            this.alertasDb = alertasDb;
            this.db = db;
        }

        [HttpGet("paginado")]
        public async Task<ActionResult<SeguimientosPaginadosDto>>
            ListarPaginado(
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 12,
                [FromQuery] string? buscar = null,
                [FromQuery] string? estado = null,
                [FromQuery] int? responsableId = null,
                [FromQuery] int? terrenoId = null,
                CancellationToken cancellationToken = default)
        {
            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 6, 100);

            string estadoNormalizado =
                (estado ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant();

            if (!string.IsNullOrWhiteSpace(estadoNormalizado) &&
                !EstadosValidos.Contains(estadoNormalizado))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El estado indicado no es válido."
                });
            }

            IQueryable<SeguimientoAlertaAgricola> consulta =
                alertasDb.Seguimientos
                    .AsNoTracking()
                    .Where(item => item.Activo);

            if (terrenoId is > 0)
            {
                consulta = consulta.Where(item =>
                    item.TerrenoId == terrenoId.Value);
            }

            if (!string.IsNullOrWhiteSpace(estadoNormalizado))
            {
                consulta = consulta.Where(item =>
                    item.Estado == estadoNormalizado);
            }

            if (responsableId.HasValue)
            {
                consulta = responsableId.Value == 0
                    ? consulta.Where(item =>
                        !item.UsuarioAsignadoId.HasValue)
                    : consulta.Where(item =>
                        item.UsuarioAsignadoId ==
                        responsableId.Value);
            }

            string texto =
                (buscar ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(texto))
            {
                int[] terrenosCoincidentes =
                    await BuscarTerrenosAsync(
                        texto,
                        cancellationToken);

                int[] usuariosCoincidentes =
                    await BuscarUsuariosAsync(
                        texto,
                        cancellationToken);

                consulta = consulta.Where(item =>
                    item.TipoAlerta.Contains(texto) ||
                    item.Observacion.Contains(texto) ||
                    terrenosCoincidentes.Contains(item.TerrenoId) ||
                    (item.UsuarioAsignadoId.HasValue &&
                     usuariosCoincidentes.Contains(
                         item.UsuarioAsignadoId.Value)));
            }

            Dictionary<string, int> conteos =
                await consulta
                    .GroupBy(item => item.Estado)
                    .Select(grupo => new
                    {
                        Estado = grupo.Key,
                        Total = grupo.Count()
                    })
                    .ToDictionaryAsync(
                        item => item.Estado,
                        item => item.Total,
                        cancellationToken);

            int totalRegistros =
                conteos.Values.Sum();

            int totalPaginas = totalRegistros == 0
                ? 0
                : (int)Math.Ceiling(
                    totalRegistros / (double)tamanoPagina);

            if (totalPaginas > 0 && pagina > totalPaginas)
                pagina = totalPaginas;

            List<SeguimientoAlertaAgricola> entidades =
                totalRegistros == 0
                    ? new()
                    : await consulta
                        .OrderBy(item =>
                            item.Estado == "ATENDIDA" ||
                            item.Estado == "DESCARTADA")
                        .ThenByDescending(item =>
                            item.FechaUltimaModificacionUtc)
                        .ThenByDescending(item =>
                            item.SeguimientoAlertaAgricolaId)
                        .Skip((pagina - 1) * tamanoPagina)
                        .Take(tamanoPagina)
                        .ToListAsync(cancellationToken);

            List<SeguimientoAlertaResponse> items =
                await MapearPaginaAsync(
                    entidades,
                    cancellationToken);

            return Ok(
                new SeguimientosPaginadosDto
                {
                    Items = items,
                    Pagina = pagina,
                    TamanoPagina = tamanoPagina,
                    TotalRegistros = totalRegistros,
                    TotalPaginas = totalPaginas,
                    Resumen = new ResumenSeguimientoAlertasDto
                    {
                        Total = totalRegistros,
                        Pendientes = ObtenerConteo(
                            conteos,
                            "PENDIENTE"),
                        EnProceso = ObtenerConteo(
                            conteos,
                            "EN_PROCESO"),
                        Atendidas = ObtenerConteo(
                            conteos,
                            "ATENDIDA"),
                        Descartadas = ObtenerConteo(
                            conteos,
                            "DESCARTADA")
                    }
                });
        }

        private async Task<int[]> BuscarTerrenosAsync(
            string texto,
            CancellationToken cancellationToken)
        {
            return await db.Terreno
                .AsNoTracking()
                .Where(item =>
                    item.activo &&
                    (
                        item.codigoTerreno.Contains(texto) ||
                        item.direccionTerreno.Contains(texto) ||
                        item.Municipio.NombreMunicipio.Contains(texto) ||
                        item.Municipio.Departamento
                            .NombreDepartamento.Contains(texto) ||
                        item.RelacionesPropietario.Any(relacion =>
                            relacion.activo &&
                            relacion.Propietario.activo &&
                            relacion.Propietario
                                .nombreCompleto.Contains(texto))
                    ))
                .Select(item => item.terrenoId)
                .ToArrayAsync(cancellationToken);
        }

        private async Task<int[]> BuscarUsuariosAsync(
            string texto,
            CancellationToken cancellationToken)
        {
            return await db.Usuarios
                .AsNoTracking()
                .Where(item =>
                    item.activo &&
                    (
                        item.nombreCompletoUsuario.Contains(texto) ||
                        item.nombreUsuario.Contains(texto)
                    ))
                .Select(item => item.UsuarioId)
                .ToArrayAsync(cancellationToken);
        }

        private async Task<List<SeguimientoAlertaResponse>>
            MapearPaginaAsync(
                IReadOnlyCollection<SeguimientoAlertaAgricola> entidades,
                CancellationToken cancellationToken)
        {
            if (entidades.Count == 0)
                return new();

            int[] usuarioIds = entidades
                .Where(item => item.UsuarioAsignadoId.HasValue)
                .Select(item => item.UsuarioAsignadoId!.Value)
                .Distinct()
                .ToArray();

            Dictionary<int, string> usuarios =
                usuarioIds.Length == 0
                    ? new()
                    : await db.Usuarios
                        .AsNoTracking()
                        .Where(item =>
                            usuarioIds.Contains(item.UsuarioId))
                        .ToDictionaryAsync(
                            item => item.UsuarioId,
                            item => item.nombreCompletoUsuario,
                            cancellationToken);

            int[] terrenoIds = entidades
                .Select(item => item.TerrenoId)
                .Distinct()
                .ToArray();

            Dictionary<int, TerrenoInfo> terrenos =
                await db.Terreno
                    .AsNoTracking()
                    .Where(item =>
                        terrenoIds.Contains(item.terrenoId))
                    .Select(item =>
                        new TerrenoInfo
                        {
                            TerrenoId = item.terrenoId,
                            Codigo = item.codigoTerreno,
                            Propietario =
                                item.RelacionesPropietario
                                    .Where(relacion =>
                                        relacion.activo &&
                                        relacion.Propietario.activo)
                                    .Select(relacion =>
                                        relacion.Propietario.nombreCompleto)
                                    .FirstOrDefault() ??
                                string.Empty,
                            Direccion = item.direccionTerreno,
                            Municipio =
                                item.Municipio.NombreMunicipio,
                            Departamento =
                                item.Municipio.Departamento
                                    .NombreDepartamento
                        })
                    .ToDictionaryAsync(
                        item => item.TerrenoId,
                        cancellationToken);

            return entidades
                .Select(entidad =>
                {
                    terrenos.TryGetValue(
                        entidad.TerrenoId,
                        out TerrenoInfo? terreno);

                    string? usuarioAsignado = null;

                    if (entidad.UsuarioAsignadoId.HasValue)
                    {
                        usuarios.TryGetValue(
                            entidad.UsuarioAsignadoId.Value,
                            out usuarioAsignado);
                    }

                    return new SeguimientoAlertaResponse
                    {
                        seguimientoAlertaAgricolaId =
                            entidad.SeguimientoAlertaAgricolaId,
                        terrenoId = entidad.TerrenoId,
                        codigoTerreno =
                            terreno?.Codigo ??
                            $"Terreno #{entidad.TerrenoId}",
                        propietario =
                            terreno?.Propietario ?? string.Empty,
                        direccion =
                            terreno?.Direccion ?? string.Empty,
                        municipio =
                            terreno?.Municipio ?? string.Empty,
                        departamento =
                            terreno?.Departamento ?? string.Empty,
                        tipoAlerta = entidad.TipoAlerta,
                        nivel = entidad.Nivel,
                        estado = entidad.Estado,
                        usuarioAsignadoId =
                            entidad.UsuarioAsignadoId,
                        usuarioAsignado = usuarioAsignado,
                        observacion = entidad.Observacion,
                        fechaCreacionUtc =
                            entidad.FechaCreacionUtc,
                        fechaUltimaModificacionUtc =
                            entidad.FechaUltimaModificacionUtc,
                        fechaCierreUtc = entidad.FechaCierreUtc
                    };
                })
                .ToList();
        }

        private static int ObtenerConteo(
            IReadOnlyDictionary<string, int> conteos,
            string estado) =>
            conteos.TryGetValue(estado, out int total)
                ? total
                : 0;

        private sealed class TerrenoInfo
        {
            public int TerrenoId { get; set; }
            public string Codigo { get; set; } = string.Empty;
            public string Propietario { get; set; } = string.Empty;
            public string Direccion { get; set; } = string.Empty;
            public string Municipio { get; set; } = string.Empty;
            public string Departamento { get; set; } = string.Empty;
        }
    }
}
