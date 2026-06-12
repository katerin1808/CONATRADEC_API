using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{

    [ApiController]
    [Route("api/enmiendas-calcareas")]
    public class EnmiendaCalcareaController : ControllerBase
    {
        private readonly DBContext _db;

        public EnmiendaCalcareaController(DBContext db)
        {
            _db = db;
        }

        [HttpPost("calcular")]
        public async Task<IActionResult> Calcular([FromBody] EnmiendaCalcareaCrearDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.nombreAnalisis))
                    return BadRequest(new { mensaje = "El nombre del análisis es obligatorio." });

                if (dto.fuenteNutrientesId <= 0)
                    return BadRequest(new { mensaje = "Debe seleccionar una fuente nutriente válida." });

                if (dto.ca < 0 || dto.mg < 0 || dto.k < 0 || dto.acidezTotal < 0)
                    return BadRequest(new { mensaje = "Los valores de Ca, Mg, K y acidez total no pueden ser negativos." });

                var fuente = await _db.fuenteNutriente
                    .FirstOrDefaultAsync(x => x.fuenteNutrientesId == dto.fuenteNutrientesId && x.activo);

                if (fuente == null)
                    return BadRequest(new { mensaje = "La fuente nutriente no existe o está inactiva." });

                var parametro = await _db.ParametroEnmiendaCalcarea
                    .FirstOrDefaultAsync(x =>
                        x.fuenteNutrientesId == dto.fuenteNutrientesId &&
                        x.activo);

                if (parametro == null)
                    return BadRequest(new
                    {
                        mensaje = "No existe un parámetro activo de enmienda calcárea para esta fuente."
                    });

                decimal sumaBases = dto.ca + dto.mg + dto.k;
                decimal cice = sumaBases + dto.acidezTotal;

                if (cice <= 0)
                    return BadRequest(new { mensaje = "La CICE debe ser mayor a cero." });

                decimal saturacionActual = (sumaBases / cice) * 100m;

                decimal diferenciaSaturacion = parametro.saturacionBasesDeseada - saturacionActual;

                if (diferenciaSaturacion < 0)
                    diferenciaSaturacion = 0;

                decimal necesidadTonHa =
                    (diferenciaSaturacion * cice) / parametro.prnt;

                decimal necesidadKgHa =
                    necesidadTonHa * parametro.factorTonHaAKgHa;

                decimal necesidadLbHa =
                    necesidadTonHa * parametro.factorTonHaALbHa;

                var entity = new EnmiendaCalcarea
                {
                    nombreAnalisis = dto.nombreAnalisis.Trim(),
                    fuenteNutrientesId = dto.fuenteNutrientesId,

                    ph = dto.ph,
                    ca = dto.ca,
                    mg = dto.mg,
                    k = dto.k,
                    acidezTotal = dto.acidezTotal,

                    saturacionDeseada = parametro.saturacionBasesDeseada,
                    prnt = parametro.prnt,

                    sumaBases = sumaBases,
                    cice = cice,
                    saturacionActual = saturacionActual,

                    necesidadEncaladoTonHa = necesidadTonHa,
                    necesidadEncaladoKgHa = necesidadKgHa,
                    necesidadEncaladoLbHa = necesidadLbHa,

                    activo = true
                };

                _db.enmiendaCalcarea.Add(entity);
                await _db.SaveChangesAsync();

                return Ok(new EnmiendaCalcareaRespuestaDto
                {
                    enmiendaCalcareaId = entity.enmiendaCalcareaId,
                    nombreAnalisis = entity.nombreAnalisis,
                    fuenteNutriente = fuente.nombreNutriente,

                    ph = entity.ph,
                    ca = entity.ca,
                    mg = entity.mg,
                    k = entity.k,
                    acidezTotal = entity.acidezTotal,

                    saturacionDeseada = entity.saturacionDeseada,
                    prnt = entity.prnt,

                    sumaBases = Math.Round(entity.sumaBases, 4),
                    cice = Math.Round(entity.cice, 4),
                    saturacionActual = Math.Round(entity.saturacionActual, 4),

                    necesidadEncaladoTonHa = Math.Round(entity.necesidadEncaladoTonHa, 4),
                    necesidadEncaladoKgHa = Math.Round(entity.necesidadEncaladoKgHa, 4),
                    necesidadEncaladoLbHa = Math.Round(entity.necesidadEncaladoLbHa, 4)
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    mensaje = "Error al calcular la enmienda calcárea.",
                    detalle = ex.Message
                });
            }
        }

        [HttpGet("ultimo")]
        public async Task<IActionResult> Ultimo()
        {
            var data = await _db.enmiendaCalcarea
                .Include(x => x.fuenteNutriente)
                .Where(x => x.activo)
                .OrderByDescending(x => x.enmiendaCalcareaId)
                .Select(x => new EnmiendaCalcareaRespuestaDto
                {
                    enmiendaCalcareaId = x.enmiendaCalcareaId,
                    nombreAnalisis = x.nombreAnalisis,
                    fuenteNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.nombreNutriente : "",

                    ph = x.ph,
                    ca = x.ca,
                    mg = x.mg,
                    k = x.k,
                    acidezTotal = x.acidezTotal,

                    saturacionDeseada = x.saturacionDeseada,
                    prnt = x.prnt,

                    sumaBases = x.sumaBases,
                    cice = x.cice,
                    saturacionActual = x.saturacionActual,

                    necesidadEncaladoTonHa = x.necesidadEncaladoTonHa,
                    necesidadEncaladoKgHa = x.necesidadEncaladoKgHa,
                    necesidadEncaladoLbHa = x.necesidadEncaladoLbHa
                })
                .FirstOrDefaultAsync();

            if (data == null)
                return NotFound(new { mensaje = "No hay cálculos de enmienda calcárea registrados." });

            return Ok(data);
        }

        [HttpGet("listar")]
        public async Task<IActionResult> Listar()
        {
            var data = await _db.enmiendaCalcarea
                .Include(x => x.fuenteNutriente)
                .Where(x => x.activo)
                .OrderByDescending(x => x.enmiendaCalcareaId)
                .Select(x => new EnmiendaCalcareaRespuestaDto
                {
                    enmiendaCalcareaId = x.enmiendaCalcareaId,
                    nombreAnalisis = x.nombreAnalisis,
                    fuenteNutriente = x.fuenteNutriente != null ? x.fuenteNutriente.nombreNutriente : "",

                    ph = x.ph,
                    ca = x.ca,
                    mg = x.mg,
                    k = x.k,
                    acidezTotal = x.acidezTotal,

                    saturacionDeseada = x.saturacionDeseada,
                    prnt = x.prnt,

                    sumaBases = x.sumaBases,
                    cice = x.cice,
                    saturacionActual = x.saturacionActual,

                    necesidadEncaladoTonHa = x.necesidadEncaladoTonHa,
                    necesidadEncaladoKgHa = x.necesidadEncaladoKgHa,
                    necesidadEncaladoLbHa = x.necesidadEncaladoLbHa
                })
                .ToListAsync();

            return Ok(data);
        }
    }
}

