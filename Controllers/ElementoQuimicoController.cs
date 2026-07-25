using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/elemento-quimico")]
    public sealed class ElementoQuimicoController : ControllerBase
    {
        private readonly DBContext context;
        private readonly ILogger<ElementoQuimicoController> logger;

        private static readonly Expression<
            Func<ElementoQuimico, ElementoQuimicoRespuestaDto>>
            Proyeccion =
                elemento =>
                    new ElementoQuimicoRespuestaDto
                    {
                        elementoQuimicosId =
                            elemento.elementoQuimicosId,

                        simboloElementoQuimico =
                            elemento.simboloElementoQuimico,

                        nombreElementoQuimico =
                            elemento.nombreElementoQuimico,

                        pesoEquivalenteElementoQuimico =
                            elemento.pesoEquivalenteElementoQuimico,

                        activo =
                            elemento.activo
                    };

        public ElementoQuimicoController(
            DBContext context,
            ILogger<ElementoQuimicoController> logger)
        {
            this.context = context;
            this.logger = logger;
        }

        // ==========================================================
        // LISTADO COMPLETO PARA FORMULARIOS Y SELECTORES
        // ==========================================================
        [HttpGet("listar")]
        public async Task<ActionResult<IEnumerable<ElementoQuimicoRespuestaDto>>>
            Listar(CancellationToken cancellationToken)
        {
            List<ElementoQuimicoRespuestaDto> data =
                await context.elementoQuimico
                    .AsNoTracking()
                    .Where(elemento =>
                        elemento.activo)
                    .OrderBy(elemento =>
                        elemento.nombreElementoQuimico)
                    .Select(Proyeccion)
                    .ToListAsync(cancellationToken);

            return Ok(data);
        }

        // ==========================================================
        // BÚSQUEDA PAGINADA PARA LA PANTALLA ADMINISTRATIVA
        // ==========================================================
        [HttpGet("buscar")]
        public async Task<ActionResult<ElementoQuimicoPaginaResponse>>
            Buscar(
                [FromQuery] string? buscar = null,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 20,
                [FromQuery] string orden = "nombre",
                [FromQuery] string direccion = "asc",
                CancellationToken cancellationToken = default)
        {
            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(
                tamanoPagina,
                5,
                100);

            IQueryable<ElementoQuimico> query =
                context.elementoQuimico
                    .AsNoTracking()
                    .Where(elemento =>
                        elemento.activo);

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto =
                    buscar
                        .ReplaceLineEndings(" ")
                        .Trim();

                if (texto.Length > 100)
                    texto = texto[..100];

                query = query.Where(elemento =>
                    elemento.nombreElementoQuimico.Contains(texto) ||
                    elemento.simboloElementoQuimico.Contains(texto));
            }

            bool descendente =
                string.Equals(
                    direccion,
                    "desc",
                    StringComparison.OrdinalIgnoreCase);

            query = orden.Trim().ToLowerInvariant() switch
            {
                "simbolo" when descendente =>
                    query
                        .OrderByDescending(elemento =>
                            elemento.simboloElementoQuimico)
                        .ThenBy(elemento =>
                            elemento.nombreElementoQuimico),

                "simbolo" =>
                    query
                        .OrderBy(elemento =>
                            elemento.simboloElementoQuimico)
                        .ThenBy(elemento =>
                            elemento.nombreElementoQuimico),

                "peso" when descendente =>
                    query
                        .OrderByDescending(elemento =>
                            elemento.pesoEquivalenteElementoQuimico)
                        .ThenBy(elemento =>
                            elemento.nombreElementoQuimico),

                "peso" =>
                    query
                        .OrderBy(elemento =>
                            elemento.pesoEquivalenteElementoQuimico)
                        .ThenBy(elemento =>
                            elemento.nombreElementoQuimico),

                _ when descendente =>
                    query
                        .OrderByDescending(elemento =>
                            elemento.nombreElementoQuimico)
                        .ThenBy(elemento =>
                            elemento.simboloElementoQuimico),

                _ =>
                    query
                        .OrderBy(elemento =>
                            elemento.nombreElementoQuimico)
                        .ThenBy(elemento =>
                            elemento.simboloElementoQuimico)
            };

            int totalRegistros =
                await query.CountAsync(cancellationToken);

            List<ElementoQuimicoRespuestaDto> items =
                await query
                    .Skip(
                        (pagina - 1) *
                        tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(Proyeccion)
                    .ToListAsync(cancellationToken);

            int totalPaginas =
                totalRegistros == 0
                    ? 1
                    : (int)Math.Ceiling(
                        totalRegistros /
                        (double)tamanoPagina);

            return Ok(
                new ElementoQuimicoPaginaResponse
                {
                    Items = items,
                    PaginaActual = pagina,
                    TamanoPagina = tamanoPagina,
                    TotalRegistros = totalRegistros,
                    TotalPaginas = totalPaginas
                });
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ElementoQuimicoRespuestaDto>>
            ObtenerPorId(
                int id,
                CancellationToken cancellationToken)
        {
            ElementoQuimicoRespuestaDto? data =
                await context.elementoQuimico
                    .AsNoTracking()
                    .Where(elemento =>
                        elemento.elementoQuimicosId == id &&
                        elemento.activo)
                    .Select(Proyeccion)
                    .SingleOrDefaultAsync(cancellationToken);

            if (data == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El elemento químico no existe o está inactivo."
                });
            }

            return Ok(data);
        }

        // ==========================================================
        // CREAR
        // ==========================================================
        [HttpPost("crear")]
        public async Task<ActionResult> Crear(
            [FromBody] CrearElementoQuimicoDto? request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No se recibieron los datos del elemento químico."
                });
            }

            string simbolo =
                NormalizarSimbolo(
                    request.simboloElementoQuimico);

            string nombre =
                NormalizarNombre(
                    request.nombreElementoQuimico);

            decimal peso =
                request.pesoEquivalenteElementoQuimico;

            ActionResult? validacion =
                ValidarDatos(
                    simbolo,
                    nombre,
                    peso);

            if (validacion != null)
                return validacion;

            if (await ExisteSimboloAsync(
                    simbolo,
                    null,
                    cancellationToken))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        $"Ya existe un elemento químico activo con el símbolo {simbolo}."
                });
            }

            if (await ExisteNombreAsync(
                    nombre,
                    null,
                    cancellationToken))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        $"Ya existe un elemento químico activo con el nombre {nombre}."
                });
            }

            var entity =
                new ElementoQuimico
                {
                    simboloElementoQuimico =
                        simbolo,

                    nombreElementoQuimico =
                        nombre,

                    pesoEquivalenteElementoQuimico =
                        RedondearDosDecimales(peso),

                    activo =
                        true
                };

            try
            {
                context.elementoQuimico.Add(entity);

                await context.SaveChangesAsync(
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Elemento químico creado correctamente.",

                    data =
                        ProyectarRespuesta(entity)
                });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al crear el elemento químico {Simbolo}.",
                    simbolo);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible crear el elemento químico porque ya existe un registro con el mismo símbolo o nombre."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al crear un elemento químico.");

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al crear el elemento químico."
                });
            }
        }

        // ==========================================================
        // EDITAR
        // ==========================================================
        [HttpPut("editar/{id:int}")]
        public async Task<ActionResult> Editar(
            int id,
            [FromBody] EditarElementoQuimicoDto? request,
            CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El identificador del elemento químico no es válido."
                });
            }

            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No se recibieron los datos del elemento químico."
                });
            }

            if (request.elementoQuimicosId > 0 &&
                request.elementoQuimicosId != id)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El identificador de la ruta no coincide con el elemento enviado."
                });
            }

            string simbolo =
                NormalizarSimbolo(
                    request.simboloElementoQuimico);

            string nombre =
                NormalizarNombre(
                    request.nombreElementoQuimico);

            decimal peso =
                request.pesoEquivalenteElementoQuimico;

            ActionResult? validacion =
                ValidarDatos(
                    simbolo,
                    nombre,
                    peso);

            if (validacion != null)
                return validacion;

            ElementoQuimico? entity =
                await context.elementoQuimico
                    .FirstOrDefaultAsync(
                        elemento =>
                            elemento.elementoQuimicosId == id,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El elemento químico indicado no existe."
                });
            }

            if (!entity.activo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede actualizar un elemento químico que está inactivo."
                });
            }

            if (await ExisteSimboloAsync(
                    simbolo,
                    id,
                    cancellationToken))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        $"Ya existe otro elemento químico activo con el símbolo {simbolo}."
                });
            }

            if (await ExisteNombreAsync(
                    nombre,
                    id,
                    cancellationToken))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        $"Ya existe otro elemento químico activo con el nombre {nombre}."
                });
            }

            entity.simboloElementoQuimico =
                simbolo;

            entity.nombreElementoQuimico =
                nombre;

            entity.pesoEquivalenteElementoQuimico =
                RedondearDosDecimales(peso);

            try
            {
                await context.SaveChangesAsync(
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Elemento químico actualizado correctamente.",

                    data =
                        ProyectarRespuesta(entity)
                });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al actualizar el elemento químico {ElementoId}.",
                    id);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible actualizar el elemento químico porque ya existe un registro con el mismo símbolo o nombre."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al actualizar el elemento químico {ElementoId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al actualizar el elemento químico."
                });
            }
        }

        // ==========================================================
        // ELIMINACIÓN LÓGICA
        // ==========================================================
        [HttpDelete("eliminar/{id:int}")]
        public async Task<ActionResult> Eliminar(
            int id,
            CancellationToken cancellationToken)
        {
            ElementoQuimico? entity =
                await context.elementoQuimico
                    .FirstOrDefaultAsync(
                        elemento =>
                            elemento.elementoQuimicosId == id &&
                            elemento.activo,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El elemento químico no existe o ya está desactivado."
                });
            }

            List<string> dependencias =
                await ObtenerDependenciasAsync(
                    id,
                    cancellationToken);

            if (dependencias.Count > 0)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede eliminar el elemento químico porque está siendo utilizado.",

                    usadoEn =
                        dependencias
                });
            }

            entity.activo = false;

            try
            {
                await context.SaveChangesAsync(
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Elemento químico desactivado correctamente."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al desactivar el elemento químico {ElementoId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al eliminar el elemento químico."
                });
            }
        }

        private Task<bool> ExisteSimboloAsync(
            string simbolo,
            int? excluirId,
            CancellationToken cancellationToken) =>
            context.elementoQuimico.AnyAsync(
                elemento =>
                    elemento.activo &&
                    (!excluirId.HasValue ||
                     elemento.elementoQuimicosId != excluirId.Value) &&
                    EF.Functions.Collate(
                        elemento.simboloElementoQuimico,
                        "Modern_Spanish_CI_AI") ==
                    simbolo,
                cancellationToken);

        private Task<bool> ExisteNombreAsync(
            string nombre,
            int? excluirId,
            CancellationToken cancellationToken) =>
            context.elementoQuimico.AnyAsync(
                elemento =>
                    elemento.activo &&
                    (!excluirId.HasValue ||
                     elemento.elementoQuimicosId != excluirId.Value) &&
                    EF.Functions.Collate(
                        elemento.nombreElementoQuimico,
                        "Modern_Spanish_CI_AI") ==
                    nombre,
                cancellationToken);

        private async Task<List<string>> ObtenerDependenciasAsync(
            int id,
            CancellationToken cancellationToken)
        {
            var dependencias =
                new List<string>();

            if (await context
                    .fuenteNutrienteElementoQuimico
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id &&
                            item.activo,
                        cancellationToken))
            {
                dependencias.Add(
                    "fuentes de nutrientes");
            }

            if (await context
                    .ParametroExtraccionNutrienteCafe
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id &&
                            item.activo,
                        cancellationToken))
            {
                dependencias.Add(
                    "parámetros de extracción por quintal oro");
            }

            if (await context
                    .ParametroRangoNutrienteCultivo
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id &&
                            item.activo,
                        cancellationToken))
            {
                dependencias.Add(
                    "rangos nutricionales por cultivo");
            }

            if (await context
                    .ParametroFuenteOrganicaAporte
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id &&
                            item.activo,
                        cancellationToken))
            {
                dependencias.Add(
                    "parámetros de fuentes orgánicas");
            }

            /*
             * Los registros históricos se verifican sin filtrar por estado,
             * porque el elemento debe continuar disponible para consultarlos.
             */
            if (await context.AnalisisSueloElementos
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id,
                        cancellationToken))
            {
                dependencias.Add(
                    "análisis de suelo guardados");
            }

            if (await context.AnalisisSueloCalculoElementos
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id,
                        cancellationToken))
            {
                dependencias.Add(
                    "cálculos de análisis de suelo");
            }

            if (await context.formulaNutricionalDetalle
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id,
                        cancellationToken))
            {
                dependencias.Add(
                    "detalles de fórmulas nutricionales");
            }

            if (await context.formulaNutricionalAporte
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id,
                        cancellationToken))
            {
                dependencias.Add(
                    "aportes de fórmulas nutricionales");
            }

            if (await context.fertilizacionMixtaDetalle
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == id,
                        cancellationToken))
            {
                dependencias.Add(
                    "fertilizaciones mixtas");
            }

            return dependencias;
        }

        private ActionResult? ValidarDatos(
            string simbolo,
            string nombre,
            decimal peso)
        {
            if (string.IsNullOrWhiteSpace(simbolo))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El símbolo del elemento químico es obligatorio."
                });
            }

            if (simbolo.Length > 10)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El símbolo no puede superar 10 caracteres."
                });
            }

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del elemento químico es obligatorio."
                });
            }

            if (nombre.Length > 100)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre no puede superar 100 caracteres."
                });
            }

            if (peso <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El peso equivalente debe ser mayor que cero."
                });
            }

            if (peso > 99999999.99m)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El peso equivalente supera el valor permitido."
                });
            }

            if (RedondearDosDecimales(peso) != peso)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El peso equivalente solo puede contener dos decimales."
                });
            }

            return null;
        }

        private static ElementoQuimicoRespuestaDto ProyectarRespuesta(
            ElementoQuimico elemento) =>
            new()
            {
                elementoQuimicosId =
                    elemento.elementoQuimicosId,

                simboloElementoQuimico =
                    elemento.simboloElementoQuimico,

                nombreElementoQuimico =
                    elemento.nombreElementoQuimico,

                pesoEquivalenteElementoQuimico =
                    elemento.pesoEquivalenteElementoQuimico,

                activo =
                    elemento.activo
            };

        private static string NormalizarSimbolo(
            string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();

        private static string NormalizarNombre(
            string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();

        private static decimal RedondearDosDecimales(
            decimal valor) =>
            decimal.Round(
                valor,
                2,
                MidpointRounding.AwayFromZero);
    }
}
