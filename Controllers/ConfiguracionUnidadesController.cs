using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Administración de unidades permitidas y reglas de conversión
    /// utilizadas por el análisis de suelo.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/configuracion-unidades")]
    public sealed class ConfiguracionUnidadesController :
        ControllerBase
    {
        private const string PermisoAnterior =
            "elementoQuimicoPage";

        private readonly DBContext db;
        private readonly UnidadConversionService conversionService;
        private readonly PermisoApiService permisos;

        public ConfiguracionUnidadesController(
            DBContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;

            conversionService =
                new UnidadConversionService(db);
        }

        /// <summary>
        /// Esta ruta es parte del flujo operativo del análisis y permanece
        /// disponible para cualquier usuario autenticado. No es una pantalla
        /// administrativa.
        /// </summary>
        [HttpGet("formulario-analisis")]
        public async Task<IActionResult>
            ObtenerConfiguracionFormulario(
                CancellationToken cancellationToken)
        {
            ConfiguracionFormularioAnalisisDto resultado =
                await conversionService
                    .ObtenerConfiguracionFormularioAsync(
                        cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Configuración de unidades obtenida correctamente.",
                data = resultado
            });
        }

        [HttpGet("elementos")]
        public async Task<IActionResult> ListarElementos(
            [FromQuery] bool incluirInactivas = false,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            IQueryable<ElementoQuimico> query =
                db.elementoQuimico
                    .AsNoTracking();

            if (!incluirInactivas)
            {
                query = query.Where(
                    item => item.activo);
            }

            List<ElementoQuimico> elementos =
                await query
                    .OrderBy(item =>
                        item.nombreElementoQuimico)
                    .ToListAsync(cancellationToken);

            var respuesta =
                new List<ElementoConfiguracionUnidadesDto>();

            foreach (ElementoQuimico elemento in elementos)
            {
                ElementoConfiguracionUnidadesDto?
                    configuracion =
                        await conversionService
                            .ObtenerConfiguracionElementoAsync(
                                elemento.elementoQuimicosId,
                                incluirInactivas,
                                cancellationToken);

                if (configuracion != null)
                    respuesta.Add(configuracion);
            }

            return Ok(new
            {
                success = true,
                message =
                    "Configuraciones por elemento obtenidas correctamente.",
                data = respuesta
            });
        }

        [HttpGet("elemento/{elementoQuimicosId:int}")]
        public async Task<IActionResult> ObtenerElemento(
            int elementoQuimicosId,
            [FromQuery] bool incluirInactivas = true,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            ElementoConfiguracionUnidadesDto? resultado =
                await conversionService
                    .ObtenerConfiguracionElementoAsync(
                        elementoQuimicosId,
                        incluirInactivas,
                        cancellationToken);

            if (resultado == null)
            {
                return NotFound(new
                {
                    success = false,
                    message =
                        "El elemento químico no existe."
                });
            }

            return Ok(new
            {
                success = true,
                message =
                    "Configuración del elemento obtenida correctamente.",
                data = resultado
            });
        }

        [HttpPut("elemento/{elementoQuimicosId:int}")]
        public async Task<IActionResult> GuardarElemento(
            int elementoQuimicosId,
            [FromBody]
            GuardarConfiguracionElementoUnidadesDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                await conversionService
                    .GuardarConfiguracionElementoAsync(
                        elementoQuimicosId,
                        dto,
                        cancellationToken);

                ElementoConfiguracionUnidadesDto? resultado =
                    await conversionService
                        .ObtenerConfiguracionElementoAsync(
                            elementoQuimicosId,
                            incluirInactivas: true,
                            cancellationToken:
                                cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Las unidades del elemento fueron actualizadas correctamente.",
                    data = resultado
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpGet("materia-organica")]
        public async Task<IActionResult>
            ObtenerMateriaOrganica(
                [FromQuery] bool incluirInactivas = true,
                CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            List<UnidadConversionConfiguradaDto> resultado =
                await conversionService
                    .ObtenerConfiguracionMateriaOrganicaAsync(
                        incluirInactivas,
                        cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Configuración de materia orgánica obtenida correctamente.",
                data = resultado
            });
        }

        [HttpPut("materia-organica")]
        public async Task<IActionResult> GuardarMateriaOrganica(
            [FromBody]
            GuardarConfiguracionMateriaOrganicaDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                await conversionService
                    .GuardarConfiguracionMateriaOrganicaAsync(
                        dto,
                        cancellationToken);

                List<UnidadConversionConfiguradaDto> resultado =
                    await conversionService
                        .ObtenerConfiguracionMateriaOrganicaAsync(
                            incluirInactivas: true,
                            cancellationToken:
                                cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Las unidades de materia orgánica fueron actualizadas correctamente.",
                    data = resultado
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpGet("formulas")]
        public async Task<IActionResult> ListarFormulas(
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            return Ok(new
            {
                success = true,
                message =
                    "Fórmulas de conversión obtenidas correctamente.",
                data =
                    UnidadConversionService
                        .ObtenerFormulasDisponibles()
            });
        }

        [HttpPost("probar")]
        public async Task<IActionResult> ProbarConversion(
            [FromBody] ProbarConversionUnidadDto dto,
            CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                ResultadoPruebaConversionDto resultado =
                    await conversionService
                        .ProbarConversionAsync(
                            dto,
                            cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Conversión realizada correctamente.",
                    data = resultado
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
        }

        [HttpGet("catalogo-unidades")]
        public async Task<IActionResult>
            ListarCatalogoUnidades(
                [FromQuery] bool incluirInactivas = false,
                CancellationToken cancellationToken = default)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            IQueryable<UnidadMedida> query =
                db.UnidadMedidas
                    .AsNoTracking();

            if (!incluirInactivas)
            {
                query = query.Where(
                    item => item.activo);
            }

            var resultado =
                await query
                    .OrderBy(item =>
                        item.nombreUnidadMedida)
                    .Select(item => new
                    {
                        item.unidadMedidaId,
                        item.nombreUnidadMedida,
                        item.activo
                    })
                    .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Catálogo de unidades obtenido correctamente.",
                data = resultado
            });
        }

        private async Task<IActionResult?> ValidarAccesoAsync(
            TipoPermisoApi tipoPermiso,
            CancellationToken cancellationToken)
        {
            int? usuarioId =
                ObtenerUsuarioId();

            ResultadoPermisoApi resultado =
                await permisos.ValidarAsync(
                    usuarioId,
                    PortalWebDatabaseInitializer
                        .UnidadesConversionesWeb,
                    tipoPermiso,
                    cancellationToken);

            /*
             * Compatibilidad temporal para los roles que ya tenían este
             * módulo por medio de Elementos químicos.
             */
            if (!resultado.Permitido &&
                resultado.CodigoEstado ==
                    StatusCodes.Status403Forbidden)
            {
                resultado =
                    await permisos.ValidarAsync(
                        usuarioId,
                        PermisoAnterior,
                        tipoPermiso,
                        cancellationToken);
            }

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

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue("uid") ??
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("sub");

            return int.TryParse(
                       valor,
                       out int usuarioId) &&
                   usuarioId > 0
                ? usuarioId
                : null;
        }
    }
}
