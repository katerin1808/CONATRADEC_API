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


      

        // ==========================================================
        // CREAR MUNICIPIO (Departamento debe existir y estar activo)
        // ==========================================================
        [HttpPost("crear")]
        [Consumes("application/json")]
        public async Task<ActionResult> Create([FromBody] MunicipioCreateRequest? req)
        {
            if (req is null)
                return BadRequest("Body vacío o JSON mal formado.");

            var nombre = req.NombreMunicipio?.ReplaceLineEndings(" ").Trim();
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest("El nombre del municipio es requerido.");

            // Verificar que el departamento existe y esté activo
            var departamento = await _ctx.Departamento
                .Include(d => d.Pais)
                .AsNoTracking()
                .Where(d => d.DepartamentoId == req.DepartamentoId && d.Activo)
                .Select(d => new { d.DepartamentoId, d.NombreDepartamento, d.Pais!.NombrePais })
                .SingleOrDefaultAsync();

            if (departamento is null)
                return BadRequest("No se puede crear: el departamento no existe o está inactivo.");

            // Verificar unicidad global del nombre (case-insensitive)
            bool duplicado = await _ctx.Municipios
                .AnyAsync(m => m.NombreMunicipio.ToLower() == nombre!.ToLower());

            if (duplicado)
                return Conflict("Ya existe un municipio con ese nombre.");

            var entity = new Municipio
            {
                NombreMunicipio = nombre!,
                DepartamentoId = req.DepartamentoId,
                Activo = true
            };

            _ctx.Municipios.Add(entity);
            await _ctx.SaveChangesAsync();

            return Ok(new
            {
                message = "Municipio creado exitosamente",
                municipio = new
                {
                    entity.MunicipioId,
                    entity.NombreMunicipio,
                    Departamento = departamento.NombreDepartamento,
                    Pais = departamento.NombrePais
                }
            });
        }
        // ==========================================================
        // ==========================================================
        // LISTAR TODOS LOS MUNICIPIOS CON SU DEPARTAMENTO Y PAÍS
        // ==========================================================

        [HttpGet("Listar")]
        public async Task<ActionResult<IEnumerable<MunicipioResponse>>> GetAll()
        {
            var data = await _ctx.Municipios
                .AsNoTracking()
                .Include(m => m.Departamento)!.ThenInclude(d => d.Pais)
                .OrderBy(m => m.Departamento!.NombreDepartamento)
                .ThenBy(m => m.NombreMunicipio)
                .Select(m => new MunicipioResponse
                {
                    MunicipioId = m.MunicipioId,
                    NombreMunicipio = m.NombreMunicipio,
                    DepartamentoId = m.DepartamentoId,
                    NombreDepartamento = m.Departamento!.NombreDepartamento,
                    NombrePais = m.Departamento!.Pais!.NombrePais,
                    Activo = m.Activo
                })
                .ToListAsync();

            return Ok(data);
        }
        // ==========================================================
        // EDITAR MUNICIPIO
        // ==========================================================
        [HttpPut("editar/{id:int}")]
        [Consumes("application/json")]
        public async Task<ActionResult> Update(int id, [FromBody] MunicipioUpdateRequest? req)
        {
            if (req is null)
                return BadRequest("Body vacío o JSON mal formado.");

            var entity = await _ctx.Municipios.FindAsync(id);
            if (entity is null)
                return NotFound("El municipio indicado no existe.");

            var nombre = req.NombreMunicipio?.ReplaceLineEndings(" ").Trim();
            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest("El nombre del municipio es requerido.");

            // Verificar que el departamento existe y esté activo
            var departamento = await _ctx.Departamento
                .Include(d => d.Pais)
                .AsNoTracking()
                .Where(d => d.DepartamentoId == req.DepartamentoId && d.Activo)
                .Select(d => new { d.DepartamentoId, d.NombreDepartamento, d.Pais!.NombrePais })
                .SingleOrDefaultAsync();

            if (departamento is null)
                return BadRequest("No se puede actualizar: el departamento no existe o está inactivo.");

            // Verificar unicidad global del nombre
            bool duplicado = await _ctx.Municipios
                .AnyAsync(m => m.MunicipioId != id &&
                               m.NombreMunicipio.ToLower() == nombre!.ToLower());
            if (duplicado)
                return Conflict("Ya existe un municipio con ese nombre.");

            entity.NombreMunicipio = nombre!;
            entity.DepartamentoId = req.DepartamentoId;

            await _ctx.SaveChangesAsync();

            return Ok(new
            {
                message = "Municipio actualizado correctamente",
                municipio = new
                {
                    entity.MunicipioId,
                    entity.NombreMunicipio,
                    Departamento = departamento.NombreDepartamento,
                    Pais = departamento.NombrePais
                }
            });
        }

        // ==========================================================
        // ELIMINAR MUNICIPIO (borrado lógico)
        // ==========================================================
        [HttpDelete("eliminar/{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            // Verificar que el municipio exista
            var entity = await _ctx.Municipios
                .Include(m => m.Departamento)
                .ThenInclude(d => d.Pais)
                .FirstOrDefaultAsync(m => m.MunicipioId == id);

            if (entity is null)
                return NotFound("El municipio indicado no existe.");

            // Verificar si el departamento del municipio está activo
            if (!entity.Departamento.Activo)
                return Conflict("No se puede eliminar: el departamento asociado está inactivo.");

            // Borrado lógico
            entity.Activo = false;
            await _ctx.SaveChangesAsync();

            return Ok(new
            {
                message = $"El municipio '{entity.NombreMunicipio}' fue eliminado correctamente (borrado lógico).",
                municipio = new
                {
                    entity.MunicipioId,
                    entity.NombreMunicipio,
                    Departamento = entity.Departamento.NombreDepartamento,
                    Pais = entity.Departamento.Pais.NombrePais,
                    Activo = entity.Activo
                }
            });
        }
}
}
