using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/configuracion/tipos-cultivo")]
    public sealed class TipoCultivoController : ControllerBase
    {
        private readonly DBContext db;
        private readonly ILogger<TipoCultivoController> logger;

        public TipoCultivoController(
            DBContext db,
            ILogger<TipoCultivoController> logger)
        {
            this.db = db;
            this.logger = logger;
        }

        // ==========================================================
        // LISTADO COMPLETO PARA FORMULARIOS Y SELECTORES
        // ==========================================================
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TipoCultivoRespuestaDto>>>
            Listar(CancellationToken cancellationToken)
        {
            List<TipoCultivoRespuestaDto> data =
                await db.TipoCultivos
                    .AsNoTracking()
                    .Where(item =>
                        item.activo)
                    .OrderBy(item =>
                        item.nombreTipoCultivo)
                    .Select(item =>
                        new TipoCultivoRespuestaDto
                        {
                            tipoCultivoId =
                                item.tipoCultivoId,

                            nombreTipoCultivo =
                                item.nombreTipoCultivo,

                            tipoCultivo =
                                item.nombreTipoCultivo,

                            descripcionTipoCultivo =
                                item.descripcionTipoCultivo,

                            activo =
                                item.activo
                        })
                    .ToListAsync(cancellationToken);

            return Ok(data);
        }

        // ==========================================================
        // BÚSQUEDA PAGINADA PARA LA PANTALLA ADMINISTRATIVA
        // ==========================================================
        [HttpGet("buscar")]
        public async Task<ActionResult<TipoCultivoPaginaResponse>>
            Buscar(
                [FromQuery] string? buscar = null,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 20,
                [FromQuery] string orden = "nombre",
                [FromQuery] string direccion = "asc",
                CancellationToken cancellationToken = default)
        {
            pagina = Math.Max(
                1,
                pagina);

            tamanoPagina = Math.Clamp(
                tamanoPagina,
                5,
                100);

            IQueryable<TipoCultivo> query =
                db.TipoCultivos
                    .AsNoTracking()
                    .Where(item =>
                        item.activo);

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto =
                    buscar
                        .ReplaceLineEndings(" ")
                        .Trim();

                if (texto.Length > 150)
                    texto = texto[..150];

                query = query.Where(item =>
                    item.nombreTipoCultivo.Contains(texto) ||
                    item.descripcionTipoCultivo.Contains(texto));
            }

            bool descendente =
                string.Equals(
                    direccion,
                    "desc",
                    StringComparison.OrdinalIgnoreCase);

            query = orden.Trim().ToLowerInvariant() switch
            {
                "rangos" when descendente =>
                    query
                        .OrderByDescending(item =>
                            db.ParametroRangoNutrienteCultivo
                                .Count(rango =>
                                    rango.tipoCultivoId ==
                                        item.tipoCultivoId &&
                                    rango.activo))
                        .ThenBy(item =>
                            item.nombreTipoCultivo),

                "rangos" =>
                    query
                        .OrderBy(item =>
                            db.ParametroRangoNutrienteCultivo
                                .Count(rango =>
                                    rango.tipoCultivoId ==
                                        item.tipoCultivoId &&
                                    rango.activo))
                        .ThenBy(item =>
                            item.nombreTipoCultivo),

                "analisis" when descendente =>
                    query
                        .OrderByDescending(item =>
                            db.AnalisisSueloCalculos
                                .Count(analisis =>
                                    analisis.tipoCultivoId ==
                                        item.tipoCultivoId))
                        .ThenBy(item =>
                            item.nombreTipoCultivo),

                "analisis" =>
                    query
                        .OrderBy(item =>
                            db.AnalisisSueloCalculos
                                .Count(analisis =>
                                    analisis.tipoCultivoId ==
                                        item.tipoCultivoId))
                        .ThenBy(item =>
                            item.nombreTipoCultivo),

                _ when descendente =>
                    query
                        .OrderByDescending(item =>
                            item.nombreTipoCultivo),

                _ =>
                    query
                        .OrderBy(item =>
                            item.nombreTipoCultivo)
            };

            int totalRegistros =
                await query.CountAsync(cancellationToken);

            List<TipoCultivoRespuestaDto> items =
                await query
                    .Skip(
                        (pagina - 1) *
                        tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(item =>
                        new TipoCultivoRespuestaDto
                        {
                            tipoCultivoId =
                                item.tipoCultivoId,

                            nombreTipoCultivo =
                                item.nombreTipoCultivo,

                            tipoCultivo =
                                item.nombreTipoCultivo,

                            descripcionTipoCultivo =
                                item.descripcionTipoCultivo,

                            activo =
                                item.activo,

                            cantidadRangosActivos =
                                db.ParametroRangoNutrienteCultivo
                                    .Count(rango =>
                                        rango.tipoCultivoId ==
                                            item.tipoCultivoId &&
                                        rango.activo),

                            cantidadAnalisis =
                                db.AnalisisSueloCalculos
                                    .Count(analisis =>
                                        analisis.tipoCultivoId ==
                                            item.tipoCultivoId)
                        })
                    .ToListAsync(cancellationToken);

            int totalPaginas =
                totalRegistros == 0
                    ? 1
                    : (int)Math.Ceiling(
                        totalRegistros /
                        (double)tamanoPagina);

            return Ok(
                new TipoCultivoPaginaResponse
                {
                    Items = items,
                    PaginaActual = pagina,
                    TamanoPagina = tamanoPagina,
                    TotalRegistros = totalRegistros,
                    TotalPaginas = totalPaginas
                });
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<TipoCultivoRespuestaDto>>
            Obtener(
                int id,
                CancellationToken cancellationToken)
        {
            TipoCultivoRespuestaDto? data =
                await db.TipoCultivos
                    .AsNoTracking()
                    .Where(item =>
                        item.tipoCultivoId == id &&
                        item.activo)
                    .Select(item =>
                        new TipoCultivoRespuestaDto
                        {
                            tipoCultivoId =
                                item.tipoCultivoId,

                            nombreTipoCultivo =
                                item.nombreTipoCultivo,

                            tipoCultivo =
                                item.nombreTipoCultivo,

                            descripcionTipoCultivo =
                                item.descripcionTipoCultivo,

                            activo =
                                item.activo,

                            cantidadRangosActivos =
                                db.ParametroRangoNutrienteCultivo
                                    .Count(rango =>
                                        rango.tipoCultivoId ==
                                            item.tipoCultivoId &&
                                        rango.activo),

                            cantidadAnalisis =
                                db.AnalisisSueloCalculos
                                    .Count(analisis =>
                                        analisis.tipoCultivoId ==
                                            item.tipoCultivoId)
                        })
                    .SingleOrDefaultAsync(cancellationToken);

            if (data == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El tipo de cultivo no existe o está inactivo."
                });
            }

            return Ok(data);
        }

        // ==========================================================
        // CREAR O REACTIVAR
        // ==========================================================
        [HttpPost]
        public async Task<ActionResult> Crear(
            [FromBody] CrearTipoCultivoDto? request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No se recibieron los datos del tipo de cultivo."
                });
            }

            string nombre =
                NormalizarNombre(
                    request.nombreTipoCultivo);

            string descripcion =
                NormalizarDescripcion(
                    request.descripcionTipoCultivo);

            ActionResult? validacion =
                ValidarDatos(
                    nombre,
                    descripcion);

            if (validacion != null)
                return validacion;

            TipoCultivo? existente =
                await db.TipoCultivos
                    .FirstOrDefaultAsync(
                        item =>
                            EF.Functions.Collate(
                                item.nombreTipoCultivo,
                                "Modern_Spanish_CI_AI") ==
                            nombre,
                        cancellationToken);

            if (existente != null &&
                existente.activo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe un tipo de cultivo activo con ese nombre."
                });
            }

            try
            {
                if (existente != null)
                {
                    existente.nombreTipoCultivo =
                        nombre;

                    existente.descripcionTipoCultivo =
                        descripcion;

                    existente.activo =
                        true;

                    await db.SaveChangesAsync(
                        cancellationToken);

                    return Ok(new
                    {
                        success = true,
                        message =
                            "Tipo de cultivo reactivado correctamente.",

                        data =
                            CrearRespuesta(existente)
                    });
                }

                var entity =
                    new TipoCultivo
                    {
                        nombreTipoCultivo =
                            nombre,

                        descripcionTipoCultivo =
                            descripcion,

                        activo =
                            true
                    };

                db.TipoCultivos.Add(entity);

                await db.SaveChangesAsync(
                    cancellationToken);

                return StatusCode(
                    StatusCodes.Status201Created,
                    new
                    {
                        success = true,
                        message =
                            "Tipo de cultivo creado correctamente.",

                        data =
                            CrearRespuesta(entity)
                    });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al crear el tipo de cultivo {Nombre}.",
                    nombre);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible crear el tipo de cultivo porque ya existe un registro activo con el mismo nombre."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al crear un tipo de cultivo.");

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al crear el tipo de cultivo."
                });
            }
        }

        // ==========================================================
        // ACTUALIZAR
        // ==========================================================
        [HttpPut("{id:int}")]
        public async Task<ActionResult> Actualizar(
            int id,
            [FromBody] ActualizarTipoCultivoDto? request,
            CancellationToken cancellationToken)
        {
            if (id <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El identificador del tipo de cultivo no es válido."
                });
            }

            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No se recibieron los datos del tipo de cultivo."
                });
            }

            string nombre =
                NormalizarNombre(
                    request.nombreTipoCultivo);

            string descripcion =
                NormalizarDescripcion(
                    request.descripcionTipoCultivo);

            ActionResult? validacion =
                ValidarDatos(
                    nombre,
                    descripcion);

            if (validacion != null)
                return validacion;

            TipoCultivo? entity =
                await db.TipoCultivos
                    .FirstOrDefaultAsync(
                        item =>
                            item.tipoCultivoId == id,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El tipo de cultivo indicado no existe."
                });
            }

            if (!entity.activo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede actualizar un tipo de cultivo que está inactivo."
                });
            }

            bool duplicado =
                await db.TipoCultivos.AnyAsync(
                    item =>
                        item.tipoCultivoId != id &&
                        item.activo &&
                        EF.Functions.Collate(
                            item.nombreTipoCultivo,
                            "Modern_Spanish_CI_AI") ==
                        nombre,
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe otro tipo de cultivo activo con ese nombre."
                });
            }

            entity.nombreTipoCultivo =
                nombre;

            entity.descripcionTipoCultivo =
                descripcion;

            try
            {
                await db.SaveChangesAsync(
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Tipo de cultivo actualizado correctamente.",

                    data =
                        CrearRespuesta(entity)
                });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al actualizar el tipo de cultivo {TipoCultivoId}.",
                    id);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible actualizar el tipo de cultivo porque ya existe un registro activo con el mismo nombre."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al actualizar el tipo de cultivo {TipoCultivoId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al actualizar el tipo de cultivo."
                });
            }
        }

        // ==========================================================
        // ELIMINACIÓN LÓGICA
        // Se conserva PUT para compatibilidad con el frontend actual.
        // ==========================================================
        [HttpPut("{id:int}/eliminar")]
        public async Task<ActionResult> Eliminar(
            int id,
            CancellationToken cancellationToken)
        {
            TipoCultivo? entity =
                await db.TipoCultivos
                    .FirstOrDefaultAsync(
                        item =>
                            item.tipoCultivoId == id &&
                            item.activo,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El tipo de cultivo no existe o ya está desactivado."
                });
            }

            var dependencias =
                new List<string>();

            bool usadoEnRangos =
                await db.ParametroRangoNutrienteCultivo
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.tipoCultivoId == id &&
                            item.activo,
                        cancellationToken);

            if (usadoEnRangos)
            {
                dependencias.Add(
                    "rangos nutricionales por cultivo");
            }

            /*
             * Se revisa todo el historial de análisis, no solo los activos,
             * porque el cultivo debe seguir disponible para consultarlos.
             */
            bool usadoEnAnalisis =
                await db.AnalisisSueloCalculos
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.tipoCultivoId == id,
                        cancellationToken);

            if (usadoEnAnalisis)
            {
                dependencias.Add(
                    "análisis de suelo guardados");
            }

            if (dependencias.Count > 0)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede eliminar el tipo de cultivo porque está siendo utilizado.",

                    usadoEn =
                        dependencias
                });
            }

            entity.activo =
                false;

            try
            {
                await db.SaveChangesAsync(
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Tipo de cultivo desactivado correctamente."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al desactivar el tipo de cultivo {TipoCultivoId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al eliminar el tipo de cultivo."
                });
            }
        }

        private ActionResult? ValidarDatos(
            string nombre,
            string descripcion)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del tipo de cultivo es obligatorio."
                });
            }

            if (nombre.Length > 80)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del tipo de cultivo no puede superar 80 caracteres."
                });
            }

            if (descripcion.Length > 150)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La descripción no puede superar 150 caracteres."
                });
            }

            return null;
        }

        private static TipoCultivoRespuestaDto CrearRespuesta(
            TipoCultivo item) =>
            new()
            {
                tipoCultivoId =
                    item.tipoCultivoId,

                nombreTipoCultivo =
                    item.nombreTipoCultivo,

                tipoCultivo =
                    item.nombreTipoCultivo,

                descripcionTipoCultivo =
                    item.descripcionTipoCultivo,

                activo =
                    item.activo
            };

        private static string NormalizarNombre(
            string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();

        private static string NormalizarDescripcion(
            string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim();
    }
}
