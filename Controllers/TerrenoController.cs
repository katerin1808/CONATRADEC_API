using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.TerrenoDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/terreno")]
    public class TerrenoController : ControllerBase
    {
        private readonly DBContext _db;
        public TerrenoController(DBContext db) => _db = db;

        // ============================================================
        // LISTAR TERRENOS (CON UBICACIÓN ANIDADA)
        // ============================================================
        [HttpGet("listar")]
        public async Task<ActionResult<IEnumerable<TerrenoListarDto>>> Listar()
        {
            var lista = await _db.Terreno
                .Where(x => x.activo)
                .Include(x => x.Municipio)
                    .ThenInclude(m => m.Departamento)
                        .ThenInclude(d => d.Pais)
                .Select(x => new TerrenoListarDto
                {
                    terrenoId = x.terrenoId,
                    codigoTerreno = x.codigoTerreno,
                    identificacionPropietarioTerreno = x.identificacionPropietarioTerreno,
                    nombrePropietarioTerreno = x.nombrePropietarioTerreno,
                    telefonoPropietario = x.telefonoPropietario,
                    correoPropietario = x.correoPropietario,
                    direccionTerreno = x.direccionTerreno,
                    extensionManzanaTerreno = x.extensionManzanaTerreno,
                    fechaIngresoTerreno = x.fechaIngresoTerreno,
                    municipioId = x.municipioId,
                    cantidadQuintalesOro = x.cantidadQuintalesOro,
                    cantidadPlantasTerreno = x.cantidadPlantasTerreno,
                    latitud = x.latitud,
                    longitud = x.longitud,

                    ubicacion = new TerrenoUbicacionDto
                    {
                        paisId = x.Municipio.Departamento.Pais.PaisId,
                        nombrePais = x.Municipio.Departamento.Pais.NombrePais,
                        departamentoId = x.Municipio.Departamento.DepartamentoId,
                        nombreDepartamento = x.Municipio.Departamento.NombreDepartamento,
                        municipioId = x.Municipio.MunicipioId,
                        nombreMunicipio = x.Municipio.NombreMunicipio
                    }
                })
                .ToListAsync();

            return Ok(lista);
        }

        // ============================================================
        // CREAR TERRENO (CON TRANSACTION)
        // ============================================================
        [HttpPost("crear")]
        public async Task<IActionResult> Crear([FromBody] TerrenoCrearDto dto)
        {
            using var trans = await _db.Database.BeginTransactionAsync();

            try
            {
                var terreno = new Terreno
                {
                    codigoTerreno = dto.codigoTerreno,
                    identificacionPropietarioTerreno = dto.identificacionPropietarioTerreno,
                    nombrePropietarioTerreno = dto.nombrePropietarioTerreno,
                    telefonoPropietario = dto.telefonoPropietario,
                    correoPropietario = dto.correoPropietario,
                    direccionTerreno = dto.direccionTerreno,
                    extensionManzanaTerreno = dto.extensionManzanaTerreno,
                    fechaIngresoTerreno = dto.fechaIngresoTerreno,
                    municipioId = dto.municipioId,
                    cantidadQuintalesOro = dto.cantidadQuintalesOro,
                    cantidadPlantasTerreno = dto.cantidadPlantasTerreno,
                    latitud = dto.latitud,
                    longitud = dto.longitud,
                    activo = true
                };

                _db.Terreno.Add(terreno);
                await _db.SaveChangesAsync();
                await trans.CommitAsync();

                return Ok("Terreno creado correctamente.");
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                return BadRequest(ex.Message);
            }
        }

        // ============================================================
        // EDITAR TERRENO
        // ============================================================
        [HttpPut("editar/{id}")]
        public async Task<IActionResult> Editar(int id, [FromBody] TerrenoEditarDto dto)
        {
            var terreno = await _db.Terreno.FindAsync(id);
            if (terreno is null)
                return NotFound("Terreno no encontrado.");

            terreno.codigoTerreno = dto.codigoTerreno;
            terreno.identificacionPropietarioTerreno = dto.identificacionPropietarioTerreno;
            terreno.nombrePropietarioTerreno = dto.nombrePropietarioTerreno;
            terreno.telefonoPropietario = dto.telefonoPropietario;
            terreno.correoPropietario = dto.correoPropietario;
            terreno.direccionTerreno = dto.direccionTerreno;
            terreno.extensionManzanaTerreno = dto.extensionManzanaTerreno;
            terreno.fechaIngresoTerreno = dto.fechaIngresoTerreno;
            terreno.municipioId = dto.municipioId;
            terreno.cantidadQuintalesOro = dto.cantidadQuintalesOro;
            terreno.cantidadPlantasTerreno = dto.cantidadPlantasTerreno;
            terreno.latitud = dto.latitud;
            terreno.longitud = dto.longitud;

            await _db.SaveChangesAsync();

            return Ok("Terreno editado correctamente.");
        }

        // ============================================================
        // ELIMINAR (LÓGICO)
        // ============================================================
        [HttpDelete("eliminar/{id:int}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var terreno = await _db.Terreno
                .FirstOrDefaultAsync(x =>
                    x.terrenoId == id &&
                    x.activo);

            if (terreno == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "Terreno no encontrado o ya está desactivado."
                });
            }

            var dependencias = new List<string>();

            var tieneFotos = await _db.FotoTerreno
                .AnyAsync(x => x.terrenoId == id);

            if (tieneFotos)
            {
                dependencias.Add("fotografías del terreno");
            }

            var usadoEnCalculos = await _db.AnalisisSueloCalculos
                .AnyAsync(x => x.terrenoId == id);

            if (usadoEnCalculos)
            {
                dependencias.Add("cálculos de análisis de suelo");
            }

            var usadoEnInterpretaciones = await _db.Interpretaciones
                .AnyAsync(x => x.terrenoId == id);

            if (usadoEnInterpretaciones)
            {
                dependencias.Add("interpretaciones");
            }

            var usadoEnEnmiendas = await _db.enmiendaCalcarea
                .AnyAsync(x =>
                    x.terrenoId.HasValue &&
                    x.terrenoId.Value == id);

            if (usadoEnEnmiendas)
            {
                dependencias.Add("enmiendas calcáreas");
            }

            var usadoEnFormulas = await _db.formulaNutricional
                .AnyAsync(x =>
                    x.terrenoId.HasValue &&
                    x.terrenoId.Value == id);

            if (usadoEnFormulas)
            {
                dependencias.Add("fórmulas nutricionales");
            }

            if (dependencias.Count > 0)
            {
                return Conflict(new
                {
                    mensaje =
                        "No se puede eliminar el terreno porque está siendo utilizado.",

                    terreno = new
                    {
                        terreno.terrenoId,
                        terreno.codigoTerreno,
                        terreno.nombrePropietarioTerreno
                    },

                    usadoEn = dependencias
                });
            }

            terreno.activo = false;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Terreno desactivado correctamente.",
                data = new
                {
                    terreno.terrenoId,
                    terreno.codigoTerreno,
                    terreno.activo
                }
            });
        }

        [HttpGet("buscar")]
        public async Task<IActionResult> Buscar(
    string? texto,
    string? codigoTerreno,
    string? nombrePropietario,
    string? identificacionPropietario,
    string? direccion,
    int? paisId,
    int? departamentoId,
    int? municipioId,
    int page = 1,
    int pageSize = 20)
        {
            if (page < 1)
                page = 1;

            if (pageSize < 1)
                pageSize = 20;

            if (pageSize > 100)
                pageSize = 100;

            var query = _db.Terreno
                .Include(x => x.Municipio)
                    .ThenInclude(m => m.Departamento)
                        .ThenInclude(d => d.Pais)
                .Where(x => x.activo)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(texto))
            {
                texto = texto.Trim();

                query = query.Where(x =>
                    x.codigoTerreno.Contains(texto) ||
                    x.nombrePropietarioTerreno.Contains(texto) ||
                    x.identificacionPropietarioTerreno.Contains(texto) ||
                    x.direccionTerreno.Contains(texto));
            }

            if (!string.IsNullOrWhiteSpace(codigoTerreno))
            {
                codigoTerreno = codigoTerreno.Trim();
                query = query.Where(x => x.codigoTerreno.Contains(codigoTerreno));
            }

            if (!string.IsNullOrWhiteSpace(nombrePropietario))
            {
                nombrePropietario = nombrePropietario.Trim();
                query = query.Where(x => x.nombrePropietarioTerreno.Contains(nombrePropietario));
            }

            if (!string.IsNullOrWhiteSpace(identificacionPropietario))
            {
                identificacionPropietario = identificacionPropietario.Trim();
                query = query.Where(x => x.identificacionPropietarioTerreno.Contains(identificacionPropietario));
            }

            if (!string.IsNullOrWhiteSpace(direccion))
            {
                direccion = direccion.Trim();
                query = query.Where(x => x.direccionTerreno.Contains(direccion));
            }
            if (paisId.HasValue)
            {
                bool existePais = await _db.Pais
                    .AnyAsync(x => x.PaisId == paisId.Value && x.Activo);

                if (!existePais)
                    return BadRequest(new { mensaje = "El país no existe o está inactivo." });
            }

            if (departamentoId.HasValue)
            {
                var departamento = await _db.Departamento
                    .FirstOrDefaultAsync(x => x.DepartamentoId == departamentoId.Value && x.Activo);

                if (departamento == null)
                    return BadRequest(new { mensaje = "El departamento no existe o está inactivo." });

                if (paisId.HasValue && departamento.PaisId != paisId.Value)
                    return BadRequest(new { mensaje = "El departamento no pertenece al país seleccionado." });
            }

            if (municipioId.HasValue)
            {
                var municipio = await _db.Municipios
                    .FirstOrDefaultAsync(x => x.MunicipioId == municipioId.Value && x.Activo);

                if (municipio == null)
                    return BadRequest(new { mensaje = "El municipio no existe o está inactivo." });

                if (departamentoId.HasValue && municipio.DepartamentoId != departamentoId.Value)
                    return BadRequest(new { mensaje = "El municipio no pertenece al departamento seleccionado." });

                if (paisId.HasValue)
                {
                    bool municipioPertenecePais = await _db.Departamento
                        .AnyAsync(x =>
                            x.DepartamentoId == municipio.DepartamentoId &&
                            x.PaisId == paisId.Value &&
                            x.Activo);

                    if (!municipioPertenecePais)
                        return BadRequest(new { mensaje = "El municipio no pertenece al país seleccionado." });
                }
            }
            if (paisId.HasValue)
            {
                query = query.Where(x =>
            x.Municipio != null &&
            x.Municipio.Departamento != null &&
            x.Municipio.Departamento.PaisId == paisId.Value);
            }

            if (departamentoId.HasValue)
            {
                query = query.Where(x =>
                    x.Municipio != null &&
                    x.Municipio.DepartamentoId == departamentoId.Value);
            }

            if (municipioId.HasValue)
            {
                query = query.Where(x => x.municipioId == municipioId.Value);
            }

            int total = await query.CountAsync();

            var data = await query
                .OrderBy(x => x.codigoTerreno)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.terrenoId,
                    x.codigoTerreno,
                    x.identificacionPropietarioTerreno,
                    x.nombrePropietarioTerreno,
                    x.telefonoPropietario,
                    x.correoPropietario,
                    x.direccionTerreno,
                    x.extensionManzanaTerreno,
                    x.fechaIngresoTerreno,
                    x.cantidadPlantasTerreno,
                    x.cantidadQuintalesOro,
                    x.latitud,
                    x.longitud,
                    x.municipioId,

                    ubicacion = new
                    {
                        paisId = x.Municipio.Departamento.Pais.PaisId,
                        nombrePais = x.Municipio.Departamento.Pais.NombrePais,
                        departamentoId = x.Municipio.Departamento.DepartamentoId,
                        nombreDepartamento = x.Municipio.Departamento.NombreDepartamento,
                        municipioId = x.Municipio.MunicipioId,
                        nombreMunicipio = x.Municipio.NombreMunicipio
                    }
                })
                .ToListAsync();

            return Ok(new
            {
                total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(total / (decimal)pageSize),
                data
            });
        }

    }
}
