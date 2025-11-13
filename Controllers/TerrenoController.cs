using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.TerrenoDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/ terreno ")]
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
            terreno.latitud = dto.latitud;
            terreno.longitud = dto.longitud;

            await _db.SaveChangesAsync();
            return Ok("Terreno editado correctamente.");
        }

        // ============================================================
        // ELIMINAR (LÓGICO)
        // ============================================================
        [HttpDelete("eliminar/{id}")]
        public async Task<IActionResult> Eliminar(int id)
        {
            var terreno = await _db.Terreno.FindAsync(id);
            if (terreno is null)
                return NotFound("Terreno no encontrado.");

            terreno.activo = false;
            await _db.SaveChangesAsync();

            return Ok("Terreno eliminado correctamente.");
        }
    }
}
