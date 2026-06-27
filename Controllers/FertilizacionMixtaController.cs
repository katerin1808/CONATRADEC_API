using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.FertilizacionMixtaDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/fertilizacion-mixta")]
    public class FertilizacionMixtaController : Controller
    {


      
       
            private readonly DBContext _context;

            public FertilizacionMixtaController(DBContext context)
            {
                _context = context;
            }

            [HttpPost("calcular")]
            public async Task<IActionResult> Calcular([FromBody] FertilizacionMixtaCrearDto dto)
            {
                if (dto.analisisSueloCalculoId <= 0)
                    return BadRequest(new { mensaje = "Debe seleccionar un cálculo de análisis de suelo válido." });

                if (dto.fuentes == null || !dto.fuentes.Any())
                    return BadRequest(new { mensaje = "Debe agregar al menos una fuente orgánica." });

                var analisisCalculo = await _context.AnalisisSueloCalculos
                    .FirstOrDefaultAsync(x =>
                        x.analisisSueloCalculoId == dto.analisisSueloCalculoId &&
                        x.activo == true);

                if (analisisCalculo == null)
                    return BadRequest(new { mensaje = "El cálculo de análisis de suelo no existe o está inactivo." });

                var requerimientos = await _context.AnalisisSueloCalculoElementoQuimicos
                    .Include(x => x.ElementoQuimico)
                    .Where(x =>
                        x.analisisSueloCalculoId == dto.analisisSueloCalculoId &&
                        x.activo == true)
                    .ToListAsync();

                if (!requerimientos.Any())
                    return BadRequest(new { mensaje = "El cálculo seleccionado no tiene requerimientos guardados." });

                foreach (var item in dto.fuentes)
                {
                    if (item.fuenteNutrientesId <= 0)
                        return BadRequest(new { mensaje = "Una fuente nutriente no es válida." });

                    if (item.cantidadQq <= 0)
                        return BadRequest(new { mensaje = "La cantidad en quintales debe ser mayor a cero." });

                    bool fuenteHabilitada = await _context.fertilizacionMixtaFuente
                        .AnyAsync(x =>
                            x.fuenteNutrientesId == item.fuenteNutrientesId &&
                            x.activo == true &&
                            x.fuenteNutriente != null &&
                            x.fuenteNutriente.activo == true);

                    if (!fuenteHabilitada)
                    {
                        return BadRequest(new
                        {
                            mensaje = $"La fuente con ID {item.fuenteNutrientesId} no está habilitada para fertilización mixta."
                        });
                    }
                }

                var fertilizacion = new FertilizacionMixta
                {
                    analisisSueloCalculoId = dto.analisisSueloCalculoId,
                    fechaCalculo = DateTime.Now,
                    observacion = dto.observacion,
                    activo = true
                };

                _context.fertilizacionMixta.Add(fertilizacion);
                await _context.SaveChangesAsync();

                var fuentesRespuesta = new List<FertilizacionMixtaFuenteRespuestaDto>();

                foreach (var item in dto.fuentes)
                {
                    var fuente = await _context.fuenteNutriente
                        .FirstOrDefaultAsync(x =>
                            x.fuenteNutrientesId == item.fuenteNutrientesId &&
                            x.activo == true);

                    var fuenteUsada = new FertilizacionMixtaFuente
                    {
                        fertilizacionMixtaId = fertilizacion.fertilizacionMixtaId,
                        fuenteNutrientesId = item.fuenteNutrientesId,
                        cantidadQq = item.cantidadQq,
                        activo = true
                    };

                    _context.fertilizacionMixtaFuente.Add(fuenteUsada);

                    fuentesRespuesta.Add(new FertilizacionMixtaFuenteRespuestaDto
                    {
                        fuenteNutrientesId = item.fuenteNutrientesId,
                        nombreFuente = fuente?.nombreNutriente ?? "",
                        cantidadQq = item.cantidadQq
                    });
                }

                await _context.SaveChangesAsync();

                var detallesRespuesta = new List<FertilizacionMixtaDetalleRespuestaDto>();

                foreach (var req in requerimientos)
                {
                    decimal requerimientoOriginal = req.analisisSueloCalculoId;

                    decimal aporteOrganico = 0;

                    foreach (var fuenteItem in dto.fuentes)
                    {
                        var aporteFuente = await _context.fuenteNutrienteElementoQuimico
                            .FirstOrDefaultAsync(x =>
                                x.fuenteNutrientesId == fuenteItem.fuenteNutrientesId &&
                                x.elementoQuimicosId == req.elementoQuimicosId &&
                                x.activo == true);

                        if (aporteFuente != null)
                        {
                            aporteOrganico += fuenteItem.cantidadQq * aporteFuente.cantidadAporte;
                        }
                    }

                    decimal diferencia = requerimientoOriginal - aporteOrganico;

                    decimal deficit = diferencia > 0 ? diferencia : 0;
                    decimal sobrante = diferencia < 0 ? diferencia * -1 : 0;

                    var detalle = new FertilizacionMixtaDetalle
                    {
                        fertilizacionMixtaId = fertilizacion.fertilizacionMixtaId,
                        elementoQuimicosId = req.elementoQuimicosId,

                        requerimientoOriginal = requerimientoOriginal,
                        aporteOrganico = aporteOrganico,
                        diferencia = diferencia,
                        deficit = deficit,
                        sobrante = sobrante,

                        activo = true
                    };

                    _context.fertilizacionMixtaDetalle.Add(detalle);

                    detallesRespuesta.Add(new FertilizacionMixtaDetalleRespuestaDto
                    {
                        elementoQuimicosId = req.elementoQuimicosId,
                        elemento = req.ElementoQuimico?.simboloElementoQuimico ?? "",

                        requerimientoOriginal = Math.Round(requerimientoOriginal, 4),
                        aporteOrganico = Math.Round(aporteOrganico, 4),
                        diferencia = Math.Round(diferencia, 4),
                        deficit = Math.Round(deficit, 4),
                        sobrante = Math.Round(sobrante, 4)
                    });
                }

                await _context.SaveChangesAsync();

                return Ok(new FertilizacionMixtaRespuestaDto
                {
                    fertilizacionMixtaId = fertilizacion.fertilizacionMixtaId,
                    analisisSueloCalculoId = fertilizacion.analisisSueloCalculoId,
                    fechaCalculo = fertilizacion.fechaCalculo,
                    observacion = fertilizacion.observacion,
                    fuentes = fuentesRespuesta,
                    detalles = detallesRespuesta
                });
            }
}
    }



