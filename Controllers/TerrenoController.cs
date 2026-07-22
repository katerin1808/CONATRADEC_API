using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using static CONATRADEC_API.DTOs.TerrenoDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/terreno")]
    public class TerrenoController : ControllerBase
    {
        private readonly DBContext _db;

        private static readonly Regex CedulaRegex = new(
            @"^\d{3}-\d{6}-\d{4}[A-Z]$",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);

        public TerrenoController(DBContext db) => _db = db;

        private static string? ValidarDatosTerreno(
            string? identificacionPropietario,
            decimal extensionManzanas,
            decimal cantidadQuintales,
            int cantidadPlantas,
            int telefono,
            decimal latitud,
            decimal longitud)
        {
            if (string.IsNullOrWhiteSpace(identificacionPropietario) ||
                !CedulaRegex.IsMatch(identificacionPropietario.Trim()))
            {
                return "La identificación del propietario debe tener el formato 001-080701-1050R.";
            }

            if (extensionManzanas <= 0)
                return "La extensión del terreno debe ser mayor que cero.";

            if (!TieneMaximoDosDecimales(extensionManzanas))
                return "La extensión del terreno solo permite dos decimales.";

            if (cantidadQuintales < 0)
                return "La cantidad de quintales no puede ser negativa.";

            if (!TieneMaximoDosDecimales(cantidadQuintales))
                return "La cantidad de quintales solo permite dos decimales.";

            if (cantidadPlantas < 0)
                return "La cantidad de plantas debe ser un número entero positivo o cero.";

            if (telefono < 0)
                return "El teléfono solo debe contener números enteros positivos.";

            if (latitud < -90 || latitud > 90)
                return "La latitud debe estar entre -90 y 90.";

            if (longitud < -180 || longitud > 180)
                return "La longitud debe estar entre -180 y 180.";

            return null;
        }

        private static bool TieneMaximoDosDecimales(decimal valor) =>
            decimal.Round(valor, 2) == valor;

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
            string? errorValidacion = ValidarDatosTerreno(
                dto.identificacionPropietarioTerreno,
                dto.extensionManzanaTerreno,
                dto.cantidadQuintalesOro,
                dto.cantidadPlantasTerreno,
                dto.telefonoPropietario,
                dto.latitud,
                dto.longitud);

            if (errorValidacion != null)
            {
                return BadRequest(new
                {
                    mensaje = errorValidacion
                });
            }

            dto.identificacionPropietarioTerreno =
                dto.identificacionPropietarioTerreno.Trim().ToUpperInvariant();

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
                    extensionManzanaTerreno = decimal.Round(dto.extensionManzanaTerreno, 2),
                    fechaIngresoTerreno = dto.fechaIngresoTerreno,
                    municipioId = dto.municipioId,
                    cantidadQuintalesOro = decimal.Round(dto.cantidadQuintalesOro, 2),
                    cantidadPlantasTerreno = dto.cantidadPlantasTerreno,
                    latitud = dto.latitud,
                    longitud = dto.longitud,
                    activo = true
                };

                _db.Terreno.Add(terreno);
                await _db.SaveChangesAsync();
                await trans.CommitAsync();

                return Ok(new
                {
                    mensaje = "Terreno creado correctamente.",
                    data = new
                    {
                        terreno.terrenoId,
                        terreno.codigoTerreno
                    }
                });
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();

                return BadRequest(new
                {
                    mensaje = ex.InnerException?.Message ?? ex.Message
                });
            }
        }

        // ============================================================
        // EDITAR TERRENO
        // ============================================================
        [HttpPut("editar/{id}")]
        public async Task<IActionResult> Editar(int id, [FromBody] TerrenoEditarDto dto)
        {
            string? errorValidacion = ValidarDatosTerreno(
                dto.identificacionPropietarioTerreno,
                dto.extensionManzanaTerreno,
                dto.cantidadQuintalesOro,
                dto.cantidadPlantasTerreno,
                dto.telefonoPropietario,
                dto.latitud,
                dto.longitud);

            if (errorValidacion != null)
            {
                return BadRequest(new
                {
                    mensaje = errorValidacion
                });
            }

            dto.identificacionPropietarioTerreno =
                dto.identificacionPropietarioTerreno.Trim().ToUpperInvariant();

            var terreno = await _db.Terreno.FindAsync(id);

            if (terreno is null)
            {
                return NotFound(new
                {
                    mensaje = "Terreno no encontrado."
                });
            }

            terreno.codigoTerreno = dto.codigoTerreno;
            terreno.identificacionPropietarioTerreno = dto.identificacionPropietarioTerreno;
            terreno.nombrePropietarioTerreno = dto.nombrePropietarioTerreno;
            terreno.telefonoPropietario = dto.telefonoPropietario;
            terreno.correoPropietario = dto.correoPropietario;
            terreno.direccionTerreno = dto.direccionTerreno;
            terreno.extensionManzanaTerreno = decimal.Round(dto.extensionManzanaTerreno, 2);
            terreno.fechaIngresoTerreno = dto.fechaIngresoTerreno;
            terreno.municipioId = dto.municipioId;
            terreno.cantidadQuintalesOro = decimal.Round(dto.cantidadQuintalesOro, 2);
            terreno.cantidadPlantasTerreno = dto.cantidadPlantasTerreno;
            terreno.latitud = dto.latitud;
            terreno.longitud = dto.longitud;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Terreno editado correctamente."
            });
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
                    mensaje = "Terreno no encontrado o ya está desactivado."
                });
            }

            // La eliminación es lógica. No se borran físicamente las fotos,
            // análisis ni cálculos relacionados, para conservar el historial.
            terreno.activo = false;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                mensaje = "Terreno eliminado correctamente.",
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
                {
                    return BadRequest(new
                    {
                        mensaje = "El país no existe o está inactivo."
                    });
                }
            }

            if (departamentoId.HasValue)
            {
                var departamento = await _db.Departamento
                    .FirstOrDefaultAsync(x =>
                        x.DepartamentoId == departamentoId.Value &&
                        x.Activo);

                if (departamento == null)
                {
                    return BadRequest(new
                    {
                        mensaje = "El departamento no existe o está inactivo."
                    });
                }

                if (paisId.HasValue && departamento.PaisId != paisId.Value)
                {
                    return BadRequest(new
                    {
                        mensaje = "El departamento no pertenece al país seleccionado."
                    });
                }
            }

            if (municipioId.HasValue)
            {
                var municipio = await _db.Municipios
                    .FirstOrDefaultAsync(x =>
                        x.MunicipioId == municipioId.Value &&
                        x.Activo);

                if (municipio == null)
                {
                    return BadRequest(new
                    {
                        mensaje = "El municipio no existe o está inactivo."
                    });
                }

                if (departamentoId.HasValue &&
                    municipio.DepartamentoId != departamentoId.Value)
                {
                    return BadRequest(new
                    {
                        mensaje = "El municipio no pertenece al departamento seleccionado."
                    });
                }

                if (paisId.HasValue)
                {
                    bool municipioPertenecePais = await _db.Departamento
                        .AnyAsync(x =>
                            x.DepartamentoId == municipio.DepartamentoId &&
                            x.PaisId == paisId.Value &&
                            x.Activo);

                    if (!municipioPertenecePais)
                    {
                        return BadRequest(new
                        {
                            mensaje = "El municipio no pertenece al país seleccionado."
                        });
                    }
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
