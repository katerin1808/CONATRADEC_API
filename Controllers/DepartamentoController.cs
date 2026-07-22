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

        public DepartamentoController(DBContext ctx)
        {
            _ctx = ctx;
        }

        // =========================
        // 1) CREAR
        // POST /api/departamento/crear
        // =========================
        [HttpPost("crear")]
        [Consumes("application/json")]
        public async Task<ActionResult> Create(
            [FromBody] DepartamentoCreateRequest? req)
        {
            if (req is null)
                return BadRequest("Body vacío o JSON mal formado.");

            string? nombre =
                req.NombreDepartamento?
                    .ReplaceLineEndings(" ")
                    .Trim();

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(
                    "El nombre del departamento es requerido.");
            }

            await using var trx =
                await _ctx.Database.BeginTransactionAsync();

            try
            {
                var pais = await _ctx.Pais
                    .AsNoTracking()
                    .Where(
                        p => p.PaisId == req.PaisId &&
                             p.Activo)
                    .Select(
                        p => new
                        {
                            p.PaisId,
                            p.NombrePais
                        })
                    .SingleOrDefaultAsync();

                if (pais is null)
                {
                    return BadRequest(
                        "No se puede crear: el país no existe o está inactivo.");
                }

                bool duplicado =
                    await _ctx.Departamento.AnyAsync(
                        d => d.PaisId == req.PaisId &&
                             d.Activo &&
                             EF.Functions.Collate(
                                 d.NombreDepartamento.ToUpper(),
                                 "Modern_Spanish_CI_AI") ==
                             nombre.ToUpper());

                if (duplicado)
                {
                    return Conflict(
                        "Ya existe un departamento activo con ese nombre en ese país.");
                }

                var entity = new Departamento
                {
                    NombreDepartamento =
                        nombre.ToUpper(),
                    PaisId = pais.PaisId,
                    Activo = true
                };

                _ctx.Departamento.Add(entity);

                await _ctx.SaveChangesAsync();
                await trx.CommitAsync();

                return Ok(
                    new
                    {
                        message =
                            "Departamento creado exitosamente",
                        departamento =
                            new
                            {
                                entity.DepartamentoId,
                                entity.NombreDepartamento,
                                entity.PaisId,
                                NombrePais =
                                    pais.NombrePais
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
        // 2) LISTAR POR PAÍS
        // GET /api/departamento/por-pais/{paisId}
        // ===========================================
        [HttpGet("por-pais/{paisId:int}")]
        public async Task<ActionResult<IEnumerable<DepartamentoResponse>>>
            BuscarPorPais(int paisId)
        {
            var pais = await _ctx.Pais
                .AsNoTracking()
                .Where(
                    p => p.PaisId == paisId &&
                         p.Activo)
                .Select(
                    p => new
                    {
                        p.PaisId,
                        p.NombrePais,
                        p.CodigoISOPais
                    })
                .SingleOrDefaultAsync();

            if (pais is null)
            {
                return NotFound(
                    $"No existe un país activo con el ID {paisId}.");
            }

            List<DepartamentoResponse> departamentos =
                await _ctx.Departamento
                    .AsNoTracking()
                    .Where(
                        d => d.PaisId == paisId &&
                             d.Activo)
                    .OrderBy(
                        d => d.NombreDepartamento)
                    .Select(
                        d => new DepartamentoResponse
                        {
                            DepartamentoId =
                                d.DepartamentoId,
                            NombreDepartamento =
                                d.NombreDepartamento,
                            NombrePais =
                                pais.NombrePais,
                            Activo =
                                d.Activo
                        })
                    .ToListAsync();

            // Un país existente sin departamentos no representa un error.
            // Se responde 200 OK con una colección vacía.
            return Ok(departamentos);
        }

        [HttpPost("conteo-paginado")]
        public async Task<ActionResult> ConteoPaginado(
            [FromBody] ConteoPaginadoRequest req)
        {
            if (req == null)
                return BadRequest("Debe enviar datos en el JSON.");

            var query = _ctx.Departamento
                .AsNoTracking()
                .Where(d => d.Activo);

            int totalRegistros =
                await query.CountAsync();

            int totalPaginas =
                (int)Math.Ceiling(
                    totalRegistros /
                    (double)req.PageSize);

            if (!req.ContarIntervalo ||
                req.Inicio <= 0 ||
                req.Fin <= 0)
            {
                return Ok(
                    new
                    {
                        totalRegistros,
                        totalPaginas
                    });
            }

            if (req.Inicio > totalPaginas)
                req.Inicio = totalPaginas;

            if (req.Fin > totalPaginas)
                req.Fin = totalPaginas;

            if (req.Inicio > req.Fin)
                req.Inicio = req.Fin;

            int skip =
                (req.Inicio - 1) *
                req.PageSize;

            int take =
                (req.Fin - req.Inicio + 1) *
                req.PageSize;

            int cantidadIntervalo =
                await query
                    .Skip(skip)
                    .Take(take)
                    .CountAsync();

            return Ok(
                new
                {
                    inicio =
                        req.Inicio,
                    fin =
                        req.Fin,
                    pageSize =
                        req.PageSize,
                    cantidad =
                        cantidadIntervalo
                });
        }

        [HttpPut("actualizar/{id:int}")]
        [Consumes("application/json")]
        public async Task<ActionResult> Update(
            int id,
            [FromBody] DepartamentoUpdateRequest? req)
        {
            if (req is null)
                return BadRequest("Body vacío o JSON mal formado.");

            Departamento? entity =
                await _ctx.Departamento.FindAsync(id);

            if (entity is null)
                return NotFound("El departamento no existe.");

            string? nombre =
                req.NombreDepartamento?
                    .ReplaceLineEndings(" ")
                    .Trim();

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(
                    "El nombre del departamento es requerido.");
            }

            await using var trx =
                await _ctx.Database.BeginTransactionAsync();

            try
            {
                int paisIdActual =
                    entity.PaisId;

                bool duplicadoActivoMismoPais =
                    await _ctx.Departamento.AnyAsync(
                        d => d.DepartamentoId != id &&
                             d.PaisId == paisIdActual &&
                             d.Activo &&
                             EF.Functions.Collate(
                                 d.NombreDepartamento.ToUpper(),
                                 "Modern_Spanish_CI_AI") ==
                             nombre.ToUpper());

                if (duplicadoActivoMismoPais)
                {
                    return Conflict(
                        "Ya existe un departamento activo con ese nombre en este país.");
                }

                if (!entity.Activo)
                {
                    return Conflict(
                        "No se puede actualizar un departamento que está inactivo.");
                }

                entity.NombreDepartamento =
                    nombre.ToUpper();

                await _ctx.SaveChangesAsync();
                await trx.CommitAsync();

                string nombrePais =
                    await _ctx.Pais
                        .AsNoTracking()
                        .Where(
                            p => p.PaisId ==
                                 paisIdActual)
                        .Select(
                            p => p.NombrePais)
                        .SingleOrDefaultAsync()
                    ?? string.Empty;

                return Ok(
                    new
                    {
                        message =
                            "Departamento actualizado",
                        departamento =
                            new
                            {
                                entity.DepartamentoId,
                                entity.NombreDepartamento,
                                entity.PaisId,
                                NombrePais =
                                    nombrePais
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
        // 4) ELIMINAR
        // DELETE lógico /api/departamento/eliminar/{id}
        // ===========================================
        [HttpDelete("eliminar/{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            Departamento? entity =
                await _ctx.Departamento
                    .FirstOrDefaultAsync(
                        x => x.DepartamentoId == id &&
                             x.Activo);

            if (entity == null)
            {
                return NotFound(
                    new
                    {
                        mensaje =
                            "El departamento no existe o ya está desactivado."
                    });
            }

            var dependencias =
                new List<string>();

            bool tieneMunicipios =
                await _ctx.Municipios.AnyAsync(
                    x => x.DepartamentoId == id);

            if (tieneMunicipios)
                dependencias.Add("municipios");

            if (dependencias.Count > 0)
            {
                return Conflict(
                    new
                    {
                        mensaje =
                            "No se puede eliminar el departamento porque está siendo utilizado.",
                        departamento =
                            new
                            {
                                entity.DepartamentoId,
                                entity.NombreDepartamento,
                                entity.PaisId
                            },
                        usadoEn =
                            dependencias
                    });
            }

            entity.Activo = false;

            await _ctx.SaveChangesAsync();

            return Ok(
                new
                {
                    mensaje =
                        "Departamento desactivado correctamente.",
                    data =
                        new
                        {
                            entity.DepartamentoId,
                            entity.NombreDepartamento,
                            entity.Activo
                        }
                });
        }
    }
}
