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
using System.Text.Json;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Gestiona la relación posterior entre una fotografía aprobada y el Álbum
    /// Botánico. La decisión técnica y la clasificación oficial permanecen
    /// inalterables; después de confirmarlas solo se publica o se retira la
    /// copia del Álbum Botánico.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/publicaciones-album-fitosanitarias")]
    public sealed class InspeccionFitosanitariaPublicacionAlbumController :
        ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };

        private readonly DiagnosticoIADbContext db;
        private readonly AlbumJerarquiaDbContext albumJerarquia;
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaDatabase flujo;
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;
        private readonly ILogger<InspeccionFitosanitariaPublicacionAlbumController>
            logger;

        public InspeccionFitosanitariaPublicacionAlbumController(
            DiagnosticoIADbContext db,
            AlbumJerarquiaDbContext albumJerarquia,
            PermisoApiService permisos,
            ILogger<InspeccionFitosanitariaPublicacionAlbumController> logger)
        {
            this.db = db;
            this.albumJerarquia = albumJerarquia;
            this.permisos = permisos;
            this.logger = logger;
            flujo = new InspeccionFitosanitariaDatabase(db);
            control = new InspeccionFitosanitariaControlDatabaseInitializer(db);
        }

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
        /// Publica directamente una fotografía aprobada cuya clasificación fue
        /// confirmada por el aprobador. La ruta del Álbum se obtiene siempre de
        /// la evidencia original almacenada; nunca de una imagen derivada de IA.
        /// </summary>
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

            ResultadoPermisoApi permisoAprobador = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (!permisoAprobador.Permitido)
            {
                return StatusCode(
                    permisoAprobador.CodigoEstado,
                    Error(permisoAprobador.Mensaje));
            }

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

                if (aprobacion == null)
                {
                    return Conflict(Error(
                        "La fotografía no tiene una aprobación técnica registrada."));
                }

                DiagnosticoIAClasificacionJerarquia? clasificacionOficial =
                    await albumJerarquia.ClasificacionesJerarquia
                        .AsNoTracking()
                        .FirstOrDefaultAsync(item =>
                            item.DiagnosticoIAImagenId == fotografiaId &&
                            item.Estado == "RESUELTA_APROBADOR",
                            cancellationToken);

                if (clasificacionOficial == null ||
                    clasificacionOficial.CategoriaAlbumBotanicoIdSeleccionada
                        is not > 0 ||
                    clasificacionOficial.AlbumBotanicoCafeIdSeleccionado
                        is not > 0)
                {
                    return Conflict(Error(
                        "La fotografía todavía no tiene una clasificación oficial confirmada por el aprobador."));
                }

                if (request.CategoriaAlbumBotanicoId !=
                        clasificacionOficial.CategoriaAlbumBotanicoIdSeleccionada.Value ||
                    request.AlbumBotanicoCafeId !=
                        clasificacionOficial.AlbumBotanicoCafeIdSeleccionado.Value)
                {
                    return Conflict(Error(
                        "La publicación debe utilizar exactamente la categoría y subcategoría oficiales confirmadas por el aprobador."));
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
                        "La categoría o subcategoría de la clasificación oficial está inactiva. Actívela antes de publicar."));
                }

                /*
                 * Garantía defensiva: no se acepta ruta del cliente y tampoco
                 * se usa UrlImagen como respaldo. RutaRelativa pertenece a la
                 * evidencia original creada al registrar la fotografía.
                 */
                string rutaAlbum = ResolverRutaAlbum(imagen);
                if (string.IsNullOrWhiteSpace(rutaAlbum))
                {
                    return Conflict(Error(
                        "La evidencia original no posee una ruta persistente segura. No se utilizará ninguna imagen derivada para el Álbum Botánico."));
                }

                string descripcionAlbum = ConstruirDescripcionAlbum(
                    request.Descripcion,
                    aprobacion.DiagnosticosFinalesJson,
                    aprobacion.DiagnosticoFinal,
                    500);

                string descripcionPublicacion = ConstruirDescripcionAlbum(
                    request.Descripcion,
                    aprobacion.DiagnosticosFinalesJson,
                    aprobacion.DiagnosticoFinal,
                    1000);

                int orden =
                    (await db.FotosAlbum
                        .Where(item =>
                            item.AlbumBotanicoCafeId ==
                                request.AlbumBotanicoCafeId &&
                            item.Activo)
                        .Select(item => (int?)item.Orden)
                        .MaxAsync(cancellationToken) ?? 0) + 1;

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
                    DescripcionFoto = descripcionAlbum,
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
                    DescripcionPublicacion = descripcionPublicacion,
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
    N'La fotografía original limpia con clasificación oficial fue copiada al Álbum Botánico. Las imágenes marcadas de IA no participan en esta publicación.',
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
                        "La fotografía original fue publicada en el Álbum Botánico. La aprobación técnica y la clasificación oficial permanecen inalterables.",
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

        [HttpPatch("{inspeccionId:int}/fotografias/{fotografiaId:int}/publicacion/retirar")]
        public async Task<IActionResult> RetirarPublicacion(
            int inspeccionId,
            int fotografiaId,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAlbum,
                TipoPermisoApi.Eliminar,
                cancellationToken);

            if (!permiso.Permitido)
            {
                return StatusCode(
                    permiso.CodigoEstado,
                    Error(permiso.Mensaje));
            }

            if (!usuarioId.HasValue)
                return Forbid();

            ResultadoPermisoApi permisoAprobador = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (!permisoAprobador.Permitido)
            {
                return StatusCode(
                    permisoAprobador.CodigoEstado,
                    Error(permisoAprobador.Mensaje));
            }

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

            if (meta == null || !meta.Activo || meta.Descartada)
            {
                return Conflict(Error(
                    "La fotografía ya no se encuentra disponible en el expediente."));
            }

            await SincronizarPublicacionesInactivasAsync(
                fotografiaId,
                cancellationToken);

            await using var transaccion =
                await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            try
            {
                List<DiagnosticoIAAlbumPublicacion> publicaciones =
                    await db.PublicacionesAlbum
                        .Where(item =>
                            item.DiagnosticoIAImagenId == fotografiaId &&
                            item.Activo)
                        .ToListAsync(cancellationToken);

                if (publicaciones.Count == 0)
                {
                    return Conflict(Error(
                        "La fotografía no tiene una publicación activa que pueda retirarse."));
                }

                var albumesQueRequierenPortada = new HashSet<int>();

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

                    if (publicacion.AlbumBotanicoCafeId > 0)
                    {
                        albumesQueRequierenPortada.Add(
                            publicacion.AlbumBotanicoCafeId);
                    }
                }

                await db.Database.ExecuteSqlInterpolatedAsync($"""
INSERT INTO dbo.diagnosticoIAImagenHistorialV2
(
    DiagnosticoIAImagenId, UsuarioId, EstadoAnterior,
    EstadoNuevo, Accion, Detalle, FechaUtc
)
VALUES
(
    {fotografiaId}, {usuarioId.Value}, {meta.Estado}, {meta.Estado},
    N'FOTO_RETIRADA_ALBUM',
    N'La copia activa fue retirada del Álbum Botánico. La aprobación técnica y la clasificación oficial se conservaron.',
    SYSUTCDATETIME()
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
                    message =
                        "La fotografía fue retirada del Álbum Botánico. Su aprobación técnica y clasificación oficial se conservaron.",
                    data
                });
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                logger.LogError(
                    ex,
                    "Error al retirar publicación de Álbum. Inspección {InspeccionId}, fotografía {FotografiaId}.",
                    inspeccionId,
                    fotografiaId);

                return StatusCode(500, Error(
                    "No fue posible retirar la fotografía del Álbum Botánico. Intente nuevamente."));
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

                if (fotoAlbum?.Activo == true)
                    continue;

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

            DiagnosticoIAClasificacionJerarquia? clasificacion =
                await albumJerarquia.ClasificacionesJerarquia
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item =>
                        item.DiagnosticoIAImagenId == fotografiaId,
                        cancellationToken);

            bool clasificacionOficial =
                string.Equals(
                    clasificacion?.Estado,
                    "RESUELTA_APROBADOR",
                    StringComparison.OrdinalIgnoreCase) &&
                clasificacion?.CategoriaAlbumBotanicoIdSeleccionada is > 0 &&
                clasificacion.AlbumBotanicoCafeIdSeleccionado is > 0;

            bool publicada = activa != null;

            bool fichaActiva = !publicada || await db.RegistrosAlbum
                .AsNoTracking()
                .AnyAsync(item =>
                    item.AlbumBotanicoCafeId == activa!.AlbumBotanicoCafeId &&
                    item.Activo,
                    cancellationToken);

            bool categoriaActiva = !publicada || await db.CategoriasAlbum
                .AsNoTracking()
                .AnyAsync(item =>
                    item.CategoriaAlbumBotanicoId ==
                        activa!.CategoriaAlbumBotanicoId &&
                    item.Activo,
                    cancellationToken);

            bool visibleEnGaleria = publicada && fichaActiva && categoriaActiva;

            string mensaje = !aprobada
                ? "La fotografía todavía no tiene una aprobación positiva."
                : !clasificacionOficial
                    ? "La fotografía está aprobada y su clasificación oficial del Álbum Botánico está pendiente de confirmación."
                    : publicada && !visibleEnGaleria
                        ? "La publicación se conserva activa, pero está oculta porque su ficha o categoría está inactiva."
                        : publicada
                            ? "La fotografía está publicada activamente en el Álbum Botánico."
                            : tuvoPublicacion
                                ? "La clasificación oficial permanece confirmada, pero la publicación anterior fue retirada del Álbum Botánico."
                                : "La clasificación oficial está confirmada y la fotografía todavía no ha sido publicada en el Álbum Botánico.";

            return new EstadoAlbumFotografia
            {
                FotografiaId = fotografiaId,
                Aprobada = aprobada,
                Autorizada = aprobada && clasificacionOficial,
                PublicadaActiva = publicada,
                TuvoPublicacion = tuvoPublicacion,
                CategoriaAlbumBotanicoId =
                    activa?.CategoriaAlbumBotanicoId ??
                    clasificacion?.CategoriaAlbumBotanicoIdSeleccionada,
                AlbumBotanicoCafeId =
                    activa?.AlbumBotanicoCafeId ??
                    clasificacion?.AlbumBotanicoCafeIdSeleccionado,
                AlbumBotanicoCafeFotoId =
                    activa?.AlbumBotanicoCafeFotoId,
                EstadoEvidencia = meta?.Estado ?? string.Empty,
                Mensaje = mensaje
            };
        }

        /// <summary>
        /// Devuelve exclusivamente la ruta original persistida. La ruta de una
        /// imagen marcada de IA nunca se consulta desde este controlador.
        /// </summary>
        private static string ResolverRutaAlbum(DiagnosticoIAImagen imagen)
        {
            string relativa = imagen.RutaRelativa?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(relativa) && relativa.Length <= 500
                ? relativa
                : string.Empty;
        }

        private static string ConstruirDescripcionAlbum(
            string? descripcionBase,
            string? diagnosticosFinalesJson,
            string? diagnosticoPrincipalResumen,
            int maximo)
        {
            string baseNormalizada = (descripcionBase ?? string.Empty).Trim();
            List<InspeccionDiagnosticoVisualDto> diagnosticos =
                DeserializarDiagnosticos(diagnosticosFinalesJson);

            InspeccionDiagnosticoVisualDto? principal =
                diagnosticos.FirstOrDefault(item => item.EsPrincipal);

            string principalNombre = principal?.Diagnostico?.Trim() ??
                (diagnosticoPrincipalResumen ?? string.Empty).Trim();

            List<string> secundarios = diagnosticos
                .Where(item => !item.EsPrincipal)
                .Select(item => item.Diagnostico?.Trim() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Where(item => !string.Equals(
                    item,
                    principalNombre,
                    StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (secundarios.Count == 0)
                return Limitar(baseNormalizada, maximo);

            string complemento =
                $"Otras afectaciones observadas: {string.Join(", ", secundarios)}.";

            if (string.IsNullOrWhiteSpace(baseNormalizada))
                return Limitar(complemento, maximo);

            int disponibleBase = Math.Max(
                0,
                maximo - complemento.Length - Environment.NewLine.Length * 2);
            string baseLimitada = Limitar(baseNormalizada, disponibleBase);

            return Limitar(
                $"{baseLimitada}{Environment.NewLine}{Environment.NewLine}{complemento}",
                maximo);
        }

        private static List<InspeccionDiagnosticoVisualDto>
            DeserializarDiagnosticos(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            try
            {
                return JsonSerializer.Deserialize<
                    List<InspeccionDiagnosticoVisualDto>>(
                        json,
                        JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                return [];
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
            if (maximo <= 0)
                return string.Empty;
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
}
