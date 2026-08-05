using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Globalization;
using System.Text.Json;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Flujo V2 de inspección fitosanitaria. La inspección funciona como
    /// contenedor y cada fotografía mantiene estado, fechas, IA, revisión
    /// humana, aprobación, descarte e historial independientes.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/inspecciones-fitosanitarias")]
    public sealed class InspeccionFitosanitariaController : ControllerBase
    {
        private const long MaximoBytesPorFoto = 12L * 1024L * 1024L;
        private const int MaximoFotosPorSolicitud = 40;

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };

        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly DBContext db;
        private readonly ImageService imageService;
        private readonly ImageStoragePathService storage;
        private readonly PermisoApiService permisos;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly IConfiguration configuration;
        private readonly ILogger<InspeccionFitosanitariaController> logger;
        private readonly InspeccionFitosanitariaDatabase database;

        public InspeccionFitosanitariaController(
            DiagnosticoIADbContext diagnosticoDb,
            DBContext db,
            ImageService imageService,
            ImageStoragePathService storage,
            PermisoApiService permisos,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<InspeccionFitosanitariaController> logger)
        {
            this.diagnosticoDb = diagnosticoDb;
            this.db = db;
            this.imageService = imageService;
            this.storage = storage;
            this.permisos = permisos;
            this.httpClientFactory = httpClientFactory;
            this.configuration = configuration;
            this.logger = logger;
            database = new InspeccionFitosanitariaDatabase(diagnosticoDb);
        }

        [HttpPost]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(700L * 1024L * 1024L)]
        public async Task<IActionResult> Crear(
            [FromForm] InspeccionFitosanitariaCrearRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazSolicitud,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            await database.InicializarAsync(cancellationToken);

            List<IFormFile> fotos = (request.Fotos ?? [])
                .Where(item => item != null && item.Length > 0)
                .ToList();

            IActionResult? errorFotos = ValidarFotos(fotos);
            if (errorFotos != null)
                return errorFotos;

            IActionResult? errorFechas = ValidarFechasCampo(
                request.FechasIdentificacionCampo,
                fotos.Count);
            if (errorFechas != null)
                return errorFechas;

            (int? terrenoId, string codigoTerreno, IActionResult? errorTerreno) =
                await ResolverTerrenoAsync(
                    request.CodigoTerreno,
                    cancellationToken);

            if (errorTerreno != null)
                return errorTerreno;

            ProveedorEjecucion proveedor =
                await CrearProveedorService()
                    .ObtenerEjecucionAsync(cancellationToken);

            var inspeccion = new DiagnosticoIA
            {
                TerrenoId = terrenoId,
                CodigoTerreno = codigoTerreno,
                UsuarioSolicitanteId = usuarioId!.Value,
                FechaSolicitudUtc = DateTime.UtcNow,
                Estado = InspeccionFitosanitariaFlujo.InspeccionEstados.EnProceso,
                ModeloGemini = Limitar(proveedor.ModeloPrincipal, 80),
                ObservacionUsuario = Limitar(request.Observacion, 1000),
                RequiereValidacionHumana = true,
                Activo = true
            };

            diagnosticoDb.Diagnosticos.Add(inspeccion);
            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            var rutasGuardadas = new List<string>();

            try
            {
                for (int indice = 0; indice < fotos.Count; indice++)
                {
                    IFormFile foto = fotos[indice];
                    string rutaRelativa =
                        await imageService.GuardarImagenWebpAsync(
                            foto,
                            $"diagnosticos-ia/{inspeccion.DiagnosticoIAId}",
                            anchoMaximo: 1600,
                            altoMaximo: 1600,
                            calidad: 76);

                    rutasGuardadas.Add(rutaRelativa);

                    var imagen = new DiagnosticoIAImagen
                    {
                        RutaRelativa = rutaRelativa,
                        UrlImagen = ConstruirUrlPublica(rutaRelativa),
                        NombreArchivoOriginal = Limitar(foto.FileName, 255),
                        TipoFotografia = ResolverTipoFotografia(
                            request.TiposFotografia,
                            indice),
                        Orden = indice + 1,
                        FechaRegistroUtc = DateTime.UtcNow
                    };

                    inspeccion.Imagenes.Add(imagen);
                    await diagnosticoDb.SaveChangesAsync(cancellationToken);

                    DateTime? fechaCampo = ResolverFechaCampo(
                        request.FechasIdentificacionCampo,
                        indice);

                    await database.RegistrarFotoAsync(
                        imagen.DiagnosticoIAImagenId,
                        fechaCampo,
                        imagen.FechaRegistroUtc,
                        usuarioId.Value,
                        cancellationToken);
                }

                inspeccion.Historial.Add(new DiagnosticoIAHistorial
                {
                    UsuarioId = usuarioId.Value,
                    EstadoAnterior = string.Empty,
                    EstadoNuevo = inspeccion.Estado,
                    Accion = "INSPECCION_CREADA_V2",
                    Detalle =
                        $"Se registraron {fotos.Count} fotografías con expediente individual.",
                    FechaUtc = DateTime.UtcNow
                });

                await diagnosticoDb.SaveChangesAsync(cancellationToken);

                return CreatedAtAction(
                    nameof(ObtenerDetalle),
                    new { id = inspeccion.DiagnosticoIAId },
                    new
                    {
                        success = true,
                        message =
                            "La inspección y sus fotografías fueron registradas. Seleccione las fotografías que desea analizar.",
                        data = await CrearDetalleAsync(
                            inspeccion.DiagnosticoIAId,
                            usuarioId.Value,
                            cancellationToken)
                    });
            }
            catch
            {
                foreach (string ruta in rutasGuardadas)
                    EliminarImagenSeguro(ruta);

                diagnosticoDb.Diagnosticos.Remove(inspeccion);
                await diagnosticoDb.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        }

        [HttpPost("{id:int}/fotografias")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(700L * 1024L * 1024L)]
        public async Task<IActionResult> AgregarFotografias(
            int id,
            [FromForm] InspeccionFitosanitariaAgregarFotosRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            await database.InicializarAsync(cancellationToken);

            DiagnosticoIA? inspeccion = await diagnosticoDb.Diagnosticos
                .Include(item => item.Imagenes)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(item =>
                    item.DiagnosticoIAId == id && item.Activo,
                    cancellationToken);

            if (inspeccion == null)
                return NoEncontrado();

            InspeccionCierreMetadatos cierre =
                await database.ObtenerCierreInspeccionAsync(
                    id,
                    cancellationToken);

            if (cierre.CerradaTecnico)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La inspección ya fue cerrada por el técnico y no admite nuevas fotografías."
                });
            }

            bool puedeAgregar =
                inspeccion.UsuarioSolicitanteId == usuarioId.Value &&
                await TienePermisoAsync(
                    usuarioId.Value,
                    DiagnosticoIAFlujo.InterfazSolicitud,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            if (!puedeAgregar)
                return Forbid();

            List<IFormFile> fotos = (request.Fotos ?? [])
                .Where(item => item != null && item.Length > 0)
                .ToList();

            IActionResult? errorFotos = ValidarFotos(fotos);
            if (errorFotos != null)
                return errorFotos;

            IActionResult? errorFechas = ValidarFechasCampo(
                request.FechasIdentificacionCampo,
                fotos.Count);
            if (errorFechas != null)
                return errorFechas;

            int existentes = inspeccion.Imagenes.Count;
            if (existentes + fotos.Count > MaximoFotosPorSolicitud)
            {
                int disponibles = Math.Max(
                    0,
                    MaximoFotosPorSolicitud - existentes);

                return BadRequest(new
                {
                    success = false,
                    message = disponibles == 0
                        ? $"La inspección ya alcanzó el límite de {MaximoFotosPorSolicitud} fotografías."
                        : $"Solo puede agregar {disponibles} fotografía(s) más a esta inspección."
                });
            }

            int siguienteOrden = inspeccion.Imagenes.Count == 0
                ? 1
                : inspeccion.Imagenes.Max(item => item.Orden) + 1;

            var rutasGuardadas = new List<string>();

            await using var transaction =
                await diagnosticoDb.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                for (int indice = 0; indice < fotos.Count; indice++)
                {
                    IFormFile foto = fotos[indice];
                    string rutaRelativa =
                        await imageService.GuardarImagenWebpAsync(
                            foto,
                            $"diagnosticos-ia/{inspeccion.DiagnosticoIAId}",
                            anchoMaximo: 1600,
                            altoMaximo: 1600,
                            calidad: 76);

                    rutasGuardadas.Add(rutaRelativa);

                    DateTime fechaRegistroUtc = DateTime.UtcNow;
                    var imagen = new DiagnosticoIAImagen
                    {
                        RutaRelativa = rutaRelativa,
                        UrlImagen = ConstruirUrlPublica(rutaRelativa),
                        NombreArchivoOriginal = Limitar(foto.FileName, 255),
                        TipoFotografia = ResolverTipoFotografia(
                            request.TiposFotografia,
                            indice),
                        Orden = siguienteOrden + indice,
                        FechaRegistroUtc = fechaRegistroUtc
                    };

                    inspeccion.Imagenes.Add(imagen);
                    await diagnosticoDb.SaveChangesAsync(cancellationToken);

                    await database.RegistrarFotoAsync(
                        imagen.DiagnosticoIAImagenId,
                        ResolverFechaCampo(
                            request.FechasIdentificacionCampo,
                            indice),
                        fechaRegistroUtc,
                        usuarioId.Value,
                        cancellationToken);
                }

                inspeccion.Historial.Add(new DiagnosticoIAHistorial
                {
                    UsuarioId = usuarioId.Value,
                    EstadoAnterior = inspeccion.Estado,
                    EstadoNuevo = inspeccion.Estado,
                    Accion = "FOTOGRAFIAS_AGREGADAS_V2",
                    Detalle =
                        $"Se agregaron {fotos.Count} fotografía(s) a la inspección existente.",
                    FechaUtc = DateTime.UtcNow
                });

                await diagnosticoDb.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);

                foreach (string ruta in rutasGuardadas)
                    EliminarImagenSeguro(ruta);

                throw;
            }

            await ActualizarEstadoInspeccionAsync(
                inspeccion,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    $"Se agregaron {fotos.Count} fotografía(s). Ahora puede seleccionarlas para analizarlas con IA.",
                data = await CrearDetalleAsync(
                    inspeccion.DiagnosticoIAId,
                    usuarioId.Value,
                    cancellationToken)
            });
        }

        [HttpGet("bandeja")]
        public async Task<IActionResult> ObtenerBandeja(
            [FromQuery] string modo = "mis",
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            await database.InicializarAsync(cancellationToken);

            string modoNormalizado = (modo ?? "mis")
                .Trim()
                .ToLowerInvariant();

            switch (modoNormalizado)
            {
                case "analizador":
                {
                    IActionResult? acceso = await ValidarPermisoAsync(
                        usuarioId,
                        DiagnosticoIAFlujo.InterfazAnalizador,
                        TipoPermisoApi.Leer,
                        cancellationToken);
                    if (acceso != null)
                        return acceso;
                    break;
                }
                case "aprobador":
                {
                    IActionResult? acceso = await ValidarPermisoAsync(
                        usuarioId,
                        DiagnosticoIAFlujo.InterfazAprobador,
                        TipoPermisoApi.Leer,
                        cancellationToken);
                    if (acceso != null)
                        return acceso;
                    break;
                }
                case "historial":
                {
                    bool puedeLeer = await TienePermisoAsync(
                        usuarioId,
                        DiagnosticoIAFlujo.InterfazSolicitud,
                        TipoPermisoApi.Leer,
                        cancellationToken) ||
                        await TienePermisoAsync(
                            usuarioId,
                            DiagnosticoIAFlujo.InterfazAnalizador,
                            TipoPermisoApi.Leer,
                            cancellationToken) ||
                        await TienePermisoAsync(
                            usuarioId,
                            DiagnosticoIAFlujo.InterfazAprobador,
                            TipoPermisoApi.Leer,
                            cancellationToken);

                    if (!puedeLeer)
                        return Forbid();
                    break;
                }
                default:
                {
                    IActionResult? acceso = await ValidarPermisoAsync(
                        usuarioId,
                        DiagnosticoIAFlujo.InterfazSolicitud,
                        TipoPermisoApi.Leer,
                        cancellationToken);
                    if (acceso != null)
                        return acceso;
                    break;
                }
            }

            IQueryable<DiagnosticoIA> query = diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .Where(item => item.Activo);

            if (modoNormalizado is not "analizador" and
                not "aprobador" and
                not "historial")
            {
                query = query.Where(item =>
                    item.UsuarioSolicitanteId == usuarioId.Value);
            }

            List<DiagnosticoIA> inspecciones = await query
                .Include(item => item.Imagenes)
                .OrderByDescending(item => item.FechaSolicitudUtc)
                .Take(300)
                .ToListAsync(cancellationToken);

            int[] ids = inspecciones
                .Select(item => item.DiagnosticoIAId)
                .ToArray();

            Dictionary<int, List<FotoMetadatos>> fotosPorInspeccion =
                await database.ObtenerFotosPorDiagnosticosAsync(
                    ids,
                    cancellationToken);

            Dictionary<int, InspeccionCierreMetadatos> cierres =
                await database.ObtenerCierresInspeccionesAsync(
                    ids,
                    cancellationToken);

            var data = new List<InspeccionFitosanitariaListaDto>();

            foreach (DiagnosticoIA inspeccion in inspecciones)
            {
                List<FotoMetadatos> metadatos =
                    fotosPorInspeccion.GetValueOrDefault(
                        inspeccion.DiagnosticoIAId) ?? [];

                InspeccionCierreMetadatos cierre =
                    cierres.GetValueOrDefault(
                        inspeccion.DiagnosticoIAId) ??
                    new InspeccionCierreMetadatos(false, null, null);

                string estado =
                    InspeccionFitosanitariaFlujo.CalcularEstadoInspeccion(
                        metadatos.Select(item => item.Estado),
                        cierre.CerradaTecnico);

                bool incluir = modoNormalizado switch
                {
                    "analizador" =>
                        cierre.CerradaTecnico &&
                        estado == InspeccionFitosanitariaFlujo
                            .InspeccionEstados.PendienteRevision,
                    "aprobador" =>
                        estado == InspeccionFitosanitariaFlujo
                            .InspeccionEstados.PendienteAprobacion,
                    "historial" =>
                        estado is
                            InspeccionFitosanitariaFlujo.InspeccionEstados.Finalizada or
                            InspeccionFitosanitariaFlujo.InspeccionEstados.FinalizadaParcialmente,
                    _ => true
                };

                if (!incluir)
                    continue;

                data.Add(new InspeccionFitosanitariaListaDto
                {
                    InspeccionId = inspeccion.DiagnosticoIAId,
                    CodigoTerreno = inspeccion.CodigoTerreno,
                    FechaRegistroSistemaUtc = inspeccion.FechaSolicitudUtc,
                    Estado = estado,
                    CerradaTecnico = cierre.CerradaTecnico,
                    FechaCierreTecnicoUtc = cierre.FechaCierreTecnicoUtc,
                    TotalFotografias = metadatos.Count,
                    Pendientes = metadatos.Count(item =>
                        !InspeccionFitosanitariaFlujo.EsEstadoFinal(item.Estado) &&
                        item.Estado != InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA),
                    ConError = metadatos.Count(item =>
                        item.Estado == InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA),
                    Finalizadas = metadatos.Count(item =>
                        InspeccionFitosanitariaFlujo.EsEstadoFinal(item.Estado)),
                    UrlMiniatura = inspeccion.Imagenes
                        .OrderBy(item => item.Orden)
                        .Select(item => item.UrlImagen)
                        .FirstOrDefault() ?? string.Empty
                });
            }

            return Ok(new
            {
                success = true,
                message = "Inspecciones obtenidas correctamente.",
                data
            });
        }

        [HttpGet("catalogo-album")]
        public async Task<IActionResult> ObtenerCatalogoAlbum(
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAlbum,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            List<CategoriaAlbumBotanicoReferencia> categorias =
                await diagnosticoDb.CategoriasAlbum
                    .AsNoTracking()
                    .Where(item => item.Activo)
                    .OrderBy(item => item.NombreCategoria)
                    .ToListAsync(cancellationToken);

            List<AlbumBotanicoCafeReferencia> fichas =
                await diagnosticoDb.RegistrosAlbum
                    .AsNoTracking()
                    .Where(item => item.Activo)
                    .OrderBy(item => item.Titulo)
                    .ToListAsync(cancellationToken);

            List<InspeccionAlbumCategoriaDto> data = categorias
                .Select(categoria => new InspeccionAlbumCategoriaDto
                {
                    CategoriaAlbumBotanicoId =
                        categoria.CategoriaAlbumBotanicoId,
                    Nombre = categoria.NombreCategoria,
                    Fichas = fichas
                        .Where(ficha => ficha.CategoriaAlbumBotanicoId ==
                            categoria.CategoriaAlbumBotanicoId)
                        .Select(ficha => new InspeccionAlbumFichaDto
                        {
                            AlbumBotanicoCafeId = ficha.AlbumBotanicoCafeId,
                            CategoriaAlbumBotanicoId =
                                ficha.CategoriaAlbumBotanicoId,
                            Titulo = ficha.Titulo,
                            NombreCientifico =
                                ficha.NombreCientifico ?? string.Empty
                        })
                        .ToList()
                })
                .Where(item => item.Fichas.Count > 0)
                .ToList();

            return Ok(new
            {
                success = true,
                message = "Catálogo activo del álbum obtenido correctamente.",
                data
            });
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> ObtenerDetalle(
            int id,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            DiagnosticoIA? inspeccion = await diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.DiagnosticoIAId == id && item.Activo,
                    cancellationToken);

            if (inspeccion == null)
                return NoEncontrado();

            bool permitido = inspeccion.UsuarioSolicitanteId == usuarioId ||
                await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAnalizador,
                    TipoPermisoApi.Leer,
                    cancellationToken) ||
                await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAprobador,
                    TipoPermisoApi.Leer,
                    cancellationToken) ||
                await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAlbum,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            if (!permitido)
                return Forbid();

            return Ok(new
            {
                success = true,
                message = "Inspección obtenida correctamente.",
                data = await CrearDetalleAsync(
                    id,
                    usuarioId.Value,
                    cancellationToken)
            });
        }

        [HttpPost("{id:int}/procesar-fotografias")]
        public async Task<IActionResult> ProcesarFotografias(
            int id,
            [FromBody] InspeccionFotosSeleccionadasRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            DiagnosticoIA? inspeccion = await CargarInspeccionAsync(
                id,
                cancellationToken);

            IActionResult? acceso = await ValidarAccesoProcesamientoAsync(
                inspeccion,
                usuarioId,
                cancellationToken);

            if (acceso != null)
                return acceso;

            IActionResult? cierreInvalido = await ValidarInspeccionAbiertaAsync(
                id,
                cancellationToken);

            if (cierreInvalido != null)
                return cierreInvalido;

            InspeccionOperacionMasivaDto data =
                await ProcesarSeleccionAsync(
                    inspeccion!,
                    request.FotografiaIds,
                    usuarioId!.Value,
                    tipoRevision: "ANALISIS_INICIAL",
                    retroalimentacion: string.Empty,
                    diagnosticoPropuesto: string.Empty,
                    cancellationToken);

            return Ok(new
            {
                success = data.TotalExitosas > 0,
                message = CrearMensajeOperacion(data, "analizadas"),
                data
            });
        }

        [HttpPost("{id:int}/solicitar-revision-ia")]
        public async Task<IActionResult> SolicitarRevisionIA(
            int id,
            [FromBody] InspeccionFotosRevisionIARequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            DiagnosticoIA? inspeccion = await CargarInspeccionAsync(
                id,
                cancellationToken);

            if (inspeccion == null)
                return NoEncontrado();

            bool esPropietario = inspeccion.UsuarioSolicitanteId == usuarioId &&
                await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazSolicitud,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            bool puedeAnalizar = await TienePermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAnalizador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (!esPropietario && !puedeAnalizar)
                return Forbid();

            await database.InicializarAsync(cancellationToken);
            InspeccionCierreMetadatos cierreRevision =
                await database.ObtenerCierreInspeccionAsync(
                    id,
                    cancellationToken);

            if (!cierreRevision.CerradaTecnico && !esPropietario)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La inspección todavía está abierta y no se encuentra disponible para el analizador."
                });
            }

            if (cierreRevision.CerradaTecnico && !puedeAnalizar)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La inspección ya fue cerrada. Las revisiones posteriores corresponden al analizador."
                });
            }

            InspeccionOperacionMasivaDto data =
                await ProcesarSeleccionAsync(
                    inspeccion,
                    request.FotografiaIds,
                    usuarioId!.Value,
                    tipoRevision: "REVISION_SOLICITADA",
                    retroalimentacion: request.Retroalimentacion,
                    diagnosticoPropuesto: request.DiagnosticoPropuesto ?? string.Empty,
                    cancellationToken);

            return Ok(new
            {
                success = data.TotalExitosas > 0,
                message = CrearMensajeOperacion(data, "reevaluadas"),
                data
            });
        }

        [HttpPost("{id:int}/enviar-analizador")]
        public async Task<IActionResult> EnviarAnalizador(
            int id,
            [FromBody] InspeccionFotosSeleccionadasRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            DiagnosticoIA? inspeccion = await CargarInspeccionAsync(
                id,
                cancellationToken);

            if (inspeccion == null)
                return NoEncontrado();

            if (inspeccion.UsuarioSolicitanteId != usuarioId)
                return Forbid();

            IActionResult? permiso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazSolicitud,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (permiso != null)
                return permiso;

            IActionResult? cierreInvalido = await ValidarInspeccionAbiertaAsync(
                id,
                cancellationToken);

            if (cierreInvalido != null)
                return cierreInvalido;

            InspeccionOperacionMasivaDto data = await EjecutarSobreSeleccionAsync(
                inspeccion,
                request.FotografiaIds,
                async (imagen, meta) =>
                {
                    if (meta.Estado !=
                        InspeccionFitosanitariaFlujo.FotoEstados.PendienteDecisionTecnico)
                    {
                        throw new InvalidOperationException(
                            "La fotografía no está pendiente de la decisión del técnico.");
                    }

                    await database.CambiarEstadoFotoAsync(
                        imagen.DiagnosticoIAImagenId,
                        usuarioId!.Value,
                        InspeccionFitosanitariaFlujo.FotoEstados.PendienteAnalizador,
                        InspeccionFitosanitariaFlujo.Acciones.TecnicoEnviaAnalizador,
                        "El técnico marcó la fotografía como lista para el analizador. Será visible después de cerrar la inspección.",
                        cancellationToken: cancellationToken);

                    return InspeccionFitosanitariaFlujo.FotoEstados.PendienteAnalizador;
                },
                cancellationToken);

            await ActualizarEstadoInspeccionAsync(inspeccion, cancellationToken);

            return Ok(new
            {
                success = data.TotalExitosas > 0,
                message = CrearMensajeOperacion(data, "preparadas para revisión"),
                data
            });
        }

        [HttpPost("{id:int}/descartar-fotografias")]
        public async Task<IActionResult> DescartarFotografias(
            int id,
            [FromBody] InspeccionFotosDescarteRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            DiagnosticoIA? inspeccion = await CargarInspeccionAsync(
                id,
                cancellationToken);

            if (inspeccion == null)
                return NoEncontrado();

            bool esPropietario = inspeccion.UsuarioSolicitanteId == usuarioId &&
                await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazSolicitud,
                    TipoPermisoApi.Eliminar,
                    cancellationToken);

            if (!esPropietario)
                return Forbid();

            IActionResult? cierreInvalido = await ValidarInspeccionAbiertaAsync(
                id,
                cancellationToken);

            if (cierreInvalido != null)
                return cierreInvalido;

            InspeccionOperacionMasivaDto data = await EjecutarSobreSeleccionAsync(
                inspeccion,
                request.FotografiaIds,
                async (imagen, meta) =>
                {
                    if (meta.Estado ==
                        InspeccionFitosanitariaFlujo.FotoEstados.PublicadaAlbum)
                    {
                        throw new InvalidOperationException(
                            "Una fotografía publicada en el álbum no puede descartarse.");
                    }

                    await database.DescartarFotoAsync(
                        imagen.DiagnosticoIAImagenId,
                        usuarioId!.Value,
                        request.Motivo,
                        cancellationToken);

                    return InspeccionFitosanitariaFlujo.FotoEstados.Descartada;
                },
                cancellationToken);

            await ActualizarEstadoInspeccionAsync(inspeccion, cancellationToken);

            return Ok(new
            {
                success = data.TotalExitosas > 0,
                message = CrearMensajeOperacion(data, "descartadas lógicamente"),
                data
            });
        }

        [HttpPost("{id:int}/cerrar-tecnico")]
        public async Task<IActionResult> CerrarInspeccionTecnico(
            int id,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            if (!usuarioId.HasValue)
                return Forbid();

            await database.InicializarAsync(cancellationToken);

            DiagnosticoIA? inspeccion = await CargarInspeccionAsync(
                id,
                cancellationToken);

            if (inspeccion == null)
                return NoEncontrado();

            if (inspeccion.UsuarioSolicitanteId != usuarioId.Value)
                return Forbid();

            IActionResult? permiso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazSolicitud,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (permiso != null)
                return permiso;

            InspeccionCierreMetadatos cierre =
                await database.ObtenerCierreInspeccionAsync(
                    id,
                    cancellationToken);

            if (cierre.CerradaTecnico)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La inspección ya fue cerrada por el técnico."
                });
            }

            List<FotoMetadatos> fotos = await database.ObtenerFotosAsync(
                id,
                cancellationToken);

            if (!InspeccionFitosanitariaFlujo.PuedeCerrarInspeccion(
                    fotos.Where(item => item.Activo)
                        .Select(item => item.Estado)))
            {
                return BadRequest(new
                {
                    success = false,
                    message = ObtenerMotivoNoPuedeCerrar(fotos)
                });
            }

            await database.CerrarInspeccionAsync(
                id,
                usuarioId.Value,
                cancellationToken);

            string estadoAnterior = inspeccion.Estado;
            inspeccion.Estado =
                InspeccionFitosanitariaFlujo.InspeccionEstados.PendienteRevision;

            inspeccion.Historial.Add(new DiagnosticoIAHistorial
            {
                UsuarioId = usuarioId.Value,
                EstadoAnterior = Limitar(estadoAnterior, 40),
                EstadoNuevo = Limitar(inspeccion.Estado, 40),
                Accion =
                    InspeccionFitosanitariaFlujo.Acciones.TecnicoCierraInspeccion,
                Detalle =
                    "El técnico cerró la inspección. Desde este momento las fotografías listas quedan visibles para el analizador humano.",
                FechaUtc = DateTime.UtcNow
            });

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "La inspección fue cerrada y enviada a la bandeja del analizador.",
                data = await CrearDetalleAsync(
                    id,
                    usuarioId.Value,
                    cancellationToken)
            });
        }

        [HttpPost("{id:int}/analisis-humano")]
        public async Task<IActionResult> GuardarAnalisisHumano(
            int id,
            [FromBody] InspeccionFotosAnalisisHumanoRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAnalizador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DiagnosticoIA? inspeccion = await CargarInspeccionAsync(
                id,
                cancellationToken);

            if (inspeccion == null)
                return NoEncontrado();

            await database.InicializarAsync(cancellationToken);

            InspeccionCierreMetadatos cierre =
                await database.ObtenerCierreInspeccionAsync(
                    id,
                    cancellationToken);

            if (!cierre.CerradaTecnico)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La inspección todavía no ha sido cerrada por el técnico."
                });
            }

            var data = new InspeccionOperacionMasivaDto
            {
                TotalSolicitadas = request.Fotografias.Count
            };

            foreach (InspeccionFotoAnalisisHumanoItemRequest item
                     in request.Fotografias
                         .GroupBy(value => value.FotografiaId)
                         .Select(group => group.Last()))
            {
                try
                {
                    DiagnosticoIAImagen? imagen = inspeccion.Imagenes
                        .FirstOrDefault(value =>
                            value.DiagnosticoIAImagenId == item.FotografiaId);

                    if (imagen == null)
                        throw new InvalidOperationException(
                            "La fotografía no pertenece a la inspección.");

                    FotoMetadatos? meta = await database.ObtenerFotoAsync(
                        item.FotografiaId,
                        cancellationToken);

                    if (meta == null || meta.Descartada || !meta.Activo)
                        throw new InvalidOperationException(
                            "La fotografía no se encuentra disponible.");

                    if (meta.Estado is not
                        (InspeccionFitosanitariaFlujo.FotoEstados.PendienteAnalizador or
                         InspeccionFitosanitariaFlujo.FotoEstados.EnAnalisisHumano or
                         InspeccionFitosanitariaFlujo.FotoEstados.DevueltaAnalizador))
                    {
                        throw new InvalidOperationException(
                            "La fotografía no está disponible para análisis humano.");
                    }

                    if (string.IsNullOrWhiteSpace(item.Diagnostico))
                        throw new InvalidOperationException(
                            "El diagnóstico humano es obligatorio.");

                    await database.GuardarAnalisisHumanoAsync(
                        item.FotografiaId,
                        usuarioId!.Value,
                        item.CalidadEvaluacion,
                        item.EstadoGeneral,
                        item.CategoriaPrincipal,
                        SerializarLista(item.CategoriasSecundarias),
                        item.Diagnostico,
                        item.TipoDiagnostico,
                        item.Severidad,
                        item.NivelCerteza,
                        item.Observaciones,
                        request.EnviarAprobacion,
                        cancellationToken);

                    string estadoNuevo = request.EnviarAprobacion
                        ? InspeccionFitosanitariaFlujo.FotoEstados.PendienteAprobacion
                        : InspeccionFitosanitariaFlujo.FotoEstados.EnAnalisisHumano;

                    await database.CambiarEstadoFotoAsync(
                        item.FotografiaId,
                        usuarioId.Value,
                        estadoNuevo,
                        request.EnviarAprobacion
                            ? InspeccionFitosanitariaFlujo.Acciones.AnalisisHumanoEnviado
                            : InspeccionFitosanitariaFlujo.Acciones.AnalisisHumanoGuardado,
                        request.EnviarAprobacion
                            ? "El análisis humano fue guardado y enviado al aprobador."
                            : "El análisis humano fue guardado como borrador.",
                        fechaAnalisisHumanoUtc: DateTime.UtcNow,
                        cancellationToken: cancellationToken);

                    data.Resultados.Add(new InspeccionOperacionItemDto
                    {
                        FotografiaId = item.FotografiaId,
                        Exitoso = true,
                        Estado = estadoNuevo,
                        Mensaje = "Análisis humano guardado."
                    });
                    data.TotalExitosas++;
                }
                catch (Exception ex)
                {
                    data.Resultados.Add(new InspeccionOperacionItemDto
                    {
                        FotografiaId = item.FotografiaId,
                        Exitoso = false,
                        Mensaje = ex.Message
                    });
                    data.TotalConError++;
                }
            }

            await ActualizarEstadoInspeccionAsync(inspeccion, cancellationToken);

            return Ok(new
            {
                success = data.TotalExitosas > 0,
                message = CrearMensajeOperacion(
                    data,
                    request.EnviarAprobacion
                        ? "enviadas al aprobador"
                        : "clasificadas por el analizador"),
                data
            });
        }

        [HttpPost("{id:int}/aprobaciones")]
        public async Task<IActionResult> RegistrarAprobaciones(
            int id,
            [FromBody] InspeccionFotosAprobacionRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DiagnosticoIA? inspeccion = await CargarInspeccionAsync(
                id,
                cancellationToken);

            if (inspeccion == null)
                return NoEncontrado();

            await database.InicializarAsync(cancellationToken);

            InspeccionCierreMetadatos cierre =
                await database.ObtenerCierreInspeccionAsync(
                    id,
                    cancellationToken);

            if (!cierre.CerradaTecnico)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La inspección todavía no ha sido cerrada por el técnico."
                });
            }

            var data = new InspeccionOperacionMasivaDto
            {
                TotalSolicitadas = request.Fotografias.Count
            };

            foreach (InspeccionFotoAprobacionItemRequest item
                     in request.Fotografias
                         .GroupBy(value => value.FotografiaId)
                         .Select(group => group.Last()))
            {
                try
                {
                    if (!InspeccionFitosanitariaFlujo.DecisionesAprobacion.Todas
                        .Contains(item.Decision ?? string.Empty))
                    {
                        throw new InvalidOperationException(
                            "La decisión de aprobación no es válida.");
                    }

                    DiagnosticoIAImagen? imagen = inspeccion.Imagenes
                        .FirstOrDefault(value =>
                            value.DiagnosticoIAImagenId == item.FotografiaId);

                    if (imagen == null)
                        throw new InvalidOperationException(
                            "La fotografía no pertenece a la inspección.");

                    FotoMetadatos? meta = await database.ObtenerFotoAsync(
                        item.FotografiaId,
                        cancellationToken);

                    if (meta == null || meta.Estado !=
                        InspeccionFitosanitariaFlujo.FotoEstados.PendienteAprobacion)
                    {
                        throw new InvalidOperationException(
                            "La fotografía no está pendiente de aprobación.");
                    }

                    AnalisisHumanoRegistro? analisis =
                        await database.ObtenerUltimoAnalisisHumanoAsync(
                            item.FotografiaId,
                            cancellationToken);

                    if (analisis == null ||
                        !string.Equals(
                            analisis.EstadoRegistro,
                            "ENVIADO",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "No existe un análisis humano enviado para esta fotografía.");
                    }

                    string decision = item.Decision.Trim().ToUpperInvariant();
                    string estadoNuevo = decision switch
                    {
                        InspeccionFitosanitariaFlujo.DecisionesAprobacion.Aprobar =>
                            InspeccionFitosanitariaFlujo.FotoEstados.Aprobada,
                        InspeccionFitosanitariaFlujo.DecisionesAprobacion.AprobarConCorreccion =>
                            InspeccionFitosanitariaFlujo.FotoEstados.AprobadaConCorreccion,
                        InspeccionFitosanitariaFlujo.DecisionesAprobacion.Devolver =>
                            InspeccionFitosanitariaFlujo.FotoEstados.DevueltaAnalizador,
                        InspeccionFitosanitariaFlujo.DecisionesAprobacion.Rechazar =>
                            InspeccionFitosanitariaFlujo.FotoEstados.Rechazada,
                        _ => InspeccionFitosanitariaFlujo.FotoEstados.NoConcluyente
                    };

                    bool decisionPositiva = estadoNuevo is
                        InspeccionFitosanitariaFlujo.FotoEstados.Aprobada or
                        InspeccionFitosanitariaFlujo.FotoEstados.AprobadaConCorreccion;

                    await database.RegistrarAprobacionAsync(
                        item.FotografiaId,
                        analisis.AnalisisHumanoId,
                        usuarioId!.Value,
                        decision,
                        ValorOAnterior(
                            item.CalidadEvaluacionFinal,
                            analisis.CalidadEvaluacion),
                        ValorOAnterior(
                            item.EstadoGeneralFinal,
                            analisis.EstadoGeneral),
                        ValorOAnterior(
                            item.CategoriaPrincipalFinal,
                            analisis.CategoriaPrincipal),
                        item.CategoriasSecundariasFinales.Count > 0
                            ? SerializarLista(item.CategoriasSecundariasFinales)
                            : analisis.CategoriasSecundariasJson,
                        ValorOAnterior(
                            item.DiagnosticoFinal,
                            analisis.Diagnostico),
                        ValorOAnterior(
                            item.TipoDiagnosticoFinal,
                            analisis.TipoDiagnostico),
                        ValorOAnterior(
                            item.SeveridadFinal,
                            analisis.Severidad),
                        ValorOAnterior(
                            item.NivelCertezaFinal,
                            analisis.NivelCerteza),
                        item.Observaciones,
                        decisionPositiva && item.AutorizaPublicacionAlbum,
                        analisis.UsuarioAnalizadorId == usuarioId.Value,
                        cancellationToken);

                    await database.CambiarEstadoFotoAsync(
                        item.FotografiaId,
                        usuarioId.Value,
                        estadoNuevo,
                        InspeccionFitosanitariaFlujo.Acciones.AprobacionRegistrada,
                        $"El aprobador registró la decisión {decision}.",
                        fechaAprobacionUtc: DateTime.UtcNow,
                        cancellationToken: cancellationToken);

                    data.Resultados.Add(new InspeccionOperacionItemDto
                    {
                        FotografiaId = item.FotografiaId,
                        Exitoso = true,
                        Estado = estadoNuevo,
                        Mensaje = "Decisión registrada correctamente."
                    });
                    data.TotalExitosas++;
                }
                catch (Exception ex)
                {
                    data.Resultados.Add(new InspeccionOperacionItemDto
                    {
                        FotografiaId = item.FotografiaId,
                        Exitoso = false,
                        Mensaje = ex.Message
                    });
                    data.TotalConError++;
                }
            }

            await ActualizarEstadoInspeccionAsync(inspeccion, cancellationToken);

            return Ok(new
            {
                success = data.TotalExitosas > 0,
                message = CrearMensajeOperacion(data, "evaluadas por el aprobador"),
                data
            });
        }

        [HttpPost("{id:int}/fotografias/{fotografiaId:int}/publicar-album")]
        public async Task<IActionResult> PublicarAlbum(
            int id,
            int fotografiaId,
            [FromBody] InspeccionFotoPublicarAlbumRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAlbum,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DiagnosticoIA? inspeccion = await CargarInspeccionAsync(
                id,
                cancellationToken);

            if (inspeccion == null)
                return NoEncontrado();

            DiagnosticoIAImagen? imagen = inspeccion.Imagenes
                .FirstOrDefault(item =>
                    item.DiagnosticoIAImagenId == fotografiaId);

            if (imagen == null)
                return BadRequest(new
                {
                    success = false,
                    message = "La fotografía no pertenece a la inspección."
                });

            FotoMetadatos? meta = await database.ObtenerFotoAsync(
                fotografiaId,
                cancellationToken);

            if (meta == null || meta.Estado is not
                (InspeccionFitosanitariaFlujo.FotoEstados.Aprobada or
                 InspeccionFitosanitariaFlujo.FotoEstados.AprobadaConCorreccion))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Solo se pueden publicar fotografías aprobadas individualmente."
                });
            }

            AprobacionRegistro? aprobacion =
                await database.ObtenerUltimaAprobacionAsync(
                    fotografiaId,
                    cancellationToken);

            if (aprobacion == null || !aprobacion.AutorizaPublicacionAlbum)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "El aprobador no autorizó la publicación de esta fotografía."
                });
            }

            bool yaPublicada = await diagnosticoDb.PublicacionesAlbum
                .AsNoTracking()
                .AnyAsync(item =>
                    item.DiagnosticoIAImagenId == fotografiaId &&
                    item.Activo,
                    cancellationToken);

            if (yaPublicada)
            {
                return Conflict(new
                {
                    success = false,
                    message = "La fotografía ya fue publicada en el álbum."
                });
            }

            AlbumBotanicoCafeReferencia? ficha =
                await diagnosticoDb.RegistrosAlbum
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item =>
                        item.AlbumBotanicoCafeId == request.AlbumBotanicoCafeId &&
                        item.CategoriaAlbumBotanicoId == request.CategoriaAlbumBotanicoId &&
                        item.Activo,
                        cancellationToken);

            if (ficha == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La ficha seleccionada no existe, no está activa o no pertenece a la categoría indicada."
                });
            }

            int orden = request.Orden > 0
                ? request.Orden
                : (await diagnosticoDb.FotosAlbum
                    .Where(item =>
                        item.AlbumBotanicoCafeId == request.AlbumBotanicoCafeId)
                    .Select(item => (int?)item.Orden)
                    .MaxAsync(cancellationToken) ?? 0) + 1;

            var fotoAlbum = new AlbumBotanicoCafeFotoReferencia
            {
                AlbumBotanicoCafeId = request.AlbumBotanicoCafeId,
                RutaFoto = imagen.RutaRelativa,
                DescripcionFoto = Limitar(request.Descripcion, 500),
                EsPortada = request.EsPortada,
                Orden = orden,
                Activo = true
            };

            diagnosticoDb.FotosAlbum.Add(fotoAlbum);
            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            diagnosticoDb.PublicacionesAlbum.Add(
                new DiagnosticoIAAlbumPublicacion
                {
                    DiagnosticoIAId = id,
                    DiagnosticoIAImagenId = fotografiaId,
                    CategoriaAlbumBotanicoId = request.CategoriaAlbumBotanicoId,
                    AlbumBotanicoCafeId = request.AlbumBotanicoCafeId,
                    AlbumBotanicoCafeFotoId = fotoAlbum.AlbumBotanicoCafeFotoId,
                    UsuarioPublicacionId = usuarioId!.Value,
                    FechaPublicacionUtc = DateTime.UtcNow,
                    DescripcionPublicacion = Limitar(request.Descripcion, 1000),
                    ClasificacionFinal = Limitar(ficha.Titulo, 50),
                    DiagnosticoFinal = Limitar(aprobacion.DiagnosticoFinal, 300),
                    RutaFotoAlbum = Limitar(imagen.RutaRelativa, 600),
                    Activo = true
                });

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            await database.CambiarEstadoFotoAsync(
                fotografiaId,
                usuarioId.Value,
                InspeccionFitosanitariaFlujo.FotoEstados.PublicadaAlbum,
                InspeccionFitosanitariaFlujo.Acciones.FotoPublicadaAlbum,
                $"La fotografía fue vinculada con la ficha {ficha.Titulo}.",
                cancellationToken: cancellationToken);

            await ActualizarEstadoInspeccionAsync(inspeccion, cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "La fotografía aprobada fue publicada en el álbum sin eliminar ni mover la evidencia original.",
                data = new
                {
                    fotografiaId,
                    albumBotanicoCafeFotoId =
                        fotoAlbum.AlbumBotanicoCafeFotoId,
                    estado =
                        InspeccionFitosanitariaFlujo.FotoEstados.PublicadaAlbum
                }
            });
        }

        private async Task<InspeccionOperacionMasivaDto> ProcesarSeleccionAsync(
            DiagnosticoIA inspeccion,
            IReadOnlyCollection<int> fotografiaIds,
            int usuarioId,
            string tipoRevision,
            string retroalimentacion,
            string diagnosticoPropuesto,
            CancellationToken cancellationToken)
        {
            ProveedorIAClienteService proveedorService =
                CrearProveedorService();

            ProveedorEjecucion proveedor =
                await proveedorService.ObtenerEjecucionAsync(
                    cancellationToken);

            var data = new InspeccionOperacionMasivaDto
            {
                TotalSolicitadas = fotografiaIds.Distinct().Count()
            };

            foreach (int fotografiaId in fotografiaIds.Distinct())
            {
                int? revisionId = null;

                try
                {
                    DiagnosticoIAImagen? imagen = inspeccion.Imagenes
                        .FirstOrDefault(item =>
                            item.DiagnosticoIAImagenId == fotografiaId);

                    if (imagen == null)
                        throw new InvalidOperationException(
                            "La fotografía no pertenece a la inspección.");

                    FotoMetadatos? meta = await database.ObtenerFotoAsync(
                        fotografiaId,
                        cancellationToken);

                    if (meta == null || !meta.Activo || meta.Descartada)
                        throw new InvalidOperationException(
                            "La fotografía no se encuentra disponible para análisis.");

                    if (InspeccionFitosanitariaFlujo.EsEstadoFinal(meta.Estado) &&
                        meta.Estado !=
                            InspeccionFitosanitariaFlujo.FotoEstados.NoConcluyente)
                    {
                        throw new InvalidOperationException(
                            "La fotografía ya tiene una decisión final y no puede reprocesarse.");
                    }

                    await database.CambiarEstadoFotoAsync(
                        fotografiaId,
                        usuarioId,
                        InspeccionFitosanitariaFlujo.FotoEstados.AnalizandoIA,
                        InspeccionFitosanitariaFlujo.Acciones.AnalisisIAIniciado,
                        string.IsNullOrWhiteSpace(retroalimentacion)
                            ? "Se inició el análisis preliminar de la fotografía."
                            : "Se inició una reevaluación individual solicitada por un usuario.",
                        error: string.Empty,
                        modeloIA: proveedor.ModeloPrincipal,
                        incrementarIntento: true,
                        cancellationToken: cancellationToken);

                    revisionId = await database.CrearRevisionIAAsync(
                        fotografiaId,
                        usuarioId,
                        tipoRevision,
                        retroalimentacion,
                        diagnosticoPropuesto,
                        proveedor.Proveedor,
                        proveedor.ModeloPrincipal,
                        cancellationToken);

                    ProveedorIAResultadoFoto resultado =
                        await proveedorService.AnalizarFotoAsync(
                            imagen,
                            inspeccion.ObservacionUsuario,
                            retroalimentacion,
                            diagnosticoPropuesto,
                            cancellationToken);

                    AplicarResultadoIA(imagen, resultado);
                    await diagnosticoDb.SaveChangesAsync(cancellationToken);

                    DateTime fecha = DateTime.UtcNow;

                    await database.CompletarRevisionIAAsync(
                        revisionId.Value,
                        "COMPLETADA",
                        resultado.RespuestaOriginalJson,
                        string.Empty,
                        cancellationToken);

                    await database.CambiarEstadoFotoAsync(
                        fotografiaId,
                        usuarioId,
                        InspeccionFitosanitariaFlujo.FotoEstados.PendienteDecisionTecnico,
                        InspeccionFitosanitariaFlujo.Acciones.AnalisisIACompletado,
                        "La IA terminó el análisis preliminar. El técnico debe decidir cómo continuar.",
                        fechaAnalisisIAUtc: fecha,
                        error: string.Empty,
                        modeloIA: resultado.Modelo,
                        cancellationToken: cancellationToken);

                    data.Resultados.Add(new InspeccionOperacionItemDto
                    {
                        FotografiaId = fotografiaId,
                        Exitoso = true,
                        Estado =
                            InspeccionFitosanitariaFlujo.FotoEstados.PendienteDecisionTecnico,
                        Mensaje = resultado.DiagnosticoProbable
                    });
                    data.TotalExitosas++;
                }
                catch (Exception ex)
                {
                    string mensaje = CrearMensajeErrorIA(ex);

                    if (revisionId.HasValue)
                    {
                        await database.CompletarRevisionIAAsync(
                            revisionId.Value,
                            "ERROR",
                            string.Empty,
                            mensaje,
                            CancellationToken.None);
                    }

                    try
                    {
                        await database.CambiarEstadoFotoAsync(
                            fotografiaId,
                            usuarioId,
                            InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA,
                            InspeccionFitosanitariaFlujo.Acciones.AnalisisIAError,
                            mensaje,
                            error: mensaje,
                            modeloIA: proveedor.ModeloPrincipal,
                            cancellationToken: CancellationToken.None);
                    }
                    catch (Exception metadataEx)
                    {
                        logger.LogError(
                            metadataEx,
                            "No fue posible registrar el error de la fotografía {FotografiaId}.",
                            fotografiaId);
                    }

                    data.Resultados.Add(new InspeccionOperacionItemDto
                    {
                        FotografiaId = fotografiaId,
                        Exitoso = false,
                        Estado = InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA,
                        Mensaje = mensaje
                    });
                    data.TotalConError++;
                }

                await ActualizarEstadoInspeccionAsync(
                    inspeccion,
                    CancellationToken.None);
            }

            await ActualizarResumenInspeccionAsync(
                inspeccion,
                CancellationToken.None);

            return data;
        }

        private async Task<InspeccionOperacionMasivaDto>
            EjecutarSobreSeleccionAsync(
                DiagnosticoIA inspeccion,
                IReadOnlyCollection<int> fotografiaIds,
                Func<DiagnosticoIAImagen, FotoMetadatos, Task<string>> accion,
                CancellationToken cancellationToken)
        {
            var data = new InspeccionOperacionMasivaDto
            {
                TotalSolicitadas = fotografiaIds.Distinct().Count()
            };

            foreach (int fotografiaId in fotografiaIds.Distinct())
            {
                try
                {
                    DiagnosticoIAImagen? imagen = inspeccion.Imagenes
                        .FirstOrDefault(item =>
                            item.DiagnosticoIAImagenId == fotografiaId);

                    if (imagen == null)
                        throw new InvalidOperationException(
                            "La fotografía no pertenece a la inspección.");

                    FotoMetadatos? meta = await database.ObtenerFotoAsync(
                        fotografiaId,
                        cancellationToken);

                    if (meta == null)
                        throw new InvalidOperationException(
                            "No se encontró el expediente de la fotografía.");

                    string estado = await accion(imagen, meta);

                    data.Resultados.Add(new InspeccionOperacionItemDto
                    {
                        FotografiaId = fotografiaId,
                        Exitoso = true,
                        Estado = estado,
                        Mensaje = "Operación completada."
                    });
                    data.TotalExitosas++;
                }
                catch (Exception ex)
                {
                    data.Resultados.Add(new InspeccionOperacionItemDto
                    {
                        FotografiaId = fotografiaId,
                        Exitoso = false,
                        Mensaje = ex.Message
                    });
                    data.TotalConError++;
                }
            }

            return data;
        }

        private async Task<InspeccionFitosanitariaDetalleDto> CrearDetalleAsync(
            int inspeccionId,
            int usuarioActualId,
            CancellationToken cancellationToken)
        {
            await database.InicializarAsync(cancellationToken);

            DiagnosticoIA inspeccion = await diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.ResultadoIA)
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.PublicacionesAlbum)
                .FirstAsync(item =>
                    item.DiagnosticoIAId == inspeccionId,
                    cancellationToken);

            List<FotoMetadatos> metadatos =
                await database.ObtenerFotosAsync(
                    inspeccionId,
                    cancellationToken);

            InspeccionCierreMetadatos cierre =
                await database.ObtenerCierreInspeccionAsync(
                    inspeccionId,
                    cancellationToken);

            Dictionary<int, AnalisisHumanoRegistro> humanos =
                await database.ObtenerUltimosAnalisisHumanosAsync(
                    inspeccionId,
                    cancellationToken);
            Dictionary<int, AprobacionRegistro> aprobaciones =
                await database.ObtenerUltimasAprobacionesAsync(
                    inspeccionId,
                    cancellationToken);
            Dictionary<int, List<HistorialFotoRegistro>> historiales =
                await database.ObtenerHistorialInspeccionAsync(
                    inspeccionId,
                    cancellationToken);

            var usuariosIds = new HashSet<int>
            {
                inspeccion.UsuarioSolicitanteId
            };

            foreach (AnalisisHumanoRegistro humano in humanos.Values)
                usuariosIds.Add(humano.UsuarioAnalizadorId);

            foreach (AprobacionRegistro aprobacion in aprobaciones.Values)
                usuariosIds.Add(aprobacion.UsuarioAprobadorId);

            foreach (HistorialFotoRegistro item in
                     historiales.Values.SelectMany(lista => lista))
            {
                usuariosIds.Add(item.UsuarioId);
            }

            Dictionary<int, string> usuarios = await db.Usuarios
                .AsNoTracking()
                .Where(item => usuariosIds.Contains(item.UsuarioId))
                .ToDictionaryAsync(
                    item => item.UsuarioId,
                    item => item.nombreCompletoUsuario,
                    cancellationToken);

            bool tienePermisoGestionar =
                inspeccion.UsuarioSolicitanteId == usuarioActualId &&
                await TienePermisoAsync(
                    usuarioActualId,
                    DiagnosticoIAFlujo.InterfazSolicitud,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            bool puedeGestionar =
                tienePermisoGestionar && !cierre.CerradaTecnico;

            bool puedeCerrar =
                puedeGestionar &&
                InspeccionFitosanitariaFlujo.PuedeCerrarInspeccion(
                    metadatos.Where(item => item.Activo)
                        .Select(item => item.Estado));

            bool puedeAnalizar = cierre.CerradaTecnico &&
                await TienePermisoAsync(
                    usuarioActualId,
                    DiagnosticoIAFlujo.InterfazAnalizador,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            bool puedeAprobar = cierre.CerradaTecnico &&
                await TienePermisoAsync(
                    usuarioActualId,
                    DiagnosticoIAFlujo.InterfazAprobador,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            bool puedePublicar = await TienePermisoAsync(
                usuarioActualId,
                DiagnosticoIAFlujo.InterfazAlbum,
                TipoPermisoApi.Agregar,
                cancellationToken);

            return new InspeccionFitosanitariaDetalleDto
            {
                InspeccionId = inspeccion.DiagnosticoIAId,
                TerrenoId = inspeccion.TerrenoId,
                CodigoTerreno = inspeccion.CodigoTerreno,
                UsuarioSolicitanteId = inspeccion.UsuarioSolicitanteId,
                UsuarioSolicitante = usuarios.GetValueOrDefault(
                    inspeccion.UsuarioSolicitanteId,
                    $"Usuario {inspeccion.UsuarioSolicitanteId}"),
                Observacion = inspeccion.ObservacionUsuario,
                Estado = InspeccionFitosanitariaFlujo.CalcularEstadoInspeccion(
                    metadatos.Select(item => item.Estado),
                    cierre.CerradaTecnico),
                FechaRegistroSistemaUtc = inspeccion.FechaSolicitudUtc,
                CerradaTecnico = cierre.CerradaTecnico,
                FechaCierreTecnicoUtc = cierre.FechaCierreTecnicoUtc,
                UsuarioCierreTecnicoId = cierre.UsuarioCierreTecnicoId,
                PuedeGestionarSolicitud = puedeGestionar,
                PuedeCerrarInspeccion = puedeCerrar,
                MotivoNoPuedeCerrar = cierre.CerradaTecnico
                    ? "La inspección ya fue cerrada por el técnico."
                    : puedeCerrar
                        ? "Todas las fotografías activas están listas. Ya puede cerrar la inspección y habilitar la revisión humana."
                        : ObtenerMotivoNoPuedeCerrar(metadatos),
                PuedeAnalizar = puedeAnalizar,
                PuedeAprobar = puedeAprobar,
                PuedePublicarAlbum = puedePublicar,
                Fotografias = inspeccion.Imagenes
                    .OrderBy(item => item.Orden)
                    .Select(imagen =>
                    {
                        FotoMetadatos meta = metadatos.First(item =>
                            item.FotografiaId == imagen.DiagnosticoIAImagenId);
                        humanos.TryGetValue(
                            imagen.DiagnosticoIAImagenId,
                            out AnalisisHumanoRegistro? humano);
                        aprobaciones.TryGetValue(
                            imagen.DiagnosticoIAImagenId,
                            out AprobacionRegistro? aprobacion);

                        return new InspeccionFotoDto
                        {
                            FotografiaId = imagen.DiagnosticoIAImagenId,
                            Orden = imagen.Orden,
                            TipoFotografia = imagen.TipoFotografia,
                            NombreArchivoOriginal = imagen.NombreArchivoOriginal,
                            UrlImagen = imagen.UrlImagen,
                            Estado = meta.Estado,
                            FechaIdentificacionCampo =
                                meta.FechaIdentificacionCampo,
                            FechaRegistroSistemaUtc =
                                meta.FechaRegistroSistemaUtc,
                            FechaAnalisisIAUtc = meta.FechaAnalisisIAUtc,
                            FechaAnalisisHumanoUtc =
                                meta.FechaAnalisisHumanoUtc,
                            FechaAprobacionUtc = meta.FechaAprobacionUtc,
                            ModeloIAUtilizado = meta.ModeloIAUtilizado,
                            IntentosIA = meta.IntentosIA,
                            ErrorProcesamiento = meta.ErrorProcesamiento,
                            Descartada = meta.Descartada,
                            MotivoDescarte = meta.MotivoDescarte,
                            PublicadaAlbum = imagen.PublicacionesAlbum.Any(item =>
                                item.Activo),
                            ResultadoIA = CrearResultadoDto(
                                imagen.ResultadoIA,
                                meta.FechaAnalisisIAUtc),
                            UltimoAnalisisHumano = humano == null
                                ? null
                                : new InspeccionFotoAnalisisHumanoDto
                                {
                                    AnalisisHumanoId = humano.AnalisisHumanoId,
                                    Version = humano.Version,
                                    UsuarioAnalizadorId =
                                        humano.UsuarioAnalizadorId,
                                    UsuarioAnalizador = usuarios.GetValueOrDefault(
                                        humano.UsuarioAnalizadorId,
                                        $"Usuario {humano.UsuarioAnalizadorId}"),
                                    EstadoRegistro = humano.EstadoRegistro,
                                    CalidadEvaluacion = humano.CalidadEvaluacion,
                                    EstadoGeneral = humano.EstadoGeneral,
                                    CategoriaPrincipal = humano.CategoriaPrincipal,
                                    CategoriasSecundarias = DeserializarLista(
                                        humano.CategoriasSecundariasJson),
                                    Diagnostico = humano.Diagnostico,
                                    TipoDiagnostico = humano.TipoDiagnostico,
                                    Severidad = humano.Severidad,
                                    NivelCerteza = humano.NivelCerteza,
                                    Observaciones = humano.Observaciones,
                                    FechaCreacionUtc = humano.FechaCreacionUtc,
                                    FechaEnvioUtc = humano.FechaEnvioUtc
                                },
                            UltimaAprobacion = aprobacion == null
                                ? null
                                : new InspeccionFotoAprobacionDto
                                {
                                    AprobacionId = aprobacion.AprobacionId,
                                    UsuarioAprobadorId =
                                        aprobacion.UsuarioAprobadorId,
                                    UsuarioAprobador = usuarios.GetValueOrDefault(
                                        aprobacion.UsuarioAprobadorId,
                                        $"Usuario {aprobacion.UsuarioAprobadorId}"),
                                    Decision = aprobacion.Decision,
                                    DiagnosticoFinal = aprobacion.DiagnosticoFinal,
                                    Observaciones = aprobacion.Observaciones,
                                    AutorizaPublicacionAlbum =
                                        aprobacion.AutorizaPublicacionAlbum,
                                    MismoUsuarioQueAnalizo =
                                        aprobacion.MismoUsuarioQueAnalizo,
                                    FechaAprobacionUtc =
                                        aprobacion.FechaAprobacionUtc
                                },
                            Historial = (historiales.GetValueOrDefault(
                                    imagen.DiagnosticoIAImagenId) ?? [])
                                .Select(item => new InspeccionFotoHistorialDto
                                {
                                    HistorialId = item.HistorialId,
                                    UsuarioId = item.UsuarioId,
                                    Usuario = usuarios.GetValueOrDefault(
                                        item.UsuarioId,
                                        $"Usuario {item.UsuarioId}"),
                                    EstadoAnterior = item.EstadoAnterior,
                                    EstadoNuevo = item.EstadoNuevo,
                                    Accion = item.Accion,
                                    Detalle = item.Detalle,
                                    FechaUtc = item.FechaUtc
                                })
                                .ToList()
                        };
                    })
                    .ToList()
            };
        }

        private static InspeccionFotoResultadoIADto? CrearResultadoDto(
            DiagnosticoIAImagenResultadoIA? resultado,
            DateTime? fechaAnalisis)
        {
            if (resultado == null)
                return null;

            return new InspeccionFotoResultadoIADto
            {
                ImagenValida = resultado.ImagenValida,
                ParecePlantaCafe = resultado.ParecePlantaCafe,
                ResultadoConcluyente = resultado.ResultadoConcluyente,
                PartePlanta = resultado.PartePlanta,
                CalidadEvaluacion = resultado.CalidadEvaluacion,
                EstadoGeneral = resultado.EstadoGeneral,
                CategoriaPrincipal = resultado.CategoriaPrincipal,
                CategoriasSecundarias = DeserializarLista(
                    resultado.CategoriasSecundariasJson),
                DiagnosticoProbable = resultado.DiagnosticoProbable,
                TipoDiagnostico = resultado.TipoDiagnostico,
                SeveridadVisual = resultado.SeveridadVisual,
                NivelCerteza = resultado.NivelCerteza,
                CategoriaAlbumBotanicoIdSugerida =
                    resultado.CategoriaAlbumBotanicoIdSugerida,
                AlbumBotanicoCafeIdSugerido =
                    resultado.AlbumBotanicoCafeIdSugerido,
                CategoriaAlbumSugerida = resultado.CategoriaAlbumSugerida,
                ClasificacionAlbumSugerida =
                    resultado.ClasificacionAlbumSugerida,
                NombreCientificoSugerido =
                    resultado.NombreCientificoSugerido,
                CoincideCatalogoAlbum = resultado.CoincideCatalogoAlbum,
                RequiereDecisionClasificacion =
                    resultado.RequiereDecisionClasificacion,
                MotivoClasificacionAlbum =
                    resultado.MotivoClasificacionAlbum,
                ResumenImagen = resultado.ResumenImagen,
                SintomasVisibles = DeserializarLista(
                    resultado.SintomasVisiblesJson),
                EvidenciasObservadas = DeserializarLista(
                    resultado.EvidenciasObservadasJson),
                EvidenciasNoObservadas = DeserializarLista(
                    resultado.EvidenciasNoObservadasJson),
                DiagnosticosAlternativos = DeserializarLista(
                    resultado.DiagnosticosAlternativosJson),
                InformacionFaltante = DeserializarLista(
                    resultado.InformacionFaltanteJson),
                RecomendacionesCaptura = DeserializarLista(
                    resultado.RecomendacionesCapturaJson),
                Advertencias = DeserializarLista(
                    resultado.AdvertenciasJson),
                FechaAnalisisIAUtc = fechaAnalisis
            };
        }

        private static void AplicarResultadoIA(
            DiagnosticoIAImagen imagen,
            ProveedorIAResultadoFoto resultado)
        {
            DiagnosticoIAImagenResultadoIA destino = imagen.ResultadoIA ??
                new DiagnosticoIAImagenResultadoIA
                {
                    DiagnosticoIAImagenId = imagen.DiagnosticoIAImagenId
                };

            destino.ImagenValida = resultado.ImagenValida;
            destino.ParecePlantaCafe = resultado.ParecePlantaCafe;
            destino.ResultadoConcluyente = resultado.ResultadoConcluyente;
            destino.PartePlanta = Limitar(resultado.PartePlanta, 80);
            destino.CalidadEvaluacion = Limitar(
                resultado.CalidadEvaluacion,
                30);
            destino.EstadoGeneral = Limitar(resultado.EstadoGeneral, 40);
            destino.CategoriaPrincipal = Limitar(
                resultado.CategoriaPrincipal,
                50);
            destino.CategoriasSecundariasJson = SerializarLista(
                resultado.CategoriasSecundarias);
            destino.DiagnosticoProbable = Limitar(
                resultado.DiagnosticoProbable,
                300);
            destino.TipoDiagnostico = Limitar(
                resultado.TipoDiagnostico,
                80);
            destino.SeveridadVisual = Limitar(
                resultado.SeveridadVisual,
                30);
            destino.NivelCerteza = Limitar(resultado.NivelCerteza, 30);
            destino.CategoriaAlbumBotanicoIdSugerida =
                resultado.CategoriaAlbumBotanicoIdSugerida is > 0
                    ? resultado.CategoriaAlbumBotanicoIdSugerida
                    : null;
            destino.AlbumBotanicoCafeIdSugerido =
                resultado.AlbumBotanicoCafeIdSugerido is > 0
                    ? resultado.AlbumBotanicoCafeIdSugerido
                    : null;
            destino.CategoriaAlbumSugerida = Limitar(
                resultado.CategoriaAlbumSugerida,
                150);
            destino.ClasificacionAlbumSugerida = Limitar(
                resultado.ClasificacionAlbumSugerida,
                200);
            destino.NombreCientificoSugerido = Limitar(
                resultado.NombreCientificoSugerido,
                200);
            destino.CoincideCatalogoAlbum =
                resultado.CoincideCatalogoAlbum;
            destino.RequiereDecisionClasificacion =
                resultado.RequiereDecisionClasificacion;
            destino.MotivoClasificacionAlbum = Limitar(
                resultado.MotivoClasificacionAlbum,
                1000);
            destino.EstadoClasificacionAlbum =
                resultado.CoincideCatalogoAlbum &&
                destino.AlbumBotanicoCafeIdSugerido.HasValue
                    ? DiagnosticoIAFlujo.ClasificacionAlbum.ResueltaAutomatica
                    : resultado.RequiereDecisionClasificacion
                        ? DiagnosticoIAFlujo.ClasificacionAlbum.PendienteAnalizador
                        : DiagnosticoIAFlujo.ClasificacionAlbum.NoAplica;
            destino.ResumenImagen = Limitar(resultado.ResumenImagen, 1600);
            destino.SintomasVisiblesJson = SerializarLista(
                resultado.SintomasVisibles);
            destino.EvidenciasObservadasJson = SerializarLista(
                resultado.EvidenciasObservadas);
            destino.EvidenciasNoObservadasJson = SerializarLista(
                resultado.EvidenciasNoObservadas);
            destino.DiagnosticosAlternativosJson = SerializarLista(
                resultado.DiagnosticosAlternativos);
            destino.InformacionFaltanteJson = SerializarLista(
                resultado.InformacionFaltante);
            destino.RecomendacionesCapturaJson = SerializarLista(
                resultado.RecomendacionesCaptura);
            destino.AdvertenciasJson = SerializarLista(resultado.Advertencias);
            destino.FechaResultadoUtc = DateTime.UtcNow;

            if (imagen.ResultadoIA == null)
                imagen.ResultadoIA = destino;
        }

        private async Task ActualizarEstadoInspeccionAsync(
            DiagnosticoIA inspeccion,
            CancellationToken cancellationToken)
        {
            List<FotoMetadatos> fotos = await database.ObtenerFotosAsync(
                inspeccion.DiagnosticoIAId,
                cancellationToken);

            InspeccionCierreMetadatos cierre =
                await database.ObtenerCierreInspeccionAsync(
                    inspeccion.DiagnosticoIAId,
                    cancellationToken);

            string estadoNuevo =
                InspeccionFitosanitariaFlujo.CalcularEstadoInspeccion(
                    fotos.Select(item => item.Estado),
                    cierre.CerradaTecnico);

            if (inspeccion.Estado == estadoNuevo)
                return;

            string anterior = inspeccion.Estado;
            inspeccion.Estado = estadoNuevo;
            inspeccion.Historial.Add(new DiagnosticoIAHistorial
            {
                UsuarioId = inspeccion.UsuarioSolicitanteId,
                EstadoAnterior = Limitar(anterior, 40),
                EstadoNuevo = Limitar(estadoNuevo, 40),
                Accion = "ESTADO_INSPECCION_CALCULADO",
                Detalle =
                    "El estado general fue recalculado automáticamente desde los expedientes de las fotografías.",
                FechaUtc = DateTime.UtcNow
            });

            await diagnosticoDb.SaveChangesAsync(cancellationToken);
        }

        private async Task ActualizarResumenInspeccionAsync(
            DiagnosticoIA inspeccion,
            CancellationToken cancellationToken)
        {
            List<DiagnosticoIAImagenResultadoIA> resultados =
                await diagnosticoDb.ResultadosImagenIA
                    .AsNoTracking()
                    .Where(item => item.Imagen.DiagnosticoIAId ==
                        inspeccion.DiagnosticoIAId)
                    .ToListAsync(cancellationToken);

            if (resultados.Count == 0)
                return;

            inspeccion.ImagenValida = resultados.Any(item => item.ImagenValida);
            inspeccion.ParecePlantaCafe = resultados.Count(item =>
                    item.ParecePlantaCafe) >=
                Math.Ceiling(resultados.Count / 2m);
            inspeccion.ResultadoConcluyente = resultados.Any(item =>
                item.ResultadoConcluyente);
            inspeccion.CalidadEvaluacionIA = resultados.All(item =>
                    item.CalidadEvaluacion ==
                        DiagnosticoIAFlujo.CalidadEvaluacion.Evaluable)
                ? DiagnosticoIAFlujo.CalidadEvaluacion.Evaluable
                : resultados.Any(item => item.ImagenValida)
                    ? DiagnosticoIAFlujo.CalidadEvaluacion.Parcial
                    : DiagnosticoIAFlujo.CalidadEvaluacion.NoEvaluable;
            inspeccion.EstadoGeneralIA = resultados.Any(item =>
                    item.EstadoGeneral ==
                        DiagnosticoIAFlujo.EstadoGeneral.Afectada)
                ? DiagnosticoIAFlujo.EstadoGeneral.Afectada
                : resultados.All(item =>
                    item.EstadoGeneral == DiagnosticoIAFlujo.EstadoGeneral.Sana)
                    ? DiagnosticoIAFlujo.EstadoGeneral.Sana
                    : DiagnosticoIAFlujo.EstadoGeneral.Indeterminada;
            inspeccion.CategoriaPrincipalIA = resultados
                .Where(item => !string.IsNullOrWhiteSpace(
                    item.CategoriaPrincipal))
                .GroupBy(item => item.CategoriaPrincipal)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .FirstOrDefault() ?? DiagnosticoIAFlujo.Categoria.NoAplica;
            inspeccion.CategoriasSecundariasIAJson = SerializarLista(
                resultados
                    .SelectMany(item => DeserializarLista(
                        item.CategoriasSecundariasJson))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            inspeccion.DiagnosticoSugerido = Limitar(
                string.Join(
                    "; ",
                    resultados
                        .Select(item => item.DiagnosticoProbable)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Distinct(StringComparer.OrdinalIgnoreCase)),
                300);
            inspeccion.Resumen = Limitar(
                $"Se analizaron {resultados.Count} fotografías. " +
                $"{resultados.Count(item => item.ResultadoConcluyente)} tuvieron un resultado preliminar concluyente y " +
                $"{resultados.Count(item => !item.ResultadoConcluyente)} requieren validación o nueva evidencia.",
                2000);
            inspeccion.FechaRespuestaIAUtc = DateTime.UtcNow;
            inspeccion.ErrorAnalisis = string.Empty;

            await diagnosticoDb.SaveChangesAsync(cancellationToken);
        }

        private async Task<DiagnosticoIA?> CargarInspeccionAsync(
            int id,
            CancellationToken cancellationToken) =>
            await diagnosticoDb.Diagnosticos
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.ResultadoIA)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(item =>
                    item.DiagnosticoIAId == id && item.Activo,
                    cancellationToken);

        private async Task<IActionResult?> ValidarAccesoProcesamientoAsync(
            DiagnosticoIA? inspeccion,
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (inspeccion == null)
                return NoEncontrado();

            bool esPropietario = inspeccion.UsuarioSolicitanteId == usuarioId &&
                await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazSolicitud,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            return esPropietario
                ? null
                : Forbid();
        }

        private async Task<IActionResult?> ValidarInspeccionAbiertaAsync(
            int inspeccionId,
            CancellationToken cancellationToken)
        {
            await database.InicializarAsync(cancellationToken);

            InspeccionCierreMetadatos cierre =
                await database.ObtenerCierreInspeccionAsync(
                    inspeccionId,
                    cancellationToken);

            if (!cierre.CerradaTecnico)
                return null;

            return BadRequest(new
            {
                success = false,
                message =
                    "La inspección ya fue cerrada por el técnico. No se pueden agregar, descartar, reenviar ni volver a analizar fotografías desde la etapa técnica."
            });
        }

        private static string ObtenerMotivoNoPuedeCerrar(
            IReadOnlyCollection<FotoMetadatos> fotos)
        {
            List<FotoMetadatos> activas = fotos
                .Where(item => item.Activo)
                .ToList();

            if (activas.Count == 0)
                return "La inspección debe contener al menos una fotografía activa.";

            if (activas.Any(item =>
                    item.Estado ==
                    InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA))
            {
                return "Hay fotografías con error de IA. Reintente el análisis o descártelas antes de cerrar la inspección.";
            }

            if (activas.Any(item => item.Estado is
                    InspeccionFitosanitariaFlujo.FotoEstados.Borrador or
                    InspeccionFitosanitariaFlujo.FotoEstados.PendienteIA or
                    InspeccionFitosanitariaFlujo.FotoEstados.AnalizandoIA))
            {
                return "Todas las fotografías activas deben analizarse con IA o descartarse antes de cerrar la inspección.";
            }

            if (activas.Any(item =>
                    item.Estado ==
                    InspeccionFitosanitariaFlujo.FotoEstados.PendienteDecisionTecnico))
            {
                return "Todavía hay fotografías pendientes de la decisión del técnico. Envíelas al analizador o descártelas.";
            }

            if (!activas.Any(item =>
                    item.Estado ==
                    InspeccionFitosanitariaFlujo.FotoEstados.PendienteAnalizador))
            {
                return "Debe enviar al menos una fotografía al analizador antes de cerrar la inspección.";
            }

            return "Para cerrar, todas las fotografías activas deben estar enviadas al analizador o descartadas.";
        }

        private ProveedorIAClienteService CrearProveedorService() =>
            new(
                httpClientFactory,
                configuration,
                storage,
                db,
                logger);

        private async Task<(int? TerrenoId, string Codigo, IActionResult? Error)>
            ResolverTerrenoAsync(
                string? codigo,
                CancellationToken cancellationToken)
        {
            string codigoTerreno = (codigo ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(codigoTerreno))
            {
                return (
                    null,
                    string.Empty,
                    BadRequest(new
                    {
                        success = false,
                        message =
                            "Debe seleccionar un terreno activo antes de crear la inspección fitosanitaria."
                    }));
            }

            var terreno = await db.Terreno
                .AsNoTracking()
                .Where(item => item.activo &&
                    item.codigoTerreno == codigoTerreno)
                .Select(item => new
                {
                    item.terrenoId,
                    item.codigoTerreno
                })
                .FirstOrDefaultAsync(cancellationToken);

            return terreno == null
                ? (
                    null,
                    codigoTerreno,
                    BadRequest(new
                    {
                        success = false,
                        message =
                            "No se encontró un terreno activo con el código indicado."
                    }))
                : (terreno.terrenoId, terreno.codigoTerreno, null);
        }

        private IActionResult? ValidarFotos(IReadOnlyCollection<IFormFile> fotos)
        {
            if (fotos.Count == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Seleccione al menos una fotografía."
                });
            }

            if (fotos.Count > MaximoFotosPorSolicitud)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        $"Puede registrar como máximo {MaximoFotosPorSolicitud} fotografías por inspección."
                });
            }

            IFormFile? demasiadoGrande = fotos.FirstOrDefault(item =>
                item.Length > MaximoBytesPorFoto);

            if (demasiadoGrande != null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        $"La fotografía {demasiadoGrande.FileName} supera el límite de 12 MB."
                });
            }

            IFormFile? tipoInvalido = fotos.FirstOrDefault(item =>
                string.IsNullOrWhiteSpace(item.ContentType) ||
                !item.ContentType.StartsWith(
                    "image/",
                    StringComparison.OrdinalIgnoreCase));

            return tipoInvalido == null
                ? null
                : BadRequest(new
                {
                    success = false,
                    message =
                        $"El archivo {tipoInvalido.FileName} no es una imagen válida."
                });
        }

        private IActionResult? ValidarFechasCampo(
            IReadOnlyList<string>? fechas,
            int cantidadFotos)
        {
            if (fechas == null || fechas.Count != cantidadFotos)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Debe indicar una fecha de identificación en campo por cada fotografía."
                });
            }

            for (int indice = 0; indice < fechas.Count; indice++)
            {
                if (!DateTime.TryParseExact(
                        fechas[indice],
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateTime fecha))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            $"La fecha de campo de la fotografía {indice + 1} no es válida. Use yyyy-MM-dd."
                    });
                }

                if (fecha.Date > DateTime.UtcNow.Date)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            $"La fecha de campo de la fotografía {indice + 1} no puede estar en el futuro."
                    });
                }
            }

            return null;
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            int? usuarioId,
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                tipo,
                cancellationToken);

            return resultado.Permitido
                ? null
                : StatusCode(
                    resultado.CodigoEstado,
                    new
                    {
                        success = false,
                        message = resultado.Mensaje
                    });
        }

        private async Task<bool> TienePermisoAsync(
            int? usuarioId,
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                tipo,
                cancellationToken);
            return resultado.Permitido;
        }

        private int? ObtenerUsuarioId()
        {
            string? valor =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("usuarioId") ??
                User.FindFirstValue("uid") ??
                User.FindFirstValue("sub");

            return int.TryParse(valor, out int usuarioId)
                ? usuarioId
                : null;
        }

        private string ConstruirUrlPublica(string rutaRelativa)
        {
            if (Uri.TryCreate(rutaRelativa, UriKind.Absolute, out _))
                return rutaRelativa;

            return $"{Request.Scheme}://{Request.Host}" +
                   $"{Request.PathBase}/{rutaRelativa.TrimStart('/')}";
        }

        private void EliminarImagenSeguro(string ruta)
        {
            try
            {
                imageService.EliminarImagen(ruta);
            }
            catch
            {
            }
        }

        private static string ResolverTipoFotografia(
            IReadOnlyList<string>? tipos,
            int indice)
        {
            if (tipos == null || indice < 0 || indice >= tipos.Count ||
                string.IsNullOrWhiteSpace(tipos[indice]))
            {
                return "EVIDENCIA";
            }

            return Limitar(
                tipos[indice]
                    .Trim()
                    .ToUpperInvariant()
                    .Replace(' ', '_'),
                40);
        }

        private static DateTime? ResolverFechaCampo(
            IReadOnlyList<string>? fechas,
            int indice)
        {
            if (fechas == null || indice < 0 || indice >= fechas.Count ||
                string.IsNullOrWhiteSpace(fechas[indice]))
            {
                return null;
            }

            return DateTime.TryParseExact(
                    fechas[indice],
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime fecha)
                ? fecha.Date
                : null;
        }

        private static string CrearMensajeOperacion(
            InspeccionOperacionMasivaDto resultado,
            string accion)
        {
            if (resultado.TotalConError == 0)
            {
                return $"Las {resultado.TotalExitosas} fotografías fueron {accion} correctamente.";
            }

            return $"Resultado parcial: {resultado.TotalExitosas} fotografías fueron {accion} y " +
                   $"{resultado.TotalConError} no pudieron procesarse.";
        }

        private static string CrearMensajeErrorIA(Exception ex) =>
            ex is ProveedorIAException proveedorError
                ? proveedorError.Message
                : ex is OperationCanceledException
                    ? "La operación fue cancelada antes de finalizar."
                    : "Ocurrió un error al analizar esta fotografía. " +
                      Limitar(ex.Message, 1000);

        private static string ValorOAnterior(
            string? nuevo,
            string anterior) =>
            string.IsNullOrWhiteSpace(nuevo)
                ? anterior
                : nuevo.Trim();

        private static string SerializarLista(IEnumerable<string>? valores) =>
            JsonSerializer.Serialize(
                (valores ?? [])
                    .Select(item => item?.Trim() ?? string.Empty)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                JsonOptions);

        private static List<string> DeserializarLista(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            try
            {
                return JsonSerializer.Deserialize<List<string>>(
                    json,
                    JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static string Limitar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo ? texto : texto[..maximo];
        }

        private IActionResult NoEncontrado() =>
            NotFound(new
            {
                success = false,
                message = "La inspección solicitada no existe."
            });
    }
}
