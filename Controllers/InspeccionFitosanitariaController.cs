using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Operaciones propias del técnico y consulta del expediente individual.
    /// Las etapas de analizador, aprobador, publicación y cierre definitivo se
    /// encuentran separadas para impedir que un controlador histórico omita
    /// las reglas de asignación, concurrencia y segregación de funciones.
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
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;
        private readonly InspeccionFitosanitariaAsignacionDatabase asignaciones;
        private readonly DiagnosticoIAImagenMarcadaService imagenMarcadaService;

        public InspeccionFitosanitariaController(
            DiagnosticoIADbContext diagnosticoDb,
            DBContext db,
            ImageService imageService,
            ImageStoragePathService storage,
            PermisoApiService permisos,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<InspeccionFitosanitariaController> logger,
            InspeccionFitosanitariaControlDatabaseInitializer control)
        {
            this.diagnosticoDb = diagnosticoDb;
            this.db = db;
            this.imageService = imageService;
            this.storage = storage;
            this.permisos = permisos;
            this.httpClientFactory = httpClientFactory;
            this.configuration = configuration;
            this.logger = logger;
            this.control = control;
            database = new InspeccionFitosanitariaDatabase(diagnosticoDb);
            asignaciones = new InspeccionFitosanitariaAsignacionDatabase(
                diagnosticoDb);
            imagenMarcadaService = new DiagnosticoIAImagenMarcadaService(
                storage,
                logger);
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

            await InicializarAsync(cancellationToken);

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
                Estado = InspeccionFitosanitariaFlujo
                    .InspeccionEstados.EnProceso,
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

                    DateTime fechaRegistroUtc = DateTime.UtcNow;
                    var imagen = new DiagnosticoIAImagen
                    {
                        RutaRelativa = rutaRelativa,
                        UrlImagen = ConstruirUrlPublica(rutaRelativa),
                        NombreArchivoOriginal = Limitar(foto.FileName, 255),
                        TipoFotografia = ResolverTipoFotografia(
                            request.TiposFotografia,
                            indice),
                        Orden = indice + 1,
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
                    EstadoAnterior = string.Empty,
                    EstadoNuevo = inspeccion.Estado,
                    Accion = "INSPECCION_CREADA_V2",
                    Detalle =
                        $"Se registraron {fotos.Count} fotografía(s) con expediente individual.",
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

            await InicializarAsync(cancellationToken);

            DiagnosticoIA? inspeccion = await CargarInspeccionAsync(
                id,
                cancellationToken);
            if (inspeccion == null)
                return NoEncontrado();

            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(id, cancellationToken);

            if (registro == null || !registro.Activo)
                return NoEncontrado();

            if (registro.EtapaTecnicaFinalizada ||
                registro.CerradaDefinitiva)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "La etapa técnica ya fue finalizada y no admite nuevas fotografías."
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
            await using var transaccion =
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
                await transaccion.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);
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

            List<AlbumBotanicoCafeReferencia> subcategorias =
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
                    Fichas = subcategorias
                        .Where(item => item.CategoriaAlbumBotanicoId ==
                            categoria.CategoriaAlbumBotanicoId)
                        .Select(item => new InspeccionAlbumFichaDto
                        {
                            AlbumBotanicoCafeId = item.AlbumBotanicoCafeId,
                            CategoriaAlbumBotanicoId =
                                item.CategoriaAlbumBotanicoId,
                            Titulo = item.Titulo,
                            NombreCientifico =
                                item.NombreCientifico ?? string.Empty
                        })
                        .ToList()
                })
                .Where(item => item.Fichas.Count > 0)
                .ToList();

            return Ok(new
            {
                success = true,
                message =
                    "Catálogo activo del álbum obtenido correctamente.",
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

            await InicializarAsync(cancellationToken);

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

            IActionResult? acceso = await ValidarAccesoTecnicoAsync(
                inspeccion,
                usuarioId,
                TipoPermisoApi.Agregar,
                cancellationToken);
            if (acceso != null)
                return acceso;

            IActionResult? etapa = await ValidarEtapaTecnicaAbiertaAsync(
                id,
                cancellationToken);
            if (etapa != null)
                return etapa;

            InspeccionOperacionMasivaDto data =
                await ProcesarSeleccionAsync(
                    inspeccion!,
                    request.FotografiaIds,
                    usuarioId!.Value,
                    "ANALISIS_INICIAL",
                    string.Empty,
                    string.Empty,
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

            IActionResult? acceso = await ValidarAccesoTecnicoAsync(
                inspeccion,
                usuarioId,
                TipoPermisoApi.Agregar,
                cancellationToken);
            if (acceso != null)
                return acceso;

            IActionResult? etapa = await ValidarEtapaTecnicaAbiertaAsync(
                id,
                cancellationToken);
            if (etapa != null)
                return etapa;

            InspeccionOperacionMasivaDto data =
                await ProcesarSeleccionAsync(
                    inspeccion!,
                    request.FotografiaIds,
                    usuarioId!.Value,
                    "REVISION_SOLICITADA",
                    request.Retroalimentacion,
                    request.DiagnosticoPropuesto ?? string.Empty,
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

            IActionResult? acceso = await ValidarAccesoTecnicoAsync(
                inspeccion,
                usuarioId,
                TipoPermisoApi.Agregar,
                cancellationToken);
            if (acceso != null)
                return acceso;

            IActionResult? etapa = await ValidarEtapaTecnicaAbiertaAsync(
                id,
                cancellationToken);
            if (etapa != null)
                return etapa;

            InspeccionOperacionMasivaDto data =
                await EjecutarSobreSeleccionAsync(
                    inspeccion!,
                    request.FotografiaIds,
                    async (imagen, meta) =>
                    {
                        if (!string.Equals(
                                meta.Estado,
                                InspeccionFitosanitariaFlujo.FotoEstados
                                    .PendienteDecisionTecnico,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                "La fotografía no está pendiente de la decisión del técnico.");
                        }

                        await database.CambiarEstadoFotoAsync(
                            imagen.DiagnosticoIAImagenId,
                            usuarioId!.Value,
                            InspeccionFitosanitariaFlujo.FotoEstados
                                .PendienteAnalizador,
                            InspeccionFitosanitariaFlujo.Acciones
                                .TecnicoEnviaAnalizador,
                            "El técnico envió la fotografía al analizador humano.",
                            cancellationToken: cancellationToken);

                        return InspeccionFitosanitariaFlujo.FotoEstados
                            .PendienteAnalizador;
                    },
                    cancellationToken);

            await ActualizarEstadoInspeccionAsync(
                inspeccion!,
                cancellationToken);

            return Ok(new
            {
                success = data.TotalExitosas > 0,
                message = CrearMensajeOperacion(
                    data,
                    "enviadas al analizador"),
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

            IActionResult? acceso = await ValidarAccesoTecnicoAsync(
                inspeccion,
                usuarioId,
                TipoPermisoApi.Eliminar,
                cancellationToken);
            if (acceso != null)
                return acceso;

            IActionResult? etapa = await ValidarEtapaTecnicaAbiertaAsync(
                id,
                cancellationToken);
            if (etapa != null)
                return etapa;

            InspeccionOperacionMasivaDto data =
                await EjecutarSobreSeleccionAsync(
                    inspeccion!,
                    request.FotografiaIds,
                    async (imagen, meta) =>
                    {
                        string[] permitidos =
                        [
                            InspeccionFitosanitariaFlujo.FotoEstados.Borrador,
                            InspeccionFitosanitariaFlujo.FotoEstados.PendienteIA,
                            InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA,
                            InspeccionFitosanitariaFlujo.FotoEstados
                                .PendienteDecisionTecnico,
                            InspeccionFitosanitariaFlujo.FotoEstados
                                .DevueltaTecnico
                        ];

                        if (!permitidos.Contains(
                                meta.Estado,
                                StringComparer.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                "La fotografía ya fue enviada a revisión y no puede descartarse desde la etapa técnica.");
                        }

                        await database.DescartarFotoAsync(
                            imagen.DiagnosticoIAImagenId,
                            usuarioId!.Value,
                            request.Motivo,
                            cancellationToken);

                        return InspeccionFitosanitariaFlujo.FotoEstados
                            .Descartada;
                    },
                    cancellationToken);

            await ActualizarEstadoInspeccionAsync(
                inspeccion!,
                cancellationToken);

            return Ok(new
            {
                success = data.TotalExitosas > 0,
                message = CrearMensajeOperacion(
                    data,
                    "descartadas lógicamente"),
                data
            });
        }

        private async Task InicializarAsync(
            CancellationToken cancellationToken)
        {
            await database.InicializarAsync(cancellationToken);
            await control.InicializarAsync(cancellationToken);
            await asignaciones.InicializarAsync(cancellationToken);
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

                    string[] permitidos = tipoRevision == "ANALISIS_INICIAL"
                        ?
                        [
                            InspeccionFitosanitariaFlujo.FotoEstados.Borrador,
                            InspeccionFitosanitariaFlujo.FotoEstados.PendienteIA,
                            InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA,
                            InspeccionFitosanitariaFlujo.FotoEstados
                                .NoConcluyente
                        ]
                        :
                        [
                            InspeccionFitosanitariaFlujo.FotoEstados
                                .PendienteDecisionTecnico,
                            InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA,
                            InspeccionFitosanitariaFlujo.FotoEstados.PendienteIA
                        ];

                    if (!permitidos.Contains(
                            meta.Estado,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "La fotografía no se encuentra en un estado válido para esta evaluación de IA.");
                    }

                    /*
                     * El metadato fue leído antes de incrementar IntentosIA.
                     * Por eso +1 identifica de forma estable la revisión que
                     * estamos generando y nunca reutiliza un archivo anterior.
                     */
                    int revisionVisual = meta.IntentosIA + 1;

                    await database.CambiarEstadoFotoAsync(
                        fotografiaId,
                        usuarioId,
                        InspeccionFitosanitariaFlujo.FotoEstados.AnalizandoIA,
                        InspeccionFitosanitariaFlujo.Acciones.AnalisisIAIniciado,
                        string.IsNullOrWhiteSpace(retroalimentacion)
                            ? "Se inició el análisis preliminar de la fotografía."
                            : "Se inició una nueva evaluación de IA solicitada por el técnico.",
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

                    ResultadoImagenMarcadaGenerada? imagenMarcada =
                        await imagenMarcadaService.GenerarAsync(
                            inspeccion.DiagnosticoIAId,
                            imagen,
                            revisionVisual,
                            resultado.Diagnosticos,
                            cancellationToken);

                    string diagnosticosJson = JsonSerializer.Serialize(
                        resultado.Diagnosticos ?? [],
                        JsonOptions);

                    await database.GuardarResultadoVisualAsync(
                        fotografiaId,
                        revisionVisual,
                        diagnosticosJson,
                        imagenMarcada?.RutaRelativa ?? string.Empty,
                        resultado.Proveedor,
                        resultado.Modelo,
                        cancellationToken);

                    AplicarResultadoIA(imagen, resultado);
                    await diagnosticoDb.SaveChangesAsync(cancellationToken);

                    await database.CompletarRevisionIAAsync(
                        revisionId.Value,
                        "COMPLETADA",
                        resultado.RespuestaOriginalJson,
                        string.Empty,
                        cancellationToken);

                    await database.CambiarEstadoFotoAsync(
                        fotografiaId,
                        usuarioId,
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .PendienteDecisionTecnico,
                        InspeccionFitosanitariaFlujo.Acciones
                            .AnalisisIACompletado,
                        "La IA terminó el análisis preliminar. El técnico debe decidir cómo continuar.",
                        fechaAnalisisIAUtc: DateTime.UtcNow,
                        error: string.Empty,
                        modeloIA: resultado.Modelo,
                        cancellationToken: cancellationToken);

                    data.Resultados.Add(new InspeccionOperacionItemDto
                    {
                        FotografiaId = fotografiaId,
                        Exitoso = true,
                        Estado = InspeccionFitosanitariaFlujo.FotoEstados
                            .PendienteDecisionTecnico,
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
            await InicializarAsync(cancellationToken);

            DiagnosticoIA inspeccion = await diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.ResultadoIA)
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.PublicacionesAlbum)
                .FirstAsync(
                    item => item.DiagnosticoIAId == inspeccionId,
                    cancellationToken);

            List<FotoMetadatos> metadatos =
                await database.ObtenerFotosAsync(
                    inspeccionId,
                    cancellationToken);

            Dictionary<int, ResultadoVisualRegistro> resultadosVisuales =
                await database.ObtenerResultadosVisualesVigentesAsync(
                    inspeccionId,
                    cancellationToken);

            InspeccionFitosanitariaControlRegistro registro =
                await control.ObtenerAsync(
                    inspeccionId,
                    cancellationToken) ??
                throw new InvalidOperationException(
                    "No se encontró el control de la inspección.");

            InspeccionFitosanitariaAsignacionRegistro asignacion =
                await asignaciones.ObtenerAsync(
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
                usuariosIds.Add(item.UsuarioId);

            if (asignacion.UsuarioAnalizadorId.HasValue)
                usuariosIds.Add(asignacion.UsuarioAnalizadorId.Value);
            if (asignacion.UsuarioAprobadorId.HasValue)
                usuariosIds.Add(asignacion.UsuarioAprobadorId.Value);

            Dictionary<int, string> usuarios = await db.Usuarios
                .AsNoTracking()
                .Where(item => usuariosIds.Contains(item.UsuarioId))
                .ToDictionaryAsync(
                    item => item.UsuarioId,
                    item => string.IsNullOrWhiteSpace(
                        item.nombreCompletoUsuario)
                            ? item.nombreUsuario
                            : item.nombreCompletoUsuario,
                    cancellationToken);

            bool esPropietario =
                inspeccion.UsuarioSolicitanteId == usuarioActualId;

            bool tienePermisoGestionar = esPropietario &&
                await TienePermisoAsync(
                    usuarioActualId,
                    DiagnosticoIAFlujo.InterfazSolicitud,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            bool puedeGestionar =
                tienePermisoGestionar &&
                !registro.EtapaTecnicaFinalizada &&
                !registro.CerradaDefinitiva;

            InspeccionFitosanitariaEstadoEtapaTecnica estadoTecnico =
                await control.ObtenerEstadoEtapaTecnicaAsync(
                    inspeccionId,
                    cancellationToken);

            bool puedeCerrarEtapa =
                puedeGestionar && estadoTecnico.ListaParaCerrar;

            bool permisoAnalizador = await TienePermisoAsync(
                usuarioActualId,
                DiagnosticoIAFlujo.InterfazAnalizador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            bool puedeAnalizar =
                permisoAnalizador &&
                !registro.CerradaDefinitiva &&
                (!asignacion.UsuarioAnalizadorId.HasValue ||
                 asignacion.UsuarioAnalizadorId == usuarioActualId);

            bool permisoAprobador = await TienePermisoAsync(
                usuarioActualId,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            bool puedeAprobar =
                permisoAprobador &&
                registro.EtapaTecnicaFinalizada &&
                !registro.CerradaDefinitiva &&
                asignacion.UsuarioAnalizadorId != usuarioActualId &&
                (!asignacion.UsuarioAprobadorId.HasValue ||
                 asignacion.UsuarioAprobadorId == usuarioActualId);

            bool puedePublicar = await TienePermisoAsync(
                usuarioActualId,
                DiagnosticoIAFlujo.InterfazAlbum,
                TipoPermisoApi.Agregar,
                cancellationToken);

            string motivoNoPuedeCerrar = registro.EtapaTecnicaFinalizada
                ? "La etapa técnica ya fue finalizada."
                : registro.CerradaDefinitiva
                    ? "La inspección está cerrada definitivamente."
                    : puedeCerrarEtapa
                        ? "Todas las fotografías activas están enviadas o descartadas. Ya puede finalizar la etapa técnica."
                        : CrearMotivoEtapaTecnica(estadoTecnico);

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
                    metadatos.Where(item => item.Activo)
                        .Select(item => item.Estado),
                    registro.CerradaDefinitiva),
                FechaRegistroSistemaUtc = inspeccion.FechaSolicitudUtc,
                EtapaTecnicaFinalizada = registro.EtapaTecnicaFinalizada,
                FechaFinEtapaTecnicaUtc = registro.FechaFinEtapaTecnicaUtc,
                UsuarioFinEtapaTecnicaId =
                    registro.UsuarioFinEtapaTecnicaId,
                CerradaDefinitiva = registro.CerradaDefinitiva,
                FechaCierreDefinitivoUtc =
                    registro.FechaCierreDefinitivoUtc,
                UsuarioCierreDefinitivoId =
                    registro.UsuarioCierreDefinitivoId,
                UsuarioAnalizadorAsignadoId =
                    asignacion.UsuarioAnalizadorId,
                UsuarioAprobadorAsignadoId =
                    asignacion.UsuarioAprobadorId,
                VersionAsignacion = asignacion.VersionConcurrencia,
                PuedeGestionarSolicitud = puedeGestionar,
                PuedeCerrarInspeccion = puedeCerrarEtapa,
                MotivoNoPuedeCerrar = motivoNoPuedeCerrar,
                PuedeAnalizar = puedeAnalizar,
                PuedeAprobar = puedeAprobar,
                PuedePublicarAlbum = puedePublicar,
                Fotografias = inspeccion.Imagenes
                    .OrderBy(item => item.Orden)
                    .Select(imagen => CrearFotoDto(
                        imagen,
                        metadatos,
                        resultadosVisuales,
                        humanos,
                        aprobaciones,
                        historiales,
                        usuarios))
                    .ToList()
            };
        }

        private InspeccionFotoDto CrearFotoDto(
            DiagnosticoIAImagen imagen,
            IReadOnlyCollection<FotoMetadatos> metadatos,
            IReadOnlyDictionary<int, ResultadoVisualRegistro> resultadosVisuales,
            IReadOnlyDictionary<int, AnalisisHumanoRegistro> humanos,
            IReadOnlyDictionary<int, AprobacionRegistro> aprobaciones,
            IReadOnlyDictionary<int, List<HistorialFotoRegistro>> historiales,
            IReadOnlyDictionary<int, string> usuarios)
        {
            FotoMetadatos meta = metadatos.First(item =>
                item.FotografiaId == imagen.DiagnosticoIAImagenId);

            resultadosVisuales.TryGetValue(
                imagen.DiagnosticoIAImagenId,
                out ResultadoVisualRegistro? visual);
            humanos.TryGetValue(
                imagen.DiagnosticoIAImagenId,
                out AnalisisHumanoRegistro? humano);
            aprobaciones.TryGetValue(
                imagen.DiagnosticoIAImagenId,
                out AprobacionRegistro? aprobacion);

            string urlMarcada = !string.IsNullOrWhiteSpace(
                visual?.RutaImagenMarcada)
                    ? ConstruirUrlPublica(visual.RutaImagenMarcada)
                    : string.Empty;

            return new InspeccionFotoDto
            {
                FotografiaId = imagen.DiagnosticoIAImagenId,
                Orden = imagen.Orden,
                TipoFotografia = imagen.TipoFotografia,
                NombreArchivoOriginal = imagen.NombreArchivoOriginal,
                UrlImagen = imagen.UrlImagen,
                UrlImagenMarcadaIA = urlMarcada,
                TieneImagenMarcadaIA = !string.IsNullOrWhiteSpace(urlMarcada),
                VersionImagenMarcadaIA = visual?.Revision,
                Estado = meta.Estado,
                FechaIdentificacionCampo = meta.FechaIdentificacionCampo,
                FechaRegistroSistemaUtc = meta.FechaRegistroSistemaUtc,
                FechaAnalisisIAUtc = meta.FechaAnalisisIAUtc,
                FechaAnalisisHumanoUtc = meta.FechaAnalisisHumanoUtc,
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
                    meta.FechaAnalisisIAUtc,
                    visual),
                UltimoAnalisisHumano = humano == null
                    ? null
                    : new InspeccionFotoAnalisisHumanoDto
                    {
                        AnalisisHumanoId = humano.AnalisisHumanoId,
                        Version = humano.Version,
                        UsuarioAnalizadorId = humano.UsuarioAnalizadorId,
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
                        FechaEnvioUtc = humano.FechaEnvioUtc,
                        Diagnosticos = DeserializarDiagnosticos(
                            humano.DiagnosticosJson)
                    },
                UltimaAprobacion = aprobacion == null
                    ? null
                    : new InspeccionFotoAprobacionDto
                    {
                        AprobacionId = aprobacion.AprobacionId,
                        UsuarioAprobadorId = aprobacion.UsuarioAprobadorId,
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
                        FechaAprobacionUtc = aprobacion.FechaAprobacionUtc,
                        DiagnosticosFinales = DeserializarDiagnosticos(
                            aprobacion.DiagnosticosFinalesJson)
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
        }

        private static InspeccionFotoResultadoIADto? CrearResultadoDto(
            DiagnosticoIAImagenResultadoIA? resultado,
            DateTime? fechaAnalisis,
            ResultadoVisualRegistro? visual)
        {
            if (resultado == null)
                return null;

            List<InspeccionDiagnosticoVisualDto> diagnosticos =
                DeserializarDiagnosticos(visual?.DiagnosticosJson);

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
                FechaAnalisisIAUtc = fechaAnalisis,
                Diagnosticos = diagnosticos,
                LocalizacionVisualDisponible =
                    diagnosticos.Any(item => item.Lesiones.Count > 0),
                VersionVisual = visual?.Revision
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
            destino.CoincideCatalogoAlbum = resultado.CoincideCatalogoAlbum;
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
                        ? DiagnosticoIAFlujo.ClasificacionAlbum
                            .PendienteAnalizador
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

            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(
                    inspeccion.DiagnosticoIAId,
                    cancellationToken);

            string estadoNuevo =
                InspeccionFitosanitariaFlujo.CalcularEstadoInspeccion(
                    fotos.Where(item => item.Activo)
                        .Select(item => item.Estado),
                    registro?.CerradaDefinitiva == true);

            if (string.Equals(
                    inspeccion.Estado,
                    estadoNuevo,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

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
                    item.EstadoGeneral ==
                        DiagnosticoIAFlujo.EstadoGeneral.Sana)
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
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.PublicacionesAlbum)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(
                    item => item.DiagnosticoIAId == id && item.Activo,
                    cancellationToken);

        private async Task<IActionResult?> ValidarAccesoTecnicoAsync(
            DiagnosticoIA? inspeccion,
            int? usuarioId,
            TipoPermisoApi permisoRequerido,
            CancellationToken cancellationToken)
        {
            if (inspeccion == null)
                return NoEncontrado();

            if (!usuarioId.HasValue ||
                inspeccion.UsuarioSolicitanteId != usuarioId.Value)
            {
                return Forbid();
            }

            return await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazSolicitud,
                permisoRequerido,
                cancellationToken);
        }

        private async Task<IActionResult?> ValidarEtapaTecnicaAbiertaAsync(
            int inspeccionId,
            CancellationToken cancellationToken)
        {
            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(
                    inspeccionId,
                    cancellationToken);

            if (registro == null || !registro.Activo)
                return NoEncontrado();

            if (!registro.EtapaTecnicaFinalizada &&
                !registro.CerradaDefinitiva)
            {
                return null;
            }

            return Conflict(new
            {
                success = false,
                message = registro.CerradaDefinitiva
                    ? "La inspección está cerrada definitivamente y solo puede consultarse."
                    : "La etapa técnica ya fue finalizada. El técnico no puede modificar las evidencias."
            });
        }

        private static string CrearMotivoEtapaTecnica(
            InspeccionFitosanitariaEstadoEtapaTecnica estado)
        {
            if (estado.TotalActivas == 0)
                return "La inspección debe conservar al menos una fotografía activa.";
            if (estado.TotalEnviadasRevision == 0)
                return "Debe enviar al menos una fotografía al analizador.";
            if (estado.TotalProcesando > 0)
                return $"Hay {estado.TotalProcesando} fotografía(s) procesándose.";
            if (estado.TotalNoPreparadas > 0)
            {
                return $"Todavía existen {estado.TotalNoPreparadas} fotografía(s) que deben enviarse al analizador o descartarse.";
            }

            return "La inspección todavía no cumple las condiciones para finalizar la etapa técnica.";
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

        private IActionResult? ValidarFotos(
            IReadOnlyCollection<IFormFile> fotos)
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

            return int.TryParse(valor, out int usuarioId) && usuarioId > 0
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

        private static string SerializarLista(
            IEnumerable<string>? valores) =>
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
