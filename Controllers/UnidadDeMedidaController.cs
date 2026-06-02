using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.UnidadDeMedidaDto;

namespace CONATRADEC_API.Controllers
{

    [ApiController]
    [Route("api/unidad-medida")]
    public class UnidadMedidaController : ControllerBase
    {
        private readonly DBContext _db;

        public UnidadMedidaController(DBContext db)
        {
            _db = db;
        }

        [HttpGet("listar")]
        public async Task<IActionResult> Listar()
        {
            var data = await _db.UnidadMedidas
                .Where(x => x.activo)
                .OrderBy(x => x.nombreUnidadMedida)
                .Select(x => new UnidadMedidaRespuestaDto
                {
                    unidadMedidaId = x.unidadMedidaId,
                    nombreUnidadMedida = x.nombreUnidadMedida,
                    activo = x.activo
                })
                .ToListAsync();

            return Ok(data);
        }

        [HttpGet("obtener/{id:int}")]
        public async Task<IActionResult> ObtenerPorId(int id)
        {
            var data = await _db.UnidadMedidas
                .Where(x => x.unidadMedidaId == id && x.activo)
                .Select(x => new UnidadMedidaRespuestaDto
                {
                    unidadMedidaId = x.unidadMedidaId,
                    nombreUnidadMedida = x.nombreUnidadMedida,
                    activo = x.activo
                })
                .FirstOrDefaultAsync();

            if (data == null)
                return NotFound(new { mensaje = "Unidad de medida no encontrada." });

            return Ok(data);
        }

        [HttpPost("crear")]
        public async Task<IActionResult> Crear([FromBody] UnidadMedidaCrearDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.nombreUnidadMedida))
                return BadRequest(new { mensaje = "El nombre es obligatorio." });

            string nombre = dto.nombreUnidadMedida.Trim().ToUpper();

            bool existe = await _db.UnidadMedidas
                .AnyAsync(x => x.nombreUnidadMedida.Trim().ToUpper() == nombre && x.activo);

            if (existe)
                return BadRequest(new
                {
                    mensaje = "Ya existe una unidad de medida con ese nombre."
                });

            var entity = new UnidadMedida
            {
                nombreUnidadMedida = nombre,
                activo = true
            };

            _db.UnidadMedidas.Add(entity);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Unidad de medida creada correctamente.",
                data = new
                {
                    entity.unidadMedidaId,
                    entity.nombreUnidadMedida,
                    entity.activo
                }
            });
        }

        [HttpPut("editar/{id:int}")]
        public async Task<IActionResult> Editar(int id, [FromBody] UnidadMedidaEditarDto dto)
        {
            var entity = await _db.UnidadMedidas
                .FirstOrDefaultAsync(x => x.unidadMedidaId == id && x.activo);

            if (entity == null)
                return NotFound(new { mensaje = "Unidad de medida no encontrada." });

            string nombre = dto.nombreUnidadMedida.Trim().ToUpper();

            bool existe = await _db.UnidadMedidas
                .AnyAsync(x =>
                    x.unidadMedidaId != id &&
                    x.nombreUnidadMedida.Trim().ToUpper() == nombre &&
                    x.activo);

            if (existe)
                return BadRequest(new
                {
                    mensaje = "Ya existe una unidad de medida con ese nombre."
                });

            entity.nombreUnidadMedida = nombre;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Unidad de medida actualizada correctamente.",
                data = new
                {
                    entity.unidadMedidaId,
                    entity.nombreUnidadMedida,
                    entity.activo
                }
            });
        }

        [HttpDelete("eliminar/{id:int}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var entity = await _db.UnidadMedidas
                .FirstOrDefaultAsync(x => x.unidadMedidaId == id && x.activo);

            if (entity == null)
                return NotFound(new { mensaje = "Unidad de medida no encontrada." });

            entity.activo = false;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Unidad de medida eliminada correctamente.",
                data = new
                {
                    entity.unidadMedidaId,
                    entity.nombreUnidadMedida
                }
            });
        }
    }
}
