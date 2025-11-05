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

            await using var trx = await _ctx.Database.BeginTransactionAsync();
            try
            {
                // País debe existir y estar ACTIVO
                var pais = await _ctx.Pais
                    .AsNoTracking()
                    .Where(p => p.PaisId == req.PaisId && p.Activo)
                    .Select(p => new { p.PaisId, p.NombrePais })
                    .SingleOrDefaultAsync();

                if (pais is null)
                    return BadRequest("No se puede crear: el país no existe o está inactivo.");

                // Unicidad POR PAÍS y SOLO entre activos
                /*EF.Functions.Collate(d.NombreDepartamento.ToUpper(), "Modern_Spanish_CI_AI") == nombre.ToUpper());
                 * permite guardar todo en mayusculas respetando los signos de acentuacion */
                bool duplicado = await _ctx.Departamento
                    .AnyAsync(d => d.PaisId == req.PaisId
                                && d.Activo
                                && EF.Functions.Collate(d.NombreDepartamento.ToUpper(), "Modern_Spanish_CI_AI") == nombre!.ToUpper());
                if (duplicado) return Conflict("Ya existe un departamento activo con ese nombre en ese país.");

                var entity = new Departamento
                {
                    NombreDepartamento = nombre!.ToUpper(),
                    PaisId = pais.PaisId,
                    Activo = true
                };

                _ctx.Departamento.Add(entity);
                await _ctx.SaveChangesAsync();
                await trx.CommitAsync();

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
            catch (Exception)
            {
                await trx.RollbackAsync();
                throw;
            }
        }

        // ===========================================
        // ===========================================
        // 2) LISTAR POR PAÍS (GET /api/departamento/por-pais/{paisId})
        // ===========================================
        [HttpGet("por-pais/{paisId:int}")]
        public async Task<ActionResult<IEnumerable<DepartamentoResponse>>> BuscarPorPais(int paisId)
        {
            var pais = await _ctx.Pais
                .AsNoTracking()
                .Where(p => p.PaisId == paisId && p.Activo)
                .Select(p => new { p.PaisId, p.NombrePais , p.CodigoISOPais})
                .SingleOrDefaultAsync();

            if (pais is null)
                return NotFound($"No existe un país activo con el ID {paisId}.");

            var departamentos = await _ctx.Departamento
                .AsNoTracking()
                .Where(d => d.PaisId == paisId && d.Activo)
                .OrderBy(d => d.NombreDepartamento)
                .Select(d => new DepartamentoResponse
                {
                    DepartamentoId = d.DepartamentoId,
                    NombreDepartamento = d.NombreDepartamento,
                    NombrePais = pais.NombrePais,
                    Activo = d.Activo
                })
                .ToListAsync();

            if (departamentos.Count == 0)
                return NotFound($"El país '{pais.NombrePais}' no tiene departamentos activos.");

            return Ok(departamentos);
        }

        [HttpPut("actualizar/{id:int}")]
        [Consumes("application/json")]
        public async Task<ActionResult> Update(int id, [FromBody] DepartamentoUpdateRequest? req)
        {
            if (req is null) return BadRequest("Body vacío o JSON mal formado.");

            var entity = await _ctx.Departamento.FindAsync(id);
            if (entity is null) return NotFound("El departamento no existe.");

            var nombre = req.NombreDepartamento?.ReplaceLineEndings(" ").Trim();
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest("El nombre del departamento es requerido.");

            await using var trx = await _ctx.Database.BeginTransactionAsync();
            try
            {
                var paisIdActual = entity.PaisId;

                bool duplicadoActivoMismoPais = await _ctx.Departamento
                    .AnyAsync(d => d.DepartamentoId != id
                                && d.PaisId == paisIdActual
                                && d.Activo
                                && EF.Functions.Collate(d.NombreDepartamento.ToUpper(), "Modern_Spanish_CI_AI") == nombre.ToUpper());
                if (duplicadoActivoMismoPais)
                    return Conflict("Ya existe un departamento activo con ese nombre en este país.");

                if (!entity.Activo)
                    return Conflict("No se puede actualizar un departamento que está inactivo."
     );

                entity.NombreDepartamento = nombre!.ToUpper();
                await _ctx.SaveChangesAsync();
                await trx.CommitAsync();

                var nombrePais = await _ctx.Pais.AsNoTracking()
                    .Where(p => p.PaisId == paisIdActual)
                    .Select(p => p.NombrePais)
                    .SingleOrDefaultAsync() ?? string.Empty;

                return Ok(new
                {
                    message = "Departamento actualizado",
                    departamento = new
                    {
                        entity.DepartamentoId,
                        entity.NombreDepartamento,
                        entity.PaisId,          // se mantiene igual
                        NombrePais = nombrePais
                    }
                });
            }
            catch
            {
                await trx.RollbackAsync();
                throw;
            }
        }

        // ===========================================
        // 4) ELIMINAR (DELETE lógico /api/departamento/eliminar/{id})
        //    Bloquea si hay municipios activos
        // ===========================================
        [HttpDelete("eliminar/{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            await using var trx = await _ctx.Database.BeginTransactionAsync();
            try
            {
                var entity = await _ctx.Departamento
                    .FirstOrDefaultAsync(d => d.DepartamentoId == id);

                if (entity is null)
                    return NotFound("El departamento indicado no existe.");

                if (!entity.Activo)
                    return Conflict("El departamento ya está inactivo.");

                bool tieneMunicipios = await _ctx.Municipios
                    .AnyAsync(m => m.DepartamentoId == id && m.Activo);

                if (tieneMunicipios)
                    return Conflict("No se puede eliminar: el departamento tiene municipios activos asociados.");

                entity.Activo = false;
                await _ctx.SaveChangesAsync();
                await trx.CommitAsync();

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
            catch (Exception)
            {
                await trx.RollbackAsync();
                throw;
            }
        }
}

}
