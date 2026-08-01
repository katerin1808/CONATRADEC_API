using CONATRADEC_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.DTOs.UnidadDeMedidaDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Route("api/unidad-medida")]
    public class UnidadMedidaController : ControllerBase
    {
        private readonly DBContext db;

        public UnidadMedidaController(DBContext db)
        {
            this.db = db;
        }

        [HttpGet("listar")]
        public async Task<IActionResult> Listar(
            CancellationToken cancellationToken)
        {
            List<UnidadMedidaRespuestaDto> data =
                await db.UnidadMedidas
                    .AsNoTracking()
                    .Where(x => x.activo)
                    .OrderBy(x => x.nombreUnidadMedida)
                    .Select(x => new UnidadMedidaRespuestaDto
                    {
                        unidadMedidaId = x.unidadMedidaId,
                        nombreUnidadMedida = x.nombreUnidadMedida,
                        activo = x.activo
                    })
                    .ToListAsync(cancellationToken);

            return Ok(data);
        }

        [HttpGet("listar-inactivas")]
        public async Task<IActionResult> ListarInactivas(
            CancellationToken cancellationToken)
        {
            List<UnidadMedidaRespuestaDto> data =
                await db.UnidadMedidas
                    .AsNoTracking()
                    .Where(x => !x.activo)
                    .OrderBy(x => x.nombreUnidadMedida)
                    .Select(x => new UnidadMedidaRespuestaDto
                    {
                        unidadMedidaId = x.unidadMedidaId,
                        nombreUnidadMedida = x.nombreUnidadMedida,
                        activo = x.activo
                    })
                    .ToListAsync(cancellationToken);

            return Ok(data);
        }

        [HttpGet("obtener/{id:int}")]
        public async Task<IActionResult> ObtenerPorId(
            int id,
            CancellationToken cancellationToken)
        {
            UnidadMedidaRespuestaDto? data =
                await db.UnidadMedidas
                    .AsNoTracking()
                    .Where(x =>
                        x.unidadMedidaId == id &&
                        x.activo)
                    .Select(x => new UnidadMedidaRespuestaDto
                    {
                        unidadMedidaId = x.unidadMedidaId,
                        nombreUnidadMedida = x.nombreUnidadMedida,
                        activo = x.activo
                    })
                    .FirstOrDefaultAsync(cancellationToken);

            if (data == null)
            {
                return NotFound(new
                {
                    mensaje = "Unidad de medida no encontrada."
                });
            }

            return Ok(data);
        }

        [HttpPost("crear")]
        public async Task<IActionResult> Crear(
            [FromBody] UnidadMedidaCrearDto dto,
            CancellationToken cancellationToken)
        {
            string nombre =
                NormalizarNombre(dto.nombreUnidadMedida);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    mensaje = "El nombre es obligatorio."
                });
            }

            bool existeActiva =
                await db.UnidadMedidas
                    .AnyAsync(
                        x =>
                            x.nombreUnidadMedida
                                .Trim()
                                .ToUpper() == nombre &&
                            x.activo,
                        cancellationToken);

            if (existeActiva)
            {
                return BadRequest(new
                {
                    mensaje =
                        "Ya existe una unidad de medida activa con ese nombre."
                });
            }

            UnidadMedida? inactiva =
                await db.UnidadMedidas
                    .FirstOrDefaultAsync(
                        x =>
                            x.nombreUnidadMedida
                                .Trim()
                                .ToUpper() == nombre &&
                            !x.activo,
                        cancellationToken);

            if (inactiva != null)
            {
                return Conflict(new
                {
                    mensaje =
                        "Existe una unidad desactivada con ese nombre. Reactívela para conservar su historial.",
                    data = new
                    {
                        inactiva.unidadMedidaId,
                        inactiva.nombreUnidadMedida,
                        inactiva.activo
                    }
                });
            }

            var entity = new UnidadMedida
            {
                nombreUnidadMedida = nombre,
                activo = true
            };

            db.UnidadMedidas.Add(entity);
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                mensaje =
                    "Unidad de medida creada correctamente.",
                data = new
                {
                    entity.unidadMedidaId,
                    entity.nombreUnidadMedida,
                    entity.activo
                }
            });
        }

        [HttpPut("editar/{id:int}")]
        public async Task<IActionResult> Editar(
            int id,
            [FromBody] UnidadMedidaEditarDto dto,
            CancellationToken cancellationToken)
        {
            UnidadMedida? entity =
                await db.UnidadMedidas
                    .FirstOrDefaultAsync(
                        x =>
                            x.unidadMedidaId == id &&
                            x.activo,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    mensaje = "Unidad de medida no encontrada."
                });
            }

            string nombre =
                NormalizarNombre(dto.nombreUnidadMedida);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    mensaje = "El nombre es obligatorio."
                });
            }

            bool existe =
                await db.UnidadMedidas
                    .AnyAsync(
                        x =>
                            x.unidadMedidaId != id &&
                            x.nombreUnidadMedida
                                .Trim()
                                .ToUpper() == nombre &&
                            x.activo,
                        cancellationToken);

            if (existe)
            {
                return BadRequest(new
                {
                    mensaje =
                        "Ya existe una unidad de medida activa con ese nombre."
                });
            }

            entity.nombreUnidadMedida = nombre;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                mensaje =
                    "Unidad de medida actualizada correctamente.",
                data = new
                {
                    entity.unidadMedidaId,
                    entity.nombreUnidadMedida,
                    entity.activo
                }
            });
        }

        [HttpDelete("eliminar/{id:int}")]
        public async Task<IActionResult> Eliminar(
            int id,
            CancellationToken cancellationToken)
        {
            UnidadMedida? entity =
                await db.UnidadMedidas
                    .FirstOrDefaultAsync(
                        x =>
                            x.unidadMedidaId == id &&
                            x.activo,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "Unidad de medida no encontrada o ya está desactivada."
                });
            }

            List<string> dependencias =
                await ObtenerDependenciasAsync(
                    id,
                    cancellationToken);

            if (dependencias.Count > 0)
            {
                return Conflict(new
                {
                    mensaje =
                        "No se puede eliminar la unidad de medida porque está siendo utilizada.",
                    unidadMedida = new
                    {
                        entity.unidadMedidaId,
                        entity.nombreUnidadMedida
                    },
                    usadoEn = dependencias
                });
            }

            entity.activo = false;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                mensaje =
                    "Unidad de medida desactivada correctamente.",
                data = new
                {
                    entity.unidadMedidaId,
                    entity.nombreUnidadMedida,
                    entity.activo
                }
            });
        }

        [HttpPut("reactivar/{id:int}")]
        public async Task<IActionResult> Reactivar(
            int id,
            CancellationToken cancellationToken)
        {
            UnidadMedida? entity =
                await db.UnidadMedidas
                    .FirstOrDefaultAsync(
                        x =>
                            x.unidadMedidaId == id &&
                            !x.activo,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "Unidad de medida inactiva no encontrada."
                });
            }

            string nombre =
                NormalizarNombre(entity.nombreUnidadMedida);

            bool duplicada =
                await db.UnidadMedidas
                    .AnyAsync(
                        x =>
                            x.unidadMedidaId != id &&
                            x.activo &&
                            x.nombreUnidadMedida
                                .Trim()
                                .ToUpper() == nombre,
                        cancellationToken);

            if (duplicada)
            {
                return Conflict(new
                {
                    mensaje =
                        "No se puede reactivar porque ya existe otra unidad activa con el mismo nombre."
                });
            }

            entity.activo = true;
            await db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                mensaje =
                    "Unidad de medida reactivada correctamente.",
                data = new
                {
                    entity.unidadMedidaId,
                    entity.nombreUnidadMedida,
                    entity.activo
                }
            });
        }

        private async Task<List<string>> ObtenerDependenciasAsync(
            int id,
            CancellationToken cancellationToken)
        {
            var dependencias = new List<string>();

            if (await db.AnalisisSueloElementos.AnyAsync(
                    x => x.unidadMedidaId == id,
                    cancellationToken))
            {
                dependencias.Add(
                    "elementos de análisis de suelo");
            }

            if (await db.AnalisisSueloCalculoElementos.AnyAsync(
                    x => x.unidadMedidaId == id,
                    cancellationToken))
            {
                dependencias.Add(
                    "cálculos de análisis de suelo");
            }

            if (await db.AnalisisSueloCalculos.AnyAsync(
                    x => x.unidadMedidaMateriaOrganicaId == id,
                    cancellationToken))
            {
                dependencias.Add(
                    "mediciones de materia orgánica");
            }

            if (await db.RangoNutrimentales.AnyAsync(
                    x => x.unidadMedidaId == id,
                    cancellationToken))
            {
                dependencias.Add(
                    "rangos nutrimentales");
            }

            return dependencias;
        }

        private static string NormalizarNombre(string? nombre)
        {
            return (nombre ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();
        }
    }
}
