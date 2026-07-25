using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Consultas optimizadas para la pantalla administrativa.
    ///
    /// Este controlador es deliberadamente independiente del controlador
    /// CRUD existente. No modifica la lógica de creación, edición,
    /// clasificación, PRNT, fertilización mixta ni eliminación.
    /// </summary>
    [ApiController]
    [Route("api/fuente-nutriente")]
    public sealed class FuenteNutrienteConsultaController : ControllerBase
    {
        private const string CategoriaTodas =
            "TODAS";

        private const string CategoriaBalance =
            "BALANCE_NUTRICIONAL";

        private const string CategoriaEnmienda =
            "ENMIENDA_CALCAREA";

        private const string CategoriaMixta =
            "FERTILIZACION_MIXTA";

        private readonly DBContext db;

        public FuenteNutrienteConsultaController(
            DBContext db)
        {
            this.db =
                db;
        }

        // ==========================================================
        // LISTADO ADMINISTRATIVO PAGINADO
        // ==========================================================
        [HttpGet("buscar")]
        public async Task<ActionResult<FuenteNutrientePaginaResponse>>
            Buscar(
                [FromQuery] string? buscar = null,
                [FromQuery] string? categoria = null,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 20,
                CancellationToken cancellationToken = default)
        {
            pagina =
                Math.Max(
                    1,
                    pagina);

            tamanoPagina =
                Math.Clamp(
                    tamanoPagina,
                    5,
                    100);

            IQueryable<FuenteNutriente> consulta =
                CrearConsultaBase(
                    buscar,
                    categoria);

            int totalRegistros =
                await consulta.CountAsync(
                    cancellationToken);

            /*
             * Primero se pagina solamente por ID. Luego se proyectan
             * las relaciones de esa página. Esto evita cargar la matriz
             * completa de todas las fuentes en cada solicitud.
             */
            List<int> idsPagina =
                await consulta
                    .OrderBy(item =>
                        item.nombreNutriente)
                    .ThenBy(item =>
                        item.fuenteNutrientesId)
                    .Skip(
                        (pagina - 1) *
                        tamanoPagina)
                    .Take(
                        tamanoPagina)
                    .Select(item =>
                        item.fuenteNutrientesId)
                    .ToListAsync(
                        cancellationToken);

            List<FuenteNutrienteConElementosRespuestaDto> items =
                idsPagina.Count == 0
                    ? new List<
                        FuenteNutrienteConElementosRespuestaDto>()
                    : await ProyectarFuentes(
                            db.fuenteNutriente
                                .AsNoTracking()
                                .Where(item =>
                                    idsPagina.Contains(
                                        item.fuenteNutrientesId)))
                        .ToListAsync(
                            cancellationToken);

            Dictionary<int, int> ordenIds =
                idsPagina
                    .Select(
                        (id, indice) =>
                            new
                            {
                                id,
                                indice
                            })
                    .ToDictionary(
                        item =>
                            item.id,
                        item =>
                            item.indice);

            items =
                items
                    .OrderBy(item =>
                        ordenIds.TryGetValue(
                            item.fuenteNutrientesId,
                            out int indice)
                                ? indice
                                : int.MaxValue)
                    .ToList();

            int totalPaginas =
                totalRegistros == 0
                    ? 1
                    : (int)Math.Ceiling(
                        totalRegistros /
                        (double)tamanoPagina);

            return Ok(
                new FuenteNutrientePaginaResponse
                {
                    Items =
                        items,

                    PaginaActual =
                        pagina,

                    TamanoPagina =
                        tamanoPagina,

                    TotalRegistros =
                        totalRegistros,

                    TotalPaginas =
                        totalPaginas
                });
        }

        // ==========================================================
        // MATRIZ DINÁMICA CARGADA ÚNICAMENTE CUANDO SE SOLICITA
        // ==========================================================
        [HttpGet("composicion")]
        public async Task<
            ActionResult<
                IEnumerable<
                    FuenteNutrienteConElementosRespuestaDto>>>
            ObtenerComposicion(
                [FromQuery] string? buscar = null,
                [FromQuery] string? categoria = null,
                CancellationToken cancellationToken = default)
        {
            string codigoCategoria =
                NormalizarCategoria(
                    categoria);

            /*
             * Las enmiendas calcáreas no utilizan composición química
             * en esta interfaz. Se retorna una colección vacía.
             */
            if (codigoCategoria ==
                CategoriaEnmienda)
            {
                return Ok(
                    Array.Empty<
                        FuenteNutrienteConElementosRespuestaDto>());
            }

            IQueryable<FuenteNutriente> consulta =
                CrearConsultaBase(
                    buscar,
                    codigoCategoria)
                .Where(item =>
                    item.fuenteNutrienteElementoQuimico
                        .Any(relacion =>
                            relacion.activo &&
                            relacion.cantidadAporte > 0));

            List<FuenteNutrienteConElementosRespuestaDto> data =
                await ProyectarFuentes(
                        consulta)
                    .OrderBy(item =>
                        item.nombreNutriente)
                    .ToListAsync(
                        cancellationToken);

            return Ok(data);
        }

        private IQueryable<FuenteNutriente> CrearConsultaBase(
            string? buscar,
            string? categoria)
        {
            IQueryable<FuenteNutriente> consulta =
                db.fuenteNutriente
                    .AsNoTracking()
                    .Where(item =>
                        item.activo);

            string texto =
                NormalizarBusqueda(
                    buscar);

            if (!string.IsNullOrWhiteSpace(
                    texto))
            {
                consulta =
                    consulta.Where(item =>
                        item.nombreNutriente.Contains(
                            texto) ||
                        item.descripcionNutriente.Contains(
                            texto));
            }

            string codigoCategoria =
                NormalizarCategoria(
                    categoria);

            consulta =
                codigoCategoria switch
                {
                    CategoriaEnmienda =>
                        consulta.Where(item =>
                            db.ParametroEnmiendaCalcarea
                                .Any(configuracion =>
                                    configuracion.fuenteNutrientesId ==
                                        item.fuenteNutrientesId &&
                                    configuracion.activo)),

                    CategoriaMixta =>
                        consulta.Where(item =>
                            db.fuenteFertilizacionMixta
                                .Any(configuracion =>
                                    configuracion.fuenteNutrientesId ==
                                        item.fuenteNutrientesId &&
                                    configuracion.activo)),

                    CategoriaBalance =>
                        consulta.Where(item =>
                            !db.ParametroEnmiendaCalcarea
                                .Any(configuracion =>
                                    configuracion.fuenteNutrientesId ==
                                        item.fuenteNutrientesId &&
                                    configuracion.activo) &&
                            !db.fuenteFertilizacionMixta
                                .Any(configuracion =>
                                    configuracion.fuenteNutrientesId ==
                                        item.fuenteNutrientesId &&
                                    configuracion.activo)),

                    _ =>
                        consulta
                };

            return consulta;
        }

        private IQueryable<
            FuenteNutrienteConElementosRespuestaDto>
            ProyectarFuentes(
                IQueryable<FuenteNutriente> consulta)
        {
            return consulta.Select(item =>
                new FuenteNutrienteConElementosRespuestaDto
                {
                    fuenteNutrientesId =
                        item.fuenteNutrientesId,

                    nombreNutriente =
                        item.nombreNutriente,

                    descripcionNutriente =
                        item.descripcionNutriente,

                    precioNutriente =
                        item.precioNutriente,

                    activo =
                        item.activo,

                    habilitadaEnmiendaCalcarea =
                        db.ParametroEnmiendaCalcarea
                            .Any(configuracion =>
                                configuracion.fuenteNutrientesId ==
                                    item.fuenteNutrientesId &&
                                configuracion.activo),

                    habilitadaFertilizacionMixta =
                        db.fuenteFertilizacionMixta
                            .Any(configuracion =>
                                configuracion.fuenteNutrientesId ==
                                    item.fuenteNutrientesId &&
                                configuracion.activo),

                    prnt =
                        db.ParametroEnmiendaCalcarea
                            .Where(configuracion =>
                                configuracion.fuenteNutrientesId ==
                                    item.fuenteNutrientesId &&
                                configuracion.activo)
                            .Select(configuracion =>
                                (decimal?)configuracion.prnt)
                            .FirstOrDefault(),

                    descripcionParametro =
                        db.ParametroEnmiendaCalcarea
                            .Where(configuracion =>
                                configuracion.fuenteNutrientesId ==
                                    item.fuenteNutrientesId &&
                                configuracion.activo)
                            .Select(configuracion =>
                                configuracion.descripcionParametro)
                            .FirstOrDefault(),

                    parametrosEnmiendaCalcarea =
                        db.ParametroEnmiendaCalcarea
                            .Where(configuracion =>
                                configuracion.fuenteNutrientesId ==
                                    item.fuenteNutrientesId &&
                                configuracion.activo)
                            .Select(configuracion =>
                                new ParametroEnmiendaCalcareaFuenteDto
                                {
                                    prnt =
                                        configuracion.prnt,

                                    descripcionParametro =
                                        configuracion
                                            .descripcionParametro
                                })
                            .ToList(),

                    elementosQuimicos =
                        item.fuenteNutrienteElementoQuimico
                            .Where(relacion =>
                                relacion.activo)
                            .OrderBy(relacion =>
                                relacion.elementoQuimico != null
                                    ? relacion.elementoQuimico
                                        .nombreElementoQuimico
                                    : string.Empty)
                            .Select(relacion =>
                                new ElementoFuenteRespuestaDto
                                {
                                    fuenteNutrienteElementoQuimicoId =
                                        relacion
                                            .fuenteNutrienteElementoQuimicoId,

                                    elementoQuimicosId =
                                        relacion.elementoQuimicosId,

                                    nombreElementoQuimico =
                                        relacion.elementoQuimico != null
                                            ? relacion.elementoQuimico
                                                .nombreElementoQuimico
                                            : string.Empty,

                                    simboloElementoQuimico =
                                        relacion.elementoQuimico != null
                                            ? relacion.elementoQuimico
                                                .simboloElementoQuimico
                                            : string.Empty,

                                    cantidadAporte =
                                        relacion.cantidadAporte
                                })
                            .ToList()
                });
        }

        private static string NormalizarBusqueda(
            string? valor)
        {
            string texto =
                (valor ?? string.Empty)
                    .ReplaceLineEndings(" ")
                    .Trim();

            return texto.Length <= 100
                ? texto
                : texto[..100];
        }

        private static string NormalizarCategoria(
            string? valor)
        {
            string codigo =
                (valor ?? CategoriaTodas)
                    .Trim()
                    .ToUpperInvariant();

            return codigo is
                CategoriaBalance or
                CategoriaEnmienda or
                CategoriaMixta
                    ? codigo
                    : CategoriaTodas;
        }
    }
}
