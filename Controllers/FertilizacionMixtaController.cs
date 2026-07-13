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
        public async Task<IActionResult> Calcular(
            [FromBody] FertilizacionMixtaCrearDto dto)
        {
            if (dto.elementos == null || !dto.elementos.Any())
            {
                return BadRequest(new
                {
                    mensaje = "Debe enviar al menos un elemento calculado."
                });
            }

            if (dto.fuentes == null || !dto.fuentes.Any())
            {
                return BadRequest(new
                {
                    mensaje = "Debe agregar al menos una fuente orgánica."
                });
            }

            if (dto.elementos.Any(x => x.elementoQuimicosId <= 0))
            {
                return BadRequest(new
                {
                    mensaje = "Uno de los elementos químicos no es válido."
                });
            }

            if (dto.elementos.Any(x => x.exportable < 0))
            {
                return BadRequest(new
                {
                    mensaje = "El valor exportable no puede ser negativo."
                });
            }

            if (dto.fuentes.Any(x => x.fuenteNutrientesId <= 0))
            {
                return BadRequest(new
                {
                    mensaje = "Una de las fuentes nutrientes no es válida."
                });
            }

            if (dto.fuentes.Any(x => x.cantidadQq <= 0))
            {
                return BadRequest(new
                {
                    mensaje = "La cantidad en quintales debe ser mayor a cero."
                });
            }

            var fuentesIds = dto.fuentes
                .Select(x => x.fuenteNutrientesId)
                .Distinct()
                .ToList();

            var elementosIds = dto.elementos
                .Select(x => x.elementoQuimicosId)
                .Distinct()
                .ToList();

            var fuentesActivas = await _db.fuenteNutriente
                .Where(x =>
                    fuentesIds.Contains(x.fuenteNutrientesId) &&
                    x.activo == true)
                .ToListAsync();

            var fuentesHabilitadas = await _db.fuenteFertilizacionMixta
                .Where(x =>
                    fuentesIds.Contains(x.fuenteNutrientesId) &&
                    x.activo == true)
                .ToListAsync();

            var elementosActivos = await _db.elementoQuimico
                .Where(x =>
                    elementosIds.Contains(x.elementoQuimicosId) &&
                    x.activo == true)
                .ToListAsync();

            var aportesRegistrados = await _db.fuenteNutrienteElementoQuimico
                .Where(x =>
                    fuentesIds.Contains(x.fuenteNutrientesId) &&
                    elementosIds.Contains(x.elementoQuimicosId) &&
                    x.activo == true)
                .ToListAsync();

            foreach (var fuenteItem in dto.fuentes)
            {
                var fuenteExiste = fuentesActivas.Any(x =>
                    x.fuenteNutrientesId ==
                    fuenteItem.fuenteNutrientesId);

                if (!fuenteExiste)
                {
                    return BadRequest(new
                    {
                        mensaje =
                            $"La fuente con ID {fuenteItem.fuenteNutrientesId} no existe o está inactiva."
                    });
                }

                var fuenteHabilitada = fuentesHabilitadas.Any(x =>
                    x.fuenteNutrientesId ==
                    fuenteItem.fuenteNutrientesId);

                if (!fuenteHabilitada)
                {
                    return BadRequest(new
                    {
                        mensaje =
                            $"La fuente con ID {fuenteItem.fuenteNutrientesId} no está habilitada para fertilización mixta."
                    });
                }
            }

            foreach (var elementoItem in dto.elementos)
            {
                var elementoExiste = elementosActivos.Any(x =>
                    x.elementoQuimicosId ==
                    elementoItem.elementoQuimicosId);

                if (!elementoExiste)
                {
                    return BadRequest(new
                    {
                        mensaje =
                            $"El elemento con ID {elementoItem.elementoQuimicosId} no existe o está inactivo."
                    });
                }
            }

            var fuentesRespuesta =
                new List<FertilizacionMixtaFuenteRespuestaDto>();

            foreach (var fuenteItem in dto.fuentes)
            {
                var fuente = fuentesActivas.First(x =>
                    x.fuenteNutrientesId ==
                    fuenteItem.fuenteNutrientesId);

                fuentesRespuesta.Add(
                    new FertilizacionMixtaFuenteRespuestaDto
                    {
                        fuenteNutrientesId =
                            fuenteItem.fuenteNutrientesId,

                        nombreFuente =
                            fuente.nombreNutriente ?? string.Empty,

                        cantidadQq =
                            Math.Round(fuenteItem.cantidadQq, 4)
                    });
            }

            var detallesRespuesta =
                new List<FertilizacionMixtaDetalleRespuestaDto>();

            foreach (var elementoItem in dto.elementos)
            {
                var elementoQuimico = elementosActivos.First(x =>
                    x.elementoQuimicosId ==
                    elementoItem.elementoQuimicosId);

                decimal aporteOrganico = 0;

                var fuentesDetalle =
                    new List<FertilizacionMixtaFuenteElementoDetalleDto>();

                foreach (var fuenteItem in dto.fuentes)
                {
                    var fuente = fuentesActivas.First(x =>
                        x.fuenteNutrientesId ==
                        fuenteItem.fuenteNutrientesId);

                    var aporteRegistrado =
                        aportesRegistrados.FirstOrDefault(x =>
                            x.fuenteNutrientesId ==
                            fuenteItem.fuenteNutrientesId &&
                            x.elementoQuimicosId ==
                            elementoItem.elementoQuimicosId);

                    decimal aportePorUnidad =
                        aporteRegistrado?.cantidadAporte ?? 0;

                    decimal aporteTotal =
                        fuenteItem.cantidadQq * aportePorUnidad;

                    aporteOrganico += aporteTotal;

                    fuentesDetalle.Add(
                        new FertilizacionMixtaFuenteElementoDetalleDto
                        {
                            fuenteNutrientesId =
                                fuenteItem.fuenteNutrientesId,

                            nombreFuente =
                                fuente.nombreNutriente ?? string.Empty,

                            cantidadQq =
                                Math.Round(fuenteItem.cantidadQq, 4),

                            aportePorUnidad =
                                Math.Round(aportePorUnidad, 4),

                            aporteTotal =
                                Math.Round(aporteTotal, 4)
                        });
                }

                decimal exportable = elementoItem.exportable;

                decimal diferencia =
                    exportable - aporteOrganico;

                decimal deficit =
                    diferencia > 0 ? diferencia : 0;

                decimal sobrante =
                    diferencia < 0
                        ? Math.Abs(diferencia)
                        : 0;

                detallesRespuesta.Add(
                    new FertilizacionMixtaDetalleRespuestaDto
                    {
                        elementoQuimicosId =
                            elementoItem.elementoQuimicosId,

                        elemento =
                            elementoQuimico.simboloElementoQuimico?
                                .Trim() ?? string.Empty,

                        exportable =
                            Math.Round(exportable, 4),

                        aporteOrganico =
                            Math.Round(aporteOrganico, 4),

                        diferencia =
                            Math.Round(diferencia, 4),

                        deficit =
                            Math.Round(deficit, 4),

                        sobrante =
                            Math.Round(sobrante, 4),

                        fuentes = fuentesDetalle
                    });
            }

            return Ok(new FertilizacionMixtaRespuestaDto
            {
                observacion = dto.observacion ?? string.Empty,
                fuentes = fuentesRespuesta,
                detalles = detallesRespuesta
            });
        }
    }
}
