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

        // ============================================================
        // VALIDAR LLAVES FORÁNEAS ACTIVAS
        // ============================================================
        private async Task<(bool ok, string mensaje)> ValidarLlavesForaneas(int fuenteNutrientesId, int elementoQuimicosId)
        {
            var fuente = await _db.FuenteNutrientes
                .FirstOrDefaultAsync(x => x.fuenteNutrientesId == fuenteNutrientesId && x.activo);

            if (fuente == null)
                return (false, "La fuente de nutriente no existe o está inactiva.");

            var elemento = await _db.ElementoQuimicos
                .FirstOrDefaultAsync(x => x.elementoQuimicosId == elementoQuimicosId && x.activo);

            if (elemento == null)
                return (false, "El elemento químico no existe o está inactivo.");

            return (true, "");
        }

        // ============================================================
        // LISTAR TODOS (solo activos)
        // ============================================================
        [HttpGet("listar")]
        public async Task<ActionResult<IEnumerable<FuenteNutrienteElementoQuimicoListarDto>>> Listar()
        {
            var lista = await _db.FuenteNutrienteElementoQuimicos
                .Where(x => x.activo)
                .Include(x => x.FuenteNutriente)
                .Include(x => x.ElementoQuimico)
                .Select(x => new FuenteNutrienteElementoQuimicoListarDto
                {
                    fuenteNutrienteElementoQuimicoId = x.fuenteNutrienteElementoQuimicoId,
                    cantidadAporte = x.cantidadAporte,
                    fuenteNutrientesId = x.fuenteNutrientesId,
                    nombreNutriente = x.FuenteNutriente!.nombreNutriente,
                    elementoQuimicosId = x.elementoQuimicosId,
                    nombreElementoQuimico = x.ElementoQuimico!.nombreElementoQuimico
                })
                .ToListAsync();

            return Ok(lista);
        }

        // ============================================================
        // LISTAR POR ID (solo activos)
        // ============================================================
        [HttpGet("listar/{id:int}")]
        public async Task<ActionResult<FuenteNutrienteElementoQuimicoListarDto>> ListarPorId(int id)
        {
            var entity = await _db.FuenteNutrienteElementoQuimicos
                .Where(x => x.fuenteNutrienteElementoQuimicoId == id && x.activo)
                .Include(x => x.FuenteNutriente)
                .Include(x => x.ElementoQuimico)
                .FirstOrDefaultAsync();

            if (entity == null)
                return NotFound("El registro no existe o está inactivo.");

            return Ok(new FuenteNutrienteElementoQuimicoListarDto
            {
                fuenteNutrienteElementoQuimicoId = entity.fuenteNutrienteElementoQuimicoId,
                cantidadAporte = entity.cantidadAporte,
                fuenteNutrientesId = entity.fuenteNutrientesId,
                nombreNutriente = entity.FuenteNutriente!.nombreNutriente,
                elementoQuimicosId = entity.elementoQuimicosId,
                nombreElementoQuimico = entity.ElementoQuimico!.nombreElementoQuimico
            });
        }

        // ============================================================
        // CREAR (CON TRANSACCIÓN)
        // ============================================================
        [HttpPost("crear")]
        public async Task<IActionResult> Crear(FuenteNutrienteElementoQuimicoCrearDto dto)
        {
            using var trans = await _db.Database.BeginTransactionAsync();

            var valid = await ValidarLlavesForaneas(dto.fuenteNutrientesId, dto.elementoQuimicosId);
            if (!valid.ok)
                return BadRequest(valid.mensaje);

            var entity = new FuenteNutrienteElementoQuimico
            {
                cantidadAporte = dto.cantidadAporte,
                fuenteNutrientesId = dto.fuenteNutrientesId,
                elementoQuimicosId = dto.elementoQuimicosId,
                activo = true
            };

            _db.FuenteNutrienteElementoQuimicos.Add(entity);
            await _db.SaveChangesAsync();

            var fuente = await _db.FuenteNutrientes.FirstAsync(x => x.fuenteNutrientesId == dto.fuenteNutrientesId);
            var elemento = await _db.ElementoQuimicos.FirstAsync(x => x.elementoQuimicosId == dto.elementoQuimicosId);

            await trans.CommitAsync();

            return Ok(new
            {
                mensaje = "Creado correctamente",
                id = entity.fuenteNutrienteElementoQuimicoId,
                cantidadAporte = entity.cantidadAporte,
                fuenteNutrientesId = entity.fuenteNutrientesId,
                nombreNutriente = fuente.nombreNutriente,
                precioNutriente = fuente.precioNutriente,
                elementoQuimicosId = entity.elementoQuimicosId,
                nombreElementoQuimico = elemento.nombreElementoQuimico
            });
        }

        // ============================================================
        // ACTUALIZAR (CON TRANSACCIÓN)
        // ============================================================
        [HttpPut("actualizar/{id:int}")]
        public async Task<IActionResult> Actualizar(int id, FuenteNutrienteElementoQuimicoActualizarDto dto)
        {
            using var trans = await _db.Database.BeginTransactionAsync();

            var entity = await _db.FuenteNutrienteElementoQuimicos
                .FirstOrDefaultAsync(x => x.fuenteNutrienteElementoQuimicoId == id && x.activo);

            if (entity == null)
                return NotFound("El registro no existe o está inactivo.");

            var valid = await ValidarLlavesForaneas(dto.fuenteNutrientesId, dto.elementoQuimicosId);
            if (!valid.ok)
                return BadRequest(valid.mensaje);

            entity.cantidadAporte = dto.cantidadAporte;
            entity.fuenteNutrientesId = dto.fuenteNutrientesId;
            entity.elementoQuimicosId = dto.elementoQuimicosId;

            await _db.SaveChangesAsync();

            var fuente = await _db.FuenteNutrientes.FirstAsync(x => x.fuenteNutrientesId == dto.fuenteNutrientesId);
            var elemento = await _db.ElementoQuimicos.FirstAsync(x => x.elementoQuimicosId == dto.elementoQuimicosId);

            await trans.CommitAsync();

            return Ok(new
            {
                mensaje = "Actualizado correctamente",
                id = entity.fuenteNutrienteElementoQuimicoId,
                cantidadAporte = entity.cantidadAporte,
                fuenteNutrientesId = entity.fuenteNutrientesId,
                nombreNutriente = fuente.nombreNutriente,
                precioNutriente = fuente.precioNutriente,
                elementoQuimicosId = entity.elementoQuimicosId,
                nombreElementoQuimico = elemento.nombreElementoQuimico
            });
        }

        // ============================================================
        // ELIMINAR (ELIMINADO LÓGICO)
        // ============================================================
        [HttpDelete("eliminar/{id:int}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            using var trans = await _db.Database.BeginTransactionAsync();

            var entity = await _db.FuenteNutrienteElementoQuimicos
                .FirstOrDefaultAsync(x => x.fuenteNutrienteElementoQuimicoId == id && x.activo);

            if (entity == null)
                return NotFound("No encontrado o ya está inactivo.");

            entity.activo = false;

            await _db.SaveChangesAsync();
            await trans.CommitAsync();

            return Ok("Eliminado correctamente (lógico).");
        }


    }
}
