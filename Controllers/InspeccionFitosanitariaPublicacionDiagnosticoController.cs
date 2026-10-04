using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using SixLabors.ImageSharp;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Publicación V2 del Álbum Botánico por diagnóstico individual.
    ///
    /// Una fotografía continúa siendo un único archivo físico. Cada diagnóstico
    /// confirmado conserva su propia clasificación y su propia relación de
    /// publicación, incluyendo las coordenadas normalizadas de señalización.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/inspecciones-fitosanitarias")]
    public sealed class InspeccionFitosanitariaPublicacionDiagnosticoController :
        ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };

        private readonly DiagnosticoIADbContext db;
        private readonly AlbumJerarquiaDbContext albumDb;
        private readonly PermisoApiService permisos;
        private readonly ImageStoragePathService storage;
        private readonly ILogger<InspeccionFitosanitariaPublicacionDiagnosticoController>
            logger;
        private readonly InspeccionFitosanitariaDatabase flujo;
        private readonly
            InspeccionFitosanitariaClasificacionDiagnosticoDatabase
            clasificaciones;
        private readonly
            InspeccionFitosanitariaPublicacionDiagnosticoDatabase
            publicaciones;

        public InspeccionFitosanitariaPublicacionDiagnosticoController(
            DiagnosticoIADbContext db,
            AlbumJerarquiaDbContext albumDb,
            PermisoApiService permisos,
            ImageStoragePathService storage,
            ILogger<InspeccionFitosanitariaPublicacionDiagnosticoController>
                logger)
        {
            this.db = db;
            this.albumDb = albumDb;
            this.permisos = permisos;
            this.storage = storage;
            this.logger = logger;
            flujo = new InspeccionFitosanitariaDatabase(db);
            clasificaciones =
                new InspeccionFitosanitariaClasificacionDiagnosticoDatabase(
                    db,
                    albumDb);
            publicaciones =
                new InspeccionFitosanitariaPublicacionDiagnosticoDatabase(db);
        }

        [HttpGet(
            "{id:int}/fotografias/{fotografiaId:int}/" +
            "publicaciones-diagnosticos")]
        public async Task<IActionResult> ObtenerEstadoDiagnosticos(
            int id,
            int fotografiaId,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarLecturaAsync(
                usuarioId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (!await FotografiaPerteneceAsync(
                    id,
                    fotografiaId,
                    cancellationToken))
            {
                return NotFound(Error(
                    "No se encontró la fotografía indicada en la inspección."));
            }

            List<PublicacionDiagnosticoEstadoDto> data =
                await ConstruirEstadosAsync(
                    id,
                    fotografiaId,
                    usuarioId,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = data.Count == 0
                    ? "La fotografía todavía no contiene diagnósticos clasificables."
                    : "Clasificaciones y publicaciones por diagnóstico obtenidas correctamente.",
                data
            });
        }

        /// <summary>
        /// Estado agregado conservado para las tarjetas existentes del
        /// aprobador. PublicadaActiva significa que al menos un diagnóstico de
        /// la fotografía continúa publicado.
        /// </summary>
        [HttpGet(
            "{id:int}/fotografias/{fotografiaId:int}/" +
            "estado-publicaciones-diagnosticos")]
        public async Task<IActionResult> ObtenerEstadoAgregado(
            int id,
            int fotografiaId,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarLecturaAsync(
                usuarioId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            if (!await FotografiaPerteneceAsync(
                    id,
                    fotografiaId,
                    cancellationToken))
            {
                return NotFound(Error(
                    "No se encontró la fotografía indicada en la inspección."));
            }

            await flujo.InicializarAsync(cancellationToken);
            FotoMetadatos? meta = await flujo.ObtenerFotoAsync(
                fotografiaId,
                cancellationToken);
            AprobacionRegistro? aprobacion = await flujo.ObtenerUltimaAprobacionAsync(
                fotografiaId,
                cancellationToken);

            List<PublicacionDiagnosticoEstadoDto> estados =
                await ConstruirEstadosAsync(
                    id,
                    fotografiaId,
                    usuarioId,
                    cancellationToken);

            PublicacionDiagnosticoEstadoDto? activa = estados
                .Where(item => item.PublicadaActiva)
                .OrderByDescending(item => item.EsPrincipal)
                .ThenBy(item => item.OrdenDiagnostico)
                .FirstOrDefault();

            bool publicada = estados.Any(item => item.PublicadaActiva);
            bool tuvo = estados.Any(item => item.TuvoPublicacion);
            bool autorizada = estados.Any(item => item.PuedePublicarse);

            return Ok(new
            {
                success = true,
                message = publicada
                    ? $"{estados.Count(item => item.PublicadaActiva)} diagnóstico(s) publicado(s) activamente en el Álbum Botánico."
                    : tuvo
                        ? "La fotografía tuvo publicaciones por diagnóstico, pero actualmente ninguna está activa."
                        : "La fotografía todavía no tiene publicaciones por diagnóstico.",
                data = new
                {
                    fotografiaId,
                    aprobada = aprobacion != null,
                    autorizada,
                    publicadaActiva = publicada,
                    tuvoPublicacion = tuvo,
                    categoriaAlbumBotanicoId =
                        activa?.CategoriaAlbumBotanicoIdSeleccionada,
                    albumBotanicoCafeId =
                        activa?.AlbumBotanicoCafeIdSeleccionado,
                    albumBotanicoCafeFotoId = activa?.AlbumBotanicoCafeFotoId,
                    estadoEvidencia = meta?.Estado ?? string.Empty,
                    totalDiagnosticos = estados.Count,
                    publicacionesActivas = estados.Count(item =>
                        item.PublicadaActiva),
                    mensaje = publicada
                        ? $"{estados.Count(item => item.PublicadaActiva)} diagnóstico(s) publicado(s) activamente en el Álbum Botánico."
                        : tuvo
                            ? "No hay publicaciones activas en este momento."
                            : "Sin publicaciones por diagnóstico."
                }
            });
        }

        [HttpPost(
            "{id:int}/fotografias/{fotografiaId:int}/" +
            "publicaciones-diagnosticos/sincronizar")]
        public async Task<IActionResult> SincronizarPublicaciones(
            int id,
            int fotografiaId,
            [FromBody] SincronizarPublicacionesDiagnosticosRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Unauthorized(Error("Sesión no válida."));

            if (request?.Diagnosticos == null ||
                request.Diagnosticos.Count == 0)
            {
                return BadRequest(Error(
                    "Envíe al menos una decisión de publicación por diagnóstico."));
            }

            IActionResult? accesoAprobador = await ValidarPermisoAsync(
                usuarioId.Value,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (accesoAprobador != null)
                return accesoAprobador;

            bool solicitaPublicar = request.Diagnosticos.Any(item =>
                item.Publicar);

            /*
             * No se exige permiso Eliminar simplemente porque un interruptor
             * llegue en false. Solo se considera retiro cuando actualmente
             * existe una publicación activa de ese diagnóstico.
             */
            List<PublicacionDiagnosticoRegistro> activasPrevias =
                await publicaciones.ObtenerPorFotografiaAsync(
                    fotografiaId,
                    incluirInactivas: false,
                    cancellationToken);

            HashSet<string> clavesActivas = activasPrevias
                .Select(item => item.DiagnosticoClave)
                .ToHashSet(StringComparer.Ordinal);

            bool solicitaRetirar = request.Diagnosticos.Any(item =>
                !item.Publicar &&
                clavesActivas.Contains(item.DiagnosticoClave ?? string.Empty));

            if (solicitaPublicar)
            {
                IActionResult? accesoAlbum = await ValidarPermisoAsync(
                    usuarioId.Value,
                    DiagnosticoIAFlujo.InterfazAlbum,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

                if (accesoAlbum != null)
                    return accesoAlbum;
            }

            if (solicitaRetirar)
            {
                IActionResult? accesoAlbum = await ValidarPermisoAsync(
                    usuarioId.Value,
                    DiagnosticoIAFlujo.InterfazAlbum,
                    TipoPermisoApi.Eliminar,
                    cancellationToken);

                if (accesoAlbum != null)
                    return accesoAlbum;
            }

            try
            {
                List<PublicacionDiagnosticoEstadoDto> data =
                    await SincronizarInternoAsync(
                        id,
                        fotografiaId,
                        request.Diagnosticos,
                        usuarioId.Value,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Las publicaciones del Álbum Botánico fueron actualizadas por diagnóstico.",
                    data
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(Error(ex.Message));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error al sincronizar publicaciones por diagnóstico. Inspección {InspeccionId}, fotografía {FotografiaId}.",
                    id,
                    fotografiaId);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    Error(
                        "No fue posible actualizar las publicaciones por diagnóstico. No se modificó la evidencia original."));
            }
        }

        [HttpPatch(
            "{id:int}/fotografias/{fotografiaId:int}/" +
            "publicaciones-diagnosticos/retirar-todas")]
        public async Task<IActionResult> RetirarTodas(
            int id,
            int fotografiaId,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Unauthorized(Error("Sesión no válida."));

            IActionResult? accesoAprobador = await ValidarPermisoAsync(
                usuarioId.Value,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Actualizar,
                cancellationToken);
            if (accesoAprobador != null)
                return accesoAprobador;

            IActionResult? accesoAlbum = await ValidarPermisoAsync(
                usuarioId.Value,
                DiagnosticoIAFlujo.InterfazAlbum,
                TipoPermisoApi.Eliminar,
                cancellationToken);
            if (accesoAlbum != null)
                return accesoAlbum;

            List<PublicacionDiagnosticoEstadoDto> actuales =
                await ConstruirEstadosAsync(
                    id,
                    fotografiaId,
                    usuarioId,
                    cancellationToken);

            List<PublicacionDiagnosticoSeleccionRequest> decisiones = actuales
                .Where(item => item.PublicadaActiva)
                .Select(item => new PublicacionDiagnosticoSeleccionRequest
                {
                    DiagnosticoClave = item.DiagnosticoClave,
                    Publicar = false
                })
                .ToList();

            if (decisiones.Count > 0)
            {
                actuales = await SincronizarInternoAsync(
                    id,
                    fotografiaId,
                    decisiones,
                    usuarioId.Value,
                    cancellationToken);
            }

            return Ok(new
            {
                success = true,
                message = "Las publicaciones activas de la fotografía fueron retiradas del Álbum Botánico.",
                data = new
                {
                    fotografiaId,
                    aprobada = true,
                    autorizada = actuales.Any(item => item.PuedePublicarse),
                    publicadaActiva = actuales.Any(item => item.PublicadaActiva),
                    tuvoPublicacion = actuales.Any(item => item.TuvoPublicacion),
                    categoriaAlbumBotanicoId = (int?)null,
                    albumBotanicoCafeId = (int?)null,
                    albumBotanicoCafeFotoId = (int?)null,
                    estadoEvidencia = string.Empty,
                    totalDiagnosticos = actuales.Count,
                    publicacionesActivas = actuales.Count(item => item.PublicadaActiva),
                    mensaje = "No hay publicaciones activas en este momento."
                }
            });
        }

        /// <summary>
        /// Señalizaciones de las fotografías que pertenecen a una subcategoría
        /// del Álbum Botánico. Las fotografías cargadas de forma ordinaria no
        /// aparecen aquí y conservan exactamente su comportamiento actual.
        /// </summary>
        [HttpGet(
            "~/api/album-botanico/{albumBotanicoCafeId:int}/" +
            "senalizaciones-fitosanitarias")]
        public async Task<IActionResult> ObtenerSenalizacionesAlbum(
            int albumBotanicoCafeId,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Unauthorized(Error("Sesión no válida."));

            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId.Value,
                DiagnosticoIAFlujo.InterfazAlbum,
                TipoPermisoApi.Leer,
                cancellationToken);
            if (acceso != null)
                return acceso;

            await publicaciones.InicializarAsync(cancellationToken);
            List<PublicacionDiagnosticoRegistro> registros =
                await publicaciones.ObtenerPorAlbumAsync(
                    albumBotanicoCafeId,
                    cancellationToken);

            if (registros.Count == 0)
            {
                return Ok(new
                {
                    success = true,
                    message = "La subcategoría no contiene señalizaciones fitosanitarias publicadas.",
                    data = Array.Empty<AlbumFotoSenalizacionDto>()
                });
            }

            int[] idsFotosAlbum = registros
                .Select(item => item.AlbumBotanicoCafeFotoId)
                .Distinct()
                .ToArray();

            HashSet<int> fotosActivas = await db.FotosAlbum
                .AsNoTracking()
                .Where(item =>
                    idsFotosAlbum.Contains(item.AlbumBotanicoCafeFotoId) &&
                    item.Activo)
                .Select(item => item.AlbumBotanicoCafeFotoId)
                .ToHashSetAsync(cancellationToken);

            List<AlbumFotoSenalizacionDto> data = registros
                .Where(item => fotosActivas.Contains(
                    item.AlbumBotanicoCafeFotoId))
                .GroupBy(item => item.AlbumBotanicoCafeFotoId)
                .Select(grupo =>
                {
                    PublicacionDiagnosticoRegistro primero = grupo.First();
                    return new AlbumFotoSenalizacionDto
                    {
                        AlbumBotanicoCafeFotoId = grupo.Key,
                        AlbumBotanicoCafeId = primero.AlbumBotanicoCafeId,
                        DiagnosticoIAId = primero.InspeccionId,
                        DiagnosticoIAImagenId = primero.FotografiaId,
                        TerrenoId = primero.TerrenoId,
                        CodigoTerreno = primero.CodigoTerreno,
                        AnchoImagen = primero.AnchoImagen,
                        AltoImagen = primero.AltoImagen,
                        Capas = grupo
                            .OrderByDescending(item => item.EsPrincipal)
                            .ThenBy(item => item.OrdenDiagnostico)
                            .Select(item => new AlbumFitosanitarioCapaDto
                            {
                                DiagnosticoClave = item.DiagnosticoClave,
                                Diagnostico = item.Diagnostico,
                                EsPrincipal = item.EsPrincipal,
                                ColorMarcador = item.ColorMarcador,
                                Lesiones = DeserializarLesiones(item.LesionesJson)
                            })
                            .ToList()
                    };
                })
                .OrderBy(item => item.AlbumBotanicoCafeFotoId)
                .ToList();

            return Ok(new
            {
                success = true,
                message = "Señalizaciones fitosanitarias obtenidas correctamente.",
                data
            });
        }

        private async Task<List<PublicacionDiagnosticoEstadoDto>>
            SincronizarInternoAsync(
                int inspeccionId,
                int fotografiaId,
                IReadOnlyCollection<PublicacionDiagnosticoSeleccionRequest>
                    decisiones,
                int usuarioId,
                CancellationToken cancellationToken)
        {
            if (!await FotografiaPerteneceAsync(
                    inspeccionId,
                    fotografiaId,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    "La fotografía no pertenece a la inspección indicada.");
            }

            await flujo.InicializarAsync(cancellationToken);
            await publicaciones.InicializarAsync(cancellationToken);

            FotoMetadatos? meta = await flujo.ObtenerFotoAsync(
                fotografiaId,
                cancellationToken);
            AprobacionRegistro? aprobacion = await flujo.ObtenerUltimaAprobacionAsync(
                fotografiaId,
                cancellationToken);

            if (meta == null ||
                !meta.Activo ||
                meta.Descartada ||
                aprobacion == null)
            {
                throw new InvalidOperationException(
                    "La fotografía debe estar aprobada y activa antes de administrar publicaciones del Álbum Botánico.");
            }

            DiagnosticoIA? inspeccion = await db.Diagnosticos
                .AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.DiagnosticoIAId == inspeccionId &&
                    item.Activo,
                    cancellationToken);

            DiagnosticoIAImagen? imagen = await db.Imagenes
                .AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.DiagnosticoIAId == inspeccionId &&
                    item.DiagnosticoIAImagenId == fotografiaId,
                    cancellationToken);

            if (inspeccion == null || imagen == null)
            {
                throw new InvalidOperationException(
                    "No fue posible recuperar la evidencia original de la inspección.");
            }

            List<ClasificacionDiagnosticoFitosanitarioRegistro> clasificacionesFoto =
                (await clasificaciones.SincronizarYObtenerAsync(
                    inspeccionId,
                    usuarioId,
                    cancellationToken))
                .Where(item =>
                    item.FotografiaId == fotografiaId &&
                    item.Activo)
                .ToList();

            Dictionary<string, ClasificacionDiagnosticoFitosanitarioRegistro>
                clasificacionesPorClave = clasificacionesFoto
                    .ToDictionary(
                        item => item.DiagnosticoClave,
                        StringComparer.Ordinal);

            List<DiagnosticoVisualPersistenciaDto> diagnosticos =
                await ObtenerDiagnosticosVigentesAsync(
                    inspeccionId,
                    fotografiaId,
                    aprobacion,
                    cancellationToken);

            List<PublicacionDiagnosticoRegistro> historial =
                await publicaciones.ObtenerPorFotografiaAsync(
                    fotografiaId,
                    incluirInactivas: true,
                    cancellationToken);

            await NormalizarActivasSinFotoAlbumAsync(
                historial,
                usuarioId,
                cancellationToken);

            historial = await publicaciones.ObtenerPorFotografiaAsync(
                fotografiaId,
                incluirInactivas: true,
                cancellationToken);

            Dictionary<string, PublicacionDiagnosticoRegistro> activas = historial
                .Where(item => item.Activo)
                .GroupBy(item => item.DiagnosticoClave)
                .ToDictionary(
                    grupo => grupo.Key,
                    grupo => grupo
                        .OrderByDescending(item => item.FechaPublicacionUtc)
                        .First(),
                    StringComparer.Ordinal);

            /*
             * Compatibilidad hacia atrás: las versiones anteriores solo podían
             * tener una publicación por fotografía. Se conserva como espejo del
             * diagnóstico principal para que clientes antiguos y nuevos vean un
             * estado coherente durante la transición.
             */
            DiagnosticoIAAlbumPublicacion? legadaActiva =
                await db.PublicacionesAlbum
                    .FirstOrDefaultAsync(item =>
                        item.DiagnosticoIAImagenId == fotografiaId &&
                        item.Activo,
                        cancellationToken);

            bool requiereDimensiones = decisiones.Any(item =>
                item.Publicar &&
                !activas.ContainsKey(item.DiagnosticoClave));

            int anchoImagen = 0;
            int altoImagen = 0;
            if (requiereDimensiones)
            {
                (anchoImagen, altoImagen) =
                    await ObtenerDimensionesOriginalAsync(
                        imagen,
                        cancellationToken);
            }

            await using var transaccion = await db.Database
                .BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            try
            {
                var albumsAfectados = new HashSet<int>();

                foreach (PublicacionDiagnosticoSeleccionRequest decision in
                         decisiones
                             .Where(item => !string.IsNullOrWhiteSpace(
                                 item.DiagnosticoClave))
                             .GroupBy(item => item.DiagnosticoClave.Trim())
                             .Select(grupo => grupo.Last()))
                {
                    string clave = decision.DiagnosticoClave.Trim();

                    if (!clasificacionesPorClave.TryGetValue(
                            clave,
                            out ClasificacionDiagnosticoFitosanitarioRegistro?
                                clasificacion))
                    {
                        throw new InvalidOperationException(
                            "Una de las decisiones de publicación ya no corresponde a un diagnóstico vigente de la fotografía.");
                    }

                    activas.TryGetValue(
                        clave,
                        out PublicacionDiagnosticoRegistro? activa);

                    if (!decision.Publicar)
                    {
                        if (activa == null)
                        {
                            /*
                             * Una publicación histórica pertenece siempre al
                             * diagnóstico principal. Si todavía no había sido
                             * adoptada por V2, retirarla desde la pantalla nueva
                             * debe seguir funcionando.
                             */
                            if (clasificacion.EsPrincipal &&
                                legadaActiva != null)
                            {
                                int albumLegado = legadaActiva.AlbumBotanicoCafeId;
                                int fotoLegada = legadaActiva.AlbumBotanicoCafeFotoId;

                                legadaActiva.Activo = false;
                                albumsAfectados.Add(albumLegado);

                                AlbumBotanicoCafeFotoReferencia? fotoAlbum =
                                    await db.FotosAlbum.FirstOrDefaultAsync(item =>
                                        item.AlbumBotanicoCafeFotoId == fotoLegada,
                                        cancellationToken);

                                if (fotoAlbum != null)
                                {
                                    fotoAlbum.Activo = false;
                                    fotoAlbum.EsPortada = false;
                                }

                                legadaActiva = null;
                            }

                            continue;
                        }

                        await publicaciones.RetirarAsync(
                            activa.Id,
                            usuarioId,
                            cancellationToken);

                        albumsAfectados.Add(activa.AlbumBotanicoCafeId);

                        bool otraActiva =
                            await publicaciones.ExisteOtraActivaParaFotoAlbumAsync(
                                activa.AlbumBotanicoCafeFotoId,
                                cancellationToken);

                        if (!otraActiva)
                        {
                            AlbumBotanicoCafeFotoReferencia? fotoAlbum =
                                await db.FotosAlbum.FirstOrDefaultAsync(item =>
                                    item.AlbumBotanicoCafeFotoId ==
                                        activa.AlbumBotanicoCafeFotoId,
                                    cancellationToken);

                            if (fotoAlbum != null)
                            {
                                fotoAlbum.Activo = false;
                                fotoAlbum.EsPortada = false;
                            }
                        }

                        if (clasificacion.EsPrincipal &&
                            legadaActiva != null &&
                            legadaActiva.AlbumBotanicoCafeFotoId ==
                                activa.AlbumBotanicoCafeFotoId)
                        {
                            legadaActiva.Activo = false;
                            legadaActiva = null;
                        }

                        activas.Remove(clave);
                        continue;
                    }

                    if (activa != null)
                        continue;

                    if (!PuedePublicarse(clasificacion))
                    {
                        throw new InvalidOperationException(
                            $"El diagnóstico «{clasificacion.Diagnostico}» todavía no tiene una clasificación oficial confirmada por el aprobador.");
                    }

                    AlbumBotanicoCafeReferencia? subcategoria =
                        await db.RegistrosAlbum
                            .AsNoTracking()
                            .FirstOrDefaultAsync(item =>
                                item.AlbumBotanicoCafeId ==
                                    clasificacion.AlbumBotanicoCafeIdSeleccionado!.Value &&
                                item.CategoriaAlbumBotanicoId ==
                                    clasificacion.CategoriaAlbumBotanicoIdSeleccionada!.Value &&
                                item.Activo,
                                cancellationToken);

                    bool categoriaActiva = await db.CategoriasAlbum
                        .AsNoTracking()
                        .AnyAsync(item =>
                            item.CategoriaAlbumBotanicoId ==
                                clasificacion.CategoriaAlbumBotanicoIdSeleccionada!.Value &&
                            item.Activo,
                            cancellationToken);

                    if (subcategoria == null || !categoriaActiva)
                    {
                        throw new InvalidOperationException(
                            $"La clasificación oficial de «{clasificacion.Diagnostico}» apunta a una categoría o subcategoría inactiva.");
                    }

                    DiagnosticoVisualPersistenciaDto? diagnostico =
                        BuscarDiagnostico(
                            clasificacion,
                            diagnosticos);

                    string descripcion = Limitar(
                        decision.Descripcion,
                        1000);
                    if (string.IsNullOrWhiteSpace(descripcion))
                    {
                        descripcion = Limitar(
                            $"{clasificacion.Diagnostico} · evidencia fitosanitaria aprobada.",
                            1000);
                    }

                    int albumFotoId;
                    bool adoptaPublicacionLegada =
                        clasificacion.EsPrincipal && legadaActiva != null;

                    if (adoptaPublicacionLegada)
                    {
                        if (legadaActiva!.CategoriaAlbumBotanicoId !=
                                clasificacion.CategoriaAlbumBotanicoIdSeleccionada!.Value ||
                            legadaActiva.AlbumBotanicoCafeId !=
                                clasificacion.AlbumBotanicoCafeIdSeleccionado!.Value)
                        {
                            throw new InvalidOperationException(
                                "La publicación histórica de la fotografía utiliza una clasificación diferente a la clasificación principal vigente. Retire primero la publicación histórica antes de volver a publicar.");
                        }

                        albumFotoId = legadaActiva.AlbumBotanicoCafeFotoId;
                    }
                    else
                    {
                        albumFotoId = await ObtenerOCrearFotoAlbumAsync(
                            fotografiaId,
                            clasificacion,
                            imagen,
                            descripcion,
                            historial,
                            cancellationToken);
                    }

                    List<AlbumFitosanitarioLesionDto> lesiones =
                        NormalizarLesiones(diagnostico?.Lesiones);

                    string lesionesJson = JsonSerializer.Serialize(
                        lesiones,
                        JsonOptions);

                    int publicacionId = await publicaciones.InsertarAsync(
                        new NuevaPublicacionDiagnosticoRegistro
                        {
                            InspeccionId = inspeccionId,
                            FotografiaId = fotografiaId,
                            DiagnosticoClave = clasificacion.DiagnosticoClave,
                            DiagnosticoIdOrigenIA =
                                clasificacion.DiagnosticoIdOrigenIA,
                            OrdenDiagnostico = clasificacion.OrdenDiagnostico,
                            EsPrincipal = clasificacion.EsPrincipal,
                            Diagnostico = clasificacion.Diagnostico,
                            TerrenoId = inspeccion.TerrenoId,
                            CodigoTerreno = inspeccion.CodigoTerreno,
                            CategoriaAlbumBotanicoId =
                                clasificacion.CategoriaAlbumBotanicoIdSeleccionada!.Value,
                            AlbumBotanicoCafeId =
                                clasificacion.AlbumBotanicoCafeIdSeleccionado!.Value,
                            AlbumBotanicoCafeFotoId = albumFotoId,
                            AprobacionId = aprobacion.AprobacionId,
                            ColorMarcador =
                                NormalizarColor(diagnostico?.ColorMarcador),
                            LesionesJson = lesionesJson,
                            AnchoImagen = anchoImagen,
                            AltoImagen = altoImagen,
                            Descripcion = descripcion,
                            UsuarioId = usuarioId
                        },
                        cancellationToken);

                    if (clasificacion.EsPrincipal &&
                        legadaActiva == null)
                    {
                        AlbumBotanicoCafeFotoReferencia? fotoAlbumPrincipal =
                            await db.FotosAlbum
                                .AsNoTracking()
                                .FirstOrDefaultAsync(item =>
                                    item.AlbumBotanicoCafeFotoId == albumFotoId,
                                    cancellationToken);

                        db.PublicacionesAlbum.Add(new DiagnosticoIAAlbumPublicacion
                        {
                            DiagnosticoIAId = inspeccionId,
                            DiagnosticoIAImagenId = fotografiaId,
                            CategoriaAlbumBotanicoId =
                                clasificacion.CategoriaAlbumBotanicoIdSeleccionada!.Value,
                            AlbumBotanicoCafeId =
                                clasificacion.AlbumBotanicoCafeIdSeleccionado!.Value,
                            AlbumBotanicoCafeFotoId = albumFotoId,
                            UsuarioPublicacionId = usuarioId,
                            FechaPublicacionUtc = DateTime.UtcNow,
                            DescripcionPublicacion = descripcion,
                            ClasificacionFinal = Limitar(subcategoria.Titulo, 50),
                            DiagnosticoFinal = Limitar(clasificacion.Diagnostico, 300),
                            RutaFotoAlbum = Limitar(
                                fotoAlbumPrincipal?.RutaFoto ?? imagen.RutaRelativa,
                                600),
                            Activo = true
                        });
                    }

                    activas[clave] = new PublicacionDiagnosticoRegistro
                    {
                        Id = publicacionId,
                        InspeccionId = inspeccionId,
                        FotografiaId = fotografiaId,
                        DiagnosticoClave = clasificacion.DiagnosticoClave,
                        AlbumBotanicoCafeId =
                            clasificacion.AlbumBotanicoCafeIdSeleccionado!.Value,
                        AlbumBotanicoCafeFotoId = albumFotoId,
                        Activo = true
                    };

                    albumsAfectados.Add(
                        clasificacion.AlbumBotanicoCafeIdSeleccionado!.Value);
                }

                await db.SaveChangesAsync(cancellationToken);

                foreach (int albumId in albumsAfectados)
                    await GarantizarPortadaActivaAsync(
                        albumId,
                        cancellationToken);

                await db.SaveChangesAsync(cancellationToken);
                await transaccion.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }

            return await ConstruirEstadosAsync(
                inspeccionId,
                fotografiaId,
                usuarioId,
                cancellationToken);
        }

        private async Task<List<PublicacionDiagnosticoEstadoDto>>
            ConstruirEstadosAsync(
                int inspeccionId,
                int fotografiaId,
                int? usuarioId,
                CancellationToken cancellationToken)
        {
            await flujo.InicializarAsync(cancellationToken);
            await publicaciones.InicializarAsync(cancellationToken);

            List<ClasificacionDiagnosticoFitosanitarioRegistro> clasificacionesFoto =
                (await clasificaciones.SincronizarYObtenerAsync(
                    inspeccionId,
                    usuarioId,
                    cancellationToken))
                .Where(item =>
                    item.FotografiaId == fotografiaId &&
                    item.Activo)
                .OrderByDescending(item => item.EsPrincipal)
                .ThenBy(item => item.OrdenDiagnostico)
                .ToList();

            AprobacionRegistro? aprobacion = await flujo.ObtenerUltimaAprobacionAsync(
                fotografiaId,
                cancellationToken);

            List<DiagnosticoVisualPersistenciaDto> diagnosticos =
                await ObtenerDiagnosticosVigentesAsync(
                    inspeccionId,
                    fotografiaId,
                    aprobacion,
                    cancellationToken);

            List<PublicacionDiagnosticoRegistro> historial =
                await publicaciones.ObtenerPorFotografiaAsync(
                    fotografiaId,
                    incluirInactivas: true,
                    cancellationToken);

            List<DiagnosticoIAAlbumPublicacion> historialLegado =
                await db.PublicacionesAlbum
                    .AsNoTracking()
                    .Where(item =>
                        item.DiagnosticoIAImagenId == fotografiaId)
                    .OrderByDescending(item => item.FechaPublicacionUtc)
                    .ToListAsync(cancellationToken);

            int[] idsFotosAlbum = historial
                .Where(item => item.Activo)
                .Select(item => item.AlbumBotanicoCafeFotoId)
                .Concat(
                    historialLegado
                        .Where(item => item.Activo)
                        .Select(item => item.AlbumBotanicoCafeFotoId))
                .Distinct()
                .ToArray();

            HashSet<int> fotosAlbumActivas = idsFotosAlbum.Length == 0
                ? []
                : await db.FotosAlbum
                    .AsNoTracking()
                    .Where(item =>
                        idsFotosAlbum.Contains(item.AlbumBotanicoCafeFotoId) &&
                        item.Activo)
                    .Select(item => item.AlbumBotanicoCafeFotoId)
                    .ToHashSetAsync(cancellationToken);

            var resultado = new List<PublicacionDiagnosticoEstadoDto>();

            foreach (ClasificacionDiagnosticoFitosanitarioRegistro clasificacion in
                     clasificacionesFoto)
            {
                List<PublicacionDiagnosticoRegistro> historialDiagnostico = historial
                    .Where(item => string.Equals(
                        item.DiagnosticoClave,
                        clasificacion.DiagnosticoClave,
                        StringComparison.Ordinal))
                    .OrderByDescending(item => item.FechaPublicacionUtc)
                    .ToList();

                PublicacionDiagnosticoRegistro? activa =
                    historialDiagnostico.FirstOrDefault(item =>
                        item.Activo &&
                        fotosAlbumActivas.Contains(
                            item.AlbumBotanicoCafeFotoId));

                DiagnosticoIAAlbumPublicacion? activaLegada =
                    activa == null && clasificacion.EsPrincipal
                        ? historialLegado.FirstOrDefault(item =>
                            item.Activo &&
                            fotosAlbumActivas.Contains(
                                item.AlbumBotanicoCafeFotoId))
                        : null;

                bool tuvoLegada =
                    clasificacion.EsPrincipal && historialLegado.Count > 0;

                DiagnosticoVisualPersistenciaDto? diagnostico =
                    BuscarDiagnostico(
                        clasificacion,
                        diagnosticos);

                resultado.Add(new PublicacionDiagnosticoEstadoDto
                {
                    FotografiaId = clasificacion.FotografiaId,
                    DiagnosticoClave = clasificacion.DiagnosticoClave,
                    DiagnosticoIdOrigenIA =
                        clasificacion.DiagnosticoIdOrigenIA,
                    OrdenDiagnostico = clasificacion.OrdenDiagnostico,
                    EsPrincipal = clasificacion.EsPrincipal,
                    Diagnostico = clasificacion.Diagnostico,
                    CategoriaIA = clasificacion.CategoriaIA,
                    TipoDiagnosticoIA = clasificacion.TipoDiagnosticoIA,
                    CategoriaAlbumBotanicoIdSugerida =
                        clasificacion.CategoriaAlbumBotanicoIdSugerida,
                    AlbumBotanicoCafeIdSugerido =
                        clasificacion.AlbumBotanicoCafeIdSugerido,
                    CategoriaSugerida = clasificacion.CategoriaSugerida,
                    SubcategoriaSugerida =
                        clasificacion.SubcategoriaSugerida,
                    NombreCientificoSugerido =
                        clasificacion.NombreCientificoSugerido,
                    CoincideCatalogo = clasificacion.CoincideCatalogo,
                    RequiereDecision = clasificacion.RequiereDecision,
                    CategoriaAlbumBotanicoIdSeleccionada =
                        clasificacion.CategoriaAlbumBotanicoIdSeleccionada,
                    AlbumBotanicoCafeIdSeleccionado =
                        clasificacion.AlbumBotanicoCafeIdSeleccionado,
                    CategoriaSeleccionada =
                        clasificacion.CategoriaSeleccionada,
                    SubcategoriaSeleccionada =
                        clasificacion.SubcategoriaSeleccionada,
                    AccionHumana = clasificacion.AccionHumana,
                    Estado = clasificacion.Estado,
                    FuenteVigente = clasificacion.FuenteVigente,
                    Activo = clasificacion.Activo,
                    PublicadaActiva = activa != null || activaLegada != null,
                    TuvoPublicacion =
                        historialDiagnostico.Count > 0 || tuvoLegada,
                    PublicarEnAlbum = activa != null || activaLegada != null,
                    DiagnosticoIAAlbumPublicacionDiagnosticoId = activa?.Id,
                    AlbumBotanicoCafeFotoId =
                        activa?.AlbumBotanicoCafeFotoId ??
                        activaLegada?.AlbumBotanicoCafeFotoId,
                    FechaPublicacionUtc =
                        activa?.FechaPublicacionUtc ??
                        activaLegada?.FechaPublicacionUtc,
                    DescripcionPublicacion =
                        activa?.Descripcion ??
                        activaLegada?.DescripcionPublicacion ??
                        string.Empty,
                    ColorMarcador = activa?.ColorMarcador ??
                        NormalizarColor(diagnostico?.ColorMarcador),
                    Lesiones = activa != null
                        ? DeserializarLesiones(activa.LesionesJson)
                        : NormalizarLesiones(diagnostico?.Lesiones),
                    AnchoImagen = activa?.AnchoImagen ?? 0,
                    AltoImagen = activa?.AltoImagen ?? 0
                });
            }

            return resultado;
        }

        private async Task<List<DiagnosticoVisualPersistenciaDto>>
            ObtenerDiagnosticosVigentesAsync(
                int inspeccionId,
                int fotografiaId,
                AprobacionRegistro? aprobacion,
                CancellationToken cancellationToken)
        {
            if (aprobacion != null)
            {
                List<DiagnosticoVisualPersistenciaDto> finales =
                    DeserializarDiagnosticos(
                        aprobacion.DiagnosticosFinalesJson);
                if (finales.Count > 0)
                    return finales;
            }

            Dictionary<int, AnalisisHumanoRegistro> humanos =
                await flujo.ObtenerUltimosAnalisisHumanosAsync(
                    inspeccionId,
                    cancellationToken);

            if (humanos.TryGetValue(
                    fotografiaId,
                    out AnalisisHumanoRegistro? humano))
            {
                List<DiagnosticoVisualPersistenciaDto> humanosLista =
                    DeserializarDiagnosticos(humano.DiagnosticosJson);
                if (humanosLista.Count > 0)
                    return humanosLista;
            }

            Dictionary<int, ResultadoVisualRegistro> visuales =
                await flujo.ObtenerResultadosVisualesVigentesAsync(
                    inspeccionId,
                    cancellationToken);

            return visuales.TryGetValue(
                    fotografiaId,
                    out ResultadoVisualRegistro? visual)
                ? DeserializarDiagnosticos(visual.DiagnosticosJson)
                : [];
        }

        private async Task<(int Ancho, int Alto)> ObtenerDimensionesOriginalAsync(
            DiagnosticoIAImagen imagen,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(imagen.RutaRelativa) ||
                !storage.ArchivoExiste(imagen.RutaRelativa))
            {
                throw new InvalidOperationException(
                    "La evidencia original no está disponible en el almacenamiento persistente. No se utilizará una imagen derivada de IA para publicar.");
            }

            string ruta = storage.ResolverRutaPublica(imagen.RutaRelativa);
            IImageInfo? info = await Image.IdentifyAsync(
                ruta,
                cancellationToken);

            if (info == null || info.Width <= 0 || info.Height <= 0)
            {
                throw new InvalidOperationException(
                    "No fue posible determinar las dimensiones de la evidencia original.");
            }

            return (info.Width, info.Height);
        }

        private async Task<int> ObtenerOCrearFotoAlbumAsync(
            int fotografiaId,
            ClasificacionDiagnosticoFitosanitarioRegistro clasificacion,
            DiagnosticoIAImagen imagen,
            string descripcion,
            IReadOnlyCollection<PublicacionDiagnosticoRegistro> historial,
            CancellationToken cancellationToken)
        {
            int albumId = clasificacion.AlbumBotanicoCafeIdSeleccionado!.Value;

            PublicacionDiagnosticoRegistro? reutilizable = historial
                .Where(item =>
                    item.FotografiaId == fotografiaId &&
                    item.AlbumBotanicoCafeId == albumId)
                .OrderByDescending(item => item.FechaPublicacionUtc)
                .FirstOrDefault();

            if (reutilizable != null)
            {
                AlbumBotanicoCafeFotoReferencia? fotoExistente =
                    await db.FotosAlbum.FirstOrDefaultAsync(item =>
                        item.AlbumBotanicoCafeFotoId ==
                            reutilizable.AlbumBotanicoCafeFotoId &&
                        item.AlbumBotanicoCafeId == albumId,
                        cancellationToken);

                if (fotoExistente != null)
                {
                    fotoExistente.Activo = true;
                    fotoExistente.RutaFoto = Limitar(imagen.RutaRelativa, 500);
                    fotoExistente.DescripcionFoto = Limitar(descripcion, 500);
                    return fotoExistente.AlbumBotanicoCafeFotoId;
                }
            }

            int orden =
                (await db.FotosAlbum
                    .Where(item =>
                        item.AlbumBotanicoCafeId == albumId &&
                        item.Activo)
                    .Select(item => (int?)item.Orden)
                    .MaxAsync(cancellationToken) ?? 0) + 1;

            bool existenActivas = await db.FotosAlbum
                .AsNoTracking()
                .AnyAsync(item =>
                    item.AlbumBotanicoCafeId == albumId &&
                    item.Activo,
                    cancellationToken);

            var nueva = new AlbumBotanicoCafeFotoReferencia
            {
                AlbumBotanicoCafeId = albumId,
                RutaFoto = Limitar(imagen.RutaRelativa, 500),
                DescripcionFoto = Limitar(descripcion, 500),
                EsPortada = !existenActivas,
                Orden = orden,
                Activo = true
            };

            db.FotosAlbum.Add(nueva);
            await db.SaveChangesAsync(cancellationToken);
            return nueva.AlbumBotanicoCafeFotoId;
        }

        private async Task NormalizarActivasSinFotoAlbumAsync(
            IReadOnlyCollection<PublicacionDiagnosticoRegistro> historial,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            List<PublicacionDiagnosticoRegistro> activas = historial
                .Where(item => item.Activo)
                .ToList();

            if (activas.Count == 0)
                return;

            int[] ids = activas
                .Select(item => item.AlbumBotanicoCafeFotoId)
                .Distinct()
                .ToArray();

            HashSet<int> existentes = await db.FotosAlbum
                .AsNoTracking()
                .Where(item =>
                    ids.Contains(item.AlbumBotanicoCafeFotoId) &&
                    item.Activo)
                .Select(item => item.AlbumBotanicoCafeFotoId)
                .ToHashSetAsync(cancellationToken);

            foreach (PublicacionDiagnosticoRegistro activa in activas)
            {
                if (!existentes.Contains(activa.AlbumBotanicoCafeFotoId))
                {
                    await publicaciones.RetirarAsync(
                        activa.Id,
                        usuarioId,
                        cancellationToken);
                }
            }
        }

        private async Task GarantizarPortadaActivaAsync(
            int albumBotanicoCafeId,
            CancellationToken cancellationToken)
        {
            List<AlbumBotanicoCafeFotoReferencia> fotos = await db.FotosAlbum
                .Where(item =>
                    item.AlbumBotanicoCafeId == albumBotanicoCafeId)
                .OrderBy(item => item.Orden)
                .ThenBy(item => item.AlbumBotanicoCafeFotoId)
                .ToListAsync(cancellationToken);

            List<AlbumBotanicoCafeFotoReferencia> activas = fotos
                .Where(item => item.Activo)
                .ToList();

            if (activas.Count == 0)
            {
                foreach (AlbumBotanicoCafeFotoReferencia item in fotos)
                    item.EsPortada = false;
                return;
            }

            AlbumBotanicoCafeFotoReferencia portada =
                activas.FirstOrDefault(item => item.EsPortada) ??
                activas[0];

            foreach (AlbumBotanicoCafeFotoReferencia item in fotos)
                item.EsPortada = item.Activo &&
                    item.AlbumBotanicoCafeFotoId ==
                        portada.AlbumBotanicoCafeFotoId;
        }

        private async Task<bool> FotografiaPerteneceAsync(
            int inspeccionId,
            int fotografiaId,
            CancellationToken cancellationToken) =>
            await db.Imagenes
                .AsNoTracking()
                .AnyAsync(item =>
                    item.DiagnosticoIAId == inspeccionId &&
                    item.DiagnosticoIAImagenId == fotografiaId,
                    cancellationToken);

        private static bool PuedePublicarse(
            ClasificacionDiagnosticoFitosanitarioRegistro item) =>
            item.Activo &&
            !string.Equals(
                item.Estado,
                "DESCARTADA_DIAGNOSTICO",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                item.Estado,
                "RESUELTA_APROBADOR",
                StringComparison.OrdinalIgnoreCase) &&
            item.CategoriaAlbumBotanicoIdSeleccionada is > 0 &&
            item.AlbumBotanicoCafeIdSeleccionado is > 0;

        private static DiagnosticoVisualPersistenciaDto? BuscarDiagnostico(
            ClasificacionDiagnosticoFitosanitarioRegistro clasificacion,
            IReadOnlyList<DiagnosticoVisualPersistenciaDto> diagnosticos)
        {
            if (!string.IsNullOrWhiteSpace(
                    clasificacion.DiagnosticoIdOrigenIA))
            {
                DiagnosticoVisualPersistenciaDto? porId = diagnosticos
                    .FirstOrDefault(item =>
                        string.Equals(
                            PrimerTexto(item.IdOrigenIA, item.Id),
                            clasificacion.DiagnosticoIdOrigenIA,
                            StringComparison.OrdinalIgnoreCase));
                if (porId != null)
                    return porId;
            }

            DiagnosticoVisualPersistenciaDto? porOrden = diagnosticos
                .Where(item => !string.Equals(
                    item.AccionHumana,
                    "DESCARTAR",
                    StringComparison.OrdinalIgnoreCase))
                .Skip(Math.Max(0, clasificacion.OrdenDiagnostico - 1))
                .FirstOrDefault();

            if (porOrden != null &&
                NormalizarTexto(porOrden.Diagnostico) ==
                    NormalizarTexto(clasificacion.Diagnostico))
            {
                return porOrden;
            }

            return diagnosticos.FirstOrDefault(item =>
                NormalizarTexto(item.Diagnostico) ==
                    NormalizarTexto(clasificacion.Diagnostico));
        }

        private static List<DiagnosticoVisualPersistenciaDto>
            DeserializarDiagnosticos(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            try
            {
                return JsonSerializer.Deserialize<
                    List<DiagnosticoVisualPersistenciaDto>>(
                        json,
                        JsonOptions) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private static List<AlbumFitosanitarioLesionDto>
            NormalizarLesiones(
                IEnumerable<AlbumFitosanitarioLesionDto>? lesiones) =>
            (lesiones ?? [])
                .Where(item =>
                    item.Box2d is { Count: 4 } &&
                    item.Box2d.All(value => value is >= 0 and <= 1000) &&
                    item.Box2d[0] < item.Box2d[2] &&
                    item.Box2d[1] < item.Box2d[3])
                .Select(item => new AlbumFitosanitarioLesionDto
                {
                    Id = Limitar(item.Id, 120),
                    Descripcion = Limitar(item.Descripcion, 500),
                    Box2d = item.Box2d.ToList()
                })
                .Take(50)
                .ToList();

        private static List<AlbumFitosanitarioLesionDto>
            DeserializarLesiones(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            try
            {
                return NormalizarLesiones(
                    JsonSerializer.Deserialize<
                        List<AlbumFitosanitarioLesionDto>>(
                            json,
                            JsonOptions));
            }
            catch
            {
                return [];
            }
        }

        private static string NormalizarColor(string? color)
        {
            string valor = (color ?? string.Empty).Trim();
            if (valor.Length == 7 &&
                valor[0] == '#' &&
                valor.Skip(1).All(Uri.IsHexDigit))
            {
                return valor.ToUpperInvariant();
            }

            return "#E53935";
        }

        private async Task<IActionResult?> ValidarLecturaAsync(
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
                ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                    usuarioId,
                    interfaz,
                    TipoPermisoApi.Leer,
                    cancellationToken);

                if (permiso.Permitido)
                    return null;
            }

            return StatusCode(
                StatusCodes.Status403Forbidden,
                Error(
                    "No tiene permisos para consultar las publicaciones fitosanitarias."));
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            int usuarioId,
            string interfaz,
            TipoPermisoApi permiso,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                permiso,
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

        private static string PrimerTexto(params string?[] valores) =>
            valores.FirstOrDefault(valor =>
                !string.IsNullOrWhiteSpace(valor))?.Trim() ?? string.Empty;

        private static string NormalizarTexto(string? valor) =>
            (valor ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace('_', ' ');

        private static string Limitar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo
                ? texto
                : texto[..maximo];
        }

        private static object Error(string? mensaje) => new
        {
            success = false,
            message = string.IsNullOrWhiteSpace(mensaje)
                ? "No fue posible completar la operación."
                : mensaje
        };

        private sealed class DiagnosticoVisualPersistenciaDto
        {
            public string Id { get; set; } = string.Empty;
            public string IdOrigenIA { get; set; } = string.Empty;
            public string AccionHumana { get; set; } = string.Empty;
            public string Diagnostico { get; set; } = string.Empty;
            public bool EsPrincipal { get; set; }
            public string ColorMarcador { get; set; } = "#E53935";
            public List<AlbumFitosanitarioLesionDto> Lesiones { get; set; } = [];
        }
    }
}
