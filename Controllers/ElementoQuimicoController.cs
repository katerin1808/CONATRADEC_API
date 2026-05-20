using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace CONATRADEC_API.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class ElementoQuimicoController : ControllerBase
    {
        private readonly DBContext _context;

        public ElementoQuimicoController(DBContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ElementoQuimicoRespuestaDto>>> Listar()
        {
            var data = await _context.elementoQuimico
                .OrderBy(x => x.nombreElementoQuimico)
                .Select(x => new ElementoQuimicoRespuestaDto
                {
                    elementoQuimicosId = x.elementoQuimicosId,
                    simboloElementoQuimico = x.simboloElementoQuimico,
                    nombreElementoQuimico = x.nombreElementoQuimico,
                    pesoEquivalenteElementoQuimico = x.pesoEquivalenteElementoQuimico,
                    activo = x.activo
                })
                .ToListAsync();

            return Ok(data);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ElementoQuimicoRespuestaDto>> ObtenerPorId(int id)
        {
            var data = await _context.elementoQuimico
                .Where(x => x.elementoQuimicosId == id)
                .Select(x => new ElementoQuimicoRespuestaDto
                {
                    elementoQuimicosId = x.elementoQuimicosId,
                    simboloElementoQuimico = x.simboloElementoQuimico,
                    nombreElementoQuimico = x.nombreElementoQuimico,
                    pesoEquivalenteElementoQuimico = x.pesoEquivalenteElementoQuimico,
                    activo = x.activo
                })
                .FirstOrDefaultAsync();

            if (data == null)
                return NotFound(new { mensaje = "Elemento químico no encontrado." });

            return Ok(data);
        }

        [HttpPost("api/elemento-quimico/crear/{id:int}")]
        public async Task<ActionResult> Crear([FromBody] CrearElementoQuimicoDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.simboloElementoQuimico))
                return BadRequest(new { mensaje = "El símbolo es obligatorio." });

            if (string.IsNullOrWhiteSpace(dto.nombreElementoQuimico))
                return BadRequest(new { mensaje = "El nombre es obligatorio." });

            bool existe = await _context.elementoQuimico.AnyAsync(x =>
                x.simboloElementoQuimico.ToLower() == dto.simboloElementoQuimico.ToLower());

            if (existe)
                return BadRequest(new { mensaje = "Ya existe un elemento químico con ese símbolo." });

            var entity = new ElementoQuimico
            {
                simboloElementoQuimico = dto.simboloElementoQuimico.Trim(),
                nombreElementoQuimico = dto.nombreElementoQuimico.Trim(),
                pesoEquivalenteElementoQuimico = dto.pesoEquivalenteElementoQuimico,
                activo = dto.activo
            };

            _context.elementoQuimico.Add(entity);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Elemento químico creado correctamente.",
                elementoQuimicosId = entity.elementoQuimicosId
            });
        }

        [HttpPut("api/elemento-quimico/editar/{id:int}")]
        public async Task<ActionResult> Editar(int id, [FromBody] EditarElementoQuimicoDto dto)
        {
            if (id != dto.elementoQuimicosId)
                return BadRequest(new { mensaje = "El ID de la ruta no coincide con el del objeto." });

            var entity = await _context.elementoQuimico
                .FirstOrDefaultAsync(x => x.elementoQuimicosId == id);

            if (entity == null)
                return NotFound(new { mensaje = "Elemento químico no encontrado." });

            bool existeDuplicado = await _context.elementoQuimico.AnyAsync(x =>
                x.elementoQuimicosId != id &&
                x.simboloElementoQuimico.ToLower() == dto.simboloElementoQuimico.ToLower());

            if (existeDuplicado)
                return BadRequest(new { mensaje = "Ya existe otro elemento químico con ese símbolo." });

            entity.simboloElementoQuimico = dto.simboloElementoQuimico.Trim();
            entity.nombreElementoQuimico = dto.nombreElementoQuimico.Trim();
            entity.pesoEquivalenteElementoQuimico = dto.pesoEquivalenteElementoQuimico;
            entity.activo = dto.activo;

            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Elemento químico actualizado correctamente." });
        }

        [HttpDelete("api/elemento-quimico/eliminar/{id:int}")]
        public async Task<ActionResult> Eliminar(int id)
        {
            var entity = await _context.elementoQuimico
                .FirstOrDefaultAsync(x => x.elementoQuimicosId == id);

            if (entity == null)
                return NotFound(new { mensaje = "Elemento químico no encontrado." });

            entity.activo = false;
            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Elemento químico desactivado correctamente." });
        }
    }
}

