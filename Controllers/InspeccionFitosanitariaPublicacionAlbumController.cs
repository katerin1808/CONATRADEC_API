using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Publica una copia autorizada en el Álbum Botánico sin modificar el
    /// estado de la evidencia original. Esta operación continúa disponible
    /// después del cierre definitivo porque pertenece a la gobernanza del
    /// álbum, no a la edición del expediente fitosanitario.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/publicaciones-album-fitosanitarias")]
    public sealed class InspeccionFitosanitariaPublicacionAlbumController :
        ControllerBase
    {
        private readonly DiagnosticoIADbContext db;
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaDatabase flujo;
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;

        public InspeccionFitosanitariaPublicacionAlbumController(
            DiagnosticoIADbContext db,
            PermisoApiService permisos)
        {
            this.db = db;
            this.permisos = permisos;
            flujo = new InspeccionFitosanitariaDatabase(db);
            control = new InspeccionFitosanitariaControlDatabaseInitializer(db);
        }

        [HttpPost("{inspeccionId:int}/fotografias/{fotografiaId:int}")]
        [HttpPost("~/api/inspecciones-fitosanitarias/{inspeccionId:int}/fotografias/{fotografiaId:int}/publicar-album")]
        public async Task<IActionResult> Publicar(
            int inspeccionId,
            int fotografiaId,
            [FromBody] InspeccionFotoPublicarAlbumRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAlbum,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    Error(permiso.Mensaje));
            }

            if (!usuarioId.HasValue)
                return Forbid();

            await flujo.InicializarAsync(cancellationToken);

            await using var transaccion =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            try
            {
                DiagnosticoIA? inspeccion = await db.Diagnosticos
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item =>
                        item.DiagnosticoIAId == inspeccionId && item.Activo,
                        cancellationToken);

                if (inspeccion == null)
                    return NotFound(Error("No se encontró la inspección indicada."));

                DiagnosticoIAImagen? imagen = await db.Imagenes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item =>
                        item.DiagnosticoIAId == inspeccionId &&
                        item.DiagnosticoIAImagenId == fotografiaId,
                        cancellationToken);

                if (imagen == null)
                {
                    return BadRequest(Error(
                        "La fotografía no pertenece a la inspección o ya no está activa."));
                }

                FotoMetadatos? meta = await flujo.ObtenerFotoAsync(
                    fotografiaId,
                    cancellationToken);

                if (meta == null || !meta.Activo || meta.Descartada ||
                    meta.Estado is not
                        (InspeccionFitosanitariaFlujo.FotoEstados.Aprobada or
                         InspeccionFitosanitariaFlujo.FotoEstados
                            .AprobadaConCorreccion or
                         InspeccionFitosanitariaFlujo.FotoEstados.PublicadaAlbum))
                {
                    return Conflict(Error(
                        "Solo se pueden publicar fotografías aprobadas individualmente."));
                }

                AprobacionRegistro? aprobacion =
                    await flujo.ObtenerUltimaAprobacionAsync(
                        fotografiaId,
                        cancellationToken);

                if (aprobacion == null || !aprobacion.AutorizaPublicacionAlbum)
                {
                    return Conflict(Error(
                        "El aprobador no autorizó la publicación de esta fotografía."));
                }

                bool yaPublicada = await db.PublicacionesAlbum
                    .AnyAsync(item =>
                        item.DiagnosticoIAImagenId == fotografiaId &&
                        item.Activo,
                        cancellationToken);

                if (yaPublicada)
                    return Conflict(Error("La fotografía ya fue publicada en el álbum."));

                AlbumBotanicoCafeReferencia? subcategoria =
                    await db.RegistrosAlbum
                        .AsNoTracking()
                        .FirstOrDefaultAsync(item =>
                            item.AlbumBotanicoCafeId ==
                                request.AlbumBotanicoCafeId &&
                            item.CategoriaAlbumBotanicoId ==
                                request.CategoriaAlbumBotanicoId &&
                            item.Activo,
                            cancellationToken);

                if (subcategoria == null)
                {
                    return BadRequest(Error(
                        "La subcategoría seleccionada no existe, no está activa o no pertenece a la categoría indicada."));
                }

                int orden = request.Orden > 0
                    ? request.Orden
                    : (await db.FotosAlbum
                        .Where(item =>
                            item.AlbumBotanicoCafeId ==
                                request.AlbumBotanicoCafeId)
                        .Select(item => (int?)item.Orden)
                        .MaxAsync(cancellationToken) ?? 0) + 1;

                if (request.EsPortada)
                {
                    await db.FotosAlbum
                        .Where(item =>
                            item.AlbumBotanicoCafeId ==
                                request.AlbumBotanicoCafeId &&
                            item.Activo && item.EsPortada)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(
                                item => item.EsPortada,
                                false),
                            cancellationToken);
                }

                var fotoAlbum = new AlbumBotanicoCafeFotoReferencia
                {
                    AlbumBotanicoCafeId = request.AlbumBotanicoCafeId,
                    RutaFoto = imagen.RutaRelativa,
                    DescripcionFoto = Limitar(request.Descripcion, 500),
                    EsPortada = request.EsPortada,
                    Orden = orden,
                    Activo = true
                };

                db.FotosAlbum.Add(fotoAlbum);
                await db.SaveChangesAsync(cancellationToken);

                db.PublicacionesAlbum.Add(new DiagnosticoIAAlbumPublicacion
                {
                    DiagnosticoIAId = inspeccionId,
                    DiagnosticoIAImagenId = fotografiaId,
                    CategoriaAlbumBotanicoId =
                        request.CategoriaAlbumBotanicoId,
                    AlbumBotanicoCafeId = request.AlbumBotanicoCafeId,
                    AlbumBotanicoCafeFotoId =
                        fotoAlbum.AlbumBotanicoCafeFotoId,
                    UsuarioPublicacionId = usuarioId.Value,
                    FechaPublicacionUtc = DateTime.UtcNow,
                    DescripcionPublicacion =
                        Limitar(request.Descripcion, 1000),
                    ClasificacionFinal = Limitar(subcategoria.Titulo, 50),
                    DiagnosticoFinal =
                        Limitar(aprobacion.DiagnosticoFinal, 300),
                    RutaFotoAlbum = Limitar(imagen.RutaRelativa, 600),
                    Activo = true
                });

                await db.SaveChangesAsync(cancellationToken);

                await db.Database.ExecuteSqlInterpolatedAsync($"""
INSERT INTO dbo.diagnosticoIAImagenHistorialV2
(
    DiagnosticoIAImagenId, UsuarioId, EstadoAnterior,
    EstadoNuevo, Accion, Detalle, FechaUtc
)
VALUES
(
    {fotografiaId}, {usuarioId.Value}, {meta.Estado}, {meta.Estado},
    N'FOTO_COPIADA_ALBUM',
    N'La fotografía autorizada fue copiada al Álbum Botánico sin modificar el expediente original.',
    SYSUTCDATETIME()
);
""", cancellationToken);

                await transaccion.CommitAsync(cancellationToken);

                InspeccionFitosanitariaControlRegistro? controlActual =
                    await control.ObtenerAsync(
                        inspeccionId,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "La fotografía autorizada fue copiada al Álbum Botánico. El expediente original permanece inalterado.",
                    data = new
                    {
                        fotografiaId,
                        albumBotanicoCafeFotoId =
                            fotoAlbum.AlbumBotanicoCafeFotoId,
                        expedienteCerrado = controlActual?.CerradaDefinitiva == true,
                        estadoEvidencia = meta.Estado
                    }
                });
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private int? ObtenerUsuarioId()
        {
            string? valor = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                            User.FindFirstValue("usuarioId") ??
                            User.FindFirstValue("sub");

            return int.TryParse(valor, out int id) && id > 0
                ? id
                : null;
        }

        private static object Error(string mensaje) => new
        {
            success = false,
            message = mensaje
        };

        private static string Limitar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo ? texto : texto[..maximo];
        }
    }
}
