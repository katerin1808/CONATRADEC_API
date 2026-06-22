using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.FormulaNutricionalDto;

namespace CONATRADEC_API.Controllers
{
 
        [ApiController]
        [Route("api/formula-nutricional")]
        public class FormulaNutricionalController : ControllerBase
        {
            private readonly DBContext _db;

            public FormulaNutricionalController(DBContext db)
            {
                _db = db;
            }

        [HttpPost("calcular")]
        public async Task<IActionResult> Calcular([FromBody] FormulaNutricionalCrearDto dto)
        {
            await using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                if (dto.items == null || !dto.items.Any())
                    return BadRequest(new { mensaje = "Debe enviar al menos un item." });


                var terreno = await _db.Terreno
               .FirstOrDefaultAsync(x => x.terrenoId == dto.terrenoId && x.activo);

                if (terreno == null)
                    return BadRequest(new { mensaje = "El terreno no existe o está inactivo." });

                int totalPlantasUsadas = dto.totalPlantas.HasValue && dto.totalPlantas.Value > 0
                    ? dto.totalPlantas.Value
                    : terreno.cantidadPlantasTerreno;

                if (dto.totalAplicaciones < 1 || dto.totalAplicaciones > 4)
                    return BadRequest(new { mensaje = "El total de aplicaciones debe estar entre 1 y 4." });

                decimal totalLibras = dto.items.Sum(x => x.libras);
                decimal mezclaTotalQq = totalLibras / 100m;

                if (mezclaTotalQq <= 0)
                    return BadRequest(new { mensaje = "La mezcla total en QQ debe ser mayor a cero." });
                var itemsOrdenados = dto.items
                .OrderByDescending(x => x.libras)
                .ToList();

                var totalesFormula = new Dictionary<string, decimal>();

                var detallesGuardar = new List<FormulaNutricionalDetalle>();
                var detallesRespuesta = new List<FormulaNutricionalDetalleRespuestaDto>();

                foreach (var item in itemsOrdenados)
                {
                    if (item.libras <= 0)
                        return BadRequest(new { mensaje = "Las libras deben ser mayores a cero." });

                    var fuente = await _db.fuenteNutriente
                        .FirstOrDefaultAsync(x => x.fuenteNutrientesId == item.fuenteNutrientesId && x.activo);

                    if (fuente == null)
                        return BadRequest(new { mensaje = $"La fuente nutriente con ID {item.fuenteNutrientesId} no existe o está inactiva." });


                    var elementoBase = await _db.elementoQuimico
                        .FirstOrDefaultAsync(x => x.elementoQuimicosId == item.elementoQuimicosId && x.activo);

                    if (elementoBase == null)
                        return BadRequest(new { mensaje = $"El elemento químico con ID {item.elementoQuimicosId} no existe o está inactivo." });

                    var composicionFuente = await _db.fuenteNutrienteElementoQuimico
                        .Include(x => x.elementoQuimico)
                        .Where(x => x.fuenteNutrientesId == item.fuenteNutrientesId && x.activo)
                        .ToListAsync();

                    if (!composicionFuente.Any())
                        return BadRequest(new { mensaje = $"La fuente {fuente.nombreNutriente} no tiene aportes registrados." });

                    bool fuenteAportaElementoBase = composicionFuente
                        .Any(x => x.elementoQuimicosId == item.elementoQuimicosId);

                    if (!fuenteAportaElementoBase)
                    {
                        return BadRequest(new
                        {
                            mensaje = $"La fuente {fuente.nombreNutriente} no aporta el elemento {elementoBase.simboloElementoQuimico}."
                        });
                    }

                    decimal qq = item.libras / 100m;
                    decimal onzasAnuales = item.libras * 16m;
                    decimal librasPorAplicacion = item.libras / dto.totalAplicaciones;
                    decimal onzasPorAplicacion = onzasAnuales / dto.totalAplicaciones;
                    decimal precioPorQuintal = fuente.precioNutriente;
                    decimal subtotalFuente = qq * precioPorQuintal;

                    var aportesRespuesta = new Dictionary<string, decimal>();
                    var aportesGuardar = new List<FormulaNutricionalAporte>();

                    foreach (var comp in composicionFuente)
                    {
                        string simbolo = comp.elementoQuimico!.simboloElementoQuimico.Trim().ToLower();

                        decimal aporte = qq * comp.cantidadAporte;

                        if (aporte > 0)
                        {
                            aportesRespuesta[simbolo] = Math.Round(aporte, 4);

                            if (!totalesFormula.ContainsKey(simbolo))
                                totalesFormula[simbolo] = 0;

                            totalesFormula[simbolo] += aporte;

                            aportesGuardar.Add(new FormulaNutricionalAporte
                            {
                                elementoQuimicosId = comp.elementoQuimicosId,
                                valor = aporte,
                                activo = true
                            });
                        }
                    }



                    var detalle = new FormulaNutricionalDetalle
                    {
                        fuenteNutrientesId = item.fuenteNutrientesId,
                        elementoQuimicosId = item.elementoQuimicosId,
                        libras = item.libras,
                        requerimientoLibras = item.libras,
                        onzasAnuales = onzasAnuales,
                        onzasPorAplicacion = onzasPorAplicacion,
                        precioPorQuintal = precioPorQuintal,
                        subtotalFuente = subtotalFuente,
                        qq = qq,
                        activo = true,
                        aportes = aportesGuardar
                    };

                    detallesGuardar.Add(detalle);

                    detallesRespuesta.Add(new FormulaNutricionalDetalleRespuestaDto
                    {
                        fuente = fuente.nombreNutriente,
                        elemento = elementoBase.simboloElementoQuimico,
                        lb = item.libras,
                        qq = Math.Round(qq, 4),
                        aportes = aportesRespuesta
                          .OrderBy(x => OrdenElemento(x.Key))
                         .ToDictionary(x => x.Key, x => x.Value),
                       requerimientoLibras = Math.Round(item.libras, 4),
                        librasPorAplicacion = Math.Round(librasPorAplicacion, 4),
                        onzasAnuales = Math.Round(onzasAnuales, 4),
                        onzasPorAplicacion = Math.Round(onzasPorAplicacion, 4),
                        precioPorQuintal = Math.Round(precioPorQuintal, 4),
                        subtotalFuente = Math.Round(subtotalFuente, 4),

                    });
                }

                decimal totalOnzas = totalLibras * 16m;
                decimal precioTotalFormula = detallesGuardar.Sum(x => x.subtotalFuente);
                decimal precioPorAplicacion = precioTotalFormula / dto.totalAplicaciones;
                decimal dosisPlantaAnualOz = totalOnzas / totalPlantasUsadas;
                decimal dosisPlantaPorAplicacionOz = dosisPlantaAnualOz / dto.totalAplicaciones;
                var formula = new FormulaNutricional
                {
                    nombreFormula = dto.nombreFormula ?? "",
                    terrenoId = terreno.terrenoId,
                    totalPlantas = totalPlantasUsadas,
                    totalAplicaciones = dto.totalAplicaciones,

                    totalLibras = totalLibras,
                    mezclaTotalQq = mezclaTotalQq,
                    totalOnzas = totalOnzas,

                    precioTotalFormula = precioTotalFormula,
                    precioPorAplicacion = precioPorAplicacion,

                    dosisPlantaAnualOz = dosisPlantaAnualOz,
                    dosisPlantaPorAplicacionOz = dosisPlantaPorAplicacionOz,

                    activo = true
                };

                _db.formulaNutricional.Add(formula);
                await _db.SaveChangesAsync();

                foreach (var d in detallesGuardar)
                {
                    d.formulaNutricionalId = formula.formulaNutricionalId;
                }

                _db.formulaNutricionalDetalle.AddRange(detallesGuardar);
                await _db.SaveChangesAsync();

                await trans.CommitAsync();

                var response = new FormulaNutricionalRespuestaDto
                {
                    formulaNutricionalId = formula.formulaNutricionalId,
                    nombreFormula = formula.nombreFormula,
                    totalLibras = Math.Round(formula.totalLibras, 4),
                    mezclaTotalQq = Math.Round(formula.mezclaTotalQq, 4),
                    formulaComercial = totalesFormula
                  .Where(x => x.Value > 0)
                   .Select(x => new
                     {
                   Elemento = x.Key,
                  Valor = Math.Round(x.Value / mezclaTotalQq, 4)
                     })
                 .OrderBy(x => OrdenElemento(x.Elemento))
                  .ToDictionary(x => x.Elemento, x => x.Valor),
                    totalPlantas = formula.totalPlantas,
                    totalAplicaciones = formula.totalAplicaciones,
                    totalOnzas = Math.Round(formula.totalOnzas, 4),
                    precioTotalFormula = Math.Round(formula.precioTotalFormula, 4),
                    precioPorAplicacion = Math.Round(formula.precioPorAplicacion, 4),
                    dosisPlantaAnualOz = Math.Round(formula.dosisPlantaAnualOz, 4),
                    dosisPlantaPorAplicacionOz = Math.Round(formula.dosisPlantaPorAplicacionOz, 4),
                  
                    detalle = detallesRespuesta
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();

                return StatusCode(500, new
                {
                    mensaje = "Error al calcular fórmula nutricional.",
                    detalle = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }

        private int OrdenElemento(string simbolo)
        {
            simbolo = simbolo.Trim().ToUpper();

            return simbolo switch
            {
                "N" => 1,
                "K" => 2,
                "MG" => 3,
                "CA" => 4,
                "P" => 5,
                _ => 99
            };
        }
        [HttpGet("ultima-total")]
        public async Task<IActionResult> UltimaSoloTotal()
        {
            var formula = await _db.formulaNutricional
                .Where(x => x.activo)
                .OrderByDescending(x => x.formulaNutricionalId)
                .FirstOrDefaultAsync();

            if (formula == null)
                return NotFound(new { mensaje = "No hay fórmulas registradas." });

            var aportes = await _db.formulaNutricionalDetalle
                .Where(d => d.formulaNutricionalId == formula.formulaNutricionalId && d.activo)
                .SelectMany(d => d.aportes!)
                .Where(a => a.activo)
                .Include(a => a.elementoQuimico)
                .ToListAsync();

            var formulaComercial = aportes
      .Where(a => a.valor > 0)
      .GroupBy(a => a.elementoQuimico!.simboloElementoQuimico.Trim().ToLower())
      .Select(g => new
      {
          Elemento = g.Key,
          Valor = Math.Round(g.Sum(a => a.valor) / formula.mezclaTotalQq, 4)
      })
      .OrderBy(x => OrdenElemento(x.Elemento))
      .ToDictionary(x => x.Elemento, x => x.Valor);

            return Ok(new
            {
                formula.formulaNutricionalId,
                formula.nombreFormula,

                totalLibras = Math.Round(formula.totalLibras, 4),
                mezclaTotalQq = Math.Round(formula.mezclaTotalQq, 4),
                totalOnzas = Math.Round(formula.totalOnzas, 4),

                totalPlantas = formula.totalPlantas,
                totalAplicaciones = formula.totalAplicaciones,

                precioTotalFormula = Math.Round(formula.precioTotalFormula, 4),
                precioPorAplicacion = Math.Round(formula.precioPorAplicacion, 4),

                dosisPlantaAnualOz = Math.Round(formula.dosisPlantaAnualOz, 4),
                dosisPlantaPorAplicacionOz = Math.Round(formula.dosisPlantaPorAplicacionOz, 4),

                formulaComercial
            });
        }

        [HttpGet("ultima-detalle")]
        public async Task<IActionResult> UltimaConDetalle()
        {
            var formula = await _db.formulaNutricional
                .Where(x => x.activo)
                .OrderByDescending(x => x.formulaNutricionalId)
                .FirstOrDefaultAsync();

            if (formula == null)
                return NotFound(new { mensaje = "No hay fórmulas registradas." });

            var detalles = await _db.formulaNutricionalDetalle
                .Include(x => x.fuenteNutriente)
                .Include(x => x.elementoQuimico)
                .Include(x => x.aportes!)
                    .ThenInclude(a => a.elementoQuimico)
                .Where(x => x.formulaNutricionalId == formula.formulaNutricionalId && x.activo)
                .ToListAsync();

            var detalleRespuesta = detalles.Select(x => new
            {
                x.formulaNutricionalDetalleId,
                fuente = x.fuenteNutriente != null ? x.fuenteNutriente.nombreNutriente : "",
                elemento = x.elementoQuimico != null ? x.elementoQuimico.simboloElementoQuimico : "",
                lb = Math.Round(x.libras, 4),
                qq = Math.Round(x.qq, 4),
                requerimientoLibras = Math.Round(x.requerimientoLibras, 4),
                precioPorQuintal = Math.Round(x.precioPorQuintal, 4),
                subtotalFuente = Math.Round(x.subtotalFuente, 4),
                aportes = x.aportes!
               .Where(a => a.activo && a.valor > 0)
               .OrderBy(a => OrdenElemento(a.elementoQuimico!.simboloElementoQuimico))
                .ToDictionary(
        a => a.elementoQuimico!.simboloElementoQuimico.Trim().ToLower(),
        a => Math.Round(a.valor, 4)
    )
            }).ToList();

            var todosAportes = detalles
                .SelectMany(x => x.aportes ?? new List<FormulaNutricionalAporte>())
                .Where(a => a.activo && a.valor > 0)
                .ToList();

            var formulaComercial = todosAportes
      .GroupBy(a => a.elementoQuimico!.simboloElementoQuimico.Trim().ToLower())
      .Select(g => new
      {
          Elemento = g.Key,
          Valor = Math.Round(g.Sum(a => a.valor) / formula.mezclaTotalQq, 4)
      })
      .OrderBy(x => OrdenElemento(x.Elemento))
      .ToDictionary(x => x.Elemento, x => x.Valor);

            return Ok(new
            {
                formula.nombreFormula,

                totalLibras = Math.Round(formula.totalLibras, 4),
                mezclaTotalQq = Math.Round(formula.mezclaTotalQq, 4),
                totalOnzas = Math.Round(formula.totalOnzas, 4),

                totalPlantas = formula.totalPlantas,
                totalAplicaciones = formula.totalAplicaciones,

                precioTotalFormula = Math.Round(formula.precioTotalFormula, 4),
                precioPorAplicacion = Math.Round(formula.precioPorAplicacion, 4),

                dosisPlantaAnualOz = Math.Round(formula.dosisPlantaAnualOz, 4),
                dosisPlantaPorAplicacionOz = Math.Round(formula.dosisPlantaPorAplicacionOz, 4),

                formulaComercial,
                detalle = detalleRespuesta
            });
        }
    }

}
