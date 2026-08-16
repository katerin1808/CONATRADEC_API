using CONATRADEC_API.Constants;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/configuracion/tipos-analisis-suelo")]
    public sealed class TipoAnalisisSueloController : ControllerBase
    {
        private const string NombreInterfaz =
            "tipoAnalisisSueloPage";

        private readonly DBContext db;
        private readonly PermisoApiService permisoApiService;
        private readonly ILogger<TipoAnalisisSueloController> logger;

        public TipoAnalisisSueloController(
            DBContext db,
            PermisoApiService permisoApiService,
            ILogger<TipoAnalisisSueloController> logger)
        {
            this.db = db;
            this.permisoApiService = permisoApiService;
            this.logger = logger;
        }

        // ==========================================================
        // LISTADO COMPLETO PARA FORMULARIOS Y SELECTORES
        // ==========================================================
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TipoAnalisisSueloRespuestaDto>>>
            Listar(CancellationToken cancellationToken)
        {
            /*
             * Este endpoint se conserva como catálogo compartido por formularios
             * y selectores fuera de Configuración. La autenticación JWT global
             * continúa protegiéndolo; los permisos funcionales se aplican en las
             * operaciones administrativas y de escritura de este controlador.
             */
            List<TipoAnalisisSueloRespuestaDto> data =
                await db.TipoAnalisisSuelos
                    .AsNoTracking()
                    .Where(item =>
                        item.activo)
                    .OrderBy(item =>
                        item.nombreTipoAnalisisSuelo)
                    .ThenBy(item =>
                        item.tipoAnalisisSueloId)
                    .Select(item =>
                        new TipoAnalisisSueloRespuestaDto
                        {
                            tipoAnalisisSueloId =
                                item.tipoAnalisisSueloId,

                            codigoTipoAnalisisSuelo =
                                item.codigoTipoAnalisisSuelo,

                            nombreTipoAnalisisSuelo =
                                item.nombreTipoAnalisisSuelo,

                            descripcionTipoAnalisisSuelo =
                                item.descripcionTipoAnalisisSuelo,

                            activo =
                                item.activo,

                            esTipoSistema =
                                (
                                    item.codigoTipoAnalisisSuelo ==
                                        TipoAnalisisSueloCodigos.RequerimientoAnual ||
                                    item.codigoTipoAnalisisSuelo ==
                                        TipoAnalisisSueloCodigos.BalanceFormula ||
                                    item.codigoTipoAnalisisSuelo ==
                                        TipoAnalisisSueloCodigos.EnmiendaCalcarea ||
                                    item.codigoTipoAnalisisSuelo ==
                                        TipoAnalisisSueloCodigos.FertilizacionMixta
                                ),

                            puedeEliminar =
                                item.codigoTipoAnalisisSuelo !=
                                    TipoAnalisisSueloCodigos.RequerimientoAnual &&
                                item.codigoTipoAnalisisSuelo !=
                                    TipoAnalisisSueloCodigos.BalanceFormula &&
                                item.codigoTipoAnalisisSuelo !=
                                    TipoAnalisisSueloCodigos.EnmiendaCalcarea &&
                                item.codigoTipoAnalisisSuelo !=
                                    TipoAnalisisSueloCodigos.FertilizacionMixta
                        })
                    .ToListAsync(cancellationToken);

            return Ok(data);
        }

        // ==========================================================
        // BÚSQUEDA PAGINADA PARA LA PANTALLA ADMINISTRATIVA
        // ==========================================================
        [HttpGet("buscar")]
        public async Task<ActionResult<TipoAnalisisSueloPaginaResponse>>
            Buscar(
                [FromQuery] string? buscar = null,
                [FromQuery] int pagina = 1,
                [FromQuery] int tamanoPagina = 20,
                [FromQuery] string orden = "nombre",
                [FromQuery] string direccion = "asc",
                [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId = null,
                CancellationToken cancellationToken = default)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            pagina =
                Math.Max(
                    1,
                    pagina);

            tamanoPagina =
                Math.Clamp(
                    tamanoPagina,
                    5,
                    100);

            IQueryable<TipoAnalisisSuelo> query =
                db.TipoAnalisisSuelos
                    .AsNoTracking()
                    .Where(item =>
                        item.activo);

            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string texto =
                    buscar
                        .ReplaceLineEndings(" ")
                        .Trim();

                if (texto.Length > 200)
                    texto = texto[..200];

                query = query.Where(item =>
                    item.nombreTipoAnalisisSuelo.Contains(texto) ||
                    item.descripcionTipoAnalisisSuelo.Contains(texto) ||
                    item.codigoTipoAnalisisSuelo.Contains(texto));
            }

            bool descendente =
                string.Equals(
                    direccion,
                    "desc",
                    StringComparison.OrdinalIgnoreCase);

            /*
             * El ID actúa como desempate estable. Dos registros con el mismo
             * nombre ya no pueden intercambiar posición entre páginas.
             */
            query =
                descendente
                    ? query
                        .OrderByDescending(item =>
                            item.nombreTipoAnalisisSuelo)
                        .ThenByDescending(item =>
                            item.tipoAnalisisSueloId)
                    : query
                        .OrderBy(item =>
                            item.nombreTipoAnalisisSuelo)
                        .ThenBy(item =>
                            item.tipoAnalisisSueloId);

            int totalRegistros =
                await query.CountAsync(cancellationToken);

            List<TipoAnalisisSueloRespuestaDto> items =
                await query
                    .Skip(
                        (pagina - 1) *
                        tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(item =>
                        new TipoAnalisisSueloRespuestaDto
                        {
                            tipoAnalisisSueloId =
                                item.tipoAnalisisSueloId,

                            codigoTipoAnalisisSuelo =
                                item.codigoTipoAnalisisSuelo,

                            nombreTipoAnalisisSuelo =
                                item.nombreTipoAnalisisSuelo,

                            descripcionTipoAnalisisSuelo =
                                item.descripcionTipoAnalisisSuelo,

                            activo =
                                item.activo,

                            esTipoSistema =
                                (
                                    item.codigoTipoAnalisisSuelo ==
                                        TipoAnalisisSueloCodigos.RequerimientoAnual ||
                                    item.codigoTipoAnalisisSuelo ==
                                        TipoAnalisisSueloCodigos.BalanceFormula ||
                                    item.codigoTipoAnalisisSuelo ==
                                        TipoAnalisisSueloCodigos.EnmiendaCalcarea ||
                                    item.codigoTipoAnalisisSuelo ==
                                        TipoAnalisisSueloCodigos.FertilizacionMixta
                                ),

                            puedeEliminar =
                                item.codigoTipoAnalisisSuelo !=
                                    TipoAnalisisSueloCodigos.RequerimientoAnual &&
                                item.codigoTipoAnalisisSuelo !=
                                    TipoAnalisisSueloCodigos.BalanceFormula &&
                                item.codigoTipoAnalisisSuelo !=
                                    TipoAnalisisSueloCodigos.EnmiendaCalcarea &&
                                item.codigoTipoAnalisisSuelo !=
                                    TipoAnalisisSueloCodigos.FertilizacionMixta
                        })
                    .ToListAsync(cancellationToken);

            await AsignarConteosAsync(
                items,
                cancellationToken);

            int totalPaginas =
                totalRegistros == 0
                    ? 1
                    : (int)Math.Ceiling(
                        totalRegistros /
                        (double)tamanoPagina);

            return Ok(
                new TipoAnalisisSueloPaginaResponse
                {
                    Items = items,
                    PaginaActual = pagina,
                    TamanoPagina = tamanoPagina,
                    TotalRegistros = totalRegistros,
                    TotalPaginas = totalPaginas
                });
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<TipoAnalisisSueloRespuestaDto>>
            Obtener(
                int id,
                [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
                CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            TipoAnalisisSueloRespuestaDto? data =
                await db.TipoAnalisisSuelos
                    .AsNoTracking()
                    .Where(item =>
                        item.tipoAnalisisSueloId == id &&
                        item.activo)
                    .Select(item =>
                        new TipoAnalisisSueloRespuestaDto
                        {
                            tipoAnalisisSueloId =
                                item.tipoAnalisisSueloId,

                            codigoTipoAnalisisSuelo =
                                item.codigoTipoAnalisisSuelo,

                            nombreTipoAnalisisSuelo =
                                item.nombreTipoAnalisisSuelo,

                            descripcionTipoAnalisisSuelo =
                                item.descripcionTipoAnalisisSuelo,

                            activo =
                                item.activo,

                            esTipoSistema =
                                (
                                    item.codigoTipoAnalisisSuelo ==
                                        TipoAnalisisSueloCodigos.RequerimientoAnual ||
                                    item.codigoTipoAnalisisSuelo ==
                                        TipoAnalisisSueloCodigos.BalanceFormula ||
                                    item.codigoTipoAnalisisSuelo ==
                                        TipoAnalisisSueloCodigos.EnmiendaCalcarea ||
                                    item.codigoTipoAnalisisSuelo ==
                                        TipoAnalisisSueloCodigos.FertilizacionMixta
                                ),

                            puedeEliminar =
                                item.codigoTipoAnalisisSuelo !=
                                    TipoAnalisisSueloCodigos.RequerimientoAnual &&
                                item.codigoTipoAnalisisSuelo !=
                                    TipoAnalisisSueloCodigos.BalanceFormula &&
                                item.codigoTipoAnalisisSuelo !=
                                    TipoAnalisisSueloCodigos.EnmiendaCalcarea &&
                                item.codigoTipoAnalisisSuelo !=
                                    TipoAnalisisSueloCodigos.FertilizacionMixta
                        })
                    .SingleOrDefaultAsync(cancellationToken);

            if (data == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El tipo de análisis de suelo no existe o está inactivo."
                });
            }

            await AsignarConteosAsync(
                new List<TipoAnalisisSueloRespuestaDto>
                {
                    data
                },
                cancellationToken);

            return Ok(data);
        }

        // ==========================================================
        // DIAGNÓSTICO DE RELACIONES
        // ==========================================================
        [HttpGet("diagnostico-relaciones")]
        public async Task<ActionResult> DiagnosticoRelaciones(
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            int formulasSinRelacion =
                await db.formulaNutricional
                    .AsNoTracking()
                    .CountAsync(
                        item =>
                            item.analisisSueloCalculoId.HasValue &&
                            !db.AnalisisSueloCalculos.Any(calculo =>
                                calculo.analisisSueloCalculoId ==
                                item.analisisSueloCalculoId.Value),
                        cancellationToken);

            int enmiendasSinRelacion =
                await db.enmiendaCalcarea
                    .AsNoTracking()
                    .CountAsync(
                        item =>
                            item.analisisSueloCalculoId.HasValue &&
                            !db.AnalisisSueloCalculos.Any(calculo =>
                                calculo.analisisSueloCalculoId ==
                                item.analisisSueloCalculoId.Value),
                        cancellationToken);

            int mixtasSinRelacion =
                await db.fertilizacionMixta
                    .AsNoTracking()
                    .CountAsync(
                        item =>
                            !db.AnalisisSueloCalculos.Any(calculo =>
                                calculo.analisisSueloCalculoId ==
                                item.analisisSueloCalculoId),
                        cancellationToken);

            int formulasNoVinculadas =
                await db.formulaNutricional
                    .AsNoTracking()
                    .CountAsync(
                        item =>
                            !item.analisisSueloCalculoId.HasValue,
                        cancellationToken);

            int enmiendasNoVinculadas =
                await db.enmiendaCalcarea
                    .AsNoTracking()
                    .CountAsync(
                        item =>
                            !item.analisisSueloCalculoId.HasValue,
                        cancellationToken);

            return Ok(new
            {
                success = true,
                relacionesValidas =
                    formulasSinRelacion == 0 &&
                    enmiendasSinRelacion == 0 &&
                    mixtasSinRelacion == 0,

                data = new
                {
                    formulasSinRelacion,
                    enmiendasSinRelacion,
                    mixtasSinRelacion,

                    /*
                     * Fórmula y enmienda permiten null porque existieron
                     * registros independientes antes del guardado integral.
                     * No son huérfanos, pero no se cuentan como asociados
                     * a un análisis de suelo.
                     */
                    formulasNoVinculadas,
                    enmiendasNoVinculadas
                }
            });
        }

        // ==========================================================
        // CREAR O REACTIVAR
        // ==========================================================
        [HttpPost]
        public async Task<ActionResult> Crear(
            [FromBody] CrearTipoAnalisisSueloDto? request,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No se recibieron los datos del tipo de análisis de suelo."
                });
            }

            string nombre =
                NormalizarNombre(
                    request.nombreTipoAnalisisSuelo);

            string descripcion =
                NormalizarDescripcion(
                    request.descripcionTipoAnalisisSuelo);

            ActionResult? validacion =
                ValidarDatos(
                    nombre,
                    descripcion);

            if (validacion != null)
                return validacion;

            bool existeActivo =
                await db.TipoAnalisisSuelos
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.activo &&
                            EF.Functions.Collate(
                                item.nombreTipoAnalisisSuelo,
                                "Modern_Spanish_CI_AI") ==
                            nombre,
                        cancellationToken);

            if (existeActivo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe un tipo de análisis de suelo activo con ese nombre."
                });
            }

            TipoAnalisisSuelo? existenteInactivo =
                await db.TipoAnalisisSuelos
                    .FirstOrDefaultAsync(
                        item =>
                            !item.activo &&
                            EF.Functions.Collate(
                                item.nombreTipoAnalisisSuelo,
                                "Modern_Spanish_CI_AI") ==
                            nombre,
                        cancellationToken);

            try
            {
                if (existenteInactivo != null)
                {
                    existenteInactivo.nombreTipoAnalisisSuelo =
                        nombre;

                    existenteInactivo.descripcionTipoAnalisisSuelo =
                        descripcion;

                    existenteInactivo.activo =
                        true;

                    if (string.IsNullOrWhiteSpace(
                            existenteInactivo.codigoTipoAnalisisSuelo))
                    {
                        existenteInactivo.codigoTipoAnalisisSuelo =
                            TipoAnalisisSueloCodigos
                                .CrearCodigoPersonalizado();
                    }

                    await db.SaveChangesAsync(
                        cancellationToken);

                    return Ok(new
                    {
                        success = true,
                        message =
                            "Tipo de análisis de suelo reactivado correctamente.",

                        data =
                            CrearRespuesta(existenteInactivo)
                    });
                }

                var entity =
                    new TipoAnalisisSuelo
                    {
                        codigoTipoAnalisisSuelo =
                            TipoAnalisisSueloCodigos
                                .CrearCodigoPersonalizado(),

                        nombreTipoAnalisisSuelo =
                            nombre,

                        descripcionTipoAnalisisSuelo =
                            descripcion,

                        activo =
                            true
                    };

                db.TipoAnalisisSuelos.Add(entity);

                await db.SaveChangesAsync(
                    cancellationToken);

                return StatusCode(
                    StatusCodes.Status201Created,
                    new
                    {
                        success = true,
                        message =
                            "Tipo de análisis de suelo creado correctamente.",

                        data =
                            CrearRespuesta(entity)
                    });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al crear el tipo de análisis de suelo {Nombre}.",
                    nombre);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible crear el tipo de análisis de suelo porque ya existe un registro activo con el mismo nombre."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al crear un tipo de análisis de suelo.");

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al crear el tipo de análisis de suelo."
                });
            }
        }

        // ==========================================================
        // ACTUALIZAR
        // El código interno nunca se modifica.
        // ==========================================================
        [HttpPut("{id:int}")]
        public async Task<ActionResult> Actualizar(
            int id,
            [FromBody] ActualizarTipoAnalisisSueloDto? request,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El identificador del tipo de análisis no es válido."
                });
            }

            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No se recibieron los datos del tipo de análisis de suelo."
                });
            }

            string nombre =
                NormalizarNombre(
                    request.nombreTipoAnalisisSuelo);

            string descripcion =
                NormalizarDescripcion(
                    request.descripcionTipoAnalisisSuelo);

            ActionResult? validacion =
                ValidarDatos(
                    nombre,
                    descripcion);

            if (validacion != null)
                return validacion;

            TipoAnalisisSuelo? entity =
                await db.TipoAnalisisSuelos
                    .FirstOrDefaultAsync(
                        item =>
                            item.tipoAnalisisSueloId == id,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El tipo de análisis de suelo indicado no existe."
                });
            }

            if (!entity.activo)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede actualizar un tipo de análisis de suelo que está inactivo."
                });
            }

            bool duplicado =
                await db.TipoAnalisisSuelos.AnyAsync(
                    item =>
                        item.tipoAnalisisSueloId != id &&
                        item.activo &&
                        EF.Functions.Collate(
                            item.nombreTipoAnalisisSuelo,
                            "Modern_Spanish_CI_AI") ==
                        nombre,
                    cancellationToken);

            if (duplicado)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe otro tipo de análisis de suelo activo con ese nombre."
                });
            }

            entity.nombreTipoAnalisisSuelo =
                nombre;

            entity.descripcionTipoAnalisisSuelo =
                descripcion;

            try
            {
                await db.SaveChangesAsync(
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Tipo de análisis de suelo actualizado correctamente.",

                    data =
                        CrearRespuesta(entity)
                });
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al actualizar el tipo de análisis de suelo {TipoAnalisisSueloId}.",
                    id);

                return Conflict(new
                {
                    success = false,
                    message =
                        "No fue posible actualizar el tipo de análisis de suelo porque ya existe un registro activo con el mismo nombre."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al actualizar el tipo de análisis de suelo {TipoAnalisisSueloId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al actualizar el tipo de análisis de suelo."
                });
            }
        }

        // ==========================================================
        // ELIMINACIÓN LÓGICA
        // Los cuatro tipos internos del sistema no se eliminan.
        // ==========================================================
        [HttpPut("{id:int}/eliminar")]
        public async Task<ActionResult> Eliminar(
            int id,
            [FromHeader(Name = "X-Usuario-Id")] int? usuarioSesionId,
            CancellationToken cancellationToken)
        {
            ActionResult? acceso =
                await ValidarAccesoAsync(
                    usuarioSesionId,
                    TipoPermisoApi.Eliminar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            TipoAnalisisSuelo? entity =
                await db.TipoAnalisisSuelos
                    .FirstOrDefaultAsync(
                        item =>
                            item.tipoAnalisisSueloId == id &&
                            item.activo,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El tipo de análisis de suelo no existe o ya está desactivado."
                });
            }

            if (TipoAnalisisSueloCodigos.EsTipoSistema(
                    entity.codigoTipoAnalisisSuelo))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Este tipo de análisis pertenece a un módulo interno del sistema y no puede eliminarse."
                });
            }

            bool usadoEnAnalisis =
                await db.AnalisisSueloCalculos
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.tipoAnalisisSueloId == id,
                        cancellationToken);

            if (usadoEnAnalisis)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "No se puede eliminar el tipo de análisis de suelo porque está siendo utilizado.",

                    usadoEn =
                        new[]
                        {
                            "análisis de suelo guardados"
                        }
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
                        "Tipo de análisis de suelo desactivado correctamente."
                });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al desactivar el tipo de análisis de suelo {TipoAnalisisSueloId}.",
                    id);

                return StatusCode(500, new
                {
                    success = false,
                    message =
                        "Ocurrió un error inesperado al eliminar el tipo de análisis de suelo."
                });
            }
        }

        private async Task AsignarConteosAsync(
            List<TipoAnalisisSueloRespuestaDto> items,
            CancellationToken cancellationToken)
        {
            if (items.Count == 0)
                return;

            Dictionary<int, int> conteosDirectos =
                await db.AnalisisSueloCalculos
                    .AsNoTracking()
                    .Where(item =>
                        item.activo)
                    .GroupBy(item =>
                        item.tipoAnalisisSueloId)
                    .Select(grupo =>
                        new
                        {
                            TipoId =
                                grupo.Key,

                            Cantidad =
                                grupo
                                    .Select(item =>
                                        item.analisisSueloCalculoId)
                                    .Distinct()
                                    .Count()
                        })
                    .ToDictionaryAsync(
                        item =>
                            item.TipoId,

                        item =>
                            item.Cantidad,

                        cancellationToken);

            int cantidadBalances =
                await db.formulaNutricional
                    .AsNoTracking()
                    .Where(item =>
                        item.activo &&
                        item.analisisSueloCalculoId.HasValue &&
                        db.AnalisisSueloCalculos.Any(calculo =>
                            calculo.analisisSueloCalculoId ==
                                item.analisisSueloCalculoId.Value &&
                            calculo.activo))
                    .Select(item =>
                        item.analisisSueloCalculoId!.Value)
                    .Distinct()
                    .CountAsync(cancellationToken);

            int cantidadEnmiendas =
                await db.enmiendaCalcarea
                    .AsNoTracking()
                    .Where(item =>
                        item.activo &&
                        item.analisisSueloCalculoId.HasValue &&
                        db.AnalisisSueloCalculos.Any(calculo =>
                            calculo.analisisSueloCalculoId ==
                                item.analisisSueloCalculoId.Value &&
                            calculo.activo))
                    .Select(item =>
                        item.analisisSueloCalculoId!.Value)
                    .Distinct()
                    .CountAsync(cancellationToken);

            int cantidadMixtas =
                await db.fertilizacionMixta
                    .AsNoTracking()
                    .Where(item =>
                        item.activo &&
                        db.AnalisisSueloCalculos.Any(calculo =>
                            calculo.analisisSueloCalculoId ==
                                item.analisisSueloCalculoId &&
                            calculo.activo))
                    .Select(item =>
                        item.analisisSueloCalculoId)
                    .Distinct()
                    .CountAsync(cancellationToken);

            foreach (TipoAnalisisSueloRespuestaDto item in items)
            {
                item.cantidadAnalisis =
                    item.codigoTipoAnalisisSuelo switch
                    {
                        TipoAnalisisSueloCodigos.BalanceFormula =>
                            cantidadBalances,

                        TipoAnalisisSueloCodigos.EnmiendaCalcarea =>
                            cantidadEnmiendas,

                        TipoAnalisisSueloCodigos.FertilizacionMixta =>
                            cantidadMixtas,

                        _ =>
                            conteosDirectos.TryGetValue(
                                item.tipoAnalisisSueloId,
                                out int cantidad)
                                    ? cantidad
                                    : 0
                    };
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
                        "El nombre del tipo de análisis es obligatorio."
                });
            }

            if (nombre.Length > 100)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El nombre del tipo de análisis no puede superar 100 caracteres."
                });
            }

            if (string.IsNullOrWhiteSpace(descripcion))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La descripción del tipo de análisis es obligatoria."
                });
            }

            if (descripcion.Length > 200)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La descripción no puede superar 200 caracteres."
                });
            }

            return null;
        }

        private static TipoAnalisisSueloRespuestaDto CrearRespuesta(
            TipoAnalisisSuelo item)
        {
            bool esSistema =
                TipoAnalisisSueloCodigos.EsTipoSistema(
                    item.codigoTipoAnalisisSuelo);

            return new TipoAnalisisSueloRespuestaDto
            {
                tipoAnalisisSueloId =
                    item.tipoAnalisisSueloId,

                codigoTipoAnalisisSuelo =
                    item.codigoTipoAnalisisSuelo,

                nombreTipoAnalisisSuelo =
                    item.nombreTipoAnalisisSuelo,

                descripcionTipoAnalisisSuelo =
                    item.descripcionTipoAnalisisSuelo,

                activo =
                    item.activo,

                esTipoSistema =
                    esSistema,

                puedeEliminar =
                    !esSistema
            };
        }

        private async Task<ActionResult?> ValidarAccesoAsync(
            int? usuarioSesionId,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado =
                await permisoApiService.ValidarAsync(
                    usuarioSesionId,
                    NombreInterfaz,
                    permiso,
                    cancellationToken);

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                new
                {
                    success = false,
                    message = resultado.Mensaje
                });
        }

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
