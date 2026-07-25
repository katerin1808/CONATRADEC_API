using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Consultas optimizadas para la administración de rangos.
    /// No reemplaza el controlador CRUD existente.
    /// </summary>
    [ApiController]
    [Route("api/configuracion/rangos-nutrientes")]
    public sealed class RangoNutrienteConsultaController : ControllerBase
    {
        private const string UnidadApi = "lb/Mz";
        private const decimal FactorKgHaALbMz = 1.54m;

        private readonly DBContext db;

        public RangoNutrienteConsultaController(DBContext db)
        {
            this.db = db;
        }

        [HttpGet("cultivos")]
        public async Task<ActionResult<RangoNutrienteCultivoPaginaResponse>>
            BuscarCultivos(
                [FromQuery] string? buscar = null,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 20,
                CancellationToken cancellationToken = default)
        {
            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);

            IQueryable<TipoCultivo> consulta =
                db.TipoCultivos
                    .AsNoTracking()
                    .Where(item => item.activo);

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = NormalizarBusqueda(buscar);

                consulta = consulta.Where(item =>
                    item.nombreTipoCultivo.Contains(texto) ||
                    item.descripcionTipoCultivo.Contains(texto));
            }

            consulta = consulta.OrderBy(item => item.nombreTipoCultivo);

            int totalRegistros =
                await consulta.CountAsync(cancellationToken);

            List<RangoNutrienteCultivoResumenDto> items =
                await consulta
                    .Skip((pagina - 1) * tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(item => new RangoNutrienteCultivoResumenDto
                    {
                        tipoCultivoId = item.tipoCultivoId,
                        nombreCategoria = item.nombreTipoCultivo,
                        descripcionCategoria =
                            item.descripcionTipoCultivo,
                        cantidadAportes =
                            db.ParametroRangoNutrienteCultivo.Count(rango =>
                                rango.tipoCultivoId == item.tipoCultivoId &&
                                rango.activo)
                    })
                    .ToListAsync(cancellationToken);

            return Ok(new RangoNutrienteCultivoPaginaResponse
            {
                Items = items,
                PaginaActual = pagina,
                TamanoPagina = tamanoPagina,
                TotalRegistros = totalRegistros,
                TotalPaginas =
                    CalcularTotalPaginas(totalRegistros, tamanoPagina)
            });
        }

        [HttpGet("buscar")]
        public async Task<ActionResult<RangoNutrientePaginaResponse>>
            BuscarRangos(
                [FromQuery] int tipoCultivoId,
                [FromQuery] string? buscar = null,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 20,
                CancellationToken cancellationToken = default)
        {
            if (tipoCultivoId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El tipo de cultivo indicado no es válido."
                });
            }

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);

            IQueryable<ParametroRangoNutrienteCultivo> consulta =
                db.ParametroRangoNutrienteCultivo
                    .AsNoTracking()
                    .Where(item =>
                        item.activo &&
                        item.tipoCultivoId == tipoCultivoId);

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = NormalizarBusqueda(buscar);

                consulta = consulta.Where(item =>
                    item.ElementoQuimico.nombreElementoQuimico
                        .Contains(texto) ||
                    item.ElementoQuimico.simboloElementoQuimico
                        .Contains(texto) ||
                    item.descripcionParametro.Contains(texto));
            }

            consulta = consulta
                .OrderBy(item =>
                    item.ElementoQuimico.nombreElementoQuimico)
                .ThenBy(item =>
                    item.parametroRangoNutrienteCultivoId);

            int totalRegistros =
                await consulta.CountAsync(cancellationToken);

            List<ParametroRangoNutrienteCultivo> entidades =
                await consulta
                    .Include(item => item.TipoCultivo)
                    .Include(item => item.ElementoQuimico)
                    .Skip((pagina - 1) * tamanoPagina)
                    .Take(tamanoPagina)
                    .ToListAsync(cancellationToken);

            List<RangoNutrienteConsultaDto> items =
                entidades.Select(MapearRespuesta).ToList();

            return Ok(new RangoNutrientePaginaResponse
            {
                Items = items,
                PaginaActual = pagina,
                TamanoPagina = tamanoPagina,
                TotalRegistros = totalRegistros,
                TotalPaginas =
                    CalcularTotalPaginas(totalRegistros, tamanoPagina)
            });
        }

        [HttpGet("elementos-disponibles")]
        public async Task<ActionResult<IEnumerable<ElementoQuimicoDisponibleDto>>>
            ObtenerElementosDisponibles(
                [FromQuery] int tipoCultivoId,
                [FromQuery] int parametroActualId = 0,
                CancellationToken cancellationToken = default)
        {
            if (tipoCultivoId <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "El tipo de cultivo indicado no es válido."
                });
            }

            bool cultivoExiste =
                await db.TipoCultivos
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.tipoCultivoId == tipoCultivoId &&
                            item.activo,
                        cancellationToken);

            if (!cultivoExiste)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El tipo de cultivo no existe o está inactivo."
                });
            }

            List<ElementoQuimicoDisponibleDto> items =
                await db.elementoQuimico
                    .AsNoTracking()
                    .Where(elemento =>
                        elemento.activo &&
                        !db.ParametroRangoNutrienteCultivo.Any(rango =>
                            rango.activo &&
                            rango.tipoCultivoId == tipoCultivoId &&
                            rango.elementoQuimicosId ==
                                elemento.elementoQuimicosId &&
                            rango.parametroRangoNutrienteCultivoId !=
                                parametroActualId))
                    .OrderBy(elemento =>
                        elemento.nombreElementoQuimico)
                    .Select(elemento =>
                        new ElementoQuimicoDisponibleDto
                        {
                            elementoQuimicosId =
                                elemento.elementoQuimicosId,
                            nombreElementoQuimico =
                                elemento.nombreElementoQuimico,
                            simboloElementoQuimico =
                                elemento.simboloElementoQuimico
                        })
                    .ToListAsync(cancellationToken);

            return Ok(items);
        }

        private static RangoNutrienteConsultaDto MapearRespuesta(
            ParametroRangoNutrienteCultivo item)
        {
            return new RangoNutrienteConsultaDto
            {
                parametroRangoNutrienteCultivoId =
                    item.parametroRangoNutrienteCultivoId,
                tipoCultivoId = item.tipoCultivoId,
                nombreTipoCultivo =
                    item.TipoCultivo.nombreTipoCultivo,
                elementoQuimicosId = item.elementoQuimicosId,
                nombreElementoQuimico =
                    item.ElementoQuimico.nombreElementoQuimico,
                simboloElementoQuimico =
                    item.ElementoQuimico.simboloElementoQuimico,
                valorMinimo = Math.Round(
                    ConvertirAlmacenadoALbMz(
                        item.valorMinimo,
                        item.unidadBase),
                    2),
                valorMaximo = Math.Round(
                    ConvertirAlmacenadoALbMz(
                        item.valorMaximo,
                        item.unidadBase),
                    2),
                unidadBase = UnidadApi,
                descripcionParametro = item.descripcionParametro,
                activo = item.activo
            };
        }

        private static decimal ConvertirAlmacenadoALbMz(
            decimal valor,
            string? unidad)
        {
            string normalizada =
                (unidad ?? string.Empty)
                    .Trim()
                    .Replace(" ", string.Empty)
                    .ToUpperInvariant();

            return normalizada == "LB/MZ"
                ? valor
                : valor * FactorKgHaALbMz;
        }

        private static int CalcularTotalPaginas(
            int totalRegistros,
            int tamanoPagina) =>
            totalRegistros == 0
                ? 1
                : (int)Math.Ceiling(
                    totalRegistros / (double)tamanoPagina);

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
