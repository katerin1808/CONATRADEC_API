using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Consultas paginadas para la pantalla administrativa.
    /// El CRUD y la lógica de cálculo existentes no se modifican.
    /// </summary>
    [ApiController]
    [Route("api/configuracion/extraccion-nutrientes")]
    public sealed class ParametroExtraccionNutrienteConsultaController : ControllerBase
    {
        private readonly DBContext db;

        public ParametroExtraccionNutrienteConsultaController(DBContext db)
        {
            this.db = db;
        }

        [HttpGet("buscar")]
        public async Task<ActionResult<ParametroExtraccionNutrientePaginaResponse>> Buscar(
            [FromQuery] string? buscar = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 20,
            CancellationToken cancellationToken = default)
        {
            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 5, 100);

            IQueryable<ParametroExtraccionNutrienteCafe> query =
                db.ParametroExtraccionNutrienteCafe
                    .AsNoTracking()
                    .Where(x => x.activo);

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto = buscar.ReplaceLineEndings(" ").Trim();

                if (texto.Length > 150)
                    texto = texto[..150];

                query = query.Where(x =>
                    x.ElementoQuimico.nombreElementoQuimico.Contains(texto) ||
                    x.ElementoQuimico.simboloElementoQuimico.Contains(texto) ||
                    x.descripcionParametro.Contains(texto));
            }

            query = query
                .OrderBy(x => x.ElementoQuimico.nombreElementoQuimico)
                .ThenBy(x => x.parametroExtraccionNutrienteCafeId);

            int totalRegistros = await query.CountAsync(cancellationToken);

            List<ParametroExtraccionNutrienteConsultaDto> items =
                await query
                    .Skip((pagina - 1) * tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(x => new ParametroExtraccionNutrienteConsultaDto
                    {
                        parametroExtraccionNutrienteCafeId =
                            x.parametroExtraccionNutrienteCafeId,
                        elementoQuimicosId = x.elementoQuimicosId,
                        nombreElementoQuimico =
                            x.ElementoQuimico.nombreElementoQuimico,
                        simboloElementoQuimico =
                            x.ElementoQuimico.simboloElementoQuimico,
                        cantidadExtraidaPorQQOro =
                            x.cantidadExtraidaPorQQOro,
                        descripcionParametro =
                            x.descripcionParametro,
                        activo = x.activo
                    })
                    .ToListAsync(cancellationToken);

            int totalPaginas = totalRegistros == 0
                ? 1
                : (int)Math.Ceiling(totalRegistros / (double)tamanoPagina);

            return Ok(new ParametroExtraccionNutrientePaginaResponse
            {
                Items = items,
                PaginaActual = pagina,
                TamanoPagina = tamanoPagina,
                TotalRegistros = totalRegistros,
                TotalPaginas = totalPaginas
            });
        }
    }
}
