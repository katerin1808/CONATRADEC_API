using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{

    [ApiController]
    [Route("api/configuracion/tipos-cultivo")]
    public class TipoCultivoController : ControllerBase
    {
        private readonly DBContext _db;

        public TipoCultivoController(DBContext db)
        {
            _db = db;
        }

        // Lista únicamente los registros activos
        [HttpGet]
        public async Task<IActionResult> Listar()
        {
            var data = await _db.TipoCultivos
                .AsNoTracking()
                .Where(x => x.activo)
                .OrderBy(x => x.nombreTipoCultivo)
                .Select(x => new
                {
                    x.tipoCultivoId,
                    x.nombreTipoCultivo,
                    x.descripcionTipoCultivo,
                    x.activo
                })
                .ToListAsync();

            return Ok(data);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Obtener(int id)
        {
            var data = await _db.TipoCultivos
                .AsNoTracking()
                .Where(x => x.tipoCultivoId == id)
                .Select(x => new
                {
                    x.tipoCultivoId,
                    x.nombreTipoCultivo,
                    x.descripcionTipoCultivo,
                    x.activo
                })
                .FirstOrDefaultAsync();

            if (data == null)
            {
                return NotFound(new
                {
                    mensaje = "Tipo de cultivo no encontrado."
                });
            }

            return Ok(data);
        }

        // El ID no se envía porque lo genera la base de datos
        [HttpPost]
        public async Task<IActionResult> Crear(
            [FromBody] CrearTipoCultivoDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.nombreTipoCultivo))
            {
                return BadRequest(new
                {
                    mensaje = "El nombre del tipo de cultivo es obligatorio."
                });
            }

            var nombre = dto.nombreTipoCultivo
                .Trim()
                .ToUpper();

            var existe = await _db.TipoCultivos.AnyAsync(x =>
                x.nombreTipoCultivo.Trim().ToUpper() == nombre);

            if (existe)
            {
                return Conflict(new
                {
                    mensaje = "Ya existe un tipo de cultivo con ese nombre."
                });
            }

            var entidad = new TipoCultivo
            {
                nombreTipoCultivo = nombre,
                descripcionTipoCultivo =
                    dto.descripcionTipoCultivo?.Trim() ?? string.Empty,

                // Todo nuevo registro se crea activo
                activo = true
            };

            _db.TipoCultivos.Add(entidad);
            await _db.SaveChangesAsync();

            return StatusCode(StatusCodes.Status201Created, new
            {
                mensaje = "Tipo de cultivo creado correctamente.",
                data = new
                {
                    entidad.tipoCultivoId,
                    entidad.nombreTipoCultivo,
                    entidad.descripcionTipoCultivo,
                    entidad.activo
                }
            });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Actualizar(
         int id,
         [FromBody] ActualizarTipoCultivoDto dto)
        {
            var entidad = await _db.TipoCultivos
                .FirstOrDefaultAsync(x => x.tipoCultivoId == id);

            if (entidad == null)
            {
                return NotFound(new
                {
                    mensaje = "Tipo de cultivo no encontrado."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.nombreTipoCultivo))
            {
                return BadRequest(new
                {
                    mensaje = "El nombre del tipo de cultivo es obligatorio."
                });
            }

            var nombre = dto.nombreTipoCultivo
                .Trim()
                .ToUpper();

            var existe = await _db.TipoCultivos.AnyAsync(x =>
                x.tipoCultivoId != id &&
                x.nombreTipoCultivo.Trim().ToUpper() == nombre);

            if (existe)
            {
                return Conflict(new
                {
                    mensaje = "Ya existe otro tipo de cultivo con ese nombre."
                });
            }

            entidad.nombreTipoCultivo = nombre;
            entidad.descripcionTipoCultivo =
                dto.descripcionTipoCultivo?.Trim() ?? string.Empty;

            // No se modifica:
            // entidad.tipoCultivoId
            // entidad.activo

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Tipo de cultivo actualizado correctamente.",
                data = new
                {
                    entidad.tipoCultivoId,
                    entidad.nombreTipoCultivo,
                    entidad.descripcionTipoCultivo,
                    entidad.activo
                }
            });
        }

        [HttpPut("{id:int}/eliminar")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var entidad = await _db.TipoCultivos
                .FirstOrDefaultAsync(x =>
                    x.tipoCultivoId == id &&
                    x.activo);

            if (entidad == null)
            {
                return NotFound(new
                {
                    mensaje = "Tipo de cultivo no encontrado o ya está desactivado."
                });
            }

            var dependencias = new List<string>();

            /*
             * Configuración activa.
             */
            var usadoEnRangos =
                await _db.ParametroRangoNutrienteCultivo
                    .AnyAsync(x =>
                        x.tipoCultivoId == id &&
                        x.activo);

            if (usadoEnRangos)
            {
                dependencias.Add("rangos nutricionales por cultivo");
            }

            /*
             * Datos históricos.
             *
             * No se filtra por activo porque el cultivo debe seguir
             * disponible para consultar cálculos anteriores.
             */
            var usadoEnCalculos = await _db.AnalisisSueloCalculos
                .AnyAsync(x =>
                    x.tipoCultivoId == id);

            if (usadoEnCalculos)
            {
                dependencias.Add("cálculos de análisis de suelo");
            }

            if (dependencias.Count > 0)
            {
                return Conflict(new
                {
                    mensaje =
                        "No se puede eliminar el tipo de cultivo porque está siendo utilizado.",

                    tipoCultivo = new
                    {
                        entidad.tipoCultivoId,
                        entidad.nombreTipoCultivo
                    },

                    usadoEn = dependencias
                });
            }

            entidad.activo = false;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Tipo de cultivo desactivado correctamente.",
                data = new
                {
                    entidad.tipoCultivoId,
                    entidad.nombreTipoCultivo,
                    entidad.activo
                }
            });
        }
    }
}