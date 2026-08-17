using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using static CONATRADEC_API.DTOs.ParametroRangoNutrienteCultivoDto;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// API administrativa optimizada para Rangos nutricionales.
    ///
    /// Los endpoints históricos de Tipos de cultivo y de Rangos permanecen
    /// disponibles para compatibilidad. Esta API concentra la consulta
    /// paginada y el CRUD de cultivos/rangos utilizado por la interfaz
    /// moderna, incluyendo permisos funcionales del lado del servidor.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/configuracion/rangos-nutrientes")]
    public sealed class RangoNutrienteConsultaController : ControllerBase
    {
        private const string PermisoInterfaz =
            "rangoNutrientePage";

        private const string UnidadApi = "lb/Mz";
        private const string UnidadInterna = "kg/Ha";
        private const decimal FactorKgHaALbMz = 1.54m;

        private readonly DBContext db;
        private readonly PermisoApiService permisos;
        private readonly ILogger<RangoNutrienteConsultaController> logger;

        public RangoNutrienteConsultaController(
            DBContext db,
            PermisoApiService permisos,
            ILogger<RangoNutrienteConsultaController> logger)
        {
            this.db = db;
            this.permisos = permisos;
            this.logger = logger;
        }

        // ==========================================================
        // CULTIVOS - LISTADO PAGINADO DEL MÓDULO
        // ==========================================================
        [HttpGet("cultivos")]
        public async Task<IActionResult> BuscarCultivos(
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

            /*
             * El identificador completa el orden para que Skip/Take produzca
             * páginas estables aun cuando existan nombres repetidos.
             */
            consulta = consulta
                .OrderBy(item => item.nombreTipoCultivo)
                .ThenBy(item => item.tipoCultivoId);

            int totalRegistros =
                await consulta.CountAsync(cancellationToken);

            int totalPaginas =
                CalcularTotalPaginas(
                    totalRegistros,
                    tamanoPagina);

            if (totalPaginas == 0)
            {
                pagina = 1;
            }
            else if (pagina > totalPaginas)
            {
                pagina = totalPaginas;
            }

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
                TotalPaginas = totalPaginas
            });
        }

        // ==========================================================
        // CULTIVOS - CRUD PROTEGIDO EXCLUSIVO DE RANGOS
        // Los endpoints históricos de tipos-cultivo no se modifican.
        // ==========================================================
        [HttpPost("cultivos")]
        public async Task<IActionResult> CrearCultivo(
            [FromBody] CrearTipoCultivoDto? request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (request == null)
            {
                return BadRequest(Error(
                    "No se recibieron los datos del tipo de cultivo."));
            }

            string nombre =
                NormalizarNombreCultivo(
                    request.nombreTipoCultivo);

            string descripcion =
                NormalizarDescripcionCultivo(
                    request.descripcionTipoCultivo);

            IActionResult? validacion =
                ValidarDatosCultivo(
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

            if (existente != null && existente.activo)
            {
                return Conflict(Error(
                    "Ya existe un tipo de cultivo activo con ese nombre."));
            }

            try
            {
                if (existente != null)
                {
                    existente.nombreTipoCultivo = nombre;
                    existente.descripcionTipoCultivo = descripcion;
                    existente.activo = true;

                    await db.SaveChangesAsync(cancellationToken);

                    return Ok(Exito(
                        CrearRespuestaCultivo(existente),
                        "Tipo de cultivo reactivado correctamente."));
                }

                var entity = new TipoCultivo
                {
                    nombreTipoCultivo = nombre,
                    descripcionTipoCultivo = descripcion,
                    activo = true
                };

                db.TipoCultivos.Add(entity);
                await db.SaveChangesAsync(cancellationToken);

                return StatusCode(
                    StatusCodes.Status201Created,
                    Exito(
                        CrearRespuestaCultivo(entity),
                        "Tipo de cultivo creado correctamente."));
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al crear el tipo de cultivo {Nombre} desde Rangos nutricionales.",
                    nombre);

                return Conflict(Error(
                    "No fue posible crear el tipo de cultivo porque ya existe un registro activo con el mismo nombre."));
            }
        }

        [HttpPut("cultivos/{id:int}")]
        public async Task<IActionResult> ActualizarCultivo(
            int id,
            [FromBody] ActualizarTipoCultivoDto? request,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0)
            {
                return BadRequest(Error(
                    "El identificador del tipo de cultivo no es válido."));
            }

            if (request == null)
            {
                return BadRequest(Error(
                    "No se recibieron los datos del tipo de cultivo."));
            }

            string nombre =
                NormalizarNombreCultivo(
                    request.nombreTipoCultivo);

            string descripcion =
                NormalizarDescripcionCultivo(
                    request.descripcionTipoCultivo);

            IActionResult? validacion =
                ValidarDatosCultivo(
                    nombre,
                    descripcion);

            if (validacion != null)
                return validacion;

            TipoCultivo? entity =
                await db.TipoCultivos
                    .FirstOrDefaultAsync(
                        item => item.tipoCultivoId == id,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(Error(
                    "El tipo de cultivo indicado no existe."));
            }

            if (!entity.activo)
            {
                return Conflict(Error(
                    "No se puede actualizar un tipo de cultivo que está inactivo."));
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
                return Conflict(Error(
                    "Ya existe otro tipo de cultivo activo con ese nombre."));
            }

            entity.nombreTipoCultivo = nombre;
            entity.descripcionTipoCultivo = descripcion;

            try
            {
                await db.SaveChangesAsync(cancellationToken);

                return Ok(Exito(
                    CrearRespuestaCultivo(entity),
                    "Tipo de cultivo actualizado correctamente."));
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al actualizar el tipo de cultivo {TipoCultivoId} desde Rangos nutricionales.",
                    id);

                return Conflict(Error(
                    "No fue posible actualizar el tipo de cultivo porque ya existe un registro activo con el mismo nombre."));
            }
        }

        [HttpPut("cultivos/{id:int}/eliminar")]
        public async Task<IActionResult> EliminarCultivo(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Eliminar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0)
            {
                return BadRequest(Error(
                    "El identificador del tipo de cultivo no es válido."));
            }

            TipoCultivo? entity =
                await db.TipoCultivos
                    .FirstOrDefaultAsync(
                        item =>
                            item.tipoCultivoId == id &&
                            item.activo,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(Error(
                    "El tipo de cultivo no existe o ya está desactivado."));
            }

            var dependencias = new List<string>();

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
             * Se protege también el historial de análisis: un cultivo usado
             * previamente debe seguir disponible para consultar esos registros.
             */
            bool usadoEnAnalisis =
                await db.AnalisisSueloCalculos
                    .AsNoTracking()
                    .AnyAsync(
                        item => item.tipoCultivoId == id,
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
                    usadoEn = dependencias
                });
            }

            entity.activo = false;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(Exito(
                "Tipo de cultivo desactivado correctamente."));
        }

        // ==========================================================
        // RANGOS - CRUD PROTEGIDO EXCLUSIVO DEL MÓDULO
        // El CRUD histórico permanece en su controlador original.
        // ==========================================================
        [HttpPost("rangos")]
        public async Task<IActionResult> CrearRango(
            [FromBody] CrearParametroRangoNutrienteCultivoDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            string? error =
                await ValidarDatosRangoAsync(
                    dto.tipoCultivoId,
                    dto.elementoQuimicosId,
                    dto.valorMinimo,
                    dto.valorMaximo,
                    dto.unidadBase,
                    dto.descripcionParametro,
                    cancellationToken);

            if (error != null)
                return BadRequest(Error(error));

            ParametroRangoNutrienteCultivo? existente =
                await db.ParametroRangoNutrienteCultivo
                    .FirstOrDefaultAsync(
                        item =>
                            item.tipoCultivoId == dto.tipoCultivoId &&
                            item.elementoQuimicosId == dto.elementoQuimicosId,
                        cancellationToken);

            if (existente != null && existente.activo)
            {
                return Conflict(Error(
                    "Ya existe un rango activo para este tipo de cultivo y elemento químico."));
            }

            decimal minimoInterno =
                ConvertirEntradaAKgHa(
                    dto.valorMinimo,
                    dto.unidadBase);

            decimal maximoInterno =
                ConvertirEntradaAKgHa(
                    dto.valorMaximo,
                    dto.unidadBase);

            if (existente != null)
            {
                existente.valorMinimo = minimoInterno;
                existente.valorMaximo = maximoInterno;
                existente.unidadBase = UnidadInterna;
                existente.descripcionParametro =
                    dto.descripcionParametro.Trim();
                existente.activo = true;

                await db.SaveChangesAsync(cancellationToken);

                RangoNutrienteConsultaDto? data =
                    await ObtenerDetalleRangoAsync(
                        existente.parametroRangoNutrienteCultivoId,
                        cancellationToken);

                return Ok(Exito(
                    data,
                    "Rango de aporte reactivado correctamente."));
            }

            var entidad = new ParametroRangoNutrienteCultivo
            {
                tipoCultivoId = dto.tipoCultivoId,
                elementoQuimicosId = dto.elementoQuimicosId,
                valorMinimo = minimoInterno,
                valorMaximo = maximoInterno,
                unidadBase = UnidadInterna,
                descripcionParametro = dto.descripcionParametro.Trim(),
                activo = true
            };

            db.ParametroRangoNutrienteCultivo.Add(entidad);
            await db.SaveChangesAsync(cancellationToken);

            RangoNutrienteConsultaDto? creado =
                await ObtenerDetalleRangoAsync(
                    entidad.parametroRangoNutrienteCultivoId,
                    cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                Exito(
                    creado,
                    "Rango de aporte creado correctamente."));
        }

        [HttpPut("rangos/{id:int}")]
        public async Task<IActionResult> ActualizarRango(
            int id,
            [FromBody] ActualizarParametroRangoNutrienteCultivoDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0)
            {
                return BadRequest(Error(
                    "El identificador del rango nutricional no es válido."));
            }

            ParametroRangoNutrienteCultivo? entidad =
                await db.ParametroRangoNutrienteCultivo
                    .FirstOrDefaultAsync(
                        item =>
                            item.parametroRangoNutrienteCultivoId == id &&
                            item.activo,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(Error(
                    "Rango de aporte no encontrado."));
            }

            string? error =
                await ValidarDatosRangoAsync(
                    dto.tipoCultivoId,
                    dto.elementoQuimicosId,
                    dto.valorMinimo,
                    dto.valorMaximo,
                    dto.unidadBase,
                    dto.descripcionParametro,
                    cancellationToken);

            if (error != null)
                return BadRequest(Error(error));

            bool existeOtro =
                await db.ParametroRangoNutrienteCultivo
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.parametroRangoNutrienteCultivoId != id &&
                            item.tipoCultivoId == dto.tipoCultivoId &&
                            item.elementoQuimicosId == dto.elementoQuimicosId &&
                            item.activo,
                        cancellationToken);

            if (existeOtro)
            {
                return Conflict(Error(
                    "Ya existe otro rango activo para este tipo de cultivo y elemento químico."));
            }

            entidad.tipoCultivoId = dto.tipoCultivoId;
            entidad.elementoQuimicosId = dto.elementoQuimicosId;
            entidad.valorMinimo =
                ConvertirEntradaAKgHa(
                    dto.valorMinimo,
                    dto.unidadBase);
            entidad.valorMaximo =
                ConvertirEntradaAKgHa(
                    dto.valorMaximo,
                    dto.unidadBase);
            entidad.unidadBase = UnidadInterna;
            entidad.descripcionParametro =
                dto.descripcionParametro.Trim();

            await db.SaveChangesAsync(cancellationToken);

            RangoNutrienteConsultaDto? data =
                await ObtenerDetalleRangoAsync(
                    id,
                    cancellationToken);

            return Ok(Exito(
                data,
                "Rango de aporte actualizado correctamente."));
        }

        [HttpPut("rangos/{id:int}/eliminar")]
        public async Task<IActionResult> EliminarRango(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Eliminar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0)
            {
                return BadRequest(Error(
                    "El identificador del rango nutricional no es válido."));
            }

            ParametroRangoNutrienteCultivo? entidad =
                await db.ParametroRangoNutrienteCultivo
                    .FirstOrDefaultAsync(
                        item =>
                            item.parametroRangoNutrienteCultivoId == id,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(Error(
                    "Rango de aporte no encontrado."));
            }

            if (!entidad.activo)
            {
                return Conflict(Error(
                    "El rango de aporte ya se encuentra eliminado."));
            }

            entidad.activo = false;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(Exito(
                "Rango de aporte eliminado correctamente."));
        }

        // ==========================================================
        // RANGOS - LISTADO PAGINADO
        // ==========================================================
        [HttpGet("buscar")]
        public async Task<IActionResult> BuscarRangos(
            [FromQuery] int tipoCultivoId,
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

            if (tipoCultivoId <= 0)
            {
                return BadRequest(Error(
                    "El tipo de cultivo indicado no es válido."));
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

            int totalPaginas =
                CalcularTotalPaginas(
                    totalRegistros,
                    tamanoPagina);

            if (totalPaginas == 0)
            {
                pagina = 1;
            }
            else if (pagina > totalPaginas)
            {
                pagina = totalPaginas;
            }

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
                TotalPaginas = totalPaginas
            });
        }

        [HttpGet("elementos-disponibles")]
        public async Task<IActionResult> ObtenerElementosDisponibles(
            [FromQuery] int tipoCultivoId,
            [FromQuery] int parametroActualId = 0,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (tipoCultivoId <= 0)
            {
                return BadRequest(Error(
                    "El tipo de cultivo indicado no es válido."));
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
                return NotFound(Error(
                    "El tipo de cultivo no existe o está inactivo."));
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
                    .ThenBy(elemento =>
                        elemento.elementoQuimicosId)
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

        private async Task<string?> ValidarDatosRangoAsync(
            int tipoCultivoId,
            int elementoQuimicosId,
            decimal valorMinimo,
            decimal valorMaximo,
            string? unidadBase,
            string? descripcionParametro,
            CancellationToken cancellationToken)
        {
            if (tipoCultivoId <= 0)
                return "Debe seleccionar un tipo de cultivo válido.";

            if (elementoQuimicosId <= 0)
                return "Debe seleccionar un elemento químico válido.";

            if (valorMinimo <= 0)
                return "El valor mínimo debe ser mayor que cero.";

            if (valorMaximo <= valorMinimo)
                return "El valor máximo debe ser mayor que el valor mínimo.";

            if (!EsUnidadSoportada(unidadBase))
                return "La unidad base de los rangos debe ser lb/Mz.";

            string descripcion =
                (descripcionParametro ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(descripcion))
                return "La descripción es obligatoria.";

            if (descripcion.Length > 150)
                return "La descripción no puede superar 150 caracteres.";

            bool cultivoExiste =
                await db.TipoCultivos
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.tipoCultivoId == tipoCultivoId &&
                            item.activo,
                        cancellationToken);

            if (!cultivoExiste)
                return "El tipo de cultivo no existe o está inactivo.";

            bool elementoExiste =
                await db.elementoQuimico
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.elementoQuimicosId == elementoQuimicosId &&
                            item.activo,
                        cancellationToken);

            return elementoExiste
                ? null
                : "El elemento químico no existe o está inactivo.";
        }

        private async Task<RangoNutrienteConsultaDto?>
            ObtenerDetalleRangoAsync(
                int id,
                CancellationToken cancellationToken)
        {
            ParametroRangoNutrienteCultivo? entidad =
                await db.ParametroRangoNutrienteCultivo
                    .AsNoTracking()
                    .Include(item => item.TipoCultivo)
                    .Include(item => item.ElementoQuimico)
                    .FirstOrDefaultAsync(
                        item =>
                            item.parametroRangoNutrienteCultivoId == id,
                        cancellationToken);

            return entidad == null
                ? null
                : MapearRespuesta(entidad);
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

        private IActionResult? ValidarDatosCultivo(
            string nombre,
            string descripcion)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(Error(
                    "El nombre del tipo de cultivo es obligatorio."));
            }

            if (nombre.Length > 80)
            {
                return BadRequest(Error(
                    "El nombre del tipo de cultivo no puede superar 80 caracteres."));
            }

            if (descripcion.Length > 150)
            {
                return BadRequest(Error(
                    "La descripción no puede superar 150 caracteres."));
            }

            return null;
        }

        private static TipoCultivoRespuestaDto CrearRespuestaCultivo(
            TipoCultivo item) =>
            new()
            {
                tipoCultivoId = item.tipoCultivoId,
                nombreTipoCultivo = item.nombreTipoCultivo,
                tipoCultivo = item.nombreTipoCultivo,
                descripcionTipoCultivo = item.descripcionTipoCultivo,
                activo = item.activo
            };

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

        private static bool EsUnidadSoportada(
            string? unidad)
        {
            string normalizada =
                NormalizarUnidad(unidad);

            return
                normalizada == "LB/MZ" ||
                normalizada == "KG/HA";
        }

        private static decimal ConvertirEntradaAKgHa(
            decimal valor,
            string? unidad)
        {
            string normalizada =
                NormalizarUnidad(unidad);

            return normalizada == "KG/HA"
                ? Math.Round(valor, 4)
                : Math.Round(
                    valor / FactorKgHaALbMz,
                    4);
        }

        private static decimal ConvertirAlmacenadoALbMz(
            decimal valor,
            string? unidad)
        {
            string normalizada =
                NormalizarUnidad(unidad);

            return normalizada == "LB/MZ"
                ? valor
                : valor * FactorKgHaALbMz;
        }

        private static string NormalizarUnidad(
            string? unidad) =>
            (unidad ?? string.Empty)
                .Trim()
                .Replace(" ", string.Empty)
                .ToUpperInvariant();

        private static int CalcularTotalPaginas(
            int totalRegistros,
            int tamanoPagina) =>
            totalRegistros == 0
                ? 0
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

        private static string NormalizarNombreCultivo(
            string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();

        private static string NormalizarDescripcionCultivo(
            string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim();

        private static object Error(
            string mensaje) =>
            new
            {
                success = false,
                message = mensaje
            };

        private static object Exito(
            string mensaje) =>
            new
            {
                success = true,
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
    }
}
