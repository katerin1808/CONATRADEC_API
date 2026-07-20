using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/configuracion/tipos-analisis-suelo")]
    public class TipoAnalisisSueloController : ControllerBase
    {
        private readonly DBContext _db;

        public TipoAnalisisSueloController(DBContext db)
        {
            _db = db;
        }

        // GET:
        // Lista únicamente registros activos.
        [HttpGet]
        public async Task<IActionResult> Listar()
        {
            var data = await _db.TipoAnalisisSuelos
                .AsNoTracking()
                .Where(x => x.activo)
                .OrderBy(x => x.nombreTipoAnalisisSuelo)
                .Select(x => new
                {
                    x.tipoAnalisisSueloId,
                    x.nombreTipoAnalisisSuelo,
                    x.descripcionTipoAnalisisSuelo,
                    x.activo
                })
                .ToListAsync();

            return Ok(data);
        }

        // GET por ID.
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Obtener(int id)
        {
            var data = await _db.TipoAnalisisSuelos
                .AsNoTracking()
                .Where(x =>
                    x.tipoAnalisisSueloId == id &&
                    x.activo)
                .Select(x => new
                {
                    x.tipoAnalisisSueloId,
                    x.nombreTipoAnalisisSuelo,
                    x.descripcionTipoAnalisisSuelo,
                    x.activo
                })
                .FirstOrDefaultAsync();

            if (data == null)
            {
                return NotFound(new
                {
                    mensaje = "Tipo de análisis de suelo no encontrado."
                });
            }

            return Ok(data);
        }

        // POST:
        // No solicita ID ni activo.
        [HttpPost]
        public async Task<IActionResult> Crear(
            [FromBody] CrearTipoAnalisisSueloDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.nombreTipoAnalisisSuelo))
            {
                return BadRequest(new
                {
                    mensaje = "El nombre del tipo de análisis es obligatorio."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.descripcionTipoAnalisisSuelo))
            {
                return BadRequest(new
                {
                    mensaje = "La descripción es obligatoria."
                });
            }

            var nombre = dto.nombreTipoAnalisisSuelo
                .Trim()
                .ToUpper();

            var existe = await _db.TipoAnalisisSuelos
                .AnyAsync(x =>
                    x.nombreTipoAnalisisSuelo
                        .Trim()
                        .ToUpper() == nombre);

            if (existe)
            {
                return Conflict(new
                {
                    mensaje = "Ya existe un tipo de análisis de suelo con ese nombre."
                });
            }

            var entidad = new TipoAnalisisSuelo
            {
                nombreTipoAnalisisSuelo = nombre,
                descripcionTipoAnalisisSuelo =
                    dto.descripcionTipoAnalisisSuelo.Trim(),

                // Todos los registros nuevos se crean activos.
                activo = true
            };

            _db.TipoAnalisisSuelos.Add(entidad);
            await _db.SaveChangesAsync();

            return StatusCode(
                StatusCodes.Status201Created,
                new
                {
                    mensaje = "Tipo de análisis de suelo creado correctamente.",
                    data = new
                    {
                        entidad.tipoAnalisisSueloId,
                        entidad.nombreTipoAnalisisSuelo,
                        entidad.descripcionTipoAnalisisSuelo,
                        entidad.activo
                    }
                });
        }

        // PUT:
        // El ID se recibe en la URL.
        // No permite modificar el ID ni el estado activo.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Actualizar(
            int id,
            [FromBody] ActualizarTipoAnalisisSueloDto dto)
        {
            var entidad = await _db.TipoAnalisisSuelos
                .FirstOrDefaultAsync(x =>
                    x.tipoAnalisisSueloId == id &&
                    x.activo);

            if (entidad == null)
            {
                return NotFound(new
                {
                    mensaje = "Tipo de análisis de suelo no encontrado."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.nombreTipoAnalisisSuelo))
            {
                return BadRequest(new
                {
                    mensaje = "El nombre del tipo de análisis es obligatorio."
                });
            }

            if (string.IsNullOrWhiteSpace(dto.descripcionTipoAnalisisSuelo))
            {
                return BadRequest(new
                {
                    mensaje = "La descripción es obligatoria."
                });
            }

            var nombre = dto.nombreTipoAnalisisSuelo
                .Trim()
                .ToUpper();

            var existe = await _db.TipoAnalisisSuelos
                .AnyAsync(x =>
                    x.tipoAnalisisSueloId != id &&
                    x.nombreTipoAnalisisSuelo
                        .Trim()
                        .ToUpper() == nombre);

            if (existe)
            {
                return Conflict(new
                {
                    mensaje = "Ya existe otro tipo de análisis de suelo con ese nombre."
                });
            }

            entidad.nombreTipoAnalisisSuelo = nombre;
            entidad.descripcionTipoAnalisisSuelo =
                dto.descripcionTipoAnalisisSuelo.Trim();

            // No se modifica:
            // entidad.tipoAnalisisSueloId
            // entidad.activo

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Tipo de análisis de suelo actualizado correctamente.",
                data = new
                {
                    entidad.tipoAnalisisSueloId,
                    entidad.nombreTipoAnalisisSuelo,
                    entidad.descripcionTipoAnalisisSuelo,
                    entidad.activo
                }
            });
        }

        [HttpPut("{id:int}/eliminar")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var entidad = await _db.TipoAnalisisSuelos
                .FirstOrDefaultAsync(x =>
                    x.tipoAnalisisSueloId == id &&
                    x.activo);

            if (entidad == null)
            {
                return NotFound(new
                {
                    mensaje = "Tipo de análisis de suelo no encontrado o ya está desactivado."
                });
            }

            var dependencias = new List<string>();

            var usadoEnCalculos = await _db.AnalisisSueloCalculos
                .AnyAsync(x => x.tipoAnalisisSueloId == id);

            if (usadoEnCalculos)
            {
                dependencias.Add("cálculos de análisis de suelo");
            }

            if (dependencias.Count > 0)
            {
                return Conflict(new
                {
                    mensaje =
                        "No se puede eliminar el tipo de análisis de suelo porque está siendo utilizado.",

                    tipoAnalisisSuelo = new
                    {
                        entidad.tipoAnalisisSueloId,
                        entidad.nombreTipoAnalisisSuelo
                    },

                    usadoEn = dependencias
                });
            }

            entidad.activo = false;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Tipo de análisis de suelo desactivado correctamente.",
                data = new
                {
                    entidad.tipoAnalisisSueloId,
                    entidad.nombreTipoAnalisisSuelo,
                    entidad.activo
                }
            });
        }
    }
}
