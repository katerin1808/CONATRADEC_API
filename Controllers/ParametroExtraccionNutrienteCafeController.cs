using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.ParametroExtraccionNutrienteDto;

namespace CONATRADEC_API.Controllers
{
   
        [ApiController]
        [Route("api/configuracion/extraccion-nutrientes")]
        public class ParametroExtraccionNutrienteCafeController : ControllerBase
        {
            private readonly DBContext _db;

            public ParametroExtraccionNutrienteCafeController(DBContext db)
            {
                _db = db;
            }

            // Lista únicamente los parámetros activos.
            [HttpGet]
            public async Task<IActionResult> Listar()
            {
                var data = await _db.ParametroExtraccionNutrienteCafe
                    .AsNoTracking()
                    .Where(x => x.activo)
                    .OrderBy(x => x.ElementoQuimico.nombreElementoQuimico)
                    .Select(x => new
                    {
                        x.parametroExtraccionNutrienteCafeId,
                        x.elementoQuimicosId,

                        nombreElementoQuimico =
                            x.ElementoQuimico.nombreElementoQuimico,

                        simboloElementoQuimico =
                            x.ElementoQuimico.simboloElementoQuimico,

                        x.cantidadExtraidaPorQQOro,
                        x.descripcionParametro,
                        x.activo
                    })
                    .ToListAsync();

                return Ok(data);
            }

            // Obtiene un parámetro activo por su ID.
            [HttpGet("{id:int}")]
            public async Task<IActionResult> Obtener(int id)
            {
                var data = await _db.ParametroExtraccionNutrienteCafe
                    .AsNoTracking()
                    .Where(x =>
                        x.parametroExtraccionNutrienteCafeId == id &&
                        x.activo)
                    .Select(x => new
                    {
                        x.parametroExtraccionNutrienteCafeId,
                        x.elementoQuimicosId,

                        nombreElementoQuimico =
                            x.ElementoQuimico.nombreElementoQuimico,

                        simboloElementoQuimico =
                            x.ElementoQuimico.simboloElementoQuimico,

                        x.cantidadExtraidaPorQQOro,
                        x.descripcionParametro,
                        x.activo
                    })
                    .FirstOrDefaultAsync();

                if (data == null)
                {
                    return NotFound(new
                    {
                        mensaje = "Parámetro de extracción no encontrado."
                    });
                }

                return Ok(data);
            }

            // Crea un parámetro sin solicitar el ID principal ni activo.
            [HttpPost]
            public async Task<IActionResult> Crear(
                [FromBody] CrearParametroExtraccionNutrienteCafeDto dto)
            {
                if (string.IsNullOrWhiteSpace(dto.descripcionParametro))
                {
                    return BadRequest(new
                    {
                        mensaje = "La descripción del parámetro es obligatoria."
                    });
                }

                if (dto.cantidadExtraidaPorQQOro <= 0)
                {
                    return BadRequest(new
                    {
                        mensaje = "La cantidad extraída debe ser mayor que cero."
                    });
                }

                var elemento = await _db.elementoQuimico
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.elementoQuimicosId == dto.elementoQuimicosId &&
                        x.activo);

                if (elemento == null)
                {
                    return BadRequest(new
                    {
                        mensaje = "El elemento químico no existe o está inactivo."
                    });
                }

                var parametroExistente =
                    await _db.ParametroExtraccionNutrienteCafe
                        .AnyAsync(x =>
                            x.elementoQuimicosId == dto.elementoQuimicosId &&
                            x.activo);

                if (parametroExistente)
                {
                    return Conflict(new
                    {
                        mensaje =
                            "Ya existe un parámetro de extracción activo para este elemento químico."
                    });
                }

                var entidad = new ParametroExtraccionNutrienteCafe
                {
                    elementoQuimicosId = dto.elementoQuimicosId,

                    cantidadExtraidaPorQQOro =
                        dto.cantidadExtraidaPorQQOro,

                    descripcionParametro =
                        dto.descripcionParametro.Trim(),

                    // Todo registro nuevo se crea activo.
                    activo = true
                };

                _db.ParametroExtraccionNutrienteCafe.Add(entidad);

                await _db.SaveChangesAsync();

                return StatusCode(
                    StatusCodes.Status201Created,
                    new
                    {
                        mensaje =
                            "Parámetro de extracción creado correctamente.",

                        data = new
                        {
                            entidad.parametroExtraccionNutrienteCafeId,
                            entidad.elementoQuimicosId,
                            elemento.nombreElementoQuimico,
                            elemento.simboloElementoQuimico,
                            entidad.cantidadExtraidaPorQQOro,
                            entidad.descripcionParametro,
                            entidad.activo
                        }
                    });
            }

            // Actualiza sin modificar el ID principal ni activo.
            [HttpPut("{id:int}")]
            public async Task<IActionResult> Actualizar(
                int id,
                [FromBody] ActualizarParametroExtraccionNutrienteCafeDto dto)
            {
                var entidad =
                    await _db.ParametroExtraccionNutrienteCafe
                        .FirstOrDefaultAsync(x =>
                            x.parametroExtraccionNutrienteCafeId == id &&
                            x.activo);

                if (entidad == null)
                {
                    return NotFound(new
                    {
                        mensaje = "Parámetro de extracción no encontrado."
                    });
                }

                if (string.IsNullOrWhiteSpace(dto.descripcionParametro))
                {
                    return BadRequest(new
                    {
                        mensaje = "La descripción del parámetro es obligatoria."
                    });
                }

                if (dto.cantidadExtraidaPorQQOro <= 0)
                {
                    return BadRequest(new
                    {
                        mensaje = "La cantidad extraída debe ser mayor que cero."
                    });
                }

                var elemento = await _db.elementoQuimico
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x =>
                        x.elementoQuimicosId == dto.elementoQuimicosId &&
                        x.activo);

                if (elemento == null)
                {
                    return BadRequest(new
                    {
                        mensaje = "El elemento químico no existe o está inactivo."
                    });
                }

                var existeOtro =
                    await _db.ParametroExtraccionNutrienteCafe
                        .AnyAsync(x =>
                            x.parametroExtraccionNutrienteCafeId != id &&
                            x.elementoQuimicosId == dto.elementoQuimicosId &&
                            x.activo);

                if (existeOtro)
                {
                    return Conflict(new
                    {
                        mensaje =
                            "Ya existe otro parámetro activo para este elemento químico."
                    });
                }

                entidad.elementoQuimicosId =
                    dto.elementoQuimicosId;

                entidad.cantidadExtraidaPorQQOro =
                    dto.cantidadExtraidaPorQQOro;

                entidad.descripcionParametro =
                    dto.descripcionParametro.Trim();

                // No se modifican:
                // entidad.parametroExtraccionNutrienteCafeId
                // entidad.activo

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    mensaje =
                        "Parámetro de extracción actualizado correctamente.",

                    data = new
                    {
                        entidad.parametroExtraccionNutrienteCafeId,
                        entidad.elementoQuimicosId,
                        elemento.nombreElementoQuimico,
                        elemento.simboloElementoQuimico,
                        entidad.cantidadExtraidaPorQQOro,
                        entidad.descripcionParametro,
                        entidad.activo
                    }
                });
            }

            // Eliminación lógica. No necesita body.
            [HttpPut("{id:int}/eliminar")]
            public async Task<IActionResult> Eliminar(int id)
            {
                var entidad =
                    await _db.ParametroExtraccionNutrienteCafe
                        .FirstOrDefaultAsync(x =>
                            x.parametroExtraccionNutrienteCafeId == id);

                if (entidad == null)
                {
                    return NotFound(new
                    {
                        mensaje = "Parámetro de extracción no encontrado."
                    });
                }

                if (!entidad.activo)
                {
                    return BadRequest(new
                    {
                        mensaje =
                            "El parámetro de extracción ya se encuentra eliminado."
                    });
                }

                entidad.activo = false;

                // Marca específicamente la propiedad como modificada.
                _db.Entry(entidad)
                    .Property(x => x.activo)
                    .IsModified = true;

                var filasActualizadas =
                    await _db.SaveChangesAsync();

                // Vuelve a leer el valor guardado desde la base de datos.
                await _db.Entry(entidad).ReloadAsync();

                return Ok(new
                {
                    mensaje =
                        "Parámetro de extracción eliminado correctamente.",

                    filasActualizadas,

                    data = new
                    {
                        entidad.parametroExtraccionNutrienteCafeId,
                        entidad.elementoQuimicosId,
                        entidad.cantidadExtraidaPorQQOro,
                        entidad.activo
                    }
                });
            }
        }
    }
