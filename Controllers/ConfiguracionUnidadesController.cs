using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Administración de unidades permitidas y reglas de conversión
    /// utilizadas por el análisis de suelo.
    /// </summary>
    [ApiController]
    [Route("api/configuracion-unidades")]
    public sealed class ConfiguracionUnidadesController :
        ControllerBase
    {
        private readonly DBContext db;

        private readonly UnidadConversionService
            conversionService;

        public ConfiguracionUnidadesController(
            DBContext db)
        {
            this.db = db;
            conversionService =
                new UnidadConversionService(db);
        }

        /// <summary>
        /// Devuelve en una sola petición todas las unidades necesarias
        /// para construir el formulario del análisis.
        /// </summary>
        [HttpGet("formulario-analisis")]
        public async Task<IActionResult>
            ObtenerConfiguracionFormulario(
                CancellationToken cancellationToken)
        {
            ConfiguracionFormularioAnalisisDto
                resultado =
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
        public async Task<IActionResult>
            ListarElementos(
                [FromQuery] bool incluirInactivas =
                    false,
                CancellationToken cancellationToken =
                    default)
        {
            IQueryable<ElementoQuimico> query =
                db.elementoQuimico
                    .AsNoTracking();

            if (!incluirInactivas)
            {
                query = query.Where(x =>
                    x.activo);
            }

            List<ElementoQuimico> elementos =
                await query
                    .OrderBy(x =>
                        x.nombreElementoQuimico)
                    .ToListAsync(
                        cancellationToken);

            List<ElementoConfiguracionUnidadesDto>
                respuesta = new();

            foreach (
                ElementoQuimico elemento
                in elementos)
            {
                ElementoConfiguracionUnidadesDto?
                    configuracion =
                        await conversionService
                            .ObtenerConfiguracionElementoAsync(
                                elemento
                                    .elementoQuimicosId,
                                incluirInactivas,
                                cancellationToken);

                if (configuracion != null)
                {
                    respuesta.Add(
                        configuracion);
                }
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
        public async Task<IActionResult>
            ObtenerElemento(
                int elementoQuimicosId,
                [FromQuery] bool incluirInactivas =
                    true,
                CancellationToken cancellationToken =
                    default)
        {
            ElementoConfiguracionUnidadesDto?
                resultado =
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
        public async Task<IActionResult>
            GuardarElemento(
                int elementoQuimicosId,
                [FromBody]
                GuardarConfiguracionElementoUnidadesDto
                    dto,
                CancellationToken cancellationToken =
                    default)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                await conversionService
                    .GuardarConfiguracionElementoAsync(
                        elementoQuimicosId,
                        dto,
                        cancellationToken);

                ElementoConfiguracionUnidadesDto?
                    resultado =
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
                [FromQuery] bool incluirInactivas =
                    true,
                CancellationToken cancellationToken =
                    default)
        {
            List<UnidadConversionConfiguradaDto>
                resultado =
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
        public async Task<IActionResult>
            GuardarMateriaOrganica(
                [FromBody]
                GuardarConfiguracionMateriaOrganicaDto
                    dto,
                CancellationToken cancellationToken =
                    default)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                await conversionService
                    .GuardarConfiguracionMateriaOrganicaAsync(
                        dto,
                        cancellationToken);

                List<UnidadConversionConfiguradaDto>
                    resultado =
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
        public IActionResult ListarFormulas()
        {
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
        public async Task<IActionResult>
            ProbarConversion(
                [FromBody]
                ProbarConversionUnidadDto dto,
                CancellationToken cancellationToken =
                    default)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                ResultadoPruebaConversionDto
                    resultado =
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

        /// <summary>
        /// Lista todas las unidades base para que la futura interfaz
        /// administrativa pueda seleccionar cuáles asociar.
        /// </summary>
        [HttpGet("catalogo-unidades")]
        public async Task<IActionResult>
            ListarCatalogoUnidades(
                [FromQuery] bool incluirInactivas =
                    false,
                CancellationToken cancellationToken =
                    default)
        {
            IQueryable<UnidadMedida> query =
                db.UnidadMedidas
                    .AsNoTracking();

            if (!incluirInactivas)
            {
                query = query.Where(x =>
                    x.activo);
            }

            var resultado =
                await query
                    .OrderBy(x =>
                        x.nombreUnidadMedida)
                    .Select(x => new
                    {
                        x.unidadMedidaId,
                        x.nombreUnidadMedida,
                        x.activo
                    })
                    .ToListAsync(
                        cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "Catálogo de unidades obtenido correctamente.",
                data = resultado
            });
        }
    }
}
