using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// API administrativa moderna de Fuentes de nutrientes.
    ///
    /// Los controladores históricos bajo api/fuente-nutriente permanecen
    /// intactos para selectores, cálculos y versiones anteriores. Este
    /// controlador concentra paginación, permisos y guardado atómico del
    /// formulario administrativo actual.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/administracion/fuentes-nutrientes")]
    public sealed class AdministracionFuentesNutrientesController :
        ControllerBase
    {
        private const string PermisoInterfaz =
            "fuenteNutrientePage";

        private const string CodigoInactivoExistente =
            "FUENTE_NUTRIENTE_INACTIVA_EXISTENTE";

        private const string CategoriaTodas =
            "TODAS";

        private const string CategoriaBalance =
            "BALANCE_NUTRICIONAL";

        private const string CategoriaEnmienda =
            "ENMIENDA_CALCAREA";

        private const string CategoriaMixta =
            "FERTILIZACION_MIXTA";

        private readonly DBContext db;
        private readonly PermisoApiService permisos;
        private readonly ILogger<AdministracionFuentesNutrientesController>
            logger;

        public AdministracionFuentesNutrientesController(
            DBContext db,
            PermisoApiService permisos,
            ILogger<AdministracionFuentesNutrientesController> logger)
        {
            this.db = db;
            this.permisos = permisos;
            this.logger = logger;
        }

        // ==========================================================
        // LISTADO ACTIVO PAGINADO
        // ==========================================================
        [HttpGet]
        public async Task<IActionResult> Listar(
            [FromQuery] string? buscar = null,
            [FromQuery] string? categoria = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 20,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(
                tamanoPagina,
                5,
                100);

            IQueryable<FuenteNutriente> query =
                ConstruirConsultaBase(
                    activo: true,
                    buscar,
                    categoria);

            int totalRegistros =
                await query.CountAsync(
                    cancellationToken);

            List<int> idsPagina =
                await query
                    .OrderBy(x => x.nombreNutriente)
                    .ThenBy(x => x.fuenteNutrientesId)
                    .Skip(
                        (pagina - 1) *
                        tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(x => x.fuenteNutrientesId)
                    .ToListAsync(
                        cancellationToken);

            List<FuenteNutrienteAdminDto> items =
                await ProyectarPaginaAsync(
                    idsPagina,
                    cancellationToken);

            return Ok(
                PaginaRespuesta<FuenteNutrienteAdminDto>.Crear(
                    items,
                    pagina,
                    tamanoPagina,
                    totalRegistros));
        }

        // ==========================================================
        // LISTADO INACTIVO PAGINADO
        // ==========================================================
        [HttpGet("inactivas")]
        public async Task<IActionResult> ListarInactivas(
            [FromQuery] string? buscar = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 20,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(
                tamanoPagina,
                5,
                100);

            IQueryable<FuenteNutriente> query =
                ConstruirConsultaBase(
                    activo: false,
                    buscar,
                    CategoriaTodas);

            int totalRegistros =
                await query.CountAsync(
                    cancellationToken);

            List<int> idsPagina =
                await query
                    .OrderBy(x => x.nombreNutriente)
                    .ThenBy(x => x.fuenteNutrientesId)
                    .Skip(
                        (pagina - 1) *
                        tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(x => x.fuenteNutrientesId)
                    .ToListAsync(
                        cancellationToken);

            List<FuenteNutrienteAdminDto> items =
                await ProyectarPaginaAsync(
                    idsPagina,
                    cancellationToken);

            return Ok(
                PaginaRespuesta<FuenteNutrienteAdminDto>.Crear(
                    items,
                    pagina,
                    tamanoPagina,
                    totalRegistros));
        }

        // ==========================================================
        // MATRIZ BAJO DEMANDA
        // ==========================================================
        [HttpGet("composicion")]
        public async Task<IActionResult> ObtenerComposicion(
            [FromQuery] string? buscar = null,
            [FromQuery] string? categoria = null,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            string categoriaNormalizada =
                NormalizarCategoriaFiltro(
                    categoria);

            if (categoriaNormalizada == CategoriaEnmienda)
            {
                return Ok(
                    Array.Empty<FuenteNutrienteAdminDto>());
            }

            IQueryable<FuenteNutriente> query =
                ConstruirConsultaBase(
                    activo: true,
                    buscar,
                    categoriaNormalizada)
                .Where(x =>
                    x.fuenteNutrienteElementoQuimico
                        .Any(relacion =>
                            relacion.activo &&
                            relacion.cantidadAporte > 0));

            List<int> ids =
                await query
                    .OrderBy(x => x.nombreNutriente)
                    .ThenBy(x => x.fuenteNutrientesId)
                    .Select(x => x.fuenteNutrientesId)
                    .ToListAsync(
                        cancellationToken);

            return Ok(
                await ProyectarPaginaAsync(
                    ids,
                    cancellationToken));
        }

        // ==========================================================
        // CREAR: FUENTE + APORTES + CLASIFICACIÓN, UNA TRANSACCIÓN
        // ==========================================================
        [HttpPost]
        public async Task<IActionResult> Crear(
            [FromBody] FuenteNutrienteGuardarDto? dto,
            [FromQuery] bool crearNuevoSiExisteInactivo = false,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (dto == null)
            {
                return BadRequest(
                    Error(
                        "No se recibieron los datos de la fuente de nutriente."));
            }

            DatosNormalizados datos =
                NormalizarDatos(dto);

            IActionResult? validacion =
                await ValidarDatosAsync(
                    datos,
                    cancellationToken);

            if (validacion != null)
                return validacion;

            if (await ExisteActivaConNombreAsync(
                    datos.Nombre,
                    null,
                    cancellationToken))
            {
                return Conflict(
                    Error(
                        "Ya existe una fuente de nutriente activa con ese nombre."));
            }

            FuenteNutriente? inactiva =
                await BuscarInactivaConNombreAsync(
                    datos.Nombre,
                    null,
                    cancellationToken);

            if (inactiva != null &&
                !crearNuevoSiExisteInactivo)
            {
                FuenteNutrienteAdminDto? data =
                    await ObtenerDtoAsync(
                        inactiva.fuenteNutrientesId,
                        cancellationToken);

                return Conflict(
                    ConflictoInactivo(
                        data,
                        "Ya existe una fuente de nutriente eliminada con ese nombre."));
            }

            await using var transaccion =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                var entidad =
                    new FuenteNutriente
                    {
                        nombreNutriente = datos.Nombre,
                        descripcionNutriente = datos.Descripcion,
                        precioNutriente = datos.Precio,
                        activo = true
                    };

                db.fuenteNutriente.Add(entidad);

                await db.SaveChangesAsync(
                    cancellationToken);

                await AplicarConfiguracionCompletaAsync(
                    entidad.fuenteNutrientesId,
                    datos,
                    cancellationToken);

                await db.SaveChangesAsync(
                    cancellationToken);

                await transaccion.CommitAsync(
                    cancellationToken);

                FuenteNutrienteAdminDto? respuesta =
                    await ObtenerDtoAsync(
                        entidad.fuenteNutrientesId,
                        cancellationToken);

                return Ok(
                    Exito(
                        respuesta,
                        "Fuente de nutriente creada correctamente."));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);
                throw;
            }
            catch (DbUpdateException ex)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);

                logger.LogWarning(
                    ex,
                    "Conflicto de datos al crear la fuente {Nombre}.",
                    datos.Nombre);

                return Conflict(
                    Error(
                        "No fue posible crear la fuente porque existe un conflicto con los datos actuales."));
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);

                logger.LogError(
                    ex,
                    "Error inesperado al crear una fuente de nutriente.");

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "Ocurrió un error inesperado al crear la fuente de nutriente."));
            }
        }

        // ==========================================================
        // EDITAR ATÓMICAMENTE
        // ==========================================================
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Actualizar(
            int id,
            [FromBody] FuenteNutrienteGuardarDto? dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0 || dto == null)
            {
                return BadRequest(
                    Error(
                        "No se recibió una fuente de nutriente válida para actualizar."));
            }

            if (dto.FuenteNutrientesId is > 0 &&
                dto.FuenteNutrientesId.Value != id)
            {
                return BadRequest(
                    Error(
                        "El identificador enviado no coincide con la fuente que se desea actualizar."));
            }

            FuenteNutriente? entidad =
                await db.fuenteNutriente
                    .FirstOrDefaultAsync(
                        x =>
                            x.fuenteNutrientesId == id &&
                            x.activo,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(
                    Error(
                        "La fuente de nutriente no existe o está inactiva."));
            }

            DatosNormalizados datos =
                NormalizarDatos(dto);

            IActionResult? validacion =
                await ValidarDatosAsync(
                    datos,
                    cancellationToken);

            if (validacion != null)
                return validacion;

            if (await ExisteActivaConNombreAsync(
                    datos.Nombre,
                    id,
                    cancellationToken))
            {
                return Conflict(
                    Error(
                        "Ya existe otra fuente de nutriente activa con ese nombre."));
            }

            FuenteNutriente? inactivaCoincidente =
                await BuscarInactivaConNombreAsync(
                    datos.Nombre,
                    id,
                    cancellationToken);

            if (inactivaCoincidente != null)
            {
                return Conflict(
                    Error(
                        "Existe una fuente de nutriente eliminada con ese nombre. Reactívela o use otro nombre para evitar registros ambiguos."));
            }

            await using var transaccion =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                entidad.nombreNutriente = datos.Nombre;
                entidad.descripcionNutriente = datos.Descripcion;
                entidad.precioNutriente = datos.Precio;

                await AplicarConfiguracionCompletaAsync(
                    id,
                    datos,
                    cancellationToken);

                await db.SaveChangesAsync(
                    cancellationToken);

                await transaccion.CommitAsync(
                    cancellationToken);

                FuenteNutrienteAdminDto? respuesta =
                    await ObtenerDtoAsync(
                        id,
                        cancellationToken);

                return Ok(
                    Exito(
                        respuesta,
                        "Fuente de nutriente actualizada correctamente."));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);
                throw;
            }
            catch (DbUpdateException ex)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);

                logger.LogWarning(
                    ex,
                    "Conflicto de datos al actualizar la fuente {Id}.",
                    id);

                return Conflict(
                    Error(
                        "No fue posible actualizar la fuente porque existe un conflicto con los datos actuales."));
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);

                logger.LogError(
                    ex,
                    "Error inesperado al actualizar la fuente {Id}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "Ocurrió un error inesperado al actualizar la fuente de nutriente."));
            }
        }

        // ==========================================================
        // ELIMINACIÓN LÓGICA: CONSERVA RELACIONES HISTÓRICAS
        // ==========================================================
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Eliminar(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Eliminar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            FuenteNutriente? entidad =
                await db.fuenteNutriente
                    .FirstOrDefaultAsync(
                        x => x.fuenteNutrientesId == id,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(
                    Error(
                        "Fuente de nutriente no encontrada."));
            }

            if (!entidad.activo)
            {
                return BadRequest(
                    Error(
                        "La fuente de nutriente ya está eliminada."));
            }

            try
            {
                entidad.activo = false;

                await db.SaveChangesAsync(
                    cancellationToken);

                FuenteNutrienteAdminDto? respuesta =
                    await ObtenerDtoAsync(
                        id,
                        cancellationToken);

                return Ok(
                    Exito(
                        respuesta,
                        "Fuente de nutriente eliminada correctamente."));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al eliminar la fuente {Id}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "Ocurrió un error inesperado al eliminar la fuente de nutriente."));
            }
        }

        // ==========================================================
        // REACTIVAR CONSERVANDO DATOS/CLASIFICACIÓN ANTERIORES
        // ==========================================================
        [HttpPut("{id:int}/reactivar")]
        public async Task<IActionResult> Reactivar(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            FuenteNutriente? entidad =
                await db.fuenteNutriente
                    .FirstOrDefaultAsync(
                        x => x.fuenteNutrientesId == id,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(
                    Error(
                        "Fuente de nutriente no encontrada."));
            }

            if (entidad.activo)
            {
                return BadRequest(
                    Error(
                        "La fuente de nutriente ya se encuentra activa."));
            }

            string nombre =
                NormalizarNombre(
                    entidad.nombreNutriente);

            if (await ExisteActivaConNombreAsync(
                    nombre,
                    id,
                    cancellationToken))
            {
                return Conflict(
                    Error(
                        "No se puede reactivar la fuente porque ya existe otra fuente activa con ese nombre."));
            }

            try
            {
                entidad.activo = true;

                await db.SaveChangesAsync(
                    cancellationToken);

                FuenteNutrienteAdminDto? respuesta =
                    await ObtenerDtoAsync(
                        id,
                        cancellationToken);

                return Ok(
                    Exito(
                        respuesta,
                        "Fuente de nutriente reactivada correctamente."));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al reactivar la fuente {Id}.",
                    id);

                return Conflict(
                    Error(
                        "No fue posible reactivar la fuente por un conflicto con los datos actuales."));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al reactivar la fuente {Id}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "Ocurrió un error inesperado al reactivar la fuente de nutriente."));
            }
        }

        // ==========================================================
        // REACTIVAR REEMPLAZANDO DATOS, TAMBIÉN ATÓMICO
        // ==========================================================
        [HttpPut("{id:int}/reactivar-con-datos")]
        public async Task<IActionResult> ReactivarConDatos(
            int id,
            [FromBody] FuenteNutrienteGuardarDto? dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0 || dto == null)
            {
                return BadRequest(
                    Error(
                        "No se recibieron datos válidos para reactivar la fuente."));
            }

            if (dto.FuenteNutrientesId is > 0 &&
                dto.FuenteNutrientesId.Value != id)
            {
                return BadRequest(
                    Error(
                        "El identificador enviado no coincide con la fuente que se desea reactivar."));
            }

            FuenteNutriente? entidad =
                await db.fuenteNutriente
                    .FirstOrDefaultAsync(
                        x => x.fuenteNutrientesId == id,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(
                    Error(
                        "Fuente de nutriente no encontrada."));
            }

            if (entidad.activo)
            {
                return Conflict(
                    Error(
                        "La fuente de nutriente ya se encuentra activa."));
            }

            DatosNormalizados datos =
                NormalizarDatos(dto);

            IActionResult? validacion =
                await ValidarDatosAsync(
                    datos,
                    cancellationToken);

            if (validacion != null)
                return validacion;

            if (await ExisteActivaConNombreAsync(
                    datos.Nombre,
                    id,
                    cancellationToken))
            {
                return Conflict(
                    Error(
                        "Ya existe otra fuente de nutriente activa con ese nombre."));
            }

            await using var transaccion =
                await db.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                entidad.nombreNutriente = datos.Nombre;
                entidad.descripcionNutriente = datos.Descripcion;
                entidad.precioNutriente = datos.Precio;
                entidad.activo = true;

                await AplicarConfiguracionCompletaAsync(
                    id,
                    datos,
                    cancellationToken);

                await db.SaveChangesAsync(
                    cancellationToken);

                await transaccion.CommitAsync(
                    cancellationToken);

                FuenteNutrienteAdminDto? respuesta =
                    await ObtenerDtoAsync(
                        id,
                        cancellationToken);

                return Ok(
                    Exito(
                        respuesta,
                        "Fuente de nutriente reactivada y actualizada correctamente."));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);
                throw;
            }
            catch (DbUpdateException ex)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);

                logger.LogWarning(
                    ex,
                    "Conflicto al reactivar con datos la fuente {Id}.",
                    id);

                return Conflict(
                    Error(
                        "No fue posible reactivar la fuente por un conflicto con los datos actuales."));
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync(
                    CancellationToken.None);

                logger.LogError(
                    ex,
                    "Error al reactivar con datos la fuente {Id}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "Ocurrió un error inesperado al reactivar la fuente de nutriente."));
            }
        }

        // ==========================================================
        // CONSULTAS Y PROYECCIÓN
        // ==========================================================
        private IQueryable<FuenteNutriente> ConstruirConsultaBase(
            bool activo,
            string? buscar,
            string? categoria)
        {
            IQueryable<FuenteNutriente> query =
                db.fuenteNutriente
                    .AsNoTracking()
                    .Where(x => x.activo == activo);

            string texto =
                NormalizarBusqueda(
                    buscar);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                query = query.Where(x =>
                    x.nombreNutriente.Contains(texto) ||
                    x.descripcionNutriente.Contains(texto));
            }

            if (!activo)
                return query;

            string categoriaNormalizada =
                NormalizarCategoriaFiltro(
                    categoria);

            return categoriaNormalizada switch
            {
                CategoriaEnmienda =>
                    query.Where(x =>
                        db.ParametroEnmiendaCalcarea.Any(config =>
                            config.fuenteNutrientesId ==
                                x.fuenteNutrientesId &&
                            config.activo)),

                CategoriaMixta =>
                    query.Where(x =>
                        db.fuenteFertilizacionMixta.Any(config =>
                            config.fuenteNutrientesId ==
                                x.fuenteNutrientesId &&
                            config.activo)),

                CategoriaBalance =>
                    query.Where(x =>
                        !db.ParametroEnmiendaCalcarea.Any(config =>
                            config.fuenteNutrientesId ==
                                x.fuenteNutrientesId &&
                            config.activo) &&
                        !db.fuenteFertilizacionMixta.Any(config =>
                            config.fuenteNutrientesId ==
                                x.fuenteNutrientesId &&
                            config.activo)),

                _ => query
            };
        }

        private async Task<List<FuenteNutrienteAdminDto>>
            ProyectarPaginaAsync(
                List<int> ids,
                CancellationToken cancellationToken)
        {
            if (ids.Count == 0)
                return new List<FuenteNutrienteAdminDto>();

            List<FuenteNutrienteAdminDto> data =
                await Proyectar(
                        db.fuenteNutriente
                            .AsNoTracking()
                            .Where(x =>
                                ids.Contains(
                                    x.fuenteNutrientesId)))
                    .ToListAsync(
                        cancellationToken);

            Dictionary<int, int> posiciones =
                ids.Select(
                        (id, indice) =>
                            new
                            {
                                id,
                                indice
                            })
                    .ToDictionary(
                        x => x.id,
                        x => x.indice);

            return data
                .OrderBy(x =>
                    posiciones.TryGetValue(
                        x.FuenteNutrientesId,
                        out int posicion)
                            ? posicion
                            : int.MaxValue)
                .ToList();
        }

        private Task<FuenteNutrienteAdminDto?> ObtenerDtoAsync(
            int id,
            CancellationToken cancellationToken) =>
            Proyectar(
                    db.fuenteNutriente
                        .AsNoTracking()
                        .Where(x =>
                            x.fuenteNutrientesId == id))
                .SingleOrDefaultAsync(
                    cancellationToken);

        private IQueryable<FuenteNutrienteAdminDto> Proyectar(
            IQueryable<FuenteNutriente> query) =>
            query.Select(x =>
                new FuenteNutrienteAdminDto
                {
                    FuenteNutrientesId =
                        x.fuenteNutrientesId,
                    NombreNutriente =
                        x.nombreNutriente,
                    DescripcionNutriente =
                        x.descripcionNutriente,
                    PrecioNutriente =
                        x.precioNutriente,
                    Activo =
                        x.activo,
                    HabilitadaEnmiendaCalcarea =
                        db.ParametroEnmiendaCalcarea.Any(config =>
                            config.fuenteNutrientesId ==
                                x.fuenteNutrientesId &&
                            config.activo),
                    HabilitadaFertilizacionMixta =
                        db.fuenteFertilizacionMixta.Any(config =>
                            config.fuenteNutrientesId ==
                                x.fuenteNutrientesId &&
                            config.activo),
                    Prnt =
                        db.ParametroEnmiendaCalcarea
                            .Where(config =>
                                config.fuenteNutrientesId ==
                                    x.fuenteNutrientesId &&
                                config.activo)
                            .Select(config =>
                                (decimal?)config.prnt)
                            .FirstOrDefault(),
                    DescripcionParametro =
                        db.ParametroEnmiendaCalcarea
                            .Where(config =>
                                config.fuenteNutrientesId ==
                                    x.fuenteNutrientesId &&
                                config.activo)
                            .Select(config =>
                                config.descripcionParametro)
                            .FirstOrDefault(),
                    ElementosQuimicos =
                        x.fuenteNutrienteElementoQuimico
                            .Where(relacion =>
                                relacion.activo)
                            .OrderBy(relacion =>
                                relacion.elementoQuimico != null
                                    ? relacion.elementoQuimico
                                        .nombreElementoQuimico
                                    : string.Empty)
                            .Select(relacion =>
                                new FuenteNutrienteElementoAdminDto
                                {
                                    FuenteNutrienteElementoQuimicoId =
                                        relacion
                                            .fuenteNutrienteElementoQuimicoId,
                                    ElementoQuimicosId =
                                        relacion.elementoQuimicosId,
                                    NombreElementoQuimico =
                                        relacion.elementoQuimico != null
                                            ? relacion.elementoQuimico
                                                .nombreElementoQuimico
                                            : string.Empty,
                                    SimboloElementoQuimico =
                                        relacion.elementoQuimico != null
                                            ? relacion.elementoQuimico
                                                .simboloElementoQuimico
                                            : string.Empty,
                                    CantidadAporte =
                                        relacion.cantidadAporte
                                })
                            .ToList()
                });

        // ==========================================================
        // VALIDACIÓN Y CONFIGURACIÓN ATÓMICA
        // ==========================================================
        private async Task<IActionResult?> ValidarDatosAsync(
            DatosNormalizados datos,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(datos.Nombre))
            {
                return BadRequest(
                    Error(
                        "El nombre de la fuente es obligatorio."));
            }

            if (datos.Nombre.Length > 100)
            {
                return BadRequest(
                    Error(
                        "El nombre de la fuente no puede superar 100 caracteres."));
            }

            if (datos.Descripcion.Length > 250)
            {
                return BadRequest(
                    Error(
                        "La descripción no puede superar 250 caracteres."));
            }

            if (datos.Precio <= 0)
            {
                return BadRequest(
                    Error(
                        "El precio por quintal debe ser mayor a cero."));
            }

            if (datos.Categoria is not
                CategoriaBalance and not
                CategoriaEnmienda and not
                CategoriaMixta)
            {
                return BadRequest(
                    Error(
                        "La clasificación seleccionada no es válida."));
            }

            if (datos.Categoria == CategoriaEnmienda)
            {
                if (datos.Prnt is not > 0)
                {
                    return BadRequest(
                        Error(
                            "El PRNT debe ser mayor a cero para una enmienda calcárea."));
                }

                if (string.IsNullOrWhiteSpace(
                        datos.DescripcionParametro))
                {
                    return BadRequest(
                        Error(
                            "La descripción del parámetro es obligatoria para una enmienda calcárea."));
                }

                if (datos.DescripcionParametro.Length > 200)
                {
                    return BadRequest(
                        Error(
                            "La descripción del parámetro no puede superar 200 caracteres."));
                }

                return null;
            }

            if (datos.Elementos.Count == 0)
            {
                return BadRequest(
                    Error(
                        "Debe agregar al menos un aporte de elemento químico para la clasificación seleccionada."));
            }

            if (datos.Elementos
                .GroupBy(x => x.ElementoQuimicosId)
                .Any(grupo => grupo.Count() > 1))
            {
                return BadRequest(
                    Error(
                        "No puede repetir el mismo elemento químico en una fuente."));
            }

            if (datos.Elementos.Any(x =>
                    x.ElementoQuimicosId <= 0 ||
                    x.CantidadAporte <= 0 ||
                    x.CantidadAporte > 100))
            {
                return BadRequest(
                    Error(
                        "Todos los aportes deben usar un elemento válido y un porcentaje mayor a 0 y menor o igual a 100."));
            }

            decimal total =
                datos.Elementos.Sum(x =>
                    x.CantidadAporte);

            if (total > 100)
            {
                return BadRequest(
                    Error(
                        "La suma de los aportes químicos no puede superar el 100%."));
            }

            int[] ids =
                datos.Elementos
                    .Select(x => x.ElementoQuimicosId)
                    .Distinct()
                    .ToArray();

            int existentes =
                await db.elementoQuimico.CountAsync(
                    x =>
                        ids.Contains(x.elementoQuimicosId) &&
                        x.activo,
                    cancellationToken);

            if (existentes != ids.Length)
            {
                return BadRequest(
                    Error(
                        "Uno o más elementos químicos no existen o están inactivos."));
            }

            return null;
        }

        private async Task AplicarConfiguracionCompletaAsync(
            int fuenteId,
            DatosNormalizados datos,
            CancellationToken cancellationToken)
        {
            List<FuenteNutrienteElementoQuimico> aportesActuales =
                await db.fuenteNutrienteElementoQuimico
                    .Where(x =>
                        x.fuenteNutrientesId == fuenteId &&
                        x.activo)
                    .ToListAsync(
                        cancellationToken);

            foreach (FuenteNutrienteElementoQuimico aporte
                     in aportesActuales)
            {
                aporte.activo = false;
            }

            List<ParametroEnmiendaCalcarea> enmiendas =
                await db.ParametroEnmiendaCalcarea
                    .Where(x =>
                        x.fuenteNutrientesId == fuenteId)
                    .ToListAsync(
                        cancellationToken);

            foreach (ParametroEnmiendaCalcarea enmienda
                     in enmiendas)
            {
                enmienda.activo = false;
            }

            List<FuenteFertilizacionMixta> mixtas =
                await db.fuenteFertilizacionMixta
                    .Where(x =>
                        x.fuenteNutrientesId == fuenteId)
                    .ToListAsync(
                        cancellationToken);

            foreach (FuenteFertilizacionMixta mixta
                     in mixtas)
            {
                mixta.activo = false;
            }

            if (datos.Categoria is CategoriaBalance or CategoriaMixta)
            {
                List<FuenteNutrienteElementoQuimico> nuevosAportes =
                    datos.Elementos.Select(x =>
                        new FuenteNutrienteElementoQuimico
                        {
                            fuenteNutrientesId = fuenteId,
                            elementoQuimicosId = x.ElementoQuimicosId,
                            cantidadAporte = x.CantidadAporte,
                            activo = true
                        })
                    .ToList();

                db.fuenteNutrienteElementoQuimico
                    .AddRange(nuevosAportes);
            }

            if (datos.Categoria == CategoriaEnmienda)
            {
                ParametroEnmiendaCalcarea parametro =
                    enmiendas.FirstOrDefault() ??
                    new ParametroEnmiendaCalcarea
                    {
                        fuenteNutrientesId = fuenteId,
                        saturacionBasesDeseada = 70,
                        factorTonHaALbHa = 2200,
                        factorHaAMz = 0.7026m,
                        factorTonHaAKgHa = 1000
                    };

                parametro.prnt = datos.Prnt!.Value;
                parametro.descripcionParametro =
                    datos.DescripcionParametro;
                parametro.activo = true;

                if (parametro.parametroEnmiendaCalcareaId <= 0)
                {
                    db.ParametroEnmiendaCalcarea.Add(parametro);
                }
            }

            if (datos.Categoria == CategoriaMixta)
            {
                FuenteFertilizacionMixta configuracion =
                    mixtas.FirstOrDefault() ??
                    new FuenteFertilizacionMixta
                    {
                        fuenteNutrientesId = fuenteId
                    };

                configuracion.activo = true;

                if (configuracion.fuenteFertilizacionMixtaId <= 0)
                {
                    db.fuenteFertilizacionMixta.Add(configuracion);
                }
            }
        }

        private async Task<bool> ExisteActivaConNombreAsync(
            string nombre,
            int? idExcluir,
            CancellationToken cancellationToken) =>
            await db.fuenteNutriente
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.activo &&
                        (!idExcluir.HasValue ||
                         x.fuenteNutrientesId != idExcluir.Value) &&
                        x.nombreNutriente.Trim().ToUpper() == nombre,
                    cancellationToken);

        private Task<FuenteNutriente?> BuscarInactivaConNombreAsync(
            string nombre,
            int? idExcluir,
            CancellationToken cancellationToken) =>
            db.fuenteNutriente
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        !x.activo &&
                        (!idExcluir.HasValue ||
                         x.fuenteNutrientesId != idExcluir.Value) &&
                        x.nombreNutriente.Trim().ToUpper() == nombre,
                    cancellationToken);

        private static DatosNormalizados NormalizarDatos(
            FuenteNutrienteGuardarDto dto)
        {
            string categoria =
                NormalizarCategoriaGuardado(
                    dto.Categoria);

            List<AporteNormalizado> elementos =
                categoria == CategoriaEnmienda
                    ? new List<AporteNormalizado>()
                    : (dto.ElementosQuimicos ??
                       new List<FuenteNutrienteElementoGuardarDto>())
                        .Select(x =>
                            new AporteNormalizado(
                                x.ElementoQuimicosId,
                                x.CantidadAporte))
                        .ToList();

            return new DatosNormalizados(
                NormalizarNombre(dto.NombreNutriente),
                NormalizarDescripcion(dto.DescripcionNutriente),
                dto.PrecioNutriente,
                categoria,
                dto.Prnt,
                NormalizarDescripcion(dto.DescripcionParametro),
                elementos);
        }

        private async Task<IActionResult?> ValidarAccesoAsync(
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado =
                await permisos.ValidarAsync(
                    ObtenerUsuarioId(),
                    PermisoInterfaz,
                    tipo,
                    cancellationToken);

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                Error(resultado.Mensaje));
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(
                valor,
                out int id) &&
                id > 0
                    ? id
                    : null;
        }

        private static string NormalizarBusqueda(
            string? valor)
        {
            string texto =
                (valor ?? string.Empty)
                    .ReplaceLineEndings(" ")
                    .Trim();

            return texto.Length > 100
                ? texto[..100]
                : texto;
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
                .Trim();

        private static string NormalizarCategoriaFiltro(
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

        private static string NormalizarCategoriaGuardado(
            string? valor) =>
            (valor ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

        private static object Error(string mensaje) =>
            new
            {
                success = false,
                message = mensaje
            };

        private static object Exito<T>(
            T data,
            string mensaje) =>
            new
            {
                success = true,
                message = mensaje,
                data
            };

        private static object ConflictoInactivo(
            FuenteNutrienteAdminDto? data,
            string mensaje) =>
            new
            {
                success = false,
                code = CodigoInactivoExistente,
                message = mensaje,
                data
            };

        private readonly record struct AporteNormalizado(
            int ElementoQuimicosId,
            decimal CantidadAporte);

        private readonly record struct DatosNormalizados(
            string Nombre,
            string Descripcion,
            decimal Precio,
            string Categoria,
            decimal? Prnt,
            string DescripcionParametro,
            List<AporteNormalizado> Elementos);

        public sealed class FuenteNutrienteGuardarDto
        {
            public int? FuenteNutrientesId { get; set; }
            public string NombreNutriente { get; set; } = string.Empty;
            public string DescripcionNutriente { get; set; } = string.Empty;
            public decimal PrecioNutriente { get; set; }
            public string Categoria { get; set; } = CategoriaBalance;
            public decimal? Prnt { get; set; }
            public string? DescripcionParametro { get; set; }
            public List<FuenteNutrienteElementoGuardarDto> ElementosQuimicos { get; set; } =
                new();
        }

        public sealed class FuenteNutrienteElementoGuardarDto
        {
            public int ElementoQuimicosId { get; set; }
            public decimal CantidadAporte { get; set; }
        }

        public sealed class FuenteNutrienteAdminDto
        {
            public int FuenteNutrientesId { get; set; }
            public string NombreNutriente { get; set; } = string.Empty;
            public string DescripcionNutriente { get; set; } = string.Empty;
            public decimal PrecioNutriente { get; set; }
            public bool Activo { get; set; }
            public bool HabilitadaEnmiendaCalcarea { get; set; }
            public bool HabilitadaFertilizacionMixta { get; set; }
            public decimal? Prnt { get; set; }
            public string? DescripcionParametro { get; set; }
            public List<FuenteNutrienteElementoAdminDto> ElementosQuimicos { get; set; } =
                new();
        }

        public sealed class FuenteNutrienteElementoAdminDto
        {
            public int FuenteNutrienteElementoQuimicoId { get; set; }
            public int ElementoQuimicosId { get; set; }
            public string NombreElementoQuimico { get; set; } = string.Empty;
            public string SimboloElementoQuimico { get; set; } = string.Empty;
            public decimal CantidadAporte { get; set; }
        }

        public sealed class PaginaRespuesta<T>
        {
            public List<T> Items { get; set; } = new();
            public int PaginaActual { get; set; }
            public int TamanoPagina { get; set; }
            public int TotalRegistros { get; set; }
            public int TotalPaginas { get; set; }

            public static PaginaRespuesta<T> Crear(
                List<T> items,
                int pagina,
                int tamanoPagina,
                int total) =>
                new()
                {
                    Items = items,
                    PaginaActual = pagina,
                    TamanoPagina = tamanoPagina,
                    TotalRegistros = total,
                    TotalPaginas = total == 0
                        ? 1
                        : (int)Math.Ceiling(
                            total /
                            (double)tamanoPagina)
                };
        }
    }
}
