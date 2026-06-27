using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.FertilizacionMixtaDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/fertilizacion-mixta")]
    public class FertilizacionMixtaController : ControllerBase
    {
        private readonly DBContext _db;

        public FertilizacionMixtaController(DBContext db)
        {
            _db = db;
        }

        [HttpPost("calcular")]
        public async Task<IActionResult> Calcular([FromBody] FertilizacionMixtaCrearDto dto)
        {
            if (dto.analisisSueloCalculoId <= 0)
            {
                return BadRequest(new
                {
                    mensaje = "Debe seleccionar un cálculo de análisis de suelo válido."
                });
            }

            if (dto.fuentes == null || !dto.fuentes.Any())
            {
                return BadRequest(new
                {
                    mensaje = "Debe agregar al menos una fuente orgánica."
                });
            }

            var analisisCalculo = await _db.AnalisisSueloCalculos
                .FirstOrDefaultAsync(x =>
                    x.analisisSueloCalculoId == dto.analisisSueloCalculoId &&
                    x.activo == true);

            if (analisisCalculo == null)
            {
                return BadRequest(new
                {
                    mensaje = "El cálculo de análisis de suelo no existe o está inactivo."
                });
            }

            var requerimientos = await _db.AnalisisSueloCalculoElementoQuimicos
                .Include(x => x.ElementoQuimico)
                .Where(x =>
                    x.analisisSueloCalculoId == dto.analisisSueloCalculoId &&
                    x.activo == true)
                .ToListAsync();

            if (!requerimientos.Any())
            {
                return BadRequest(new
                {
                    mensaje = "El cálculo seleccionado no tiene elementos guardados."
                });
            }

            foreach (var item in dto.fuentes)
            {
                if (item.fuenteNutrientesId <= 0)
                {
                    return BadRequest(new
                    {
                        mensaje = "Una fuente nutriente no es válida."
                    });
                }

                if (item.cantidadQq <= 0)
                {
                    return BadRequest(new
                    {
                        mensaje = "La cantidad en quintales debe ser mayor a cero."
                    });
                }

                bool fuenteExiste = await _db.fuenteNutriente
                    .AnyAsync(x =>
                        x.fuenteNutrientesId == item.fuenteNutrientesId &&
                        x.activo == true);

                if (!fuenteExiste)
                {
                    return BadRequest(new
                    {
                        mensaje = $"La fuente con ID {item.fuenteNutrientesId} no existe o está inactiva."
                    });
                }

                bool fuenteHabilitada = await _db.fuenteFertilizacionMixta
                    .AnyAsync(x =>
                        x.fuenteNutrientesId == item.fuenteNutrientesId &&
                        x.activo == true);

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

            _db.fertilizacionMixta.Add(fertilizacion);
            await _db.SaveChangesAsync();

            var fuentesRespuesta = new List<FertilizacionMixtaFuenteRespuestaDto>();

            foreach (var item in dto.fuentes)
            {
                var fuente = await _db.fuenteNutriente
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

                _db.fertilizacionMixtaFuente.Add(fuenteUsada);

                fuentesRespuesta.Add(new FertilizacionMixtaFuenteRespuestaDto
                {
                    fuenteNutrientesId = item.fuenteNutrientesId,
                    nombreFuente = fuente?.nombreNutriente ?? "",
                    cantidadQq = Math.Round(item.cantidadQq, 4)
                });
            }

            await _db.SaveChangesAsync();

            var detallesRespuesta = new List<FertilizacionMixtaDetalleRespuestaDto>();

            foreach (var req in requerimientos)
            {
                var rango = await _db.ParametroRangoNutrienteCultivo
                    .FirstOrDefaultAsync(x =>
                        x.tipoCultivoId == analisisCalculo.tipoCultivoId &&
                        x.elementoQuimicosId == req.elementoQuimicosId &&
                        x.activo == true);

                if (rango == null)
                {
                    return BadRequest(new
                    {
                        mensaje = $"No existe rango nutricional para el elemento {req.elementoQuimicosId}."
                    });
                }

                decimal rangoMaximoLbMz = rango.valorMaximo * 2.2m * 0.7m;

                if (req.requerimientoCalculado == null)
                {
                    return BadRequest(new
                    {
                        mensaje = $"El elemento {req.elementoQuimicosId} no tiene requerimiento calculado."
                    });
                }

                decimal exportable = req.requerimientoCalculado.Value - rangoMaximoLbMz;

                if (exportable < 0)
                {
                    exportable = 0;
                }

                decimal aporteOrganico = 0;

                var fuentesDetalle = new List<FertilizacionMixtaFuenteElementoDetalleDto>();

                foreach (var fuenteItem in dto.fuentes)
                {
                    var fuente = await _db.fuenteNutriente
                        .FirstOrDefaultAsync(x =>
                            x.fuenteNutrientesId == fuenteItem.fuenteNutrientesId &&
                            x.activo == true);

                    var aporteFuente = await _db.fuenteNutrienteElementoQuimico
                        .FirstOrDefaultAsync(x =>
                            x.fuenteNutrientesId == fuenteItem.fuenteNutrientesId &&
                            x.elementoQuimicosId == req.elementoQuimicosId &&
                            x.activo == true);

                    decimal aportePorUnidad = aporteFuente?.cantidadAporte ?? 0;

                    decimal aporteTotal = fuenteItem.cantidadQq * aportePorUnidad;

                    aporteOrganico += aporteTotal;

                    fuentesDetalle.Add(new FertilizacionMixtaFuenteElementoDetalleDto
                    {
                        fuenteNutrientesId = fuenteItem.fuenteNutrientesId,
                        nombreFuente = fuente?.nombreNutriente ?? "",
                        cantidadQq = Math.Round(fuenteItem.cantidadQq, 4),
                        aportePorUnidad = Math.Round(aportePorUnidad, 4),
                        aporteTotal = Math.Round(aporteTotal, 4)
                    });
                }

                decimal diferencia = exportable - aporteOrganico;
                decimal deficit = diferencia > 0 ? diferencia : 0;
                decimal sobrante = diferencia < 0 ? diferencia * -1 : 0;

                var detalle = new FertilizacionMixtaDetalle
                {
                    fertilizacionMixtaId = fertilizacion.fertilizacionMixtaId,
                    elementoQuimicosId = req.elementoQuimicosId,

                    requerimientoOriginal = Math.Round(exportable, 4),
                    aporteOrganico = Math.Round(aporteOrganico, 4),
                    diferencia = Math.Round(diferencia, 4),
                    deficit = Math.Round(deficit, 4),
                    sobrante = Math.Round(sobrante, 4),

                    activo = true
                };

                _db.fertilizacionMixtaDetalle.Add(detalle);

                detallesRespuesta.Add(new FertilizacionMixtaDetalleRespuestaDto
                {
                    elementoQuimicosId = req.elementoQuimicosId,
                    elemento = req.ElementoQuimico?.simboloElementoQuimico.Trim() ?? "",

                    exportable = Math.Round(exportable, 4),
                    aporteOrganico = Math.Round(aporteOrganico, 4),
                    diferencia = Math.Round(diferencia, 4),
                    deficit = Math.Round(deficit, 4),
                    sobrante = Math.Round(sobrante, 4),

                    fuentes = fuentesDetalle
                });
            }

            await _db.SaveChangesAsync();

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