using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace CONATRADEC_API.Controllers
{

    [ApiController]
    [Route("api/elemento-quimico")]
    public class ElementoQuimicoController : ControllerBase
    {
        private readonly DBContext _context;

        public ElementoQuimicoController(DBContext context)
        {
            _context = context;
        }

        [HttpGet("listar")]
        public async Task<ActionResult<IEnumerable<ElementoQuimicoRespuestaDto>>> Listar()
        {
            var data = await _context.elementoQuimico
                .Where(x => x.activo == true)
                .OrderBy(x => x.nombreElementoQuimico)
                .Select(x => new ElementoQuimicoRespuestaDto
                {
                    elementoQuimicosId = x.elementoQuimicosId,
                    simboloElementoQuimico = x.simboloElementoQuimico,
                    nombreElementoQuimico = x.nombreElementoQuimico,
                    pesoEquivalenteElementoQuimico = x.pesoEquivalenteElementoQuimico,
                    activo = x.activo
                })
                .ToListAsync();

            return Ok(data);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ElementoQuimicoRespuestaDto>> ObtenerPorId(int id)
        {
            var data = await _context.elementoQuimico
                .Where(x => x.elementoQuimicosId == id && x.activo == true)
                .Select(x => new ElementoQuimicoRespuestaDto
                {
                    elementoQuimicosId = x.elementoQuimicosId,
                    simboloElementoQuimico = x.simboloElementoQuimico,
                    nombreElementoQuimico = x.nombreElementoQuimico,
                    pesoEquivalenteElementoQuimico = x.pesoEquivalenteElementoQuimico,
                    activo = x.activo
                })
                .FirstOrDefaultAsync();

            if (data == null)
                return NotFound(new { mensaje = "Elemento químico no encontrado." });

            return Ok(data);
        }

        [HttpPost("crear")]
        public async Task<ActionResult> Crear([FromBody] CrearElementoQuimicoDto dto)
        {
            if (dto == null)
                return BadRequest(new { mensaje = "Datos inválidos." });

            if (string.IsNullOrWhiteSpace(dto.simboloElementoQuimico))
                return BadRequest(new { mensaje = "El símbolo es obligatorio." });

            if (string.IsNullOrWhiteSpace(dto.nombreElementoQuimico))
                return BadRequest(new { mensaje = "El nombre es obligatorio." });

            string simbolo = dto.simboloElementoQuimico.Trim();

            bool existe = await _context.elementoQuimico.AnyAsync(x =>
                x.activo == true &&
                x.simboloElementoQuimico.ToLower() == simbolo.ToLower());

            if (existe)
                return BadRequest(new { mensaje = "Ya existe un elemento químico activo con ese símbolo." });

            var entity = new ElementoQuimico
            {
                simboloElementoQuimico = simbolo,
                nombreElementoQuimico = dto.nombreElementoQuimico.Trim(),
                pesoEquivalenteElementoQuimico = dto.pesoEquivalenteElementoQuimico,
                activo = true
            };

            _context.elementoQuimico.Add(entity);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Elemento químico creado correctamente.",
                elementoQuimicosId = entity.elementoQuimicosId
            });
        }

        [HttpPut("editar/{id:int}")]
        public async Task<ActionResult> Editar(int id, [FromBody] EditarElementoQuimicoDto dto)
        {
            if (dto == null)
                return BadRequest(new { mensaje = "Datos inválidos." });

            if (id != dto.elementoQuimicosId)
                return BadRequest(new { mensaje = "El ID de la ruta no coincide con el del objeto." });

            if (string.IsNullOrWhiteSpace(dto.simboloElementoQuimico))
                return BadRequest(new { mensaje = "El símbolo es obligatorio." });

            if (string.IsNullOrWhiteSpace(dto.nombreElementoQuimico))
                return BadRequest(new { mensaje = "El nombre es obligatorio." });

            var entity = await _context.elementoQuimico
                .FirstOrDefaultAsync(x => x.elementoQuimicosId == id && x.activo == true);

            if (entity == null)
                return NotFound(new { mensaje = "Elemento químico no encontrado." });

            string simbolo = dto.simboloElementoQuimico.Trim();

            bool existeDuplicado = await _context.elementoQuimico.AnyAsync(x =>
                x.activo == true &&
                x.elementoQuimicosId != id &&
                x.simboloElementoQuimico.ToLower() == simbolo.ToLower());

            if (existeDuplicado)
                return BadRequest(new { mensaje = "Ya existe otro elemento químico activo con ese símbolo." });

            entity.simboloElementoQuimico = simbolo;
            entity.nombreElementoQuimico = dto.nombreElementoQuimico.Trim();
            entity.pesoEquivalenteElementoQuimico = dto.pesoEquivalenteElementoQuimico;

            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Elemento químico actualizado correctamente." });
        }

        [HttpDelete("eliminar/{id:int}")]
        public async Task<ActionResult> Eliminar(int id)
        {
            var entity = await _context.elementoQuimico
                .FirstOrDefaultAsync(x =>
                    x.elementoQuimicosId == id &&
                    x.activo);

            if (entity == null)
            {
                return NotFound(new
                {
                    mensaje = "Elemento químico no encontrado o ya está desactivado."
                });
            }

            var dependencias = new List<string>();

            // Relaciones y configuraciones activas
            var usadoEnFuentes = await _context
                .fuenteNutrienteElementoQuimico
                .AnyAsync(x =>
                    x.elementoQuimicosId == id &&
                    x.activo);

            if (usadoEnFuentes)
            {
                dependencias.Add("fuentes de nutrientes");
            }

            var usadoEnExtraccion = await _context
                .ParametroExtraccionNutrienteCafe
                .AnyAsync(x =>
                    x.elementoQuimicosId == id &&
                    x.activo);

            if (usadoEnExtraccion)
            {
                dependencias.Add("parámetros de extracción por quintal oro");
            }

            var usadoEnRangos = await _context
                .ParametroRangoNutrienteCultivo
                .AnyAsync(x =>
                    x.elementoQuimicosId == id &&
                    x.activo);

            if (usadoEnRangos)
            {
                dependencias.Add("rangos nutricionales por cultivo");
            }

            var usadoEnAportesOrganicos = await _context
                .ParametroFuenteOrganicaAporte
                .AnyAsync(x =>
                    x.elementoQuimicosId == id &&
                    x.activo);

            if (usadoEnAportesOrganicos)
            {
                dependencias.Add("parámetros de fuentes orgánicas");
            }

            /*
             * Datos históricos:
             * No se filtran por activo porque el elemento sigue siendo
             * necesario para consultar registros guardados anteriormente.
             */

            var usadoEnAnalisis = await _context
                .AnalisisSueloElementos
                .AnyAsync(x =>
                    x.elementoQuimicosId == id);

            if (usadoEnAnalisis)
            {
                dependencias.Add("análisis de suelo guardados");
            }

            var usadoEnCalculos = await _context
                .AnalisisSueloCalculoElementos
                .AnyAsync(x =>
                    x.elementoQuimicosId == id);

            if (usadoEnCalculos)
            {
                dependencias.Add("cálculos de análisis de suelo");
            }

            var usadoEnFormulaDetalle = await _context
                .formulaNutricionalDetalle
                .AnyAsync(x =>
                    x.elementoQuimicosId == id);

            if (usadoEnFormulaDetalle)
            {
                dependencias.Add("detalles de fórmulas nutricionales");
            }

            var usadoEnFormulaAporte = await _context
                .formulaNutricionalAporte
                .AnyAsync(x =>
                    x.elementoQuimicosId == id);

            if (usadoEnFormulaAporte)
            {
                dependencias.Add("aportes de fórmulas nutricionales");
            }

            var usadoEnFertilizacionMixta = await _context
                .fertilizacionMixtaDetalle
                .AnyAsync(x =>
                    x.elementoQuimicosId == id);

            if (usadoEnFertilizacionMixta)
            {
                dependencias.Add("fertilizaciones mixtas");
            }

            // Si tiene cualquier dependencia, no permite desactivarlo
            if (dependencias.Count > 0)
            {
                return Conflict(new
                {
                    mensaje =
                        "No se puede eliminar el elemento químico porque está siendo utilizado.",

                    elemento = new
                    {
                        entity.elementoQuimicosId,
                        entity.simboloElementoQuimico,
                        entity.nombreElementoQuimico
                    },

                    usadoEn = dependencias
                });
            }

            // Solo se desactiva cuando no tiene dependencias
            entity.activo = false;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Elemento químico desactivado correctamente.",
                data = new
                {
                    entity.elementoQuimicosId,
                    entity.simboloElementoQuimico,
                    entity.nombreElementoQuimico,
                    entity.activo
                }
            });
        }
    }
}

