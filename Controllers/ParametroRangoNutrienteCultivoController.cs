using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.ParametroRangoNutrienteCultivoDto;

namespace CONATRADEC_API.Controllers
{


        [ApiController]
        [Route("api/configuracion/rangos-nutrientes")]
        public class ParametroRangoNutrienteCultivoController : ControllerBase
        {
            private readonly DBContext _db;

            public ParametroRangoNutrienteCultivoController(DBContext db)
            {
                _db = db;
            }

            // Lista únicamente los registros activos
            [HttpGet]
            public async Task<IActionResult> Listar()
            {
                var data = await _db.ParametroRangoNutrienteCultivo
                    .AsNoTracking()
                    .Where(x => x.activo)
                    .OrderBy(x => x.TipoCultivo.nombreTipoCultivo)
                    .ThenBy(x => x.ElementoQuimico.nombreElementoQuimico)
                    .Select(x => new
                    {
                        x.parametroRangoNutrienteCultivoId,

                        x.tipoCultivoId,
                        nombreTipoCultivo =
                            x.TipoCultivo.nombreTipoCultivo,

                        x.elementoQuimicosId,
                        nombreElementoQuimico =
                            x.ElementoQuimico.nombreElementoQuimico,

                        simboloElementoQuimico =
                            x.ElementoQuimico.simboloElementoQuimico,

                        x.valorMinimo,
                        x.valorMaximo,
                        x.unidadBase,
                        x.descripcionParametro,
                        x.activo
                    })
                    .ToListAsync();

                return Ok(data);
            }

            // Obtiene un registro activo por ID
            [HttpGet("{id:int}")]
            public async Task<IActionResult> Obtener(int id)
            {
                var data = await _db.ParametroRangoNutrienteCultivo
                    .AsNoTracking()
                    .Where(x =>
                        x.parametroRangoNutrienteCultivoId == id &&
                        x.activo)
                    .Select(x => new
                    {
                        x.parametroRangoNutrienteCultivoId,

                        x.tipoCultivoId,
                        nombreTipoCultivo =
                            x.TipoCultivo.nombreTipoCultivo,

                        x.elementoQuimicosId,
                        nombreElementoQuimico =
                            x.ElementoQuimico.nombreElementoQuimico,

                        simboloElementoQuimico =
                            x.ElementoQuimico.simboloElementoQuimico,

                        x.valorMinimo,
                        x.valorMaximo,
                        x.unidadBase,
                        x.descripcionParametro,
                        x.activo
                    })
                    .FirstOrDefaultAsync();

                if (data == null)
                {
                    return NotFound(new
                    {
                        mensaje = "Rango nutricional no encontrado."
                    });
                }

                return Ok(data);
            }

            // Crea sin solicitar ID principal ni activo
            [HttpPost]
            public async Task<IActionResult> Crear(
                [FromBody] CrearParametroRangoNutrienteCultivoDto dto)
            {
                var error = await ValidarDatos(
                    dto.tipoCultivoId,
                    dto.elementoQuimicosId,
                    dto.valorMinimo,
                    dto.valorMaximo,
                    dto.unidadBase,
                    dto.descripcionParametro);

                if (error != null)
                {
                    return BadRequest(new
                    {
                        mensaje = error
                    });
                }

                var existente = await _db.ParametroRangoNutrienteCultivo
                    .FirstOrDefaultAsync(x =>
                        x.tipoCultivoId == dto.tipoCultivoId &&
                        x.elementoQuimicosId == dto.elementoQuimicosId);

                if (existente != null && existente.activo)
                {
                    return Conflict(new
                    {
                        mensaje =
                            "Ya existe un rango activo para este cultivo y elemento químico."
                    });
                }

                // Si ya existía, pero fue eliminado, se vuelve a activar
                if (existente != null && !existente.activo)
                {
                    existente.valorMinimo = dto.valorMinimo;
                    existente.valorMaximo = dto.valorMaximo;
                    existente.unidadBase = dto.unidadBase.Trim();
                    existente.descripcionParametro =
                        dto.descripcionParametro.Trim();

                    existente.activo = true;

                    await _db.SaveChangesAsync();

                    return Ok(new
                    {
                        mensaje =
                            "Rango nutricional reactivado correctamente.",

                        data = await ObtenerDetalle(
                            existente.parametroRangoNutrienteCultivoId)
                    });
                }

                var entidad =
                    new CONATRADEC_API.Models.ParametroRangoNutrienteCultivo
                    {
                        tipoCultivoId = dto.tipoCultivoId,

                        elementoQuimicosId =
                            dto.elementoQuimicosId,

                        valorMinimo = dto.valorMinimo,
                        valorMaximo = dto.valorMaximo,

                        unidadBase =
                            dto.unidadBase.Trim(),

                        descripcionParametro =
                            dto.descripcionParametro.Trim(),

                        activo = true
                    };

                _db.ParametroRangoNutrienteCultivo.Add(entidad);

                await _db.SaveChangesAsync();

                return StatusCode(
                    StatusCodes.Status201Created,
                    new
                    {
                        mensaje =
                            "Rango nutricional creado correctamente.",

                        data = await ObtenerDetalle(
                            entidad.parametroRangoNutrienteCultivoId)
                    });
            }

            // El ID principal va en la URL
            // No permite modificar el ID principal ni activo
            [HttpPut("{id:int}")]
            public async Task<IActionResult> Actualizar(
                int id,
                [FromBody] ActualizarParametroRangoNutrienteCultivoDto dto)
            {
                var entidad = await _db.ParametroRangoNutrienteCultivo
                    .FirstOrDefaultAsync(x =>
                        x.parametroRangoNutrienteCultivoId == id &&
                        x.activo);

                if (entidad == null)
                {
                    return NotFound(new
                    {
                        mensaje = "Rango nutricional no encontrado."
                    });
                }

                var error = await ValidarDatos(
                    dto.tipoCultivoId,
                    dto.elementoQuimicosId,
                    dto.valorMinimo,
                    dto.valorMaximo,
                    dto.unidadBase,
                    dto.descripcionParametro);

                if (error != null)
                {
                    return BadRequest(new
                    {
                        mensaje = error
                    });
                }

                var existeOtro =
                    await _db.ParametroRangoNutrienteCultivo
                        .AnyAsync(x =>
                            x.parametroRangoNutrienteCultivoId != id &&
                            x.tipoCultivoId == dto.tipoCultivoId &&
                            x.elementoQuimicosId ==
                                dto.elementoQuimicosId &&
                            x.activo);

                if (existeOtro)
                {
                    return Conflict(new
                    {
                        mensaje =
                            "Ya existe otro rango activo para este cultivo y elemento químico."
                    });
                }

                entidad.tipoCultivoId =
                    dto.tipoCultivoId;

                entidad.elementoQuimicosId =
                    dto.elementoQuimicosId;

                entidad.valorMinimo =
                    dto.valorMinimo;

                entidad.valorMaximo =
                    dto.valorMaximo;

                entidad.unidadBase =
                    dto.unidadBase.Trim();

                entidad.descripcionParametro =
                    dto.descripcionParametro.Trim();

                // No se modifican:
                // entidad.parametroRangoNutrienteCultivoId
                // entidad.activo

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    mensaje =
                        "Rango nutricional actualizado correctamente.",

                    data = await ObtenerDetalle(id)
                });
            }

            // Eliminación lógica
            // No necesita body
            [HttpPut("{id:int}/eliminar")]
            public async Task<IActionResult> Eliminar(int id)
            {
                var entidad = await _db.ParametroRangoNutrienteCultivo
                    .FirstOrDefaultAsync(x =>
                        x.parametroRangoNutrienteCultivoId == id);

                if (entidad == null)
                {
                    return NotFound(new
                    {
                        mensaje = "Rango nutricional no encontrado."
                    });
                }

                if (!entidad.activo)
                {
                    return BadRequest(new
                    {
                        mensaje =
                            "El rango nutricional ya se encuentra eliminado."
                    });
                }

                entidad.activo = false;

                _db.Entry(entidad)
                    .Property(x => x.activo)
                    .IsModified = true;

                var filasActualizadas =
                    await _db.SaveChangesAsync();

                await _db.Entry(entidad).ReloadAsync();

                return Ok(new
                {
                    mensaje =
                        "Rango nutricional eliminado correctamente.",

                    filasActualizadas,

                    data = new
                    {
                        entidad.parametroRangoNutrienteCultivoId,
                        entidad.tipoCultivoId,
                        entidad.elementoQuimicosId,
                        entidad.activo
                    }
                });
            }

            private async Task<string?> ValidarDatos(
                int tipoCultivoId,
                int elementoQuimicosId,
                decimal valorMinimo,
                decimal valorMaximo,
                string unidadBase,
                string descripcionParametro)
            {
                if (tipoCultivoId <= 0)
                    return "Debe seleccionar un tipo de cultivo válido.";

                if (elementoQuimicosId <= 0)
                    return "Debe seleccionar un elemento químico válido.";

                if (valorMinimo < 0)
                    return "El valor mínimo no puede ser negativo.";

                if (valorMaximo <= valorMinimo)
                {
                    return
                        "El valor máximo debe ser mayor que el valor mínimo.";
                }

                if (string.IsNullOrWhiteSpace(unidadBase))
                    return "La unidad base es obligatoria.";

                if (string.IsNullOrWhiteSpace(descripcionParametro))
                    return "La descripción es obligatoria.";

                var cultivoExiste = await _db.TipoCultivos
                    .AnyAsync(x =>
                        x.tipoCultivoId == tipoCultivoId &&
                        x.activo);

                if (!cultivoExiste)
                {
                    return
                        "El tipo de cultivo no existe o está inactivo.";
                }

                var elementoExiste = await _db.elementoQuimico
                    .AnyAsync(x =>
                        x.elementoQuimicosId == elementoQuimicosId &&
                        x.activo);

                if (!elementoExiste)
                {
                    return
                        "El elemento químico no existe o está inactivo.";
                }

                return null;
            }

            private async Task<object?> ObtenerDetalle(int id)
            {
                return await _db.ParametroRangoNutrienteCultivo
                    .AsNoTracking()
                    .Where(x =>
                        x.parametroRangoNutrienteCultivoId == id)
                    .Select(x => new
                    {
                        x.parametroRangoNutrienteCultivoId,

                        x.tipoCultivoId,
                        nombreTipoCultivo =
                            x.TipoCultivo.nombreTipoCultivo,

                        x.elementoQuimicosId,
                        nombreElementoQuimico =
                            x.ElementoQuimico.nombreElementoQuimico,

                        simboloElementoQuimico =
                            x.ElementoQuimico.simboloElementoQuimico,

                        x.valorMinimo,
                        x.valorMaximo,
                        x.unidadBase,
                        x.descripcionParametro,
                        x.activo
                    })
                    .FirstOrDefaultAsync();
            }
        }
}

