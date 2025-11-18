using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.FuenteNutrienteElementoQuimicoDto;

namespace CONATRADEC_API.Controllers
{

    [ApiController]
    [Route("api/fuente-nutriente-elemento")]
    public class FuenteNutrienteElementoQuimicoController : Controller
    {
        private readonly DBContext _db;

        public FuenteNutrienteElementoQuimicoController(DBContext db)
        {
            _db = db;
        }

        // ============================================
        // LISTAR
        // ============================================
        [HttpGet("listar")]
        public async Task<ActionResult<IEnumerable<FuenteNutrienteElementoQuimicoListarDto>>> Listar()
        {
            var lista = await _db.FuenteNutrienteElementoQuimicos
                  .Include(x => x.FuenteNutriente)
                  .Include(x => x.ElementoQuimico)
                  .Select(x => new FuenteNutrienteElementoQuimicoListarDto
                  {
                      fuenteNutrienteElementoQuimicoId = x.fuenteNutrienteElementoQuimicoId,
                      cantidadAporte = x.cantidadAporte,

                      fuenteNutrientesId = x.fuenteNutrientesId,
                      nombreNutriente = x.FuenteNutriente == null
                          ? null
                          : x.FuenteNutriente.nombreNutriente,

                      elementoQuimicosId = x.elementoQuimicosId,
                      nombreElementoQuimico = x.ElementoQuimico == null
                          ? null
                          : x.ElementoQuimico.nombreElementoQuimico
                  })
                  .ToListAsync();

            return Ok(lista);
        }

        // ============================================
        // LISTAR POR ID
        // ============================================
        [HttpGet("listar/{id:int}")]
        public async Task<ActionResult<FuenteNutrienteElementoQuimicoListarDto>> ListarPorId(int id)
        {
            var entity = await _db.FuenteNutrienteElementoQuimicos
                .Include(x => x.FuenteNutriente)
                .Include(x => x.ElementoQuimico)
                .FirstOrDefaultAsync(x => x.fuenteNutrienteElementoQuimicoId == id);

            if (entity == null)
                return NotFound();

            var dto = new FuenteNutrienteElementoQuimicoListarDto
            {
                fuenteNutrienteElementoQuimicoId = entity.fuenteNutrienteElementoQuimicoId,
                cantidadAporte = entity.cantidadAporte,
                fuenteNutrientesId = entity.fuenteNutrientesId,
                nombreNutriente = entity.FuenteNutriente?.nombreNutriente,
                elementoQuimicosId = entity.elementoQuimicosId,
                nombreElementoQuimico = entity.ElementoQuimico?.nombreElementoQuimico
            };

            return Ok(dto);
        }

        // ============================================
        // CREAR
        // ============================================
        [HttpPost("crear")]
        public async Task<IActionResult> Crear(FuenteNutrienteElementoQuimicoCrearDto dto)
        {
            var entity = new FuenteNutrienteElementoQuimico
            {
                cantidadAporte = dto.cantidadAporte,
                fuenteNutrientesId = dto.fuenteNutrientesId,
                elementoQuimicosId = dto.elementoQuimicosId
            };

            await _db.FuenteNutrienteElementoQuimicos.AddAsync(entity);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Creado correctamente",
                id = entity.fuenteNutrienteElementoQuimicoId
            });
        }

        // ============================================
        // ACTUALIZAR
        // ============================================
        [HttpPut("actualizar")]
        public async Task<IActionResult> Actualizar(FuenteNutrienteElementoQuimicoActualizarDto dto)
        {
            var entity = await _db.FuenteNutrienteElementoQuimicos
                .FirstOrDefaultAsync(x => x.fuenteNutrienteElementoQuimicoId == dto.fuenteNutrienteElementoQuimicoId);

            if (entity == null)
                return NotFound("No encontrado");

            entity.cantidadAporte = dto.cantidadAporte;
            entity.fuenteNutrientesId = dto.fuenteNutrientesId;
            entity.elementoQuimicosId = dto.elementoQuimicosId;

            await _db.SaveChangesAsync();

            return Ok("Actualizado correctamente");
        }

     
    }
}
