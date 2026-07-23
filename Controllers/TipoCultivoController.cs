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
                .Where(x =>
                    x.tipoCultivoId == id)
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
                    mensaje =
                        "Tipo de cultivo no encontrado."
                });
            }

            return Ok(data);
        }

        [HttpPost]
        public async Task<IActionResult> Crear(
            [FromBody] CrearTipoCultivoDto dto)
        {
            if (string.IsNullOrWhiteSpace(
                    dto.nombreTipoCultivo))
            {
                return BadRequest(new
                {
                    mensaje =
                        "El nombre del tipo de cultivo es obligatorio."
                });
            }

            string nombre =
                dto.nombreTipoCultivo
                    .Trim()
                    .ToUpperInvariant();

            string descripcion =
                dto.descripcionTipoCultivo?
                    .Trim() ??
                string.Empty;

            TipoCultivo? existente =
                await _db.TipoCultivos
                    .FirstOrDefaultAsync(x =>
                        x.nombreTipoCultivo
                            .Trim()
                            .ToUpper() ==
                        nombre);

            if (existente != null &&
                existente.activo)
            {
                return Conflict(new
                {
                    mensaje =
                        "Ya existe un tipo de cultivo activo con ese nombre."
                });
            }

            /*
             * El listado muestra únicamente registros activos. Antes,
             * un registro desactivado no era visible, pero impedía volver
             * a utilizar su nombre. Ahora se reactiva y se actualiza.
             */
            if (existente != null &&
                !existente.activo)
            {
                existente.nombreTipoCultivo =
                    nombre;

                existente.descripcionTipoCultivo =
                    descripcion;

                existente.activo = true;

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    mensaje =
                        "Tipo de cultivo reactivado correctamente.",

                    data = new
                    {
                        existente.tipoCultivoId,
                        existente.nombreTipoCultivo,
                        existente.descripcionTipoCultivo,
                        existente.activo
                    }
                });
            }

            var entidad = new TipoCultivo
            {
                nombreTipoCultivo = nombre,

                descripcionTipoCultivo =
                    descripcion,

                activo = true
            };

            _db.TipoCultivos.Add(entidad);

            await _db.SaveChangesAsync();

            return StatusCode(
                StatusCodes.Status201Created,
                new
                {
                    mensaje =
                        "Tipo de cultivo creado correctamente.",

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
            TipoCultivo? entidad =
                await _db.TipoCultivos
                    .FirstOrDefaultAsync(x =>
                        x.tipoCultivoId == id);

            if (entidad == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "Tipo de cultivo no encontrado."
                });
            }

            if (string.IsNullOrWhiteSpace(
                    dto.nombreTipoCultivo))
            {
                return BadRequest(new
                {
                    mensaje =
                        "El nombre del tipo de cultivo es obligatorio."
                });
            }

            string nombre =
                dto.nombreTipoCultivo
                    .Trim()
                    .ToUpperInvariant();

            bool existeOtro =
                await _db.TipoCultivos
                    .AnyAsync(x =>
                        x.tipoCultivoId != id &&
                        x.activo &&
                        x.nombreTipoCultivo
                            .Trim()
                            .ToUpper() ==
                        nombre);

            if (existeOtro)
            {
                return Conflict(new
                {
                    mensaje =
                        "Ya existe otro tipo de cultivo activo con ese nombre."
                });
            }

            entidad.nombreTipoCultivo =
                nombre;

            entidad.descripcionTipoCultivo =
                dto.descripcionTipoCultivo?
                    .Trim() ??
                string.Empty;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje =
                    "Tipo de cultivo actualizado correctamente.",

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
        public async Task<IActionResult> Eliminar(
            int id)
        {
            TipoCultivo? entidad =
                await _db.TipoCultivos
                    .FirstOrDefaultAsync(x =>
                        x.tipoCultivoId == id &&
                        x.activo);

            if (entidad == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "Tipo de cultivo no encontrado o ya está desactivado."
                });
            }

            var dependencias =
                new List<string>();

            bool usadoEnRangos =
                await _db
                    .ParametroRangoNutrienteCultivo
                    .AnyAsync(x =>
                        x.tipoCultivoId == id &&
                        x.activo);

            if (usadoEnRangos)
            {
                dependencias.Add(
                    "rangos de aporte por cultivo");
            }

            bool usadoEnCalculos =
                await _db.AnalisisSueloCalculos
                    .AnyAsync(x =>
                        x.tipoCultivoId == id);

            if (usadoEnCalculos)
            {
                dependencias.Add(
                    "cálculos de análisis de suelo");
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
                mensaje =
                    "Tipo de cultivo desactivado correctamente.",

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
