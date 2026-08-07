using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Gestiona la relación posterior entre una fotografía aprobada y el Álbum
    /// Botánico. La aprobación técnica, la autorización para usar la evidencia
    /// en el álbum y la publicación activa son decisiones independientes.
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
        private readonly ILogger<InspeccionFitosanitariaPublicacionAlbumController>
            logger;

        public InspeccionFitosanitariaPublicacionAlbumController(
            DiagnosticoIADbContext db,
            PermisoApiService permisos,
            ILogger<InspeccionFitosanitariaPublicacionAlbumController> logger)
        {
            this.db = db;
            this.permisos = permisos;
            this.logger = logger;
            flujo = new InspeccionFitosanitariaDatabase(db);
            control = new InspeccionFitosanitariaControlDatabaseInitializer(db);
        }

        /// <summary>
        /// Devuelve el estado vivo de la publicación. También reconcilia
        /// publicaciones cuya fotografía o subcategoría fue desactivada desde
        /// la administración del Álbum Botánico.
        /// </summary>
        [HttpGet("{inspeccionId:int}/fotografias/{fotografiaId:int}/estado")]
        public async Task<IActionResult> ObtenerEstado(
            int inspeccionId,
            int fotografiaId,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Leer,
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

            if (!await FotografiaPerteneceAsync(
                    inspeccionId,
                    fotografiaId,
                    cancellationToken))
            {
                return NotFound(Error(
                    "No se encontró la fotografía indicada en la inspección."));
            }

            await SincronizarPublicacionesInactivasAsync(
                fotografiaId,
                cancellationToken);

            EstadoAlbumFotografia data = await ConstruirEstadoAsync(
                fotografiaId,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = data.Mensaje,
                data
            });
        }

        /// <summary>
        /// Permite al aprobador autorizar o revocar posteriormente el uso de
        /// una fotografía aprobada en el Álbum Botánico. Revocar la autorización
        /// retira también cualquier copia activa vinculada a esa aprobación.
        /// </summary>
        [HttpPatch("{inspeccionId:int}/fotografias/{fotografiaId:int}/autorizacion")]
        public async Task<IActionResult> CambiarAutorizacion(
            int inspeccionId,
            int fotografiaId,
            [FromBody] InspeccionFotoAutorizacionAlbumRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Actualizar,
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

            if (!await FotografiaPerteneceAsync(
                    inspeccionId,
                    fotografiaId,
                    cancellationToken))
            {
                return NotFound(Error(
                    "No se encontró la fotografía indicada en la inspección."));
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
                    "La autorización del Álbum Botánico solo puede administrarse después de aprobar la fotografía."));
            }

            AprobacionRegistro? aprobacion =
                await flujo.ObtenerUltimaAprobacionAsync(
                    fotografiaId,
                    cancellationToken);

            if (aprobacion == null)
            {
                return Conflict(Error(
                    "La fotografía no tiene una aprobación registrada."));
            }

            await using var transaccion =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
UPDATE dbo.diagnosticoIAImagenAprobacionV2
SET AutorizaPublicacionAlbum = {request.Autorizar}
WHERE DiagnosticoIAImagenAprobacionId = {aprobacion.AprobacionId}
  AND DiagnosticoIAImagenId = {fotografiaId};
""", cancellationToken);

                var albumesQueRequierenPortada = new HashSet<int>();

                if (!request.Autorizar)
                {
                    List<DiagnosticoIAAlbumPublicacion> publicaciones =
                        await db.PublicacionesAlbum
                            .Where(item =>
                                item.DiagnosticoIAImagenId == fotografiaId &&
                                item.Activo)
                            .ToListAsync(cancellationToken);

                    foreach (DiagnosticoIAAlbumPublicacion publicacion
                             in publicaciones)
                    {
                        if (publicacion.AlbumBotanicoCafeId > 0)
                        {
                            albumesQueRequierenPortada.Add(
                                publicacion.AlbumBotanicoCafeId);
                        }
                    }

                    int[] fotoAlbumIds = publicaciones
                        .Select(item => item.AlbumBotanicoCafeFotoId)
                        .Where(id => id > 0)
                        .Distinct()
                        .ToArray();

                    if (fotoAlbumIds.Length > 0)
                    {
                        List<AlbumBotanicoCafeFotoReferencia> fotosAlbum =
                            await db.FotosAlbum
                                .Where(item => fotoAlbumIds.Contains(
                                    item.AlbumBotanicoCafeFotoId))
                                .ToListAsync(cancellationToken);

                        foreach (AlbumBotanicoCafeFotoReferencia fotoAlbum
                                 in fotosAlbum)
                        {
                            fotoAlbum.Activo = false;
                            fotoAlbum.EsPortada = false;
                        }
                    }

                    foreach (DiagnosticoIAAlbumPublicacion publicacion
                             in publicaciones)
                    {
                        publicacion.Activo = false;
                    }
                }

                string accion = request.Autorizar
                    ? "AUTORIZACION_ALBUM_OTORGADA"
                    : "AUTORIZACION_ALBUM_REVOCADA";
                string detalle = request.Autorizar
                    ? "El aprobador autorizó que la fotografía pueda incorporarse posteriormente al Álbum Botánico."
                    : "El aprobador revocó la autorización para el Álbum Botánico. Cualquier copia activa vinculada fue retirada lógicamente.";

                await db.Database.ExecuteSqlInterpolatedAsync($"""
INSERT INTO dbo.diagnosticoIAImagenHistorialV2
(
    DiagnosticoIAImagenId, UsuarioId, EstadoAnterior,
    EstadoNuevo, Accion, Detalle, FechaUtc
)
VALUES
(
    {fotografiaId}, {usuarioId.Value}, {meta.Estado}, {meta.Estado},
    {accion}, {detalle}, SYSUTCDATETIME()
);
""", cancellationToken);

                await db.SaveChangesAsync(cancellationToken);

                foreach (int albumBotanicoCafeId in
                         albumesQueRequierenPortada)
                {
                    await GarantizarPortadaActivaAsync(
                        albumBotanicoCafeId,
                        cancellationToken);
                }

                await transaccion.CommitAsync(cancellationToken);

                EstadoAlbumFotografia data = await ConstruirEstadoAsync(
                    fotografiaId,
                    cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = request.Autorizar
                        ? "La fotografía quedó autorizada para una publicación posterior en el Álbum Botánico."
                        : "La autorización fue cancelada y la fotografía ya no se encuentra publicada activamente en el Álbum Botánico.",
                    data
                });
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                logger.LogError(
                    ex,
                    "Error al cambiar autorización de Álbum. Inspección {InspeccionId}, fotografía {FotografiaId}.",
                    inspeccionId,
                    fotografiaId);

                return StatusCode(500, Error(
                    "No fue posible actualizar la autorización del Álbum Botánico. Intente nuevamente."));
            }
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
            await SincronizarPublicacionesInactivasAsync(
                fotografiaId,
                cancellationToken);

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
                {
                    return NotFound(Error(
                        "No se encontró la inspección indicada."));
                }

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

                if (aprobacion == null ||
                    !aprobacion.AutorizaPublicacionAlbum)
                {
                    return Conflict(Error(
                        "La fotografía todavía no está autorizada para publicarse en el Álbum Botánico. Autorícela primero desde la revisión del aprobador."));
                }

                bool yaPublicada = await db.PublicacionesAlbum
                    .AnyAsync(item =>
                        item.DiagnosticoIAImagenId == fotografiaId &&
                        item.Activo,
                        cancellationToken);

                if (yaPublicada)
                {
                    return Conflict(Error(
                        "La fotografía ya se encuentra publicada activamente en el Álbum Botánico."));
                }

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

                bool categoriaActiva = await db.CategoriasAlbum
                    .AsNoTracking()
                    .AnyAsync(item =>
                        item.CategoriaAlbumBotanicoId ==
                            request.CategoriaAlbumBotanicoId &&
                        item.Activo,
                        cancellationToken);

                if (subcategoria == null || !categoriaActiva)
                {
                    return BadRequest(Error(
                        "La categoría o subcategoría seleccionada está inactiva. Actualice la clasificación antes de publicar."));
                }

                string rutaAlbum = ResolverRutaAlbum(imagen);
                if (string.IsNullOrWhiteSpace(rutaAlbum))
                {
                    return Conflict(Error(
                        "La ruta de la fotografía no puede almacenarse de forma segura en el Álbum Botánico. Actualice el almacenamiento de la evidencia antes de publicar."));
                }

                int orden =
                    (await db.FotosAlbum
                        .Where(item =>
                            item.AlbumBotanicoCafeId ==
                                request.AlbumBotanicoCafeId &&
                            item.Activo)
                        .Select(item => (int?)item.Orden)
                        .MaxAsync(cancellationToken) ?? 0) + 1;

                /*
                 * La portada no la decide el cliente. Si la ficha no tiene
                 * fotografías activas, esta primera publicación se convierte
                 * automáticamente en portada. Si ya existen fotografías, la
                 * nueva solo se agrega y la portada existente se conserva.
                 */
                bool existenFotosActivas = await db.FotosAlbum
                    .AsNoTracking()
                    .AnyAsync(item =>
                        item.AlbumBotanicoCafeId ==
                            request.AlbumBotanicoCafeId &&
                        item.Activo,
                        cancellationToken);

                bool esPortada = !existenFotosActivas;

                var fotoAlbum = new AlbumBotanicoCafeFotoReferencia
                {
                    AlbumBotanicoCafeId = request.AlbumBotanicoCafeId,
                    RutaFoto = rutaAlbum,
                    DescripcionFoto = Limitar(request.Descripcion, 500),
                    EsPortada = esPortada,
                    Orden = orden,
                    Activo = true
                };

                db.FotosAlbum.Add(fotoAlbum);
                await db.SaveChangesAsync(cancellationToken);

                await GarantizarPortadaActivaAsync(
                    request.AlbumBotanicoCafeId,
                    cancellationToken);

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
                    RutaFotoAlbum = Limitar(rutaAlbum, 600),
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
                        "La fotografía fue publicada en el Álbum Botánico. La aprobación técnica permanece independiente.",
                    data = new
                    {
                        fotografiaId,
                        albumBotanicoCafeFotoId =
                            fotoAlbum.AlbumBotanicoCafeFotoId,
                        esPortada = fotoAlbum.EsPortada,
                        orden = fotoAlbum.Orden,
                        expedienteCerrado =
                            controlActual?.CerradaDefinitiva == true,
                        estadoEvidencia = meta.Estado
                    }
                });
            }
            catch (DbUpdateException ex)
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                logger.LogError(
                    ex,
                    "Error de base de datos al publicar en Álbum. Inspección {InspeccionId}, fotografía {FotografiaId}.",
                    inspeccionId,
                    fotografiaId);

                return StatusCode(500, Error(
                    "No fue posible guardar la publicación en la base de datos. Verifique que la categoría y subcategoría continúen activas e intente nuevamente."));
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                logger.LogError(
                    ex,
                    "Error inesperado al publicar en Álbum. Inspección {InspeccionId}, fotografía {FotografiaId}.",
                    inspeccionId,
                    fotografiaId);

                return StatusCode(500, Error(
                    "No fue posible publicar la fotografía en el Álbum Botánico. El expediente no fue modificado."));
            }
        }

        private async Task<bool> FotografiaPerteneceAsync(
            int inspeccionId,
            int fotografiaId,
            CancellationToken cancellationToken) =>
            await db.Imagenes.AsNoTracking().AnyAsync(item =>
                item.DiagnosticoIAId == inspeccionId &&
                item.DiagnosticoIAImagenId == fotografiaId,
                cancellationToken);

        /// <summary>
        /// Si una fotografía del álbum, su subcategoría o su categoría fueron
        /// desactivadas desde la administración, la publicación fitosanitaria
        /// deja de considerarse activa. La autorización del aprobador se
        /// conserva para permitir una publicación posterior si así se decide.
        /// </summary>
        private async Task SincronizarPublicacionesInactivasAsync(
            int fotografiaId,
            CancellationToken cancellationToken)
        {
            List<DiagnosticoIAAlbumPublicacion> publicaciones =
                await db.PublicacionesAlbum
                    .Where(item =>
                        item.DiagnosticoIAImagenId == fotografiaId &&
                        item.Activo)
                    .ToListAsync(cancellationToken);

            if (publicaciones.Count == 0)
                return;

            bool huboCambio = false;
            var albumesQueRequierenPortada = new HashSet<int>();

            foreach (DiagnosticoIAAlbumPublicacion publicacion in publicaciones)
            {
                AlbumBotanicoCafeFotoReferencia? fotoAlbum =
                    await db.FotosAlbum.FirstOrDefaultAsync(item =>
                        item.AlbumBotanicoCafeFotoId ==
                            publicacion.AlbumBotanicoCafeFotoId,
                        cancellationToken);

                bool fotoActiva = fotoAlbum?.Activo == true;

                bool subcategoriaActiva = await db.RegistrosAlbum
                    .AsNoTracking()
                    .AnyAsync(item =>
                        item.AlbumBotanicoCafeId ==
                            publicacion.AlbumBotanicoCafeId &&
                        item.CategoriaAlbumBotanicoId ==
                            publicacion.CategoriaAlbumBotanicoId &&
                        item.Activo,
                        cancellationToken);

                bool categoriaActiva = await db.CategoriasAlbum
                    .AsNoTracking()
                    .AnyAsync(item =>
                        item.CategoriaAlbumBotanicoId ==
                            publicacion.CategoriaAlbumBotanicoId &&
                        item.Activo,
                        cancellationToken);

                if (fotoActiva && subcategoriaActiva && categoriaActiva)
                    continue;

                /*
                 * Si la publicación dejó de ser visible por una desactivación
                 * administrativa, se desactiva también la copia vinculada para
                 * evitar que reaparezca automáticamente al reactivar su padre.
                 */
                if (fotoAlbum?.Activo == true)
                {
                    fotoAlbum.Activo = false;
                    fotoAlbum.EsPortada = false;
                }

                publicacion.Activo = false;
                huboCambio = true;

                if (publicacion.AlbumBotanicoCafeId > 0)
                {
                    albumesQueRequierenPortada.Add(
                        publicacion.AlbumBotanicoCafeId);
                }
            }

            if (!huboCambio)
                return;

            await db.SaveChangesAsync(cancellationToken);

            foreach (int albumBotanicoCafeId in albumesQueRequierenPortada)
            {
                await GarantizarPortadaActivaAsync(
                    albumBotanicoCafeId,
                    cancellationToken);
            }
        }

        /// <summary>
        /// Mantiene la regla del Álbum Botánico: una ficha que posee
        /// fotografías activas debe tener exactamente una portada activa.
        /// </summary>
        private async Task GarantizarPortadaActivaAsync(
            int albumBotanicoCafeId,
            CancellationToken cancellationToken)
        {
            if (albumBotanicoCafeId <= 0)
                return;

            bool fichaActiva = await db.RegistrosAlbum
                .AsNoTracking()
                .AnyAsync(item =>
                    item.AlbumBotanicoCafeId == albumBotanicoCafeId &&
                    item.Activo,
                    cancellationToken);

            if (!fichaActiva)
                return;

            List<AlbumBotanicoCafeFotoReferencia> fotosActivas =
                await db.FotosAlbum
                    .Where(item =>
                        item.AlbumBotanicoCafeId == albumBotanicoCafeId &&
                        item.Activo)
                    .OrderBy(item => item.Orden)
                    .ThenBy(item => item.AlbumBotanicoCafeFotoId)
                    .ToListAsync(cancellationToken);

            if (fotosActivas.Count == 0)
                return;

            AlbumBotanicoCafeFotoReferencia portada =
                fotosActivas.FirstOrDefault(item => item.EsPortada) ??
                fotosActivas[0];

            bool cambio = false;
            foreach (AlbumBotanicoCafeFotoReferencia foto in fotosActivas)
            {
                bool debeSerPortada =
                    foto.AlbumBotanicoCafeFotoId ==
                    portada.AlbumBotanicoCafeFotoId;

                if (foto.EsPortada == debeSerPortada)
                    continue;

                foto.EsPortada = debeSerPortada;
                cambio = true;
            }

            if (cambio)
                await db.SaveChangesAsync(cancellationToken);
        }

        private async Task<EstadoAlbumFotografia> ConstruirEstadoAsync(
            int fotografiaId,
            CancellationToken cancellationToken)
        {
            FotoMetadatos? meta = await flujo.ObtenerFotoAsync(
                fotografiaId,
                cancellationToken);
            AprobacionRegistro? aprobacion =
                await flujo.ObtenerUltimaAprobacionAsync(
                    fotografiaId,
                    cancellationToken);

            DiagnosticoIAAlbumPublicacion? activa =
                await db.PublicacionesAlbum
                    .AsNoTracking()
                    .Where(item =>
                        item.DiagnosticoIAImagenId == fotografiaId &&
                        item.Activo)
                    .OrderByDescending(item => item.FechaPublicacionUtc)
                    .FirstOrDefaultAsync(cancellationToken);

            bool tuvoPublicacion = await db.PublicacionesAlbum
                .AsNoTracking()
                .AnyAsync(item =>
                    item.DiagnosticoIAImagenId == fotografiaId,
                    cancellationToken);

            bool aprobada = meta?.Estado is
                InspeccionFitosanitariaFlujo.FotoEstados.Aprobada or
                InspeccionFitosanitariaFlujo.FotoEstados
                    .AprobadaConCorreccion or
                InspeccionFitosanitariaFlujo.FotoEstados.PublicadaAlbum;

            bool autorizada = aprobacion?.AutorizaPublicacionAlbum == true;
            bool publicada = activa != null;

            string mensaje = !aprobada
                ? "La fotografía todavía no tiene una aprobación positiva."
                : publicada
                    ? "La fotografía está publicada activamente en el Álbum Botánico."
                    : autorizada && tuvoPublicacion
                        ? "La fotografía continúa autorizada, pero su copia anterior del Álbum Botánico ya no está activa."
                        : autorizada
                            ? "La fotografía está autorizada y pendiente de publicación en el Álbum Botánico."
                            : "La fotografía está aprobada, pero no está autorizada para el Álbum Botánico.";

            return new EstadoAlbumFotografia
            {
                FotografiaId = fotografiaId,
                Aprobada = aprobada,
                Autorizada = autorizada,
                PublicadaActiva = publicada,
                TuvoPublicacion = tuvoPublicacion,
                CategoriaAlbumBotanicoId =
                    activa?.CategoriaAlbumBotanicoId,
                AlbumBotanicoCafeId = activa?.AlbumBotanicoCafeId,
                AlbumBotanicoCafeFotoId =
                    activa?.AlbumBotanicoCafeFotoId,
                EstadoEvidencia = meta?.Estado ?? string.Empty,
                Mensaje = mensaje
            };
        }

        private static string ResolverRutaAlbum(DiagnosticoIAImagen imagen)
        {
            string relativa = imagen.RutaRelativa?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(relativa) && relativa.Length <= 500)
                return relativa;

            string publica = imagen.UrlImagen?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(publica) && publica.Length <= 500)
                return publica;

            return string.Empty;
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

        private sealed class EstadoAlbumFotografia
        {
            public int FotografiaId { get; set; }
            public bool Aprobada { get; set; }
            public bool Autorizada { get; set; }
            public bool PublicadaActiva { get; set; }
            public bool TuvoPublicacion { get; set; }
            public int? CategoriaAlbumBotanicoId { get; set; }
            public int? AlbumBotanicoCafeId { get; set; }
            public int? AlbumBotanicoCafeFotoId { get; set; }
            public string EstadoEvidencia { get; set; } = string.Empty;
            public string Mensaje { get; set; } = string.Empty;
        }
    }

    public sealed class InspeccionFotoAutorizacionAlbumRequest
    {
        public bool Autorizar { get; set; }
    }
}
