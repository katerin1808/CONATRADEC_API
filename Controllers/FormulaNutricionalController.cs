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

                    decimal totalLibras = dto.items.Sum(x => x.libras);
                    decimal mezclaTotalQq = totalLibras / 100m;

                    if (mezclaTotalQq <= 0)
                        return BadRequest(new { mensaje = "La mezcla total en QQ debe ser mayor a cero." });

                    decimal totalN = 0;
                    decimal totalP = 0;
                    decimal totalK = 0;
                    decimal totalCa = 0;
                    decimal totalMg = 0;
                    decimal totalS = 0;
                    decimal totalZn = 0;
                    decimal totalB = 0;

                    var detallesGuardar = new List<FormulaNutricionalDetalle>();
                    var detallesRespuesta = new List<FormulaNutricionalDetalleRespuestaDto>();

                    foreach (var item in dto.items)
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

                        bool fuenteAportaElementoBase = composicionFuente.Any(x => x.elementoQuimicosId == item.elementoQuimicosId);

                        if (!fuenteAportaElementoBase)
                            return BadRequest(new
                            {
                                mensaje = $"La fuente {fuente.nombreNutriente} no aporta el elemento {elementoBase.simboloElementoQuimico}."
                            });

                        decimal qq = item.libras / 100m;

                        decimal aporteN = 0;
                        decimal aporteP = 0;
                        decimal aporteK = 0;
                        decimal aporteCa = 0;
                        decimal aporteMg = 0;
                        decimal aporteS = 0;
                        decimal aporteZn = 0;
                        decimal aporteB = 0;

                        var aportesRespuesta = new Dictionary<string, decimal>
                    {
                        { "n", 0 },
                        { "p", 0 },
                        { "k", 0 },
                        { "ca", 0 },
                        { "mg", 0 },
                        { "s", 0 },
                        { "zn", 0 },
                        { "b", 0 }
                    };

                        foreach (var comp in composicionFuente)
                        {
                            string simbolo = comp.elementoQuimico!.simboloElementoQuimico.Trim().ToLower();

                            decimal aporte = qq * comp.cantidadAporte;

                            switch (simbolo)
                            {
                                case "n":
                                    aporteN = aporte;
                                    aportesRespuesta["n"] = Math.Round(aporte, 4);
                                    break;

                                case "p":
                                    aporteP = aporte;
                                    aportesRespuesta["p"] = Math.Round(aporte, 4);
                                    break;

                                case "k":
                                    aporteK = aporte;
                                    aportesRespuesta["k"] = Math.Round(aporte, 4);
                                    break;

                                case "ca":
                                    aporteCa = aporte;
                                    aportesRespuesta["ca"] = Math.Round(aporte, 4);
                                    break;

                                case "mg":
                                    aporteMg = aporte;
                                    aportesRespuesta["mg"] = Math.Round(aporte, 4);
                                    break;

                                case "s":
                                    aporteS = aporte;
                                    aportesRespuesta["s"] = Math.Round(aporte, 4);
                                    break;

                                case "zn":
                                    aporteZn = aporte;
                                    aportesRespuesta["zn"] = Math.Round(aporte, 4);
                                    break;

                                case "b":
                                    aporteB = aporte;
                                    aportesRespuesta["b"] = Math.Round(aporte, 4);
                                    break;
                            }
                        }

                        totalN += aporteN;
                        totalP += aporteP;
                        totalK += aporteK;
                        totalCa += aporteCa;
                        totalMg += aporteMg;
                        totalS += aporteS;
                        totalZn += aporteZn;
                        totalB += aporteB;

                        detallesGuardar.Add(new FormulaNutricionalDetalle
                        {
                            fuenteNutrientesId = item.fuenteNutrientesId,
                            elementoQuimicosId = item.elementoQuimicosId,
                            libras = item.libras,
                            qq = qq,
                            aporteN = aporteN,
                            aporteP = aporteP,
                            aporteK = aporteK,
                            aporteCa = aporteCa,
                            aporteMg = aporteMg,
                            aporteS = aporteS,
                            aporteZn = aporteZn,
                            aporteB = aporteB,
                            activo = true
                        });

                        detallesRespuesta.Add(new FormulaNutricionalDetalleRespuestaDto
                        {
                            fuente = fuente.nombreNutriente,
                            elemento = elementoBase.simboloElementoQuimico,
                            lb = item.libras,
                            qq = Math.Round(qq, 4),
                            aportes = aportesRespuesta
                        });
                    }

                    var formula = new FormulaNutricional
                    {
                        nombreFormula = dto.nombreFormula,
                        totalLibras = totalLibras,
                        mezclaTotalQq = mezclaTotalQq,

                        n = Math.Round(totalN / mezclaTotalQq, 4),
                        p = Math.Round(totalP / mezclaTotalQq, 4),
                        k = Math.Round(totalK / mezclaTotalQq, 4),
                        ca = Math.Round(totalCa / mezclaTotalQq, 4),
                        mg = Math.Round(totalMg / mezclaTotalQq, 4),
                        s = Math.Round(totalS / mezclaTotalQq, 4),
                        zn = Math.Round(totalZn / mezclaTotalQq, 4),
                        b = Math.Round(totalB / mezclaTotalQq, 4),

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
                        formulaComercial = new Dictionary<string, decimal>
                    {
                        { "n", formula.n },
                        { "p", formula.p },
                        { "k", formula.k },
                        { "ca", formula.ca },
                        { "mg", formula.mg },
                        { "s", formula.s },
                        { "zn", formula.zn },
                        { "b", formula.b }
                    },
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
                        detalle = ex.Message
                    });
                }
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

            return Ok(new
            {
                formula.formulaNutricionalId,
                formula.nombreFormula,
                formula.totalLibras,
                formula.mezclaTotalQq,
                formulaComercial = new
                {
                    formula.n,
                    formula.p,
                    formula.k,
                    formula.ca,
                    formula.mg,
                    formula.s,
                    formula.zn,
                    formula.b
                }
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

            var detalle = await _db.formulaNutricionalDetalle
                .Include(x => x.fuenteNutriente)
                .Include(x => x.elementoQuimico)
                .Where(x => x.formulaNutricionalId == formula.formulaNutricionalId && x.activo)
                .Select(x => new
                {
                    x.formulaNutricionalDetalleId,
                    fuente = x.fuenteNutriente != null ? x.fuenteNutriente.nombreNutriente : "",
                    elemento = x.elementoQuimico != null ? x.elementoQuimico.simboloElementoQuimico : "",
                    lb = x.libras,
                    qq = x.qq,
                    aportes = new
                    {
                        n = x.aporteN,
                        p = x.aporteP,
                        k = x.aporteK,
                        ca = x.aporteCa,
                        mg = x.aporteMg,
                        s = x.aporteS,
                        zn = x.aporteZn,
                        b = x.aporteB
                    }
                })
                .ToListAsync();

            return Ok(new
            {
                formula.formulaNutricionalId,
                formula.nombreFormula,
                formula.totalLibras,
                formula.mezclaTotalQq,
                formulaComercial = new
                {
                    formula.n,
                    formula.p,
                    formula.k,
                    formula.ca,
                    formula.mg,
                    formula.s,
                    formula.zn,
                    formula.b
                },
                detalle
            });
        }
    }

}
