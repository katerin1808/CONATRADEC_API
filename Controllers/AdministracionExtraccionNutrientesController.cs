using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using static CONATRADEC_API.DTOs.ParametroExtraccionNutrienteDto;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// API administrativa protegida para Extracción de nutrientes.
    ///
    /// Los controladores históricos permanecen disponibles para no afectar
    /// versiones anteriores ni consumidores existentes. Esta API se utiliza
    /// por la interfaz administrativa actual para listado paginado, detalle,
    /// edición y eliminación con permisos verificados en servidor.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/administracion/extraccion-nutrientes")]
    public sealed class AdministracionExtraccionNutrientesController :
        ControllerBase
    {
        private const string PermisoInterfaz =
            "extraccionNutrientePage";

        private readonly DBContext db;
        private readonly PermisoApiService permisos;
        private readonly ILogger<AdministracionExtraccionNutrientesController>
            logger;

        public AdministracionExtraccionNutrientesController(
            DBContext db,
            PermisoApiService permisos,
            ILogger<AdministracionExtraccionNutrientesController> logger)
        {
            this.db = db;
            this.permisos = permisos;
            this.logger = logger;
        }

        // ==========================================================
        // LISTADO ADMINISTRATIVO PAGINADO
        // ==========================================================
        [HttpGet]
        public async Task<IActionResult> Listar(
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

            string texto =
                NormalizarBusqueda(buscar);

            IQueryable<ParametroExtraccionNutrienteCafe> query =
                db.ParametroExtraccionNutrienteCafe
                    .AsNoTracking()
                    .Where(x => x.activo);

            if (!string.IsNullOrWhiteSpace(texto))
            {
                query = query.Where(x =>
                    x.ElementoQuimico.nombreElementoQuimico.Contains(texto) ||
                    x.ElementoQuimico.simboloElementoQuimico.Contains(texto) ||
                    x.descripcionParametro.Contains(texto));
            }

            /*
             * El orden se aplica antes de Skip/Take. El ID actúa como
             * desempate estable para que una página no cambie entre consultas.
             */
            query = query
                .OrderBy(x => x.ElementoQuimico.nombreElementoQuimico)
                .ThenBy(x => x.parametroExtraccionNutrienteCafeId);

            int totalRegistros =
                await query.CountAsync(
                    cancellationToken);

            List<ParametroExtraccionNutrienteConsultaDto> items =
                await query
                    .Skip(
                        (pagina - 1) *
                        tamanoPagina)
                    .Take(tamanoPagina)
                    .Select(x =>
                        new ParametroExtraccionNutrienteConsultaDto
                        {
                            parametroExtraccionNutrienteCafeId =
                                x.parametroExtraccionNutrienteCafeId,
                            elementoQuimicosId =
                                x.elementoQuimicosId,
                            nombreElementoQuimico =
                                x.ElementoQuimico.nombreElementoQuimico,
                            simboloElementoQuimico =
                                x.ElementoQuimico.simboloElementoQuimico,
                            cantidadExtraidaPorQQOro =
                                x.cantidadExtraidaPorQQOro,
                            descripcionParametro =
                                x.descripcionParametro,
                            activo =
                                x.activo
                        })
                    .ToListAsync(
                        cancellationToken);

            int totalPaginas =
                totalRegistros == 0
                    ? 1
                    : (int)Math.Ceiling(
                        totalRegistros /
                        (double)tamanoPagina);

            return Ok(
                new ParametroExtraccionNutrientePaginaResponse
                {
                    Items = items,
                    PaginaActual = pagina,
                    TamanoPagina = tamanoPagina,
                    TotalRegistros = totalRegistros,
                    TotalPaginas = totalPaginas
                });
        }

        // ==========================================================
        // LISTA COMPLETA PARA SELECTORES DEL FORMULARIO
        // ==========================================================
        [HttpGet("todos")]
        public async Task<IActionResult> ListarTodos(
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            List<ParametroExtraccionNutrienteConsultaDto> data =
                await db.ParametroExtraccionNutrienteCafe
                    .AsNoTracking()
                    .Where(x => x.activo)
                    .OrderBy(x => x.ElementoQuimico.nombreElementoQuimico)
                    .ThenBy(x => x.parametroExtraccionNutrienteCafeId)
                    .Select(x =>
                        new ParametroExtraccionNutrienteConsultaDto
                        {
                            parametroExtraccionNutrienteCafeId =
                                x.parametroExtraccionNutrienteCafeId,
                            elementoQuimicosId =
                                x.elementoQuimicosId,
                            nombreElementoQuimico =
                                x.ElementoQuimico.nombreElementoQuimico,
                            simboloElementoQuimico =
                                x.ElementoQuimico.simboloElementoQuimico,
                            cantidadExtraidaPorQQOro =
                                x.cantidadExtraidaPorQQOro,
                            descripcionParametro =
                                x.descripcionParametro,
                            activo =
                                x.activo
                        })
                    .ToListAsync(
                        cancellationToken);

            return Ok(data);
        }

        // ==========================================================
        // DETALLE ADMINISTRATIVO ACTUAL
        // ==========================================================
        [HttpGet("{id:int}")]
        public async Task<IActionResult> ObtenerPorId(
            int id,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (id <= 0)
            {
                return BadRequest(
                    Error(
                        "El identificador del parámetro de extracción no es válido."));
            }

            ParametroExtraccionNutrienteConsultaDto? data =
                await db.ParametroExtraccionNutrienteCafe
                    .AsNoTracking()
                    .Where(x =>
                        x.parametroExtraccionNutrienteCafeId == id &&
                        x.activo)
                    .Select(x =>
                        new ParametroExtraccionNutrienteConsultaDto
                        {
                            parametroExtraccionNutrienteCafeId =
                                x.parametroExtraccionNutrienteCafeId,
                            elementoQuimicosId =
                                x.elementoQuimicosId,
                            nombreElementoQuimico =
                                x.ElementoQuimico.nombreElementoQuimico,
                            simboloElementoQuimico =
                                x.ElementoQuimico.simboloElementoQuimico,
                            cantidadExtraidaPorQQOro =
                                x.cantidadExtraidaPorQQOro,
                            descripcionParametro =
                                x.descripcionParametro,
                            activo =
                                x.activo
                        })
                    .SingleOrDefaultAsync(
                        cancellationToken);

            if (data == null)
            {
                return NotFound(
                    Error(
                        "El parámetro de extracción no existe o está inactivo."));
            }

            return Ok(data);
        }

        // ==========================================================
        // CREAR
        // ==========================================================
        [HttpPost]
        public async Task<IActionResult> Crear(
            [FromBody] CrearParametroExtraccionNutrienteCafeDto? dto,
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
                        "No se recibieron los datos del parámetro de extracción."));
            }

            string descripcion =
                NormalizarDescripcion(
                    dto.descripcionParametro);

            IActionResult? validacion =
                ValidarDatos(
                    dto.elementoQuimicosId,
                    dto.cantidadExtraidaPorQQOro,
                    descripcion);

            if (validacion != null)
                return validacion;

            ElementoQuimico? elemento =
                await db.elementoQuimico
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.elementoQuimicosId == dto.elementoQuimicosId &&
                            x.activo,
                        cancellationToken);

            if (elemento == null)
            {
                return BadRequest(
                    Error(
                        "El elemento químico no existe o está inactivo."));
            }

            bool existeActivo =
                await db.ParametroExtraccionNutrienteCafe
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.elementoQuimicosId == dto.elementoQuimicosId &&
                            x.activo,
                        cancellationToken);

            if (existeActivo)
            {
                return Conflict(
                    Error(
                        "Ya existe un parámetro de extracción activo para este elemento químico."));
            }

            var entidad =
                new ParametroExtraccionNutrienteCafe
                {
                    elementoQuimicosId =
                        dto.elementoQuimicosId,
                    cantidadExtraidaPorQQOro =
                        dto.cantidadExtraidaPorQQOro,
                    descripcionParametro =
                        descripcion,
                    activo =
                        true
                };

            try
            {
                db.ParametroExtraccionNutrienteCafe.Add(entidad);

                await db.SaveChangesAsync(
                    cancellationToken);

                return StatusCode(
                    StatusCodes.Status201Created,
                    Exito(
                        Proyectar(
                            entidad,
                            elemento),
                        "Parámetro de extracción creado correctamente."));
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al crear el parámetro de extracción para el elemento {ElementoId}.",
                    dto.elementoQuimicosId);

                return Conflict(
                    Error(
                        "No fue posible crear el parámetro de extracción porque existe un registro incompatible."));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al crear un parámetro de extracción.");

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "Ocurrió un error inesperado al crear el parámetro de extracción."));
            }
        }

        // ==========================================================
        // EDITAR
        // ==========================================================
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Actualizar(
            int id,
            [FromBody] ActualizarParametroExtraccionNutrienteCafeDto? dto,
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
                return BadRequest(
                    Error(
                        "El identificador del parámetro de extracción no es válido."));
            }

            if (dto == null)
            {
                return BadRequest(
                    Error(
                        "No se recibieron los datos del parámetro de extracción."));
            }

            string descripcion =
                NormalizarDescripcion(
                    dto.descripcionParametro);

            IActionResult? validacion =
                ValidarDatos(
                    dto.elementoQuimicosId,
                    dto.cantidadExtraidaPorQQOro,
                    descripcion);

            if (validacion != null)
                return validacion;

            ParametroExtraccionNutrienteCafe? entidad =
                await db.ParametroExtraccionNutrienteCafe
                    .FirstOrDefaultAsync(
                        x =>
                            x.parametroExtraccionNutrienteCafeId == id &&
                            x.activo,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(
                    Error(
                        "El parámetro de extracción no existe o está inactivo."));
            }

            ElementoQuimico? elemento =
                await db.elementoQuimico
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x =>
                            x.elementoQuimicosId == dto.elementoQuimicosId &&
                            x.activo,
                        cancellationToken);

            if (elemento == null)
            {
                return BadRequest(
                    Error(
                        "El elemento químico no existe o está inactivo."));
            }

            bool existeOtro =
                await db.ParametroExtraccionNutrienteCafe
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.parametroExtraccionNutrienteCafeId != id &&
                            x.elementoQuimicosId == dto.elementoQuimicosId &&
                            x.activo,
                        cancellationToken);

            if (existeOtro)
            {
                return Conflict(
                    Error(
                        "Ya existe otro parámetro activo para este elemento químico."));
            }

            entidad.elementoQuimicosId =
                dto.elementoQuimicosId;
            entidad.cantidadExtraidaPorQQOro =
                dto.cantidadExtraidaPorQQOro;
            entidad.descripcionParametro =
                descripcion;

            try
            {
                await db.SaveChangesAsync(
                    cancellationToken);

                return Ok(
                    Exito(
                        Proyectar(
                            entidad,
                            elemento),
                        "Parámetro de extracción actualizado correctamente."));
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(
                    ex,
                    "Conflicto al actualizar el parámetro de extracción {ParametroId}.",
                    id);

                return Conflict(
                    Error(
                        "No fue posible actualizar el parámetro de extracción porque existe un registro incompatible."));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al actualizar el parámetro de extracción {ParametroId}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "Ocurrió un error inesperado al actualizar el parámetro de extracción."));
            }
        }

        // ==========================================================
        // ELIMINACIÓN LÓGICA
        // ==========================================================
        [HttpPut("{id:int}/eliminar")]
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

            if (id <= 0)
            {
                return BadRequest(
                    Error(
                        "El identificador del parámetro de extracción no es válido."));
            }

            ParametroExtraccionNutrienteCafe? entidad =
                await db.ParametroExtraccionNutrienteCafe
                    .FirstOrDefaultAsync(
                        x =>
                            x.parametroExtraccionNutrienteCafeId == id &&
                            x.activo,
                        cancellationToken);

            if (entidad == null)
            {
                return NotFound(
                    Error(
                        "El parámetro de extracción no existe o ya está eliminado."));
            }

            entidad.activo = false;

            try
            {
                await db.SaveChangesAsync(
                    cancellationToken);

                return Ok(
                    Exito(
                        "Parámetro de extracción eliminado correctamente."));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al eliminar el parámetro de extracción {ParametroId}.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "Ocurrió un error inesperado al eliminar el parámetro de extracción."));
            }
        }

        private IActionResult? ValidarDatos(
            int elementoId,
            decimal cantidad,
            string descripcion)
        {
            if (elementoId <= 0)
            {
                return BadRequest(
                    Error(
                        "Seleccione un elemento químico válido."));
            }

            if (cantidad <= 0)
            {
                return BadRequest(
                    Error(
                        "La cantidad extraída debe ser mayor que cero."));
            }

            if (string.IsNullOrWhiteSpace(descripcion))
            {
                return BadRequest(
                    Error(
                        "La descripción del parámetro es obligatoria."));
            }

            if (descripcion.Length > 150)
            {
                return BadRequest(
                    Error(
                        "La descripción no puede superar 150 caracteres."));
            }

            return null;
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

            return texto.Length > 150
                ? texto[..150]
                : texto;
        }

        private static string NormalizarDescripcion(
            string? valor) =>
            (valor ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim();

        private static ParametroExtraccionNutrienteConsultaDto Proyectar(
            ParametroExtraccionNutrienteCafe entidad,
            ElementoQuimico elemento) =>
            new()
            {
                parametroExtraccionNutrienteCafeId =
                    entidad.parametroExtraccionNutrienteCafeId,
                elementoQuimicosId =
                    entidad.elementoQuimicosId,
                nombreElementoQuimico =
                    elemento.nombreElementoQuimico,
                simboloElementoQuimico =
                    elemento.simboloElementoQuimico,
                cantidadExtraidaPorQQOro =
                    entidad.cantidadExtraidaPorQQOro,
                descripcionParametro =
                    entidad.descripcionParametro,
                activo =
                    entidad.activo
            };

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
