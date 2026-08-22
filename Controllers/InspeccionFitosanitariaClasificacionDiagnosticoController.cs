using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Expone la clasificación del Álbum por diagnóstico individual dentro del
    /// módulo de Inspección Fitosanitaria.
    ///
    /// Las rutas históricas de clasificación por fotografía permanecen
    /// intactas para compatibilidad con clientes anteriores.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/inspecciones-fitosanitarias")]
    public sealed class
        InspeccionFitosanitariaClasificacionDiagnosticoController :
        ControllerBase
    {
        private readonly DiagnosticoIADbContext db;
        private readonly AlbumJerarquiaDbContext albumDb;
        private readonly PermisoApiService permisos;
        private readonly
            InspeccionFitosanitariaClasificacionDiagnosticoDatabase
            clasificaciones;

        public
            InspeccionFitosanitariaClasificacionDiagnosticoController(
                DiagnosticoIADbContext db,
                AlbumJerarquiaDbContext albumDb,
                PermisoApiService permisos)
        {
            this.db = db;
            this.albumDb = albumDb;
            this.permisos = permisos;

            clasificaciones =
                new InspeccionFitosanitariaClasificacionDiagnosticoDatabase(
                    db,
                    albumDb);
        }

        [HttpGet("{id:int}/clasificaciones-diagnosticos")]
        public async Task<IActionResult> Obtener(
            int id,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            IActionResult? acceso =
                await ValidarLecturaModuloAsync(
                    usuarioId,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            bool existe = await db.Diagnosticos
                .AsNoTracking()
                .AnyAsync(item =>
                    item.DiagnosticoIAId == id &&
                    item.Activo,
                    cancellationToken);

            if (!existe)
            {
                return NotFound(Error(
                    "No se encontró la inspección indicada."));
            }

            List<ClasificacionDiagnosticoFitosanitarioRegistro> data =
                await clasificaciones.SincronizarYObtenerAsync(
                    id,
                    usuarioId,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = data.Count == 0
                    ? "La inspección todavía no contiene diagnósticos clasificables."
                    : "Clasificaciones por diagnóstico sincronizadas correctamente.",
                data
            });
        }

        [HttpPost(
            "{id:int}/fotografias/{fotografiaId:int}/" +
            "clasificaciones-diagnosticos/resolver")]
        public async Task<IActionResult> Resolver(
            int id,
            int fotografiaId,
            [FromBody]
                ResolverClasificacionDiagnosticoFitosanitarioRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            if (!usuarioId.HasValue)
                return Unauthorized(Error("Sesión no válida."));

            string etapa = NormalizarEtapa(request.Etapa);

            IActionResult? acceso =
                await ValidarActualizacionEtapaAsync(
                    usuarioId.Value,
                    etapa,
                    cancellationToken);

            if (acceso != null)
                return acceso;

            bool pertenece = await db.Imagenes
                .AsNoTracking()
                .AnyAsync(item =>
                    item.DiagnosticoIAId == id &&
                    item.DiagnosticoIAImagenId ==
                        fotografiaId,
                    cancellationToken);

            if (!pertenece)
            {
                return NotFound(Error(
                    "La fotografía no pertenece a la inspección."));
            }

            string accion =
                (request.Accion ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant();

            bool descartar = accion == "DESCARTAR";

            if (!descartar)
            {
                if (request.CategoriaAlbumBotanicoId is not > 0 ||
                    request.AlbumBotanicoCafeId is not > 0)
                {
                    return BadRequest(Error(
                        "Seleccione una categoría y una subcategoría válidas para este diagnóstico."));
                }

                bool catalogoValido =
                    await albumDb.Subcategorias
                        .AsNoTracking()
                        .AnyAsync(item =>
                            item.AlbumBotanicoCafeId ==
                                request.AlbumBotanicoCafeId &&
                            item.CategoriaAlbumBotanicoId ==
                                request.CategoriaAlbumBotanicoId &&
                            item.Activo &&
                            item.Categoria.Activo,
                            cancellationToken);

                if (!catalogoValido)
                {
                    return BadRequest(Error(
                        "La categoría o subcategoría seleccionada no se encuentra activa en el Álbum Botánico."));
                }
            }

            bool actualizado = await clasificaciones.ResolverAsync(
                id,
                fotografiaId,
                request,
                usuarioId.Value,
                cancellationToken);

            if (!actualizado)
            {
                return NotFound(Error(
                    "No se encontró el diagnóstico indicado en la fotografía."));
            }

            List<ClasificacionDiagnosticoFitosanitarioRegistro> data =
                await clasificaciones.ObtenerPorInspeccionAsync(
                    id,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = descartar
                    ? "El diagnóstico fue descartado para la clasificación del Álbum."
                    : "La clasificación del diagnóstico fue guardada.",
                data
            });
        }

        private async Task<IActionResult?> ValidarLecturaModuloAsync(
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (!usuarioId.HasValue)
                return Unauthorized(Error("Sesión no válida."));

            string[] interfaces =
            [
                DiagnosticoIAFlujo.InterfazSolicitud,
                DiagnosticoIAFlujo.InterfazAnalizador,
                DiagnosticoIAFlujo.InterfazAprobador,
                DiagnosticoIAFlujo.InterfazAlbum
            ];

            foreach (string interfaz in interfaces.Distinct())
            {
                ResultadoPermisoApi resultado =
                    await permisos.ValidarAsync(
                        usuarioId,
                        interfaz,
                        TipoPermisoApi.Leer,
                        cancellationToken);

                if (resultado.Permitido)
                    return null;
            }

            return StatusCode(
                StatusCodes.Status403Forbidden,
                Error(
                    "No tiene permisos para consultar la clasificación fitosanitaria."));
        }

        private async Task<IActionResult?>
            ValidarActualizacionEtapaAsync(
                int usuarioId,
                string etapa,
                CancellationToken cancellationToken)
        {
            string interfaz = etapa switch
            {
                "APROBADOR" =>
                    DiagnosticoIAFlujo.InterfazAprobador,
                "ANALIZADOR" =>
                    DiagnosticoIAFlujo.InterfazAnalizador,
                _ =>
                    DiagnosticoIAFlujo.InterfazSolicitud
            };

            ResultadoPermisoApi resultado =
                await permisos.ValidarAsync(
                    usuarioId,
                    interfaz,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if (resultado.Permitido)
                return null;

            return StatusCode(
                resultado.CodigoEstado,
                Error(resultado.Mensaje));
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.FindFirstValue("usuarioId");

            return int.TryParse(valor, out int id)
                ? id
                : null;
        }

        private static string NormalizarEtapa(string? valor)
        {
            string etapa = (valor ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            return etapa switch
            {
                "APROBADOR" => "APROBADOR",
                "ANALIZADOR" => "ANALIZADOR",
                _ => "TECNICO"
            };
        }

        private static object Error(string? mensaje) => new
        {
            success = false,
            message = string.IsNullOrWhiteSpace(mensaje)
                ? "No fue posible completar la operación."
                : mensaje
        };
    }
}
