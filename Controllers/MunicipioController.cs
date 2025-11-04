using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using static CONATRADEC_API.DTOs.MunicipioDto;

namespace CONATRADEC_API.Controllers
{
    public class MunicipioController : Controller
    {

        private readonly DBContext _ctx;
        public MunicipioController(DBContext ctx) => _ctx = ctx;


        // ===========================================
        // 1) CREAR
        // POST /api/municipio/crear
        // ===========================================
        [HttpPost("crear")]
        [Consumes("application/json")]
        public async Task<ActionResult> Create([FromBody] MunicipioCreateRequest? req)
        {
            if (req is null) return BadRequest("Body vacío o JSON mal formado.");

            var nombre = req.NombreMunicipio?.ReplaceLineEndings(" ").Trim();
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest("El nombre del municipio es requerido.");

            await using var trx = await _ctx.Database.BeginTransactionAsync();
            try
            {
                // Departamento debe existir y estar ACTIVO (y su país también, si lo deseas)
                var dep = await _ctx.Departamento
                    .AsNoTracking()
                    .Include(d => d.Pais)
                    .Where(d => d.DepartamentoId == req.DepartamentoId && d.Activo)
                    .Select(d => new { d.DepartamentoId, d.NombreDepartamento, d.Pais!.NombrePais })
                    .SingleOrDefaultAsync();

                if (dep is null)
                    return BadRequest("No se puede crear: el departamento no existe o está inactivo.");

                // Unicidad POR DEPARTAMENTO y SOLO entre activos
                bool duplicado = await _ctx.Municipios
                    .AnyAsync(m => m.DepartamentoId == req.DepartamentoId
                                && m.Activo
                                && m.NombreMunicipio.ToLower() == nombre!.ToLower());
                if (duplicado)
                    return Conflict("Ya existe un municipio activo con ese nombre en este departamento.");

                var entity = new Municipio
                {
                    NombreMunicipio = nombre!,
                    DepartamentoId = req.DepartamentoId,
                    Activo = true
                };

                _ctx.Municipios.Add(entity);
                await _ctx.SaveChangesAsync();
                await trx.CommitAsync();

                return Ok(new
                {
                    message = "Municipio creado exitosamente",
                    municipio = new
                    {
                        entity.MunicipioId,
                        entity.NombreMunicipio,
                        Departamento = dep.NombreDepartamento,
                        Pais = dep.NombrePais
                    }
                });
            }
            catch
            {
                await trx.RollbackAsync();
                throw;
            }
        }
        // ==========================================================
        // ==========================================================
        // LISTAR TODOS LOS MUNICIPIOS CON SU DEPARTAMENTO Y PAÍS
        // ==========================================================

        // ===========================================
        // 2) LISTAR POR DEPARTAMENTO
        // GET /api/municipio/por-departamento/{departamentoId}
        // ===========================================
        [HttpGet("por-departamento/{departamentoId:int}")]
        public async Task<ActionResult<IEnumerable<MunicipioResponse>>> ListarPorDepartamento(int departamentoId)
        {
            var dep = await _ctx.Departamento.AsNoTracking()
                .Include(d => d.Pais)
                .Where(d => d.DepartamentoId == departamentoId && d.Activo)
                .Select(d => new { d.DepartamentoId, d.NombreDepartamento, d.Pais!.NombrePais })
                .SingleOrDefaultAsync();

            if (dep is null)
                return NotFound($"No existe un departamento activo con ID {departamentoId}.");

            var data = await _ctx.Municipios
                .AsNoTracking()
                .Where(m => m.DepartamentoId == departamentoId && m.Activo)
                .OrderBy(m => m.NombreMunicipio)
                .Select(m => new MunicipioResponse
                {
                    MunicipioId = m.MunicipioId,
                    NombreMunicipio = m.NombreMunicipio,
                    DepartamentoId = m.DepartamentoId,
                    NombreDepartamento = dep.NombreDepartamento,
                    NombrePais = dep.NombrePais,
                    Activo = m.Activo
                })
                .ToListAsync();

            if (data.Count == 0)
                return NotFound($"El departamento '{dep.NombreDepartamento}' no tiene municipios activos.");

            return Ok(data);
        }
        // ===========================================
        // 3) ACTUALIZAR (solo nombre; NO permite cambiar DepartamentoId)
        // PUT /api/municipio/actualizar/{id}
        // ===========================================
        [HttpPut("actualizar/{id:int}")]
        [Consumes("application/json")]
        public async Task<ActionResult> Update(int id, [FromBody] MunicipioUpdateRequest? req)
        {
            if (req is null) return BadRequest("Body vacío o JSON mal formado.");

            var entity = await _ctx.Municipios.FindAsync(id);
            if (entity is null) return NotFound("El municipio no existe.");

            var nombre = req.NombreMunicipio?.ReplaceLineEndings(" ").Trim();
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest("El nombre del municipio es requerido.");

            await using var trx = await _ctx.Database.BeginTransactionAsync();
            try
            {
                var departamentoIdActual = entity.DepartamentoId;

                // Unicidad: por departamento y solo contra ACTIVOS
                bool duplicadoActivo = await _ctx.Municipios
                    .AnyAsync(m => m.MunicipioId != id
                                && m.DepartamentoId == departamentoIdActual
                                && m.Activo
                                && m.NombreMunicipio.ToLower() == nombre!.ToLower());
                if (duplicadoActivo)
                    return Conflict("Ya existe un municipio activo con ese nombre en este departamento.");

                entity.NombreMunicipio = nombre!;
                await _ctx.SaveChangesAsync();
                await trx.CommitAsync();

                var dep = await _ctx.Departamento.AsNoTracking()
                    .Include(d => d.Pais)
                    .Where(d => d.DepartamentoId == departamentoIdActual)
                    .Select(d => new { d.NombreDepartamento, d.Pais!.NombrePais })
                    .SingleOrDefaultAsync();

                return Ok(new
                {
                    message = "Municipio actualizado",
                    municipio = new
                    {
                        entity.MunicipioId,
                        entity.NombreMunicipio,
                        Departamento = dep?.NombreDepartamento ?? string.Empty,
                        Pais = dep?.NombrePais ?? string.Empty
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
        // 4) ELIMINAR (borrado lógico)
        // DELETE /api/municipio/eliminar/{id}
        // ===========================================
        [HttpDelete("eliminar/{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            await using var trx = await _ctx.Database.BeginTransactionAsync();
            try
            {
                var entity = await _ctx.Municipios.FirstOrDefaultAsync(m => m.MunicipioId == id);
                if (entity is null)
                    return NotFound("El municipio indicado no existe.");

                entity.Activo = false;
                await _ctx.SaveChangesAsync();
                await trx.CommitAsync();

                return Ok(new
                {
                    message = $"El municipio '{entity.NombreMunicipio}' fue eliminado correctamente (borrado lógico).",
                    municipio = new
                    {
                        entity.MunicipioId,
                        entity.NombreMunicipio,
                        entity.Activo
                    }
                });
            }
            catch
            {
                await trx.RollbackAsync();
                throw;
            }
        }
}
}
