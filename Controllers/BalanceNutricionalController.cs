using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.BalanceNutricionalDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/balance-nutricional")]
    public class BalanceNutricionalController : ControllerBase
    {
        private readonly DBContext _db;

        public BalanceNutricionalController(DBContext db)
        {
            _db = db;
        }

        [HttpPost("calcular")]
        public async Task<IActionResult> Calcular([FromBody] BalanceNutricionalCrearDto dto)
        {
            await using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                var terreno = await _db.Terreno
                .FirstOrDefaultAsync(x => x.terrenoId == dto.terrenoId && x.activo);

                if (terreno == null)
                    return BadRequest(new { mensaje = "El terreno no existe o está inactivo." });

                int totalPlantasUsadas = dto.totalPlantas.HasValue && dto.totalPlantas.Value > 0
                    ? dto.totalPlantas.Value
                    : terreno.cantidadPlantasTerreno;

                if (totalPlantasUsadas <= 0)
                    return BadRequest(new { mensaje = "Debe ingresar la cantidad de plantas o configurar la cantidad en el terreno." });

                if (dto.totalAplicaciones <= 0)
                    return BadRequest(new { mensaje = "El total de aplicaciones debe ser mayor a cero." });

                if (dto.items == null || !dto.items.Any())
                    return BadRequest(new { mensaje = "Debe enviar al menos un requerimiento." });

                var elementos = await _db.elementoQuimico
                    .Where(x => x.activo)
                    .ToDictionaryAsync(
                        x => x.elementoQuimicosId,
                        x => x.simboloElementoQuimico.Trim().ToUpper()
                    );

                int Prioridad(string simbolo)
                {
                    return simbolo switch
                    {
                        "B" => 1,
                        "ZN" => 2,
                        "MG" => 3,
                        "CA" => 4,
                        "P" => 5,
                        "K" => 6,
                        "N" => 7,
                        _ => 99
                    };
                }

                var itemsOrdenados = dto.items
                    .OrderBy(x => elementos.ContainsKey(x.elementoQuimicosId)
                        ? Prioridad(elementos[x.elementoQuimicosId])
                        : 99)
                    .ToList();

                decimal totalLibras = 0;
                decimal precioTotalFormula = 0;

                var aportesAcumulados = new Dictionary<string, decimal>();

                var detallesGuardar = new List<BalanceNutricionalDetalle>();
                var detallesRespuesta = new List<BalanceNutricionalDetalleRespuestaDto>();

                foreach (var item in itemsOrdenados)
                {
                    if (item.requerimientoLibras <= 0)
                        return BadRequest(new { mensaje = "El requerimiento en libras debe ser mayor a cero." });

                    var fuente = await _db.fuenteNutriente
                        .FirstOrDefaultAsync(x => x.fuenteNutrientesId == item.fuenteNutrientesId && x.activo);

                    if (fuente == null)
                        return BadRequest(new { mensaje = $"La fuente nutriente {item.fuenteNutrientesId} no existe o está inactiva." });

                    var elemento = await _db.elementoQuimico
                        .FirstOrDefaultAsync(x => x.elementoQuimicosId == item.elementoQuimicosId && x.activo);

                    if (elemento == null)
                        return BadRequest(new { mensaje = $"El elemento químico {item.elementoQuimicosId} no existe o está inactivo." });

                    var composicionFuente = await _db.fuenteNutrienteElementoQuimico
                        .Include(x => x.elementoQuimico)
                        .Where(x => x.fuenteNutrientesId == item.fuenteNutrientesId && x.activo)
                        .ToListAsync();

                    var aportePrincipal = composicionFuente
                        .FirstOrDefault(x => x.elementoQuimicosId == item.elementoQuimicosId);

                    if (aportePrincipal == null || aportePrincipal.cantidadAporte <= 0)
                        return BadRequest(new { mensaje = $"La fuente {fuente.nombreNutriente} no aporta {elemento.simboloElementoQuimico.Trim()}." });

                    string simboloPrincipal = elemento.simboloElementoQuimico.Trim().ToUpper();

                    decimal yaAportado = aportesAcumulados.ContainsKey(simboloPrincipal)
                        ? aportesAcumulados[simboloPrincipal]
                        : 0;

                    decimal requerimientoPendiente = item.requerimientoLibras - yaAportado;

                    if (requerimientoPendiente < 0)
                        requerimientoPendiente = 0;

                    decimal librasFuenteAnual = requerimientoPendiente / (aportePrincipal.cantidadAporte / 100m);
                    decimal librasFuentePorAplicacion = librasFuenteAnual / dto.totalAplicaciones;
                    decimal onzasFuenteAnual = librasFuenteAnual * 16m;

                    decimal quintalesAnuales = librasFuenteAnual / 100m;
                    decimal precioPorQuintal = fuente.precioNutriente;
                    decimal subtotalFuente = quintalesAnuales * precioPorQuintal;

                    totalLibras += librasFuenteAnual;
                    precioTotalFormula += subtotalFuente;

                    foreach (var comp in composicionFuente)
                    {
                        string simbolo = comp.elementoQuimico!.simboloElementoQuimico.Trim().ToUpper();
                        decimal aporte = librasFuenteAnual * (comp.cantidadAporte / 100m);

                        if (!aportesAcumulados.ContainsKey(simbolo))
                            aportesAcumulados[simbolo] = 0;

                        aportesAcumulados[simbolo] += aporte;
                    }

                    detallesGuardar.Add(new BalanceNutricionalDetalle
                    {
                        fuenteNutrientesId = item.fuenteNutrientesId,
                        elementoQuimicosId = item.elementoQuimicosId,
                        requerimientoLibras = item.requerimientoLibras,
                        librasFuenteAnual = librasFuenteAnual,
                        librasFuentePorAplicacion = librasFuentePorAplicacion,
                        quintalesAnuales = quintalesAnuales,
                        precioPorQuintal = precioPorQuintal,
                        subtotalFuente = subtotalFuente,
                        activo = true
                    });

                    detallesRespuesta.Add(new BalanceNutricionalDetalleRespuestaDto
                    {
                        fuente = fuente.nombreNutriente,
                        elemento = simboloPrincipal,
                        requerimientoLibras = Math.Round(item.requerimientoLibras, 4),

                        librasAnuales = Math.Round(librasFuenteAnual, 4),
                        onzasAnuales = Math.Round(onzasFuenteAnual, 4),

                        dosAplicaciones = Math.Round(onzasFuenteAnual / 2m, 4),
                        tresAplicaciones = Math.Round(onzasFuenteAnual / 3m, 4),

                        quintalesAnuales = Math.Round(quintalesAnuales, 4),
                        precioPorQuintal = Math.Round(precioPorQuintal, 4),
                        subtotalFuente = Math.Round(subtotalFuente, 4)
                    });
                }

                decimal totalOnzas = totalLibras * 16m;
                decimal dosisPlantaAnualOz = totalOnzas / totalPlantasUsadas;
                decimal dosisPlantaPorAplicacionOz = dosisPlantaAnualOz / dto.totalAplicaciones;
                decimal totalMezclaQq = totalLibras / 100m;

                var balance = new BalanceNutricional
                {
                    nombreFormula = dto.nombreFormula,
                    terrenoId = terreno.terrenoId,
                    totalPlantas = totalPlantasUsadas,
                    totalAplicaciones = dto.totalAplicaciones,
                    totalLibras = totalLibras,
                    totalOnzas = totalOnzas,
                    totalMezclaQq = totalMezclaQq,
                    precioTotalFormula = precioTotalFormula,
                    onzasPorPlantaAnual = dosisPlantaAnualOz,
                    onzasPorPlantaPorAplicacion = dosisPlantaPorAplicacionOz,
                    activo = true
                };

                _db.balanceNutricional.Add(balance);
                await _db.SaveChangesAsync();

                foreach (var detalle in detallesGuardar)
                    detalle.balanceNutricionalId = balance.balanceNutricionalId;

                _db.balanceNutricionalDetalle.AddRange(detallesGuardar);
                await _db.SaveChangesAsync();

                await trans.CommitAsync();

                return Ok(new BalanceNutricionalRespuestaDto
                {
                    balanceNutricionalId = balance.balanceNutricionalId,
                    nombreFormula = balance.nombreFormula,

                    totalMezclaLb = Math.Round(balance.totalLibras, 4),
                    totalMezclaOz = Math.Round(balance.totalOnzas, 4),
                    totalMezclaQq = Math.Round(balance.totalMezclaQq, 4),
                    precioTotalFormula = Math.Round(balance.precioTotalFormula, 4),
                    precioPorAplicacion = Math.Round(balance.precioTotalFormula / balance.totalAplicaciones, 4),

                    librasPorDosAplicaciones = Math.Round(balance.totalLibras / 2m, 4),
                    librasPorTresAplicaciones = Math.Round(balance.totalLibras / 3m, 4),

                    totalPlantas = balance.totalPlantas,
                    dosisPlantaAnualOz = Math.Round(balance.onzasPorPlantaAnual, 4),

                    dosAplicaciones = new AplicacionResumenDto
                    {
                        dosisPlantaOz = Math.Round(balance.onzasPorPlantaAnual / 2m, 4)
                    },

                    tresAplicaciones = new AplicacionResumenDto
                    {
                        dosisPlantaOz = Math.Round(balance.onzasPorPlantaAnual / 3m, 4)
                    },

                    detalle = detallesRespuesta
                });
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();

                return StatusCode(500, new
                {
                    mensaje = "Error al calcular balance nutricional.",
                    detalle = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }

        [HttpGet("ultimo")]
        public async Task<IActionResult> Ultimo()
        {
            var balance = await _db.balanceNutricional
                .Where(x => x.activo)
                .OrderByDescending(x => x.balanceNutricionalId)
                .FirstOrDefaultAsync();

            if (balance == null)
                return NotFound(new { mensaje = "No hay balances registrados." });

            var detalle = await _db.balanceNutricionalDetalle
                .Include(x => x.fuenteNutriente)
                .Include(x => x.elementoQuimico)
                .Where(x => x.balanceNutricionalId == balance.balanceNutricionalId && x.activo)
                .Select(x => new BalanceNutricionalDetalleRespuestaDto
                {
                    fuente = x.fuenteNutriente != null ? x.fuenteNutriente.nombreNutriente : "",
                    elemento = x.elementoQuimico != null ? x.elementoQuimico.simboloElementoQuimico.Trim() : "",

                    requerimientoLibras = Math.Round(x.requerimientoLibras, 4),
                    librasAnuales = Math.Round(x.librasFuenteAnual, 4),
                    onzasAnuales = Math.Round(x.librasFuenteAnual * 16m, 4),

                    dosAplicaciones = Math.Round((x.librasFuenteAnual * 16m) / 2m, 4),
                    tresAplicaciones = Math.Round((x.librasFuenteAnual * 16m) / 3m, 4),

                    quintalesAnuales = Math.Round(x.quintalesAnuales, 4),
                    precioPorQuintal = Math.Round(x.precioPorQuintal, 4),
                    subtotalFuente = Math.Round(x.subtotalFuente, 4)
                })
                .ToListAsync();

            return Ok(new BalanceNutricionalRespuestaDto
            {
                balanceNutricionalId = balance.balanceNutricionalId,
                nombreFormula = balance.nombreFormula,

                totalMezclaLb = Math.Round(balance.totalLibras, 4),
                totalMezclaOz = Math.Round(balance.totalOnzas, 4),
                totalMezclaQq = Math.Round(balance.totalMezclaQq, 4),
                precioTotalFormula = Math.Round(balance.precioTotalFormula, 4),
                precioPorAplicacion = Math.Round(balance.precioTotalFormula / balance.totalAplicaciones, 4),

                librasPorDosAplicaciones = Math.Round(balance.totalLibras / 2m, 4),
                librasPorTresAplicaciones = Math.Round(balance.totalLibras / 3m, 4),

                totalPlantas = balance.totalPlantas,
                dosisPlantaAnualOz = Math.Round(balance.onzasPorPlantaAnual, 4),

                dosAplicaciones = new AplicacionResumenDto
                {
                    dosisPlantaOz = Math.Round(balance.onzasPorPlantaAnual / 2m, 4)
                },

                tresAplicaciones = new AplicacionResumenDto
                {
                    dosisPlantaOz = Math.Round(balance.onzasPorPlantaAnual / 3m, 4)
                },

                detalle = detalle
            });
        }
    }
}