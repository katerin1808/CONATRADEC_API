using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using static CONATRADEC_API.DTOs.DepartamentoDto;

namespace CONATRADEC_API.Controllers
{

    [ApiController]
    [Route("api/departamento")]
    public class DepartamentoController : Controller
    {
        private readonly DBContext _ctx;
        public DepartamentoController(DBContext ctx) => _ctx = ctx;


        // =========================
        // 1) CREAR  (POST /api/departamento/crear)
        // =========================
        [HttpPost("crear")]
        [Consumes("application/json")]
        public async Task<ActionResult> Create([FromBody] DepartamentoCreateRequest? req)
        {
            if (req is null) return BadRequest("Body vacío o JSON mal formado.");

            var nombre = req.NombreDepartamento?.ReplaceLineEndings(" ").Trim();
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest("El nombre del departamento es requerido.");

            // 🚫 Pais debe existir y estar ACTIVO
            var pais = await _ctx.Pais
                .AsNoTracking()
                .Where(p => p.PaisId == req.PaisId && p.Activo)   // <- ACTIVO
                .Select(p => new { p.PaisId, p.NombrePais })
                .SingleOrDefaultAsync();

            if (pais is null)
                return BadRequest("No se puede crear: el país no existe o está inactivo.");

            // Unicidad del nombre (global, o cambia a por país si así lo definiste)
            bool duplicado = await _ctx.Departamento
                .AnyAsync(d => d.NombreDepartamento.ToLower() == nombre!.ToLower());
            if (duplicado) return Conflict("El nombre del departamento ya existe.");

            var entity = new Departamento
            {
                NombreDepartamento = nombre!,
                PaisId = req.PaisId,
                Activo = true
            };

            _ctx.Departamento.Add(entity);
            await _ctx.SaveChangesAsync();

            return Ok(new
            {
                message = "Departamento creado exitosamente",
                departamento = new
                {
                    entity.DepartamentoId,
                    entity.NombreDepartamento,
                    entity.PaisId,
                    NombrePais = pais.NombrePais
                }
            });
        }

        // ===========================================
        // 2) LISTAR POR PAÍS (GET /api/departamento/por-pais/{paisId})
        //    Devuelve SOLO activos del país indicado
        // ===========================================
        // GET /api/departamento/por-pais/1
        [HttpGet("por-pais/{paisId:int}")]
        public async Task<ActionResult<IEnumerable<DepartamentoResponse>>> BuscarPorPais(int paisId)
        {
            // 1️⃣ Verificar que el país existe y esté activo
            var pais = await _ctx.Pais
                .AsNoTracking()
                .Where(p => p.PaisId == paisId && p.Activo)
                .Select(p => new { p.PaisId, p.NombrePais })
                .SingleOrDefaultAsync();

            if (pais is null)
                return NotFound($"No existe un país activo con el ID {paisId}.");

            // 2️⃣ Obtener departamentos activos del país
            var departamentos = await _ctx.Departamento
                .AsNoTracking()
                .Where(d => d.PaisId == paisId && d.Activo)
                .OrderBy(d => d.NombreDepartamento)
                .Select(d => new DepartamentoResponse
                {
                    DepartamentoId = d.DepartamentoId,
                    NombreDepartamento = d.NombreDepartamento,
                    // 👇 se incluye nombre del país asociado
                    NombrePais = pais.NombrePais,
                    Activo = d.Activo
                })
                .ToListAsync();

            // 3️⃣ Si el país no tiene departamentos activos
            if (departamentos.Count == 0)
                return NotFound($"El país '{pais.NombrePais}' no tiene departamentos activos.");

            return Ok(departamentos);
        }

        // ===========================================
        // 3) EDITAR (PUT /api/departamento/{id})
        // ===========================================
        [HttpPut("actualizar/{id:int}")]
        [Consumes("application/json")]
        public async Task<ActionResult> Update(int id, [FromBody] DepartamentoUpdateRequest? req)
        {
            if (req is null) return BadRequest("Body vacío o JSON mal formado.");

            var entity = await _ctx.Departamento.FindAsync(id);
            if (entity is null) return NotFound();

            var nombre = req.NombreDepartamento?.ReplaceLineEndings(" ").Trim();
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest("El nombre del departamento es requerido.");

            // 🚫 Pais debe existir y estar ACTIVO
            var pais = await _ctx.Pais
                .AsNoTracking()
                .Where(p => p.PaisId == req.PaisId && p.Activo)   // <- ACTIVO
                .Select(p => new { p.PaisId, p.NombrePais })
                .SingleOrDefaultAsync();

            if (pais is null)
                return BadRequest("No se puede actualizar: el país no existe o está inactivo.");

            // Unicidad global excluyendo el propio registro
            bool duplicado = await _ctx.Departamento
                .AnyAsync(d => d.DepartamentoId != id &&
                               d.NombreDepartamento.ToLower() == nombre!.ToLower());
            if (duplicado) return Conflict("El nombre del departamento ya existe.");

            entity.NombreDepartamento = nombre!;
            entity.PaisId = req.PaisId;

            await _ctx.SaveChangesAsync();

            return Ok(new
            {
                message = "Departamento actualizado",
                departamento = new
                {
                    entity.DepartamentoId,
                    entity.NombreDepartamento,
                    entity.PaisId,
                    NombrePais = pais.NombrePais
                }
            });
        }

        // ===========================================
        // 4) ELIMINAR (DELETE lógico /api/departamento/{id})
        // ===========================================
        [HttpDelete("eliminar/{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            // 1️⃣ Verificar que el departamento exista
            var entity = await _ctx.Departamento
                .FirstOrDefaultAsync(d => d.DepartamentoId == id);

            if (entity is null)
                return NotFound("El departamento indicado no existe.");

            // 2️⃣ Verificar si tiene municipios activos asociados
            bool tieneMunicipios = await _ctx.Municipios
                .AnyAsync(m => m.DepartamentoId == id && m.Activo);

            if (tieneMunicipios)
                return Conflict("No se puede eliminar: el departamento tiene municipios activos asociados.");

            // 3️⃣ Desactivar (borrado lógico)
            entity.Activo = false;
            await _ctx.SaveChangesAsync();

            // 4️⃣ Responder con confirmación
            return Ok(new
            {
                message = $"El departamento '{entity.NombreDepartamento}' fue eliminado correctamente (borrado lógico).",
                departamento = new
                {
                    entity.DepartamentoId,
                    entity.NombreDepartamento,
                    entity.Activo
                }
            });
        }
}

}
