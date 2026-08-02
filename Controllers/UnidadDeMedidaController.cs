using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using static CONATRADEC_API.DTOs.UnidadDeMedidaDto;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/unidad-medida")]
    public sealed class UnidadMedidaController :
        ControllerBase
    {
        private const string PermisoAnterior =
            "elementoQuimicoPage";

        private readonly DBContext db;
        private readonly PermisoApiService permisos;

        public UnidadMedidaController(
            DBContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;
        }

        /// <summary>
        /// Lista operativa utilizada por formularios. Continúa disponible
        /// para cualquier usuario autenticado.
        /// </summary>
        [HttpGet("listar")]
        public async Task<IActionResult> Listar(
            CancellationToken cancellationToken)
        {
            List<UnidadMedidaRespuestaDto> data =
                await db.UnidadMedidas
                    .AsNoTracking()
                    .Where(item => item.activo)
                    .OrderBy(item =>
                        item.nombreUnidadMedida)
                    .Select(item =>
                        new UnidadMedidaRespuestaDto
                        {
                            unidadMedidaId =
                                item.unidadMedidaId,
                            nombreUnidadMedida =
                                item.nombreUnidadMedida,
                            activo = item.activo
                        })
                    .ToListAsync(cancellationToken);

            return Ok(data);
        }

        [HttpGet("listar-inactivas")]
        public async Task<IActionResult> ListarInactivas(
            CancellationToken cancellationToken)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            List<UnidadMedidaRespuestaDto> data =
                await db.UnidadMedidas
                    .AsNoTracking()
                    .Where(item => !item.activo)
                    .OrderBy(item =>
                        item.nombreUnidadMedida)
                    .Select(item =>
                        new UnidadMedidaRespuestaDto
                        {
                            unidadMedidaId =
                                item.unidadMedidaId,
                            nombreUnidadMedida =
                                item.nombreUnidadMedida,
                            activo = item.activo
                        })
                    .ToListAsync(cancellationToken);

            return Ok(data);
        }

        [HttpGet("obtener/{id:int}")]
        public async Task<IActionResult> ObtenerPorId(
            int id,
            CancellationToken cancellationToken)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            UnidadMedidaRespuestaDto? data =
                await db.UnidadMedidas
                    .AsNoTracking()
                    .Where(item =>
                        item.unidadMedidaId == id &&
                        item.activo)
                    .Select(item =>
                        new UnidadMedidaRespuestaDto
                        {
                            unidadMedidaId =
                                item.unidadMedidaId,
                            nombreUnidadMedida =
                                item.nombreUnidadMedida,
                            activo = item.activo
                        })
                    .FirstOrDefaultAsync(
                        cancellationToken);

            if (data == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "Unidad de medida no encontrada."
                });
            }

            return Ok(data);
        }

        [HttpPost("crear")]
        public async Task<IActionResult> Crear(
            [FromBody] UnidadMedidaCrearDto dto,
            CancellationToken cancellationToken)
        {
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            string nombre =
                NormalizarNombre(
                    dto.nombreUnidadMedida);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    mensaje =
                        "El nombre es obligatorio."
                });
            }

            bool existeActiva =
                await db.UnidadMedidas.AnyAsync(
                    item =>
                        item.nombreUnidadMedida
                            .Trim()
                            .ToUpper() ==
                        nombre &&
                        item.activo,
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
                        item =>
                            item.nombreUnidadMedida
                                .Trim()
                                .ToUpper() ==
                            nombre &&
                            !item.activo,
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

            var entity =
                new UnidadMedida
                {
                    nombreUnidadMedida =
                        nombre,
                    activo = true
                };

            db.UnidadMedidas.Add(entity);

            await db.SaveChangesAsync(
                cancellationToken);

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
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            UnidadMedida? entity =
                await db.UnidadMedidas
                    .FirstOrDefaultAsync(
                        item =>
                            item.unidadMedidaId == id &&
                            item.activo,
                        cancellationToken);

            if (entity == null)
            {
                return NotFound(new
                {
                    mensaje =
                        "Unidad de medida no encontrada."
                });
            }

            string nombre =
                NormalizarNombre(
                    dto.nombreUnidadMedida);

            if (string.IsNullOrWhiteSpace(nombre))
            {
                return BadRequest(new
                {
                    mensaje =
                        "El nombre es obligatorio."
                });
            }

            bool existe =
                await db.UnidadMedidas.AnyAsync(
                    item =>
                        item.unidadMedidaId != id &&
                        item.nombreUnidadMedida
                            .Trim()
                            .ToUpper() ==
                        nombre &&
                        item.activo,
                    cancellationToken);

            if (existe)
            {
                return BadRequest(new
                {
                    mensaje =
                        "Ya existe una unidad de medida activa con ese nombre."
                });
            }

            entity.nombreUnidadMedida =
                nombre;

            await db.SaveChangesAsync(
                cancellationToken);

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
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Eliminar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            UnidadMedida? entity =
                await db.UnidadMedidas
                    .FirstOrDefaultAsync(
                        item =>
                            item.unidadMedidaId == id &&
                            item.activo,
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

            await db.SaveChangesAsync(
                cancellationToken);

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
            IActionResult? acceso =
                await ValidarAccesoAsync(
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            UnidadMedida? entity =
                await db.UnidadMedidas
                    .FirstOrDefaultAsync(
                        item =>
                            item.unidadMedidaId == id &&
                            !item.activo,
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
                NormalizarNombre(
                    entity.nombreUnidadMedida);

            bool duplicada =
                await db.UnidadMedidas.AnyAsync(
                    item =>
                        item.unidadMedidaId != id &&
                        item.activo &&
                        item.nombreUnidadMedida
                            .Trim()
                            .ToUpper() ==
                        nombre,
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

            await db.SaveChangesAsync(
                cancellationToken);

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

        private async Task<List<string>>
            ObtenerDependenciasAsync(
                int id,
                CancellationToken cancellationToken)
        {
            var dependencias =
                new List<string>();

            if (await db.AnalisisSueloElementos
                    .AnyAsync(
                        item =>
                            item.unidadMedidaId == id,
                        cancellationToken))
            {
                dependencias.Add(
                    "elementos de análisis de suelo");
            }

            if (await db.AnalisisSueloCalculoElementos
                    .AnyAsync(
                        item =>
                            item.unidadMedidaId == id,
                        cancellationToken))
            {
                dependencias.Add(
                    "cálculos de análisis de suelo");
            }

            if (await db.AnalisisSueloCalculos
                    .AnyAsync(
                        item =>
                            item.unidadMedidaMateriaOrganicaId ==
                            id,
                        cancellationToken))
            {
                dependencias.Add(
                    "mediciones de materia orgánica");
            }

            if (await db.RangoNutrimentales
                    .AnyAsync(
                        item =>
                            item.unidadMedidaId == id,
                        cancellationToken))
            {
                dependencias.Add(
                    "rangos nutrimentales");
            }

            return dependencias;
        }

        private async Task<IActionResult?> ValidarAccesoAsync(
            TipoPermisoApi tipoPermiso,
            CancellationToken cancellationToken)
        {
            int? usuarioId =
                ObtenerUsuarioId();

            ResultadoPermisoApi resultado =
                await permisos.ValidarAsync(
                    usuarioId,
                    PortalWebDatabaseInitializer
                        .UnidadesConversionesWeb,
                    tipoPermiso,
                    cancellationToken);

            if (!resultado.Permitido &&
                resultado.CodigoEstado ==
                    StatusCodes.Status403Forbidden)
            {
                resultado =
                    await permisos.ValidarAsync(
                        usuarioId,
                        PermisoAnterior,
                        tipoPermiso,
                        cancellationToken);
            }

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                new
                {
                    success = false,
                    message = resultado.Mensaje,
                    mensaje = resultado.Mensaje
                });
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue("uid") ??
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("sub");

            return int.TryParse(
                       valor,
                       out int usuarioId) &&
                   usuarioId > 0
                ? usuarioId
                : null;
        }

        private static string NormalizarNombre(
            string? nombre) =>
            (nombre ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim()
                .ToUpperInvariant();
    }
}
