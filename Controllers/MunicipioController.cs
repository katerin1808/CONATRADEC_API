using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.MunicipioDto;

namespace CONATRADEC_API.Controllers
{
    public class MunicipioController : Controller
    {
        private readonly DBContext _ctx;

        public MunicipioController(DBContext ctx)
        {
            _ctx = ctx;
        }

        [HttpPost("crear")]
        [Consumes("application/json")]
        public async Task<ActionResult> Create(
            [FromBody] MunicipioCreateRequest? req)
        {
            if (req is null)
                return BadRequest("No se recibieron los datos del municipio.");

            string? nombre =
                req.NombreMunicipio?
                    .ReplaceLineEndings(" ")
                    .Trim();

            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest("Ingrese el nombre del municipio.");

            var departamento = await _ctx.Departamento
                .AsNoTracking()
                .Include(d => d.Pais)
                .Where(
                    d => d.DepartamentoId == req.DepartamentoId &&
                         d.Activo &&
                         d.Pais != null &&
                         d.Pais.Activo)
                .Select(d => new
                {
                    d.DepartamentoId,
                    d.NombreDepartamento,
                    d.PaisId,
                    NombrePais = d.Pais!.NombrePais
                })
                .SingleOrDefaultAsync();

            if (departamento is null)
            {
                return BadRequest(
                    "El departamento seleccionado no existe o está inactivo.");
            }

            string nombreNormalizado =
                nombre.ToUpperInvariant();

            bool duplicado = await _ctx.Municipios.AnyAsync(
                m => m.DepartamentoId == req.DepartamentoId &&
                     m.Activo &&
                     EF.Functions.Collate(
                         m.NombreMunicipio.ToUpper(),
                         "Modern_Spanish_CI_AI") ==
                     nombreNormalizado);

            if (duplicado)
            {
                return Conflict(
                    "Ya existe un municipio activo con ese nombre en el departamento seleccionado.");
            }

            await using var transaccion =
                await _ctx.Database.BeginTransactionAsync();

            try
            {
                var municipio = new Municipio
                {
                    NombreMunicipio = nombreNormalizado,
                    DepartamentoId = req.DepartamentoId,
                    Activo = true
                };

                _ctx.Municipios.Add(municipio);
                await _ctx.SaveChangesAsync();
                await transaccion.CommitAsync();

                return Ok(new
                {
                    message = "Municipio creado correctamente.",
                    municipio = new
                    {
                        municipio.MunicipioId,
                        municipio.NombreMunicipio,
                        Departamento = departamento.NombreDepartamento,
                        Pais = departamento.NombrePais
                    }
                });
            }
            catch
            {
                await transaccion.RollbackAsync();
                throw;
            }
        }

        [HttpGet("listarTodos-por-departamento-por-pais")]
        public async Task<ActionResult<IEnumerable<MunicipioResponse>>>
            ListarTodosConDepartamentoYpais()
        {
            List<MunicipioResponse> municipios =
                await _ctx.Municipios
                    .AsNoTracking()
                    .Where(
                        m => m.Activo &&
                             m.Departamento != null &&
                             m.Departamento.Activo &&
                             m.Departamento.Pais != null &&
                             m.Departamento.Pais.Activo)
                    .OrderBy(
                        m => m.Departamento!.NombreDepartamento)
                    .ThenBy(m => m.NombreMunicipio)
                    .Select(m => new MunicipioResponse
                    {
                        MunicipioId = m.MunicipioId,
                        NombreMunicipio = m.NombreMunicipio,
                        DepartamentoId = m.DepartamentoId,
                        NombreDepartamento =
                            m.Departamento!.NombreDepartamento,
                        PaisId = m.Departamento.PaisId,
                        NombrePais =
                            m.Departamento.Pais!.NombrePais,
                        Activo = m.Activo
                    })
                    .ToListAsync();

            if (municipios.Count == 0)
                return NotFound("No existen municipios activos registrados.");

            return Ok(municipios);
        }

        [HttpGet("por-departamento/{departamentoId:int}")]
        public async Task<ActionResult<IEnumerable<MunicipioResponse>>>
            ListarPorDepartamento(int departamentoId)
        {
            var departamento = await _ctx.Departamento
                .AsNoTracking()
                .Include(d => d.Pais)
                .Where(
                    d => d.DepartamentoId == departamentoId &&
                         d.Activo &&
                         d.Pais != null &&
                         d.Pais.Activo)
                .Select(d => new
                {
                    d.DepartamentoId,
                    d.NombreDepartamento,
                    d.PaisId,
                    NombrePais = d.Pais!.NombrePais
                })
                .SingleOrDefaultAsync();

            if (departamento is null)
            {
                return NotFound(
                    "El departamento seleccionado no existe o está inactivo.");
            }

            List<MunicipioResponse> municipios =
                await _ctx.Municipios
                    .AsNoTracking()
                    .Where(
                        m => m.DepartamentoId == departamentoId &&
                             m.Activo)
                    .OrderBy(m => m.NombreMunicipio)
                    .Select(m => new MunicipioResponse
                    {
                        MunicipioId = m.MunicipioId,
                        NombreMunicipio = m.NombreMunicipio,
                        DepartamentoId = m.DepartamentoId,
                        NombreDepartamento =
                            departamento.NombreDepartamento,
                        PaisId = departamento.PaisId,
                        NombrePais = departamento.NombrePais,
                        Activo = m.Activo
                    })
                    .ToListAsync();

            if (municipios.Count == 0)
            {
                return NotFound(
                    $"El departamento '{departamento.NombreDepartamento}' no tiene municipios activos.");
            }

            return Ok(municipios);
        }

        [HttpPut("actualizar/{id:int}")]
        [Consumes("application/json")]
        public async Task<ActionResult> Update(
            int id,
            [FromBody] MunicipioUpdateRequest? req)
        {
            if (req is null)
                return BadRequest("No se recibieron los datos del municipio.");

            Municipio? municipio =
                await _ctx.Municipios.FindAsync(id);

            if (municipio is null)
                return NotFound("El municipio no existe.");

            if (!municipio.Activo)
            {
                return Conflict(
                    "No se puede actualizar un municipio inactivo.");
            }

            string? nombre =
                req.NombreMunicipio?
                    .ReplaceLineEndings(" ")
                    .Trim();

            if (string.IsNullOrWhiteSpace(nombre))
                return BadRequest("Ingrese el nombre del municipio.");

            string nombreNormalizado =
                nombre.ToUpperInvariant();

            bool duplicado = await _ctx.Municipios.AnyAsync(
                m => m.MunicipioId != id &&
                     m.DepartamentoId == municipio.DepartamentoId &&
                     m.Activo &&
                     EF.Functions.Collate(
                         m.NombreMunicipio.ToUpper(),
                         "Modern_Spanish_CI_AI") ==
                     nombreNormalizado);

            if (duplicado)
            {
                return Conflict(
                    "Ya existe un municipio activo con ese nombre en el departamento.");
            }

            await using var transaccion =
                await _ctx.Database.BeginTransactionAsync();

            try
            {
                municipio.NombreMunicipio = nombreNormalizado;

                await _ctx.SaveChangesAsync();
                await transaccion.CommitAsync();

                var departamento = await _ctx.Departamento
                    .AsNoTracking()
                    .Include(d => d.Pais)
                    .Where(
                        d => d.DepartamentoId ==
                             municipio.DepartamentoId)
                    .Select(d => new
                    {
                        d.NombreDepartamento,
                        NombrePais =
                            d.Pais != null
                                ? d.Pais.NombrePais
                                : string.Empty
                    })
                    .SingleOrDefaultAsync();

                return Ok(new
                {
                    message = "Municipio actualizado correctamente.",
                    municipio = new
                    {
                        municipio.MunicipioId,
                        municipio.NombreMunicipio,
                        Departamento =
                            departamento?.NombreDepartamento ??
                            string.Empty,
                        Pais =
                            departamento?.NombrePais ??
                            string.Empty
                    }
                });
            }
            catch
            {
                await transaccion.RollbackAsync();
                throw;
            }
        }

        [HttpDelete("eliminar/{id:int}")]
        public async Task<ActionResult> Delete(int id)
        {
            Municipio? municipio = await _ctx.Municipios
                .FirstOrDefaultAsync(
                    x => x.MunicipioId == id && x.Activo);

            if (municipio is null)
            {
                return NotFound(new
                {
                    mensaje =
                        "El municipio no existe o ya está desactivado."
                });
            }

            var dependencias = new List<string>();

            bool usadoEnUsuarios = await _ctx.Usuarios
                .AnyAsync(x => x.municipioId == id);

            if (usadoEnUsuarios)
                dependencias.Add("usuarios");

            bool usadoEnTerrenos = await _ctx.Terreno
                .AnyAsync(x => x.municipioId == id);

            if (usadoEnTerrenos)
                dependencias.Add("terrenos");

            if (dependencias.Count > 0)
            {
                return Conflict(new
                {
                    mensaje =
                        "No se puede eliminar el municipio porque está siendo utilizado.",
                    municipio = new
                    {
                        municipio.MunicipioId,
                        municipio.NombreMunicipio,
                        municipio.DepartamentoId
                    },
                    usadoEn = dependencias
                });
            }

            municipio.Activo = false;
            await _ctx.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Municipio desactivado correctamente.",
                data = new
                {
                    municipio.MunicipioId,
                    municipio.NombreMunicipio,
                    municipio.Activo
                }
            });
        }
    }
}
