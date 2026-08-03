using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Net;
using System.Security.Claims;
using System.Text.Json;

namespace CONATRADEC_API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/diagnostico-ia")]
    public sealed class DiagnosticoIAController : ControllerBase
    {
        private const long MaximoBytesPorFoto = 12L * 1024L * 1024L;

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly DBContext db;
        private readonly ImageService imageService;
        private readonly ImageStoragePathService storage;
        private readonly PermisoApiService permisos;
        private readonly GeminiDiagnosticoService gemini;
        private readonly ILogger<DiagnosticoIAController> logger;

        public DiagnosticoIAController(
            DiagnosticoIADbContext diagnosticoDb,
            DBContext db,
            ImageService imageService,
            ImageStoragePathService storage,
            PermisoApiService permisos,
            GeminiDiagnosticoService gemini,
            ILogger<DiagnosticoIAController> logger)
        {
            this.diagnosticoDb = diagnosticoDb;
            this.db = db;
            this.imageService = imageService;
            this.storage = storage;
            this.permisos = permisos;
            this.gemini = gemini;
            this.logger = logger;
        }

        [HttpGet("catalogos")]
        public async Task<IActionResult> Catalogos(
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            if (!await TieneAlgunPermisoLecturaAsync(
                    usuarioId,
                    cancellationToken))
            {
                return Forbid();
            }

            return Ok(new
            {
                success = true,
                message = "Catálogos obtenidos correctamente.",
                data = new DiagnosticoIACatalogosDto
                {
                    CalidadEvaluacion =
                        DiagnosticoIAFlujo.CalidadEvaluacion.Todos
                            .OrderBy(item => item)
                            .ToList(),
                    EstadosGenerales =
                        DiagnosticoIAFlujo.EstadoGeneral.Todos
                            .OrderBy(item => item)
                            .ToList(),
                    Categorias =
                        DiagnosticoIAFlujo.Categoria.Todos
                            .OrderBy(item => item)
                            .ToList(),
                    Severidades =
                        DiagnosticoIAFlujo.Severidad.Todos
                            .OrderBy(item => item)
                            .ToList(),
                    NivelesCerteza =
                        DiagnosticoIAFlujo.Certeza.Todos
                            .OrderBy(item => item)
                            .ToList(),
                    DecisionesAprobacion =
                        DiagnosticoIAFlujo.DecisionAprobacion.Todos
                            .OrderBy(item => item)
                            .ToList(),
                    CalidadesImagen =
                        DiagnosticoIAFlujo.CalidadImagen.Todos
                            .OrderBy(item => item)
                            .ToList(),
                    PartesPlantaSugeridas =
                    [
                        "EVIDENCIA",
                        "PLANTA_COMPLETA",
                        "HOJAS",
                        "HAZ_DE_LA_HOJA",
                        "ENVES_DE_LA_HOJA",
                        "BROTES",
                        "RAMAS",
                        "TALLO",
                        "FRUTOS",
                        "FLORES",
                        "RAICES",
                        "SUELO_ALREDEDOR",
                        "INSECTO_O_PLAGA",
                        "OTRA"
                    ],
                    MaximoFotografiasPorInspeccion =
                        gemini.ObtenerMaximoFotografiasPorInspeccion(),
                    TamanoBloqueIA =
                        gemini.ObtenerTamanoBloqueIA()
                }
            });
        }

        /// <summary>
        /// Guarda primero las fotografías en almacenamiento persistente y
        /// después solicita el resultado preliminar a Gemini.
        /// </summary>
        [HttpPost("analizar")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(700L * 1024L * 1024L)]
        public async Task<IActionResult> Analizar(
            [FromForm] DiagnosticoIACrearRequest request,
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

            List<IFormFile> fotos = (request.Fotos ?? [])
                .Where(item => item != null && item.Length > 0)
                .ToList();

            IActionResult? validacionFotos = ValidarFotos(fotos);
            if (validacionFotos != null)
                return validacionFotos;

            (int? terrenoId, string codigoTerreno, IActionResult? errorTerreno) =
                await ResolverTerrenoAsync(
                    request.CodigoTerreno,
                    cancellationToken);

            if (errorTerreno != null)
                return errorTerreno;

            var diagnostico = new DiagnosticoIA
            {
                TerrenoId = terrenoId,
                CodigoTerreno = codigoTerreno,
                UsuarioSolicitanteId = usuarioId!.Value,
                FechaSolicitudUtc = DateTime.UtcNow,
                Estado = DiagnosticoIAFlujo.Estados.AnalizandoIA,
                ModeloGemini = gemini.ObtenerModeloConfigurado(),
                ObservacionUsuario = Normalizar(request.Observacion, 1000),
                RequiereValidacionHumana = true,
                Activo = true
            };

            diagnosticoDb.Diagnosticos.Add(diagnostico);
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
                            $"diagnosticos-ia/{diagnostico.DiagnosticoIAId}",
                            anchoMaximo: 1600,
                            altoMaximo: 1600,
                            calidad: 76);

                    rutasGuardadas.Add(rutaRelativa);

                    diagnostico.Imagenes.Add(
                        new DiagnosticoIAImagen
                        {
                            RutaRelativa = rutaRelativa,
                            UrlImagen = ConstruirUrlPublica(rutaRelativa),
                            NombreArchivoOriginal =
                                Normalizar(foto.FileName, 255),
                            TipoFotografia = ResolverTipoFotografia(
                                request.TiposFotografia,
                                indice),
                            Orden = indice + 1,
                            FechaRegistroUtc = DateTime.UtcNow
                        });
                }

                AgregarHistorial(
                    diagnostico,
                    usuarioId.Value,
                    string.Empty,
                    DiagnosticoIAFlujo.Estados.AnalizandoIA,
                    "SOLICITUD_CREADA",
                    $"Se registraron {fotos.Count} fotografías para análisis.");

                await diagnosticoDb.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                foreach (string ruta in rutasGuardadas)
                    EliminarImagenSeguro(ruta);

                diagnosticoDb.Diagnosticos.Remove(diagnostico);
                await diagnosticoDb.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            return await EjecutarAnalisisGeminiAsync(
                diagnostico,
                usuarioId.Value,
                cancellationToken);
        }

        [HttpPost("{id:int}/reintentar-ia")]
        public async Task<IActionResult> ReintentarIA(
            int id,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            DiagnosticoIA? diagnostico = await CargarDiagnosticoAsync(
                id,
                cancellationToken);

            if (diagnostico == null || !diagnostico.Activo)
                return NoEncontrado();

            bool esPropietario =
                diagnostico.UsuarioSolicitanteId == usuarioId;

            bool puedeSolicitar = esPropietario &&
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

            if (!puedeSolicitar && !puedeAnalizar)
                return Forbid();

            bool requiereReconstruirResultadosIndividuales =
                diagnostico.Imagenes.Count > 0 &&
                diagnostico.Imagenes.Any(imagen =>
                    imagen.ResultadoIA == null ||
                    EsResultadoTecnicoIncompleto(
                        imagen.ResultadoIA));

            if (diagnostico.Estado !=
                    DiagnosticoIAFlujo.Estados.ErrorAnalisis &&
                !requiereReconstruirResultadosIndividuales)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Solo se puede reintentar un diagnóstico con error o con resultados individuales incompletos."
                });
            }

            EliminarResultadosTecnicosIncompletos(diagnostico);

            string anterior = diagnostico.Estado;
            diagnostico.Estado = DiagnosticoIAFlujo.Estados.AnalizandoIA;
            diagnostico.ErrorAnalisis = string.Empty;
            diagnostico.ModeloGemini = gemini.ObtenerModeloConfigurado();

            AgregarHistorial(
                diagnostico,
                usuarioId!.Value,
                anterior,
                diagnostico.Estado,
                "REINTENTO_IA",
                "Se solicitó nuevamente el análisis de las fotografías existentes.");

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            return await EjecutarAnalisisGeminiAsync(
                diagnostico,
                usuarioId.Value,
                cancellationToken);
        }

        [HttpPost("{id:int}/anular")]
        public async Task<IActionResult> Anular(
            int id,
            [FromBody] DiagnosticoIAAnularRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazSolicitud,
                TipoPermisoApi.Eliminar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DiagnosticoIA? diagnostico = await CargarDiagnosticoAsync(
                id,
                cancellationToken);

            if (diagnostico == null || !diagnostico.Activo)
                return NoEncontrado();

            string[] estadosPermitidos =
            [
                DiagnosticoIAFlujo.Estados.Rechazado,
                DiagnosticoIAFlujo.Estados.NoConcluyente,
                DiagnosticoIAFlujo.Estados.ErrorAnalisis
            ];

            if (!estadosPermitidos.Contains(
                    diagnostico.Estado,
                    StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Solo se pueden anular análisis rechazados, no concluyentes o con error."
                });
            }

            string motivo = Normalizar(request.Motivo, 1000);

            if (motivo.Length < 8)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Debe indicar un motivo de anulación de al menos 8 caracteres."
                });
            }

            string anterior = diagnostico.Estado;
            diagnostico.Estado = DiagnosticoIAFlujo.Estados.Anulado;
            diagnostico.Activo = false;

            AgregarHistorial(
                diagnostico,
                usuarioId!.Value,
                anterior,
                diagnostico.Estado,
                "ANULACION_LOGICA",
                motivo);

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message =
                    "El análisis fue anulado. Sus evidencias e historial permanecen conservados.",
                data = new
                {
                    diagnostico.DiagnosticoIAId,
                    diagnostico.Estado,
                    diagnostico.Activo
                }
            });
        }

        [HttpGet("mis-solicitudes")]
        public async Task<IActionResult> MisSolicitudes(
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazSolicitud,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            List<DiagnosticoIA> diagnosticos =
                await CargarListadoBase()
                    .Where(item =>
                        item.Activo &&
                        item.UsuarioSolicitanteId == usuarioId!.Value)
                    .OrderByDescending(item => item.FechaSolicitudUtc)
                    .Take(150)
                    .ToListAsync(cancellationToken);

            return await ResponderListadoAsync(
                diagnosticos,
                usuarioId.Value,
                cancellationToken);
        }

        [HttpGet("cola-analizador")]
        public async Task<IActionResult> ColaAnalizador(
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAnalizador,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            string[] estados =
            [
                DiagnosticoIAFlujo.Estados.PendienteAnalizador,
                DiagnosticoIAFlujo.Estados.EnAnalisisHumano,
                DiagnosticoIAFlujo.Estados.DevueltoCorreccion,
                DiagnosticoIAFlujo.Estados.ErrorAnalisis
            ];

            List<DiagnosticoIA> diagnosticos =
                await CargarListadoBase()
                    .Where(item =>
                        item.Activo &&
                        estados.Contains(item.Estado))
                    .OrderBy(item => item.FechaSolicitudUtc)
                    .Take(200)
                    .ToListAsync(cancellationToken);

            return await ResponderListadoAsync(
                diagnosticos,
                usuarioId!.Value,
                cancellationToken);
        }

        [HttpGet("cola-aprobador")]
        public async Task<IActionResult> ColaAprobador(
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            int[] pendientesPublicacion =
                await diagnosticoDb.EvaluacionesImagen
                    .AsNoTracking()
                    .Where(evaluacion =>
                        evaluacion.EsEvidenciaValida &&
                        evaluacion.AptaParaAlbum &&
                        evaluacion.Aprobacion.AutorizaPublicacionAlbum &&
                        (evaluacion.Aprobacion.Decision ==
                            DiagnosticoIAFlujo.DecisionAprobacion.AprobarSinCambios ||
                         evaluacion.Aprobacion.Decision ==
                            DiagnosticoIAFlujo.DecisionAprobacion.AprobarConCorreccion) &&
                        !diagnosticoDb.PublicacionesAlbum.Any(publicacion =>
                            publicacion.Activo &&
                            publicacion.DiagnosticoIAImagenId ==
                                evaluacion.DiagnosticoIAImagenId))
                    .Select(evaluacion =>
                        evaluacion.Aprobacion.DiagnosticoIAId)
                    .Distinct()
                    .ToArrayAsync(cancellationToken);

            List<DiagnosticoIA> diagnosticos =
                await CargarListadoBase()
                    .Where(item =>
                        item.Activo &&
                        (item.Estado ==
                            DiagnosticoIAFlujo.Estados.PendienteAprobacion ||
                         pendientesPublicacion.Contains(
                            item.DiagnosticoIAId)))
                    .OrderBy(item =>
                        item.Estado ==
                            DiagnosticoIAFlujo.Estados.PendienteAprobacion
                            ? 0
                            : 1)
                    .ThenBy(item => item.FechaSolicitudUtc)
                    .Take(200)
                    .ToListAsync(cancellationToken);

            return await ResponderListadoAsync(
                diagnosticos,
                usuarioId!.Value,
                cancellationToken);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Obtener(
            int id,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();
            DiagnosticoIA? diagnostico = await CargarDiagnosticoAsync(
                id,
                cancellationToken);

            if (diagnostico == null || !diagnostico.Activo)
                return NoEncontrado();

            bool permitido = await PuedeConsultarAsync(
                diagnostico,
                usuarioId,
                cancellationToken);

            if (!permitido)
                return Forbid();

            DiagnosticoIADetalleDto dto = await CrearDetalleDtoAsync(
                diagnostico,
                usuarioId!.Value,
                cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Diagnóstico obtenido correctamente.",
                data = dto
            });
        }

        [HttpPost("{id:int}/segunda-revision")]
        public async Task<IActionResult> SegundaRevision(
            int id,
            [FromBody] DiagnosticoIASegundaRevisionRequest request,
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

            DiagnosticoIA? diagnostico = await CargarDiagnosticoAsync(
                id,
                cancellationToken);

            if (diagnostico == null || !diagnostico.Activo)
                return NoEncontrado();

            if (diagnostico.Estado is not
                (DiagnosticoIAFlujo.Estados.PendienteAnalizador or
                 DiagnosticoIAFlujo.Estados.EnAnalisisHumano or
                 DiagnosticoIAFlujo.Estados.DevueltoCorreccion))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La segunda revisión solo está disponible durante el análisis humano."
                });
            }

            if (diagnostico.RevisionesIA.Any(item =>
                    item.Estado == "ANALIZANDO"))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe una revisión de Gemini en proceso para este diagnóstico."
                });
            }

            int completadas = diagnostico.RevisionesIA.Count(item =>
                item.Estado == "COMPLETADA");

            DiagnosticoIAConfiguracion configuracionRevisiones =
                await ObtenerConfiguracionRevisionesAsync(
                    cancellationToken);

            if (!configuracionRevisiones.RevisionesIlimitadas &&
                completadas >=
                    configuracionRevisiones.MaximoRevisionesGemini)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        $"Este diagnóstico ya alcanzó el máximo de {configuracionRevisiones.MaximoRevisionesGemini} revisiones adicionales permitido por la configuración del sistema."
                });
            }

            var revision = new DiagnosticoIARevision
            {
                DiagnosticoIAId = diagnostico.DiagnosticoIAId,
                UsuarioClasificadorId = usuarioId!.Value,
                RetroalimentacionClasificador =
                    Normalizar(request.RetroalimentacionAnalizador, 2000),
                DiagnosticoPropuestoClasificador =
                    Normalizar(request.DiagnosticoPropuestoAnalizador, 300),
                FechaSolicitudRevisionUtc = DateTime.UtcNow,
                Estado = "ANALIZANDO"
            };

            diagnosticoDb.RevisionesIA.Add(revision);
            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            try
            {
                GeminiRevisionResultado resultado = await gemini.RevisarAsync(
                    diagnostico.Imagenes.ToList(),
                    diagnostico,
                    revision.RetroalimentacionClasificador,
                    revision.DiagnosticoPropuestoClasificador,
                    cancellationToken);

                AplicarResultadoRevision(revision, resultado);
                revision.Estado = "COMPLETADA";
                revision.FechaRespuestaRevisionUtc = DateTime.UtcNow;

                AgregarHistorial(
                    diagnostico,
                    usuarioId.Value,
                    diagnostico.Estado,
                    diagnostico.Estado,
                    "SEGUNDA_REVISION_IA",
                    $"Gemini completó la revisión {completadas + 1} solicitada por el analizador.");

                await diagnosticoDb.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = "Gemini completó la revisión adicional.",
                    data = await CrearDetalleDtoAsync(
                        diagnostico,
                        usuarioId.Value,
                        cancellationToken)
                });
            }
            catch (GeminiApiException ex)
            {
                revision.Estado = "ERROR";
                revision.ErrorRevision = Normalizar(ex.Message, 2000);
                revision.FechaRespuestaRevisionUtc = DateTime.UtcNow;
                await diagnosticoDb.SaveChangesAsync(CancellationToken.None);

                return CrearRespuestaErrorGemini(ex, true, diagnostico.DiagnosticoIAId);
            }
            catch (InvalidOperationException ex)
            {
                revision.Estado = "ERROR";
                revision.ErrorRevision = Normalizar(ex.Message, 2000);
                revision.FechaRespuestaRevisionUtc = DateTime.UtcNow;
                await diagnosticoDb.SaveChangesAsync(CancellationToken.None);

                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        success = false,
                        message = ex.Message,
                        detail = ex.Message
                    });
            }
        }

        [HttpPost("{id:int}/analisis-humano/guardar")]
        public async Task<IActionResult> GuardarAnalisisHumano(
            int id,
            [FromBody] DiagnosticoIAAnalisisHumanoRequest request,
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

            DiagnosticoIA? diagnostico = await CargarDiagnosticoAsync(
                id,
                cancellationToken);

            if (diagnostico == null || !diagnostico.Activo)
                return NoEncontrado();

            if (diagnostico.Estado is not
                (DiagnosticoIAFlujo.Estados.PendienteAnalizador or
                 DiagnosticoIAFlujo.Estados.EnAnalisisHumano or
                 DiagnosticoIAFlujo.Estados.DevueltoCorreccion))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El diagnóstico no se encuentra disponible para análisis humano."
                });
            }

            string? error = ValidarClasificacionHumana(request);
            if (error != null)
                return BadRequest(new { success = false, message = error });

            DiagnosticoIAAnalisisHumano? analisis =
                diagnostico.AnalisisHumanos
                    .OrderByDescending(item => item.Version)
                    .FirstOrDefault(item =>
                        item.EstadoRegistro ==
                            DiagnosticoIAFlujo.EstadoAnalisisHumano.Borrador &&
                        item.UsuarioAnalizadorId == usuarioId!.Value);

            bool nuevo = analisis == null;

            if (analisis == null)
            {
                int siguienteVersion = diagnostico.AnalisisHumanos.Count == 0
                    ? 1
                    : diagnostico.AnalisisHumanos.Max(item => item.Version) + 1;

                analisis = new DiagnosticoIAAnalisisHumano
                {
                    DiagnosticoIAId = diagnostico.DiagnosticoIAId,
                    UsuarioAnalizadorId = usuarioId!.Value,
                    Version = siguienteVersion,
                    EstadoRegistro =
                        DiagnosticoIAFlujo.EstadoAnalisisHumano.Borrador,
                    FechaCreacionUtc = DateTime.UtcNow
                };

                diagnosticoDb.AnalisisHumanos.Add(analisis);
            }

            AplicarAnalisisHumano(analisis, request);
            analisis.FechaActualizacionUtc = DateTime.UtcNow;

            string estadoAnterior = diagnostico.Estado;
            diagnostico.Estado =
                DiagnosticoIAFlujo.Estados.EnAnalisisHumano;

            AgregarHistorial(
                diagnostico,
                usuarioId!.Value,
                estadoAnterior,
                diagnostico.Estado,
                nuevo ? "ANALISIS_HUMANO_CREADO" : "ANALISIS_HUMANO_ACTUALIZADO",
                $"Se guardó la versión {analisis.Version} del análisis humano.");

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Análisis humano guardado como borrador.",
                data = await CrearDetalleDtoAsync(
                    diagnostico,
                    usuarioId.Value,
                    cancellationToken)
            });
        }

        [HttpPost("{id:int}/analisis-humano/enviar")]
        public async Task<IActionResult> EnviarAnalisisHumano(
            int id,
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

            DiagnosticoIA? diagnostico = await CargarDiagnosticoAsync(
                id,
                cancellationToken);

            if (diagnostico == null || !diagnostico.Activo)
                return NoEncontrado();

            DiagnosticoIAAnalisisHumano? analisis =
                diagnostico.AnalisisHumanos
                    .OrderByDescending(item => item.Version)
                    .FirstOrDefault(item =>
                        item.EstadoRegistro ==
                            DiagnosticoIAFlujo.EstadoAnalisisHumano.Borrador &&
                        item.UsuarioAnalizadorId == usuarioId!.Value);

            if (analisis == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Debe guardar una clasificación humana antes de enviarla."
                });
            }

            analisis.EstadoRegistro =
                DiagnosticoIAFlujo.EstadoAnalisisHumano.Enviado;
            analisis.FechaEnvioUtc = DateTime.UtcNow;
            analisis.FechaActualizacionUtc = DateTime.UtcNow;

            string anterior = diagnostico.Estado;
            diagnostico.Estado =
                DiagnosticoIAFlujo.Estados.PendienteAprobacion;

            AgregarHistorial(
                diagnostico,
                usuarioId!.Value,
                anterior,
                diagnostico.Estado,
                "ENVIADO_APROBACION",
                $"La versión {analisis.Version} fue enviada para aprobación.");

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "El análisis fue enviado para aprobación.",
                data = await CrearDetalleDtoAsync(
                    diagnostico,
                    usuarioId.Value,
                    cancellationToken)
            });
        }

        [HttpPost("{id:int}/aprobacion")]
        public async Task<IActionResult> RegistrarAprobacion(
            int id,
            [FromBody] DiagnosticoIAAprobacionRequest request,
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

            /*
             * Serializa el cambio de estado para impedir dos aprobaciones
             * simultáneas sobre la misma versión del análisis humano.
             */
            await using var transaccion =
                await diagnosticoDb.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            DiagnosticoIA? diagnostico = await CargarDiagnosticoAsync(
                id,
                cancellationToken);

            if (diagnostico == null || !diagnostico.Activo)
                return NoEncontrado();

            if (diagnostico.Estado !=
                DiagnosticoIAFlujo.Estados.PendienteAprobacion)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "El diagnóstico no se encuentra pendiente de aprobación."
                });
            }

            DiagnosticoIAAnalisisHumano? analisis =
                diagnostico.AnalisisHumanos
                    .Where(item =>
                        item.EstadoRegistro ==
                            DiagnosticoIAFlujo.EstadoAnalisisHumano.Enviado)
                    .OrderByDescending(item => item.Version)
                    .FirstOrDefault();

            if (analisis == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "No existe un análisis humano enviado para aprobar."
                });
            }

            string decision = (request.Decision ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            if (!DiagnosticoIAFlujo.DecisionAprobacion.Todos.Contains(
                    decision))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La decisión de aprobación no es válida."
                });
            }

            List<DiagnosticoIAImagenEvaluacionRequest> evaluacionesImagen =
                request.EvaluacionesImagen ?? [];

            string? errorAprobacion = ValidarAprobacion(
                request,
                evaluacionesImagen,
                decision,
                diagnostico.Imagenes.Select(item => item.DiagnosticoIAImagenId));

            if (errorAprobacion != null)
            {
                return BadRequest(new
                {
                    success = false,
                    message = errorAprobacion
                });
            }

            var aprobacion = CrearAprobacion(
                diagnostico,
                analisis,
                request,
                decision,
                usuarioId!.Value);

            diagnosticoDb.Aprobaciones.Add(aprobacion);

            foreach (DiagnosticoIAImagenEvaluacionRequest item
                     in evaluacionesImagen)
            {
                aprobacion.EvaluacionesImagen.Add(
                    new DiagnosticoIAImagenEvaluacion
                    {
                        DiagnosticoIAImagenId =
                            item.DiagnosticoIAImagenId,
                        UsuarioAprobadorId = usuarioId.Value,
                        CalidadTecnica = DiagnosticoIAFlujo.Normalizar(
                            item.CalidadTecnica,
                            DiagnosticoIAFlujo.CalidadImagen.Todos,
                            DiagnosticoIAFlujo.CalidadImagen.NoEvaluable),
                        EsEvidenciaValida = item.EsEvidenciaValida,
                        AptaParaAlbum =
                            aprobacion.AutorizaPublicacionAlbum &&
                            item.EsEvidenciaValida &&
                            item.AptaParaAlbum,
                        Observacion = Normalizar(item.Observacion, 1000),
                        FechaEvaluacionUtc = DateTime.UtcNow
                    });
            }

            string anterior = diagnostico.Estado;
            string detalle;

            switch (decision)
            {
                case DiagnosticoIAFlujo.DecisionAprobacion.AprobarSinCambios:
                    diagnostico.Estado =
                        DiagnosticoIAFlujo.Estados.Aprobado;
                    detalle = "El aprobador confirmó el análisis sin cambios.";
                    break;

                case DiagnosticoIAFlujo.DecisionAprobacion.AprobarConCorreccion:
                    diagnostico.Estado =
                        DiagnosticoIAFlujo.Estados.AprobadoConCorreccion;
                    detalle = "El aprobador registró una clasificación final corregida.";
                    break;

                case DiagnosticoIAFlujo.DecisionAprobacion.Devolver:
                    diagnostico.Estado =
                        DiagnosticoIAFlujo.Estados.DevueltoCorreccion;
                    analisis.EstadoRegistro =
                        DiagnosticoIAFlujo.EstadoAnalisisHumano.Devuelto;
                    detalle = "El caso fue devuelto al analizador para corrección.";
                    break;

                case DiagnosticoIAFlujo.DecisionAprobacion.Rechazar:
                    diagnostico.Estado =
                        DiagnosticoIAFlujo.Estados.Rechazado;
                    detalle = "El veredicto fue rechazado por el aprobador.";
                    break;

                default:
                    diagnostico.Estado =
                        DiagnosticoIAFlujo.Estados.NoConcluyente;
                    detalle = "El aprobador determinó que el caso no es concluyente.";
                    break;
            }

            AgregarHistorial(
                diagnostico,
                usuarioId.Value,
                anterior,
                diagnostico.Estado,
                decision,
                detalle);

            await diagnosticoDb.SaveChangesAsync(cancellationToken);
            await transaccion.CommitAsync(cancellationToken);

            DiagnosticoIADetalleDto detalleActualizado =
                await CrearDetalleDtoAsync(
                    diagnostico,
                    usuarioId.Value,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = detalle,
                data = detalleActualizado
            });
        }

        [HttpGet("album/catalogo")]
        public async Task<IActionResult> CatalogoAlbum(
            [FromQuery] int? categoriaId = null,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            IActionResult? accesoAprobador = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (accesoAprobador != null)
                return accesoAprobador;

            IActionResult? accesoAlbum = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAlbum,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (accesoAlbum != null)
                return accesoAlbum;

            List<DiagnosticoIAAlbumCategoriaDto> categorias =
                await diagnosticoDb.CategoriasAlbum
                    .AsNoTracking()
                    .Where(item => item.Activo)
                    .OrderBy(item => item.NombreCategoria)
                    .Select(item => new DiagnosticoIAAlbumCategoriaDto
                    {
                        CategoriaAlbumBotanicoId =
                            item.CategoriaAlbumBotanicoId,
                        NombreCategoria = item.NombreCategoria
                    })
                    .ToListAsync(cancellationToken);

            var registrosQuery = diagnosticoDb.RegistrosAlbum
                .AsNoTracking()
                .Where(item => item.Activo);

            if (categoriaId is > 0)
            {
                registrosQuery = registrosQuery.Where(item =>
                    item.CategoriaAlbumBotanicoId == categoriaId.Value);
            }

            List<DiagnosticoIAAlbumRegistroDto> registros =
                await registrosQuery
                    .OrderBy(item => item.Titulo)
                    .Select(item => new DiagnosticoIAAlbumRegistroDto
                    {
                        AlbumBotanicoCafeId = item.AlbumBotanicoCafeId,
                        CategoriaAlbumBotanicoId =
                            item.CategoriaAlbumBotanicoId,
                        Titulo = item.Titulo
                    })
                    .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Catálogo del álbum obtenido correctamente.",
                data = new DiagnosticoIAAlbumCatalogoDto
                {
                    Categorias = categorias,
                    Registros = registros
                }
            });
        }

        [HttpPost("{id:int}/publicar-album")]
        public async Task<IActionResult> PublicarAlbum(
            int id,
            [FromBody] DiagnosticoIAPublicarAlbumRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            IActionResult? accesoAprobador = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (accesoAprobador != null)
                return accesoAprobador;

            IActionResult? lecturaAlbum = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAlbum,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (lecturaAlbum != null)
                return lecturaAlbum;

            IActionResult? agregarAlbum = await ValidarPermisoAsync(
                usuarioId,
                DiagnosticoIAFlujo.InterfazAlbum,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (agregarAlbum != null)
                return agregarAlbum;

            DiagnosticoIA? diagnostico = await CargarDiagnosticoAsync(
                id,
                cancellationToken);

            if (diagnostico == null || !diagnostico.Activo)
                return NoEncontrado();

            if (!DiagnosticoIAFlujo.EsEstadoAprobado(diagnostico.Estado))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Solo los diagnósticos aprobados pueden aportar fotografías al álbum."
                });
            }

            DiagnosticoIAAprobacion? aprobacion = diagnostico.Aprobaciones
                .Where(item =>
                    item.Decision ==
                        DiagnosticoIAFlujo.DecisionAprobacion.AprobarSinCambios ||
                    item.Decision ==
                        DiagnosticoIAFlujo.DecisionAprobacion.AprobarConCorreccion)
                .OrderByDescending(item => item.FechaAprobacionUtc)
                .FirstOrDefault();

            if (aprobacion == null ||
                !aprobacion.AutorizaPublicacionAlbum)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La aprobación vigente no autorizó el uso de fotografías en el álbum."
                });
            }

            List<DiagnosticoIAPublicarAlbumImagenRequest> imagenesSolicitadas =
                request.Imagenes ?? [];

            if (imagenesSolicitadas.Count == 0 ||
                imagenesSolicitadas.Select(item => item.DiagnosticoIAImagenId)
                    .Distinct()
                    .Count() != imagenesSolicitadas.Count)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Seleccione al menos una fotografía sin repetir elementos."
                });
            }

            if (imagenesSolicitadas.Count(item => item.EsPortada) > 1)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Solo una fotografía puede establecerse como portada."
                });
            }

            CategoriaAlbumBotanicoReferencia? categoria =
                await diagnosticoDb.CategoriasAlbum
                    .FirstOrDefaultAsync(item =>
                        item.CategoriaAlbumBotanicoId ==
                            request.CategoriaAlbumBotanicoId &&
                        item.Activo,
                        cancellationToken);

            AlbumBotanicoCafeReferencia? registro =
                await diagnosticoDb.RegistrosAlbum
                    .FirstOrDefaultAsync(item =>
                        item.AlbumBotanicoCafeId ==
                            request.AlbumBotanicoCafeId &&
                        item.CategoriaAlbumBotanicoId ==
                            request.CategoriaAlbumBotanicoId &&
                        item.Activo,
                        cancellationToken);

            if (categoria == null || registro == null)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La categoría o el registro del álbum no existe, está inactivo o no coinciden."
                });
            }

            HashSet<int> aptas = aprobacion.EvaluacionesImagen
                .Where(item =>
                    item.EsEvidenciaValida &&
                    item.AptaParaAlbum)
                .Select(item => item.DiagnosticoIAImagenId)
                .ToHashSet();

            int[] solicitadas = imagenesSolicitadas
                .Select(item => item.DiagnosticoIAImagenId)
                .ToArray();

            if (solicitadas.Any(item => !aptas.Contains(item)))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Una o más fotografías no fueron autorizadas por el aprobador para el álbum."
                });
            }

            if (diagnostico.PublicacionesAlbum.Any(item =>
                    item.Activo && solicitadas.Contains(
                        item.DiagnosticoIAImagenId)))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Una o más fotografías seleccionadas ya fueron publicadas en el álbum."
                });
            }

            Dictionary<int, DiagnosticoIAImagen> imagenes =
                diagnostico.Imagenes
                    .Where(item => solicitadas.Contains(
                        item.DiagnosticoIAImagenId))
                    .ToDictionary(item => item.DiagnosticoIAImagenId);

            if (imagenes.Count != solicitadas.Length)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Una de las fotografías no pertenece al diagnóstico."
                });
            }

            var archivosCreados = new List<string>();
            var idsFotos = new List<int>();

            await using var transaccion =
                await diagnosticoDb.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                if (imagenesSolicitadas.Any(item => item.EsPortada))
                {
                    List<AlbumBotanicoCafeFotoReferencia> portadas =
                        await diagnosticoDb.FotosAlbum
                            .Where(item =>
                                item.AlbumBotanicoCafeId ==
                                    registro.AlbumBotanicoCafeId &&
                                item.Activo &&
                                item.EsPortada)
                            .ToListAsync(cancellationToken);

                    foreach (var portada in portadas)
                        portada.EsPortada = false;
                }

                foreach (DiagnosticoIAPublicarAlbumImagenRequest item
                         in imagenesSolicitadas)
                {
                    DiagnosticoIAImagen origen =
                        imagenes[item.DiagnosticoIAImagenId];

                    string rutaAlbum = await CopiarImagenAlAlbumAsync(
                        origen.RutaRelativa,
                        registro.AlbumBotanicoCafeId,
                        cancellationToken);

                    archivosCreados.Add(rutaAlbum);

                    var fotoAlbum = new AlbumBotanicoCafeFotoReferencia
                    {
                        AlbumBotanicoCafeId =
                            registro.AlbumBotanicoCafeId,
                        RutaFoto = rutaAlbum,
                        DescripcionFoto =
                            Normalizar(item.Descripcion, 500),
                        EsPortada = item.EsPortada,
                        Orden = item.Orden,
                        Activo = true
                    };

                    diagnosticoDb.FotosAlbum.Add(fotoAlbum);
                    await diagnosticoDb.SaveChangesAsync(cancellationToken);
                    idsFotos.Add(fotoAlbum.AlbumBotanicoCafeFotoId);

                    diagnostico.PublicacionesAlbum.Add(
                        new DiagnosticoIAAlbumPublicacion
                        {
                            DiagnosticoIAImagenId =
                                origen.DiagnosticoIAImagenId,
                            CategoriaAlbumBotanicoId =
                                categoria.CategoriaAlbumBotanicoId,
                            AlbumBotanicoCafeId =
                                registro.AlbumBotanicoCafeId,
                            AlbumBotanicoCafeFotoId =
                                fotoAlbum.AlbumBotanicoCafeFotoId,
                            UsuarioPublicacionId = usuarioId!.Value,
                            FechaPublicacionUtc = DateTime.UtcNow,
                            DescripcionPublicacion =
                                Normalizar(item.Descripcion, 1000),
                            ClasificacionFinal =
                                aprobacion.CategoriaPrincipalFinal,
                            DiagnosticoFinal =
                                aprobacion.DiagnosticoFinal,
                            RutaFotoAlbum = rutaAlbum,
                            Activo = true
                        });
                }

                string anterior = diagnostico.Estado;

                HashSet<int> publicadasDespues = diagnostico
                    .PublicacionesAlbum
                    .Where(item => item.Activo)
                    .Select(item => item.DiagnosticoIAImagenId)
                    .ToHashSet();

                bool publicacionCompleta =
                    aptas.All(publicadasDespues.Contains);

                diagnostico.Estado = publicacionCompleta
                    ? DiagnosticoIAFlujo.Estados.PublicadoAlbum
                    : aprobacion.Decision ==
                        DiagnosticoIAFlujo.DecisionAprobacion.AprobarConCorreccion
                            ? DiagnosticoIAFlujo.Estados.AprobadoConCorreccion
                            : DiagnosticoIAFlujo.Estados.Aprobado;

                AgregarHistorial(
                    diagnostico,
                    usuarioId!.Value,
                    anterior,
                    diagnostico.Estado,
                    "PUBLICACION_ALBUM",
                    $"Se publicaron {imagenesSolicitadas.Count} fotografías en {categoria.NombreCategoria} → {registro.Titulo}. " +
                    (publicacionCompleta
                        ? "Todas las fotografías autorizadas ya fueron publicadas."
                        : "Aún quedan fotografías autorizadas pendientes de publicación."));

                await diagnosticoDb.SaveChangesAsync(cancellationToken);
                await transaccion.CommitAsync(cancellationToken);

                foreach (string ruta in archivosCreados)
                {
                    try
                    {
                        await imageService.ObtenerOCrearMiniaturaAsync(
                            ruta,
                            cancellationToken: cancellationToken);
                    }
                    catch
                    {
                    }
                }

                return Ok(new
                {
                    success = true,
                    message =
                        "Las fotografías aprobadas fueron copiadas al álbum botánico.",
                    data = new DiagnosticoIAPublicacionResultadoDto
                    {
                        TotalPublicadas = idsFotos.Count,
                        AlbumBotanicoCafeId =
                            registro.AlbumBotanicoCafeId,
                        AlbumBotanicoCafeFotoIds = idsFotos
                    }
                });
            }
            catch (Exception ex)
            {
                await transaccion.RollbackAsync(CancellationToken.None);

                foreach (string ruta in archivosCreados)
                    EliminarImagenSeguro(ruta);

                logger.LogError(
                    ex,
                    "No fue posible publicar fotografías del diagnóstico {DiagnosticoId} en el álbum.",
                    id);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message =
                            "No fue posible completar la publicación en el álbum."
                    });
            }
        }

        private async Task<IActionResult> EjecutarAnalisisGeminiAsync(
            DiagnosticoIA diagnostico,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            try
            {
                GeminiDiagnosticoResultado resultado =
                    await gemini.AnalizarAsync(
                        diagnostico.Imagenes.ToList(),
                        diagnostico.ObservacionUsuario,
                        cancellationToken);

                ValidarResultadoGeminiCompleto(
                    diagnostico,
                    resultado);

                AplicarResultadoGemini(diagnostico, resultado);

                string anterior = diagnostico.Estado;
                diagnostico.Estado =
                    DiagnosticoIAFlujo.Estados.PendienteAnalizador;
                diagnostico.FechaRespuestaIAUtc = DateTime.UtcNow;
                diagnostico.ErrorAnalisis = string.Empty;

                AgregarHistorial(
                    diagnostico,
                    usuarioId,
                    anterior,
                    diagnostico.Estado,
                    "IA_COMPLETADA",
                    "Gemini registró una clasificación preliminar pendiente de análisis humano.");

                await diagnosticoDb.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Gemini completó el análisis preliminar. El caso quedó pendiente de revisión humana.",
                    data = await CrearDetalleDtoAsync(
                        diagnostico,
                        usuarioId,
                        cancellationToken)
                });
            }
            catch (GeminiApiException ex)
            {
                string mensajeAmigable =
                    ResolverMensajeErrorGemini(
                        ex,
                        esSegundaRevision: false);

                await RegistrarErrorAnalisisAsync(
                    diagnostico,
                    usuarioId,
                    mensajeAmigable);

                return CrearRespuestaErrorGemini(
                    ex,
                    false,
                    diagnostico.DiagnosticoIAId);
            }
            catch (InvalidOperationException ex)
            {
                await RegistrarErrorAnalisisAsync(
                    diagnostico,
                    usuarioId,
                    ex.Message);

                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        success = false,
                        message = ex.Message,
                        detail = ex.Message,
                        data = new
                        {
                            diagnostico.DiagnosticoIAId
                        }
                    });
            }
            catch (OperationCanceledException)
            {
                await RegistrarErrorAnalisisAsync(
                    diagnostico,
                    usuarioId,
                    "La solicitud fue cancelada antes de que Gemini respondiera.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error inesperado al analizar el diagnóstico {DiagnosticoId}.",
                    diagnostico.DiagnosticoIAId);

                await RegistrarErrorAnalisisAsync(
                    diagnostico,
                    usuarioId,
                    ex.Message);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message =
                            "Las fotografías se guardaron, pero ocurrió un error inesperado al solicitar el análisis.",
                        detail = ex.Message,
                        data = new
                        {
                            diagnostico.DiagnosticoIAId
                        }
                    });
            }
        }

        private async Task RegistrarErrorAnalisisAsync(
            DiagnosticoIA diagnostico,
            int usuarioId,
            string error)
        {
            string anterior = diagnostico.Estado;
            diagnostico.Estado =
                DiagnosticoIAFlujo.Estados.ErrorAnalisis;
            diagnostico.ErrorAnalisis = Normalizar(error, 2000);
            diagnostico.FechaRespuestaIAUtc = DateTime.UtcNow;

            AgregarHistorial(
                diagnostico,
                usuarioId,
                anterior,
                diagnostico.Estado,
                "ERROR_IA",
                diagnostico.ErrorAnalisis);

            await diagnosticoDb.SaveChangesAsync(CancellationToken.None);
        }

        private IActionResult CrearRespuestaErrorGemini(
            GeminiApiException ex,
            bool esRevision,
            int diagnosticoIAId)
        {
            (int codigo, string _) = MapearErrorGemini(
                ex.StatusCode,
                esRevision);

            string mensaje = ResolverMensajeErrorGemini(
                ex,
                esRevision);

            return StatusCode(
                codigo,
                new
                {
                    success = false,
                    message = mensaje,
                    detail = mensaje,
                    data = new
                    {
                        DiagnosticoIAId = diagnosticoIAId
                    }
                });
        }

        private void EliminarResultadosTecnicosIncompletos(
            DiagnosticoIA diagnostico)
        {
            foreach (DiagnosticoIAImagen imagen in diagnostico.Imagenes)
            {
                DiagnosticoIAImagenResultadoIA? resultado =
                    imagen.ResultadoIA;

                if (resultado == null ||
                    !EsResultadoTecnicoIncompleto(resultado))
                {
                    continue;
                }

                diagnosticoDb.ResultadosImagenIA.Remove(resultado);
                imagen.ResultadoIA = null;
            }
        }

        private static bool EsResultadoTecnicoIncompleto(
            DiagnosticoIAImagenResultadoIA resultado) =>
            resultado.ResumenImagen.Contains(
                "Gemini no devolvió un resultado individual",
                StringComparison.OrdinalIgnoreCase);

        private static void ValidarResultadoGeminiCompleto(
            DiagnosticoIA diagnostico,
            GeminiDiagnosticoResultado resultado)
        {
            List<int> esperadas = diagnostico.Imagenes
                .Select(item => item.Orden)
                .OrderBy(item => item)
                .ToList();

            List<int> recibidas = resultado.ResultadosPorImagen
                .Select(item => item.Orden)
                .Distinct()
                .OrderBy(item => item)
                .ToList();

            bool hayDuplicados = resultado.ResultadosPorImagen
                .GroupBy(item => item.Orden)
                .Any(group => group.Count() > 1);

            bool hayMensajeTecnico =
                resultado.ResultadosPorImagen.Any(item =>
                    item.ResumenImagen.Contains(
                        "Gemini no devolvió",
                        StringComparison.OrdinalIgnoreCase));

            if (esperadas.SequenceEqual(recibidas) &&
                resultado.ResultadosPorImagen.Count ==
                    diagnostico.Imagenes.Count &&
                !hayDuplicados &&
                !hayMensajeTecnico)
            {
                return;
            }

            throw new GeminiApiException(
                HttpStatusCode.BadGateway,
                "Gemini devolvió una respuesta incompleta por fotografía. La solicitud no avanzará al analizador.",
                $"Esperadas: {string.Join(", ", esperadas)}. " +
                $"Recibidas: {string.Join(", ", recibidas)}.");
        }

        private static string ResolverMensajeErrorGemini(
            GeminiApiException ex,
            bool esSegundaRevision)
        {
            if (ex.StatusCode == HttpStatusCode.BadGateway &&
                (ex.Message.Contains(
                     "resultado individual",
                     StringComparison.OrdinalIgnoreCase) ||
                 ex.Message.Contains(
                     "respuesta incompleta por fotografía",
                     StringComparison.OrdinalIgnoreCase)))
            {
                return ex.Message;
            }

            return MapearErrorGemini(
                ex.StatusCode,
                esSegundaRevision).Mensaje;
        }

        private static void AplicarResultadoGemini(
            DiagnosticoIA diagnostico,
            GeminiDiagnosticoResultado resultado)
        {
            diagnostico.ImagenValida = resultado.ImagenValida;
            diagnostico.ParecePlantaCafe = resultado.ParecePlantaCafe;
            diagnostico.ResultadoConcluyente =
                resultado.ResultadoConcluyente;
            diagnostico.CalidadEvaluacionIA =
                resultado.CalidadEvaluacion;
            diagnostico.EstadoGeneralIA = resultado.EstadoGeneral;
            diagnostico.CategoriaPrincipalIA =
                resultado.CategoriaPrincipal;
            diagnostico.CategoriasSecundariasIAJson =
                SerializarLista(resultado.CategoriasSecundarias);
            diagnostico.DiagnosticoSugerido =
                resultado.DiagnosticoSugerido;
            diagnostico.TipoDiagnosticoIA =
                resultado.TipoDiagnostico;
            diagnostico.SeveridadVisualIA =
                resultado.SeveridadVisual;
            diagnostico.NivelCoincidencia =
                resultado.NivelCoincidencia;
            diagnostico.Resumen = resultado.Resumen;
            diagnostico.PartesAfectadasJson =
                SerializarLista(resultado.PartesAfectadas);
            diagnostico.SintomasVisiblesJson =
                SerializarLista(resultado.SintomasVisibles);
            diagnostico.EvidenciasNoObservadasJson =
                SerializarLista(resultado.EvidenciasNoObservadas);
            diagnostico.DiagnosticosAlternativosJson =
                SerializarLista(resultado.DiagnosticosAlternativos);
            diagnostico.InformacionFaltanteJson =
                SerializarLista(resultado.InformacionFaltante);
            diagnostico.RecomendacionesCapturaJson =
                SerializarLista(resultado.RecomendacionesCaptura);
            diagnostico.AdvertenciasJson =
                SerializarLista(resultado.Advertencias);
            diagnostico.PosibleDanoNoBiotico =
                resultado.PosibleDanoNoBiotico;
            diagnostico.PosibleCausaNoBiotica =
                resultado.PosibleCausaNoBiotica;
            diagnostico.RespuestaOriginalJson =
                resultado.RespuestaOriginalJson;

            foreach (GeminiImagenResultado resultadoImagen
                     in resultado.ResultadosPorImagen)
            {
                DiagnosticoIAImagen? imagen = diagnostico.Imagenes
                    .FirstOrDefault(item =>
                        item.Orden == resultadoImagen.Orden);

                if (imagen == null)
                    continue;

                imagen.ResultadoIA ??=
                    new DiagnosticoIAImagenResultadoIA();

                AplicarResultadoImagen(
                    imagen.ResultadoIA,
                    resultadoImagen);
            }
        }

        private static void AplicarResultadoImagen(
            DiagnosticoIAImagenResultadoIA destino,
            GeminiImagenResultado origen)
        {
            destino.ImagenValida = origen.ImagenValida;
            destino.ParecePlantaCafe = origen.ParecePlantaCafe;
            destino.ResultadoConcluyente = origen.ResultadoConcluyente;
            destino.PartePlanta = Normalizar(origen.PartePlanta, 80);
            destino.CalidadEvaluacion = origen.CalidadEvaluacion;
            destino.EstadoGeneral = origen.EstadoGeneral;
            destino.CategoriaPrincipal = origen.CategoriaPrincipal;
            destino.CategoriasSecundariasJson =
                SerializarLista(origen.CategoriasSecundarias);
            destino.DiagnosticoProbable =
                Normalizar(origen.DiagnosticoProbable, 300);
            destino.TipoDiagnostico =
                Normalizar(origen.TipoDiagnostico, 80);
            destino.SeveridadVisual = origen.SeveridadVisual;
            destino.NivelCerteza = origen.NivelCerteza;
            destino.ResumenImagen =
                Normalizar(origen.ResumenImagen, 1600);
            destino.SintomasVisiblesJson =
                SerializarLista(origen.SintomasVisibles);
            destino.EvidenciasObservadasJson =
                SerializarLista(origen.EvidenciasObservadas);
            destino.EvidenciasNoObservadasJson =
                SerializarLista(origen.EvidenciasNoObservadas);
            destino.DiagnosticosAlternativosJson =
                SerializarLista(origen.DiagnosticosAlternativos);
            destino.InformacionFaltanteJson =
                SerializarLista(origen.InformacionFaltante);
            destino.RecomendacionesCapturaJson =
                SerializarLista(origen.RecomendacionesCaptura);
            destino.AdvertenciasJson =
                SerializarLista(origen.Advertencias);
            destino.FechaResultadoUtc = DateTime.UtcNow;
        }

        private static void AplicarResultadoRevision(
            DiagnosticoIARevision revision,
            GeminiRevisionResultado resultado)
        {
            revision.ImagenValida = resultado.ImagenValida;
            revision.ResultadoConcluyente =
                resultado.ResultadoConcluyente;
            revision.MantieneVeredictoOriginal =
                resultado.MantieneVeredictoOriginal;
            revision.RelacionConCriterioTecnico =
                resultado.RelacionConCriterioTecnico;
            revision.CalidadEvaluacion =
                resultado.CalidadEvaluacion;
            revision.EstadoGeneral = resultado.EstadoGeneral;
            revision.CategoriaPrincipal =
                resultado.CategoriaPrincipal;
            revision.CategoriasSecundariasJson =
                SerializarLista(resultado.CategoriasSecundarias);
            revision.DiagnosticoRevisado =
                resultado.DiagnosticoRevisado;
            revision.TipoDiagnostico =
                resultado.TipoDiagnostico;
            revision.SeveridadVisual =
                resultado.SeveridadVisual;
            revision.NivelCoincidencia =
                resultado.NivelCoincidencia;
            revision.ResumenRevision = resultado.ResumenRevision;
            revision.PartesAfectadasJson =
                SerializarLista(resultado.PartesAfectadas);
            revision.EvidenciasApoyoJson =
                SerializarLista(resultado.EvidenciasApoyo);
            revision.EvidenciasContradiccionJson =
                SerializarLista(resultado.EvidenciasContradiccion);
            revision.InformacionFaltanteJson =
                SerializarLista(resultado.InformacionFaltante);
            revision.RecomendacionesCapturaJson =
                SerializarLista(resultado.RecomendacionesCaptura);
            revision.AdvertenciasJson =
                SerializarLista(resultado.Advertencias);
            revision.RespuestaOriginalJson =
                resultado.RespuestaOriginalJson;
            revision.ErrorRevision = string.Empty;
        }

        private static void AplicarAnalisisHumano(
            DiagnosticoIAAnalisisHumano analisis,
            DiagnosticoIAAnalisisHumanoRequest request)
        {
            analisis.CalidadEvaluacion = DiagnosticoIAFlujo.Normalizar(
                request.CalidadEvaluacion,
                DiagnosticoIAFlujo.CalidadEvaluacion.Todos,
                DiagnosticoIAFlujo.CalidadEvaluacion.NoEvaluable);
            analisis.EstadoGeneral = DiagnosticoIAFlujo.Normalizar(
                request.EstadoGeneral,
                DiagnosticoIAFlujo.EstadoGeneral.Todos,
                DiagnosticoIAFlujo.EstadoGeneral.Indeterminada);
            analisis.CategoriaPrincipal = DiagnosticoIAFlujo.Normalizar(
                request.CategoriaPrincipal,
                DiagnosticoIAFlujo.Categoria.Todos,
                DiagnosticoIAFlujo.Categoria.NoDeterminada);
            analisis.CategoriasSecundariasJson = SerializarLista(
                NormalizarCategoriasSecundarias(
                    request.CategoriasSecundarias,
                    analisis.CategoriaPrincipal));
            analisis.DiagnosticoPropuesto =
                Normalizar(request.DiagnosticoPropuesto, 300);
            analisis.TipoDiagnostico =
                Normalizar(request.TipoDiagnostico, 80);
            analisis.SeveridadPropuesta = DiagnosticoIAFlujo.Normalizar(
                request.SeveridadPropuesta,
                DiagnosticoIAFlujo.Severidad.Todos,
                DiagnosticoIAFlujo.Severidad.NoEvaluable);
            analisis.NivelCerteza = DiagnosticoIAFlujo.Normalizar(
                request.NivelCerteza,
                DiagnosticoIAFlujo.Certeza.Todos,
                DiagnosticoIAFlujo.Certeza.NoDeterminado);
            analisis.PartesAfectadasJson = SerializarLista(
                NormalizarLista(request.PartesAfectadas, 10, 100));
            analisis.EvidenciasObservadasJson = SerializarLista(
                NormalizarLista(request.EvidenciasObservadas, 12, 400));
            analisis.Observaciones =
                Normalizar(request.Observaciones, 3000);
        }

        private static DiagnosticoIAAprobacion CrearAprobacion(
            DiagnosticoIA diagnostico,
            DiagnosticoIAAnalisisHumano analisis,
            DiagnosticoIAAprobacionRequest request,
            string decision,
            int usuarioId)
        {
            bool sinCambios = decision ==
                DiagnosticoIAFlujo.DecisionAprobacion.AprobarSinCambios;

            bool aprobada = sinCambios ||
                decision ==
                    DiagnosticoIAFlujo.DecisionAprobacion.AprobarConCorreccion;

            string calidad = sinCambios
                ? analisis.CalidadEvaluacion
                : DiagnosticoIAFlujo.Normalizar(
                    request.CalidadEvaluacionFinal,
                    DiagnosticoIAFlujo.CalidadEvaluacion.Todos,
                    analisis.CalidadEvaluacion);

            string estadoGeneral = sinCambios
                ? analisis.EstadoGeneral
                : DiagnosticoIAFlujo.Normalizar(
                    request.EstadoGeneralFinal,
                    DiagnosticoIAFlujo.EstadoGeneral.Todos,
                    analisis.EstadoGeneral);

            string categoria = sinCambios
                ? analisis.CategoriaPrincipal
                : DiagnosticoIAFlujo.Normalizar(
                    request.CategoriaPrincipalFinal,
                    DiagnosticoIAFlujo.Categoria.Todos,
                    analisis.CategoriaPrincipal);

            IReadOnlyList<string> categoriasSecundarias = sinCambios
                ? DeserializarLista(
                    analisis.CategoriasSecundariasJson)
                : NormalizarCategoriasSecundarias(
                    request.CategoriasSecundariasFinales,
                    categoria);

            return new DiagnosticoIAAprobacion
            {
                DiagnosticoIAId = diagnostico.DiagnosticoIAId,
                DiagnosticoIAAnalisisHumanoId =
                    analisis.DiagnosticoIAAnalisisHumanoId,
                UsuarioAprobadorId = usuarioId,
                Decision = decision,
                CalidadEvaluacionFinal = calidad,
                EstadoGeneralFinal = estadoGeneral,
                CategoriaPrincipalFinal = categoria,
                CategoriasSecundariasFinalJson =
                    SerializarLista(categoriasSecundarias),
                DiagnosticoFinal = sinCambios
                    ? analisis.DiagnosticoPropuesto
                    : Normalizar(
                        request.DiagnosticoFinal ??
                        analisis.DiagnosticoPropuesto,
                        300),
                TipoDiagnosticoFinal = sinCambios
                    ? analisis.TipoDiagnostico
                    : Normalizar(
                        request.TipoDiagnosticoFinal ??
                        analisis.TipoDiagnostico,
                        80),
                SeveridadFinal = sinCambios
                    ? analisis.SeveridadPropuesta
                    : DiagnosticoIAFlujo.Normalizar(
                        request.SeveridadFinal,
                        DiagnosticoIAFlujo.Severidad.Todos,
                        analisis.SeveridadPropuesta),
                NivelCertezaFinal = sinCambios
                    ? analisis.NivelCerteza
                    : DiagnosticoIAFlujo.Normalizar(
                        request.NivelCertezaFinal,
                        DiagnosticoIAFlujo.Certeza.Todos,
                        analisis.NivelCerteza),
                Observaciones =
                    Normalizar(request.Observaciones, 3000),
                AutorizaPublicacionAlbum =
                    aprobada && request.AutorizaPublicacionAlbum,
                MismoUsuarioQueAnalizo =
                    analisis.UsuarioAnalizadorId == usuarioId,
                FechaAprobacionUtc = DateTime.UtcNow
            };
        }

        private static string? ValidarClasificacionHumana(
            DiagnosticoIAAnalisisHumanoRequest request)
        {
            if (!DiagnosticoIAFlujo.CalidadEvaluacion.Todos.Contains(
                    request.CalidadEvaluacion ?? string.Empty))
                return "Seleccione una calidad de evaluación válida.";

            if (!DiagnosticoIAFlujo.EstadoGeneral.Todos.Contains(
                    request.EstadoGeneral ?? string.Empty))
                return "Seleccione un estado general válido.";

            if (!DiagnosticoIAFlujo.Categoria.Todos.Contains(
                    request.CategoriaPrincipal ?? string.Empty))
                return "Seleccione una categoría principal válida.";

            if (!DiagnosticoIAFlujo.Severidad.Todos.Contains(
                    request.SeveridadPropuesta ?? string.Empty))
                return "Seleccione una severidad válida.";

            if (!DiagnosticoIAFlujo.Certeza.Todos.Contains(
                    request.NivelCerteza ?? string.Empty))
                return "Seleccione un nivel de certeza válido.";

            if (request.EstadoGeneral ==
                    DiagnosticoIAFlujo.EstadoGeneral.Afectada &&
                string.IsNullOrWhiteSpace(request.DiagnosticoPropuesto))
            {
                return "Indique el diagnóstico propuesto o describa la afectación no determinada.";
            }

            return null;
        }

        private static string? ValidarAprobacion(
            DiagnosticoIAAprobacionRequest request,
            IReadOnlyCollection<DiagnosticoIAImagenEvaluacionRequest> evaluacionesImagen,
            string decision,
            IEnumerable<int> imagenesDiagnostico)
        {
            int[] ids = evaluacionesImagen
                .Select(item => item.DiagnosticoIAImagenId)
                .ToArray();

            if (ids.Distinct().Count() != ids.Length)
                return "La evaluación contiene fotografías repetidas.";

            HashSet<int> existentes = imagenesDiagnostico.ToHashSet();

            if (ids.Length != existentes.Count ||
                ids.Any(item => !existentes.Contains(item)) ||
                existentes.Any(item => !ids.Contains(item)))
            {
                return "Debe evaluar individualmente todas las fotografías del diagnóstico.";
            }

            if (evaluacionesImagen.Any(item =>
                    !DiagnosticoIAFlujo.CalidadImagen.Todos.Contains(
                        item.CalidadTecnica ?? string.Empty)))
            {
                return "Una evaluación de fotografía tiene una calidad técnica no válida.";
            }

            if (evaluacionesImagen.Any(item =>
                    item.AptaParaAlbum && !item.EsEvidenciaValida))
            {
                return "Una fotografía no puede marcarse como apta para el álbum si no es evidencia válida.";
            }

            bool aprobada = decision is
                DiagnosticoIAFlujo.DecisionAprobacion.AprobarSinCambios or
                DiagnosticoIAFlujo.DecisionAprobacion.AprobarConCorreccion;

            if (aprobada &&
                !evaluacionesImagen.Any(item => item.EsEvidenciaValida))
            {
                return "Para aprobar el diagnóstico debe existir al menos una fotografía marcada como evidencia válida.";
            }

            if (decision ==
                    DiagnosticoIAFlujo.DecisionAprobacion.AprobarConCorreccion)
            {
                if (!DiagnosticoIAFlujo.CalidadEvaluacion.Todos.Contains(
                        request.CalidadEvaluacionFinal ?? string.Empty))
                {
                    return "Seleccione una calidad final válida.";
                }

                if (!DiagnosticoIAFlujo.EstadoGeneral.Todos.Contains(
                        request.EstadoGeneralFinal ?? string.Empty))
                {
                    return "Seleccione un estado general final válido.";
                }

                if (!DiagnosticoIAFlujo.Categoria.Todos.Contains(
                        request.CategoriaPrincipalFinal ?? string.Empty))
                {
                    return "Seleccione una categoría principal final válida.";
                }

                if (!DiagnosticoIAFlujo.Severidad.Todos.Contains(
                        request.SeveridadFinal ?? string.Empty))
                {
                    return "Seleccione una severidad final válida.";
                }

                if (!DiagnosticoIAFlujo.Certeza.Todos.Contains(
                        request.NivelCertezaFinal ?? string.Empty))
                {
                    return "Seleccione un nivel de certeza final válido.";
                }

                if (request.EstadoGeneralFinal ==
                        DiagnosticoIAFlujo.EstadoGeneral.Afectada &&
                    string.IsNullOrWhiteSpace(request.DiagnosticoFinal))
                {
                    return "Para aprobar con corrección debe completar el diagnóstico final.";
                }
            }

            if (!aprobada && request.AutorizaPublicacionAlbum)
                return "Solo una aprobación puede autorizar fotografías para el álbum.";

            if (request.AutorizaPublicacionAlbum &&
                !evaluacionesImagen.Any(item =>
                    item.EsEvidenciaValida && item.AptaParaAlbum))
            {
                return "Marque al menos una fotografía válida y apta para el álbum.";
            }

            if (decision == DiagnosticoIAFlujo.DecisionAprobacion.Devolver &&
                string.IsNullOrWhiteSpace(request.Observaciones))
            {
                return "Explique qué debe corregir el analizador.";
            }

            return null;
        }

        private IQueryable<DiagnosticoIA> CargarListadoBase() =>
            diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.ResultadoIA)
                .Include(item => item.AnalisisHumanos)
                .Include(item => item.Aprobaciones)
                    .ThenInclude(item => item.EvaluacionesImagen)
                .Include(item => item.PublicacionesAlbum);

        private Task<DiagnosticoIA?> CargarDiagnosticoAsync(
            int id,
            CancellationToken cancellationToken) =>
            diagnosticoDb.Diagnosticos
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.ResultadoIA)
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.Evaluaciones)
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.PublicacionesAlbum)
                .Include(item => item.RevisionesIA)
                .Include(item => item.ValidacionesLegadas)
                .Include(item => item.AnalisisHumanos)
                .Include(item => item.Aprobaciones)
                    .ThenInclude(item => item.EvaluacionesImagen)
                .Include(item => item.PublicacionesAlbum)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(
                    item => item.DiagnosticoIAId == id,
                    cancellationToken);

        private async Task<IActionResult> ResponderListadoAsync(
            IReadOnlyCollection<DiagnosticoIA> diagnosticos,
            int usuarioId,
            CancellationToken cancellationToken)
        {
            Dictionary<int, string> usuarios = await ObtenerUsuariosAsync(
                diagnosticos.SelectMany(item =>
                    item.AnalisisHumanos
                        .Select(a => a.UsuarioAnalizadorId)
                        .Concat(item.Aprobaciones.Select(a =>
                            a.UsuarioAprobadorId))
                        .Append(item.UsuarioSolicitanteId)),
                cancellationToken);

            bool puedePublicar = await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAprobador,
                    TipoPermisoApi.Actualizar,
                    cancellationToken) &&
                await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAlbum,
                    TipoPermisoApi.Leer,
                    cancellationToken) &&
                await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAlbum,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            List<DiagnosticoIAListaDto> data = diagnosticos
                .Select(item =>
                {
                    DiagnosticoIAAnalisisHumano? analisis =
                        item.AnalisisHumanos
                            .OrderByDescending(a => a.Version)
                            .FirstOrDefault();

                    DiagnosticoIAAprobacion? aprobacion =
                        item.Aprobaciones
                            .OrderByDescending(a => a.FechaAprobacionUtc)
                            .FirstOrDefault();

                    DiagnosticoIAImagen? primera = item.Imagenes
                        .OrderBy(i => i.Orden)
                        .FirstOrDefault();

                    return new DiagnosticoIAListaDto
                    {
                        DiagnosticoIAId = item.DiagnosticoIAId,
                        CodigoTerreno = item.CodigoTerreno,
                        UsuarioSolicitante = usuarios.GetValueOrDefault(
                            item.UsuarioSolicitanteId,
                            $"Usuario {item.UsuarioSolicitanteId}"),
                        FechaSolicitudUtc = item.FechaSolicitudUtc,
                        Estado = item.Estado,
                        DiagnosticoSugerido = item.DiagnosticoSugerido,
                        CategoriaPrincipalIA = item.CategoriaPrincipalIA,
                        EstadoGeneralIA = item.EstadoGeneralIA,
                        NivelCoincidencia = item.NivelCoincidencia,
                        TotalImagenes = item.Imagenes.Count,
                        UrlMiniatura = primera == null
                            ? null
                            : ConstruirUrlVisible(
                                primera.UrlImagen,
                                primera.RutaRelativa),
                        VersionAnalisisActual = analisis?.Version,
                        DiagnosticoPropuesto =
                            analisis?.DiagnosticoPropuesto,
                        Analizador = analisis == null
                            ? null
                            : usuarios.GetValueOrDefault(
                                analisis.UsuarioAnalizadorId,
                                $"Usuario {analisis.UsuarioAnalizadorId}"),
                        Aprobador = aprobacion == null
                            ? null
                            : usuarios.GetValueOrDefault(
                                aprobacion.UsuarioAprobadorId,
                                $"Usuario {aprobacion.UsuarioAprobadorId}"),
                        PuedePublicarAlbum =
                            puedePublicar &&
                            DiagnosticoIAFlujo.EsEstadoAprobado(item.Estado) &&
                            aprobacion is { AutorizaPublicacionAlbum: true } &&
                            aprobacion.EvaluacionesImagen.Any(evaluacion =>
                                evaluacion.EsEvidenciaValida &&
                                evaluacion.AptaParaAlbum &&
                                !item.PublicacionesAlbum.Any(publicacion =>
                                    publicacion.Activo &&
                                    publicacion.DiagnosticoIAImagenId ==
                                        evaluacion.DiagnosticoIAImagenId)),
                        TotalPublicadasAlbum =
                            item.PublicacionesAlbum.Count(p => p.Activo)
                    };
                })
                .ToList();

            return Ok(new
            {
                success = true,
                message = "Diagnósticos obtenidos correctamente.",
                data
            });
        }

        private async Task<DiagnosticoIADetalleDto> CrearDetalleDtoAsync(
            DiagnosticoIA item,
            int usuarioActualId,
            CancellationToken cancellationToken)
        {
            IEnumerable<int> usuariosIds =
                item.AnalisisHumanos.Select(a => a.UsuarioAnalizadorId)
                    .Concat(item.Aprobaciones.Select(a => a.UsuarioAprobadorId))
                    .Concat(item.RevisionesIA.Select(r => r.UsuarioClasificadorId))
                    .Concat(item.PublicacionesAlbum.Select(p => p.UsuarioPublicacionId))
                    .Concat(item.Historial.Select(h => h.UsuarioId))
                    .Append(item.UsuarioSolicitanteId);

            Dictionary<int, string> usuarios = await ObtenerUsuariosAsync(
                usuariosIds,
                cancellationToken);

            bool puedeAnalizar = await TienePermisoAsync(
                usuarioActualId,
                DiagnosticoIAFlujo.InterfazAnalizador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            bool puedeAprobar = await TienePermisoAsync(
                usuarioActualId,
                DiagnosticoIAFlujo.InterfazAprobador,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            bool puedeAlbum = puedeAprobar &&
                await TienePermisoAsync(
                    usuarioActualId,
                    DiagnosticoIAFlujo.InterfazAlbum,
                    TipoPermisoApi.Leer,
                    cancellationToken) &&
                await TienePermisoAsync(
                    usuarioActualId,
                    DiagnosticoIAFlujo.InterfazAlbum,
                    TipoPermisoApi.Agregar,
                    cancellationToken);

            List<DiagnosticoIAAlbumPublicacionDto> publicaciones =
                await CrearPublicacionesDtoAsync(
                    item.PublicacionesAlbum.Where(p => p.Activo),
                    usuarios,
                    cancellationToken);

            Dictionary<int, DiagnosticoIAAlbumPublicacionDto> pubPorImagen =
                publicaciones
                    .GroupBy(p => p.DiagnosticoIAImagenId)
                    .ToDictionary(g => g.Key, g => g.First());

            DiagnosticoIAAprobacion? ultimaAprobacionEntidad =
                item.Aprobaciones
                    .OrderByDescending(a => a.FechaAprobacionUtc)
                    .FirstOrDefault();

            var evaluacionesUltimas = ultimaAprobacionEntidad?
                .EvaluacionesImagen
                .ToDictionary(e => e.DiagnosticoIAImagenId)
                ?? new Dictionary<int, DiagnosticoIAImagenEvaluacion>();

            List<DiagnosticoIAAnalisisHumanoDto> analisis =
                item.AnalisisHumanos
                    .OrderByDescending(a => a.Version)
                    .Select(a => CrearAnalisisDto(a, usuarios))
                    .ToList();

            List<DiagnosticoIAAprobacionDto> aprobaciones =
                item.Aprobaciones
                    .OrderByDescending(a => a.FechaAprobacionUtc)
                    .Select(a => CrearAprobacionDto(a, usuarios))
                    .ToList();

            List<DiagnosticoIARevisionDto> revisiones =
                item.RevisionesIA
                    .OrderByDescending(r => r.FechaSolicitudRevisionUtc)
                    .Select(r => CrearRevisionDto(r, usuarios))
                    .ToList();

            int revisionesCompletadas =
                item.RevisionesIA.Count(r => r.Estado == "COMPLETADA");

            bool revisionEnProceso =
                item.RevisionesIA.Any(r => r.Estado == "ANALIZANDO");

            DiagnosticoIAConfiguracion configuracionRevisiones =
                await ObtenerConfiguracionRevisionesAsync(
                    cancellationToken);

            bool puedeSolicitarRevision =
                !revisionEnProceso &&
                (configuracionRevisiones.RevisionesIlimitadas ||
                 revisionesCompletadas <
                    configuracionRevisiones.MaximoRevisionesGemini);

            return new DiagnosticoIADetalleDto
            {
                DiagnosticoIAId = item.DiagnosticoIAId,
                TerrenoId = item.TerrenoId,
                CodigoTerreno = item.CodigoTerreno,
                UsuarioSolicitanteId = item.UsuarioSolicitanteId,
                UsuarioSolicitante = usuarios.GetValueOrDefault(
                    item.UsuarioSolicitanteId,
                    $"Usuario {item.UsuarioSolicitanteId}"),
                FechaSolicitudUtc = item.FechaSolicitudUtc,
                FechaRespuestaIAUtc = item.FechaRespuestaIAUtc,
                Estado = item.Estado,
                ModeloGemini = item.ModeloGemini,
                ObservacionUsuario = item.ObservacionUsuario,
                ImagenValida = item.ImagenValida,
                ParecePlantaCafe = item.ParecePlantaCafe,
                ResultadoConcluyente = item.ResultadoConcluyente,
                CalidadEvaluacionIA = item.CalidadEvaluacionIA,
                EstadoGeneralIA = item.EstadoGeneralIA,
                CategoriaPrincipalIA = item.CategoriaPrincipalIA,
                CategoriasSecundariasIA =
                    DeserializarLista(item.CategoriasSecundariasIAJson).ToList(),
                DiagnosticoSugerido = item.DiagnosticoSugerido,
                TipoDiagnosticoIA = item.TipoDiagnosticoIA,
                SeveridadVisualIA = item.SeveridadVisualIA,
                NivelCoincidencia = item.NivelCoincidencia,
                Resumen = item.Resumen,
                PartesAfectadas =
                    DeserializarLista(item.PartesAfectadasJson).ToList(),
                SintomasVisibles =
                    DeserializarLista(item.SintomasVisiblesJson).ToList(),
                EvidenciasNoObservadas =
                    DeserializarLista(item.EvidenciasNoObservadasJson).ToList(),
                DiagnosticosAlternativos =
                    DeserializarLista(item.DiagnosticosAlternativosJson).ToList(),
                InformacionFaltante =
                    DeserializarLista(item.InformacionFaltanteJson).ToList(),
                RecomendacionesCaptura =
                    DeserializarLista(item.RecomendacionesCapturaJson).ToList(),
                Advertencias =
                    DeserializarLista(item.AdvertenciasJson).ToList(),
                PosibleDanoNoBiotico = item.PosibleDanoNoBiotico,
                PosibleCausaNoBiotica = item.PosibleCausaNoBiotica,
                ErrorAnalisis = item.ErrorAnalisis,
                RequiereValidacionHumana = item.RequiereValidacionHumana,
                Imagenes = item.Imagenes
                    .OrderBy(i => i.Orden)
                    .Select(i => new DiagnosticoIAImagenDto
                    {
                        DiagnosticoIAImagenId = i.DiagnosticoIAImagenId,
                        UrlImagen = ConstruirUrlVisible(
                            i.UrlImagen,
                            i.RutaRelativa),
                        TipoFotografia = i.TipoFotografia,
                        Orden = i.Orden,
                        NombreArchivoOriginal = i.NombreArchivoOriginal,
                        ResultadoIA = i.ResultadoIA == null
                            ? null
                            : CrearResultadoImagenDto(i.ResultadoIA),
                        UltimaEvaluacion = evaluacionesUltimas.TryGetValue(
                            i.DiagnosticoIAImagenId,
                            out DiagnosticoIAImagenEvaluacion? eval)
                                ? CrearEvaluacionDto(eval, usuarios)
                                : null,
                        PublicacionAlbum = pubPorImagen.GetValueOrDefault(
                            i.DiagnosticoIAImagenId)
                    })
                    .ToList(),
                RevisionesIA = revisiones,
                UltimaRevisionIA = revisiones.FirstOrDefault(),
                AnalisisHumanos = analisis,
                AnalisisHumanoActual = analisis.FirstOrDefault(),
                Aprobaciones = aprobaciones,
                UltimaAprobacion = aprobaciones.FirstOrDefault(),
                PublicacionesAlbum = publicaciones,
                Historial = item.Historial
                    .OrderByDescending(h => h.FechaUtc)
                    .Select(h => new DiagnosticoIAHistorialDto
                    {
                        DiagnosticoIAHistorialId =
                            h.DiagnosticoIAHistorialId,
                        UsuarioId = h.UsuarioId,
                        Usuario = usuarios.GetValueOrDefault(
                            h.UsuarioId,
                            $"Usuario {h.UsuarioId}"),
                        EstadoAnterior = h.EstadoAnterior,
                        EstadoNuevo = h.EstadoNuevo,
                        Accion = h.Accion,
                        Detalle = h.Detalle,
                        FechaUtc = h.FechaUtc
                    })
                    .ToList(),
                EsPropietarioSolicitud =
                    item.UsuarioSolicitanteId == usuarioActualId,
                PuedeAnalizar = puedeAnalizar,
                PuedeAprobar = puedeAprobar,
                PuedePublicarAlbum =
                    puedeAlbum &&
                    DiagnosticoIAFlujo.EsEstadoAprobado(item.Estado) &&
                    ultimaAprobacionEntidad is
                        { AutorizaPublicacionAlbum: true } &&
                    ultimaAprobacionEntidad.EvaluacionesImagen.Any(evaluacion =>
                        evaluacion.EsEvidenciaValida &&
                        evaluacion.AptaParaAlbum &&
                        !item.PublicacionesAlbum.Any(publicacion =>
                            publicacion.Activo &&
                            publicacion.DiagnosticoIAImagenId ==
                                evaluacion.DiagnosticoIAImagenId)),
                MaximoRevisionesGemini =
                    configuracionRevisiones.MaximoRevisionesGemini,
                RevisionesGeminiIlimitadas =
                    configuracionRevisiones.RevisionesIlimitadas,
                RevisionesGeminiCompletadas =
                    revisionesCompletadas,
                PuedeSolicitarRevisionGemini =
                    puedeSolicitarRevision
            };
        }

        private static DiagnosticoIAImagenResultadoDto
            CrearResultadoImagenDto(
                DiagnosticoIAImagenResultadoIA resultado) =>
            new()
            {
                DiagnosticoIAImagenResultadoIAId =
                    resultado.DiagnosticoIAImagenResultadoIAId,
                ImagenValida = resultado.ImagenValida,
                ParecePlantaCafe = resultado.ParecePlantaCafe,
                ResultadoConcluyente = resultado.ResultadoConcluyente,
                PartePlanta = resultado.PartePlanta,
                CalidadEvaluacion = resultado.CalidadEvaluacion,
                EstadoGeneral = resultado.EstadoGeneral,
                CategoriaPrincipal = resultado.CategoriaPrincipal,
                CategoriasSecundarias =
                    DeserializarLista(
                        resultado.CategoriasSecundariasJson).ToList(),
                DiagnosticoProbable = resultado.DiagnosticoProbable,
                TipoDiagnostico = resultado.TipoDiagnostico,
                SeveridadVisual = resultado.SeveridadVisual,
                NivelCerteza = resultado.NivelCerteza,
                ResumenImagen = resultado.ResumenImagen,
                SintomasVisibles =
                    DeserializarLista(
                        resultado.SintomasVisiblesJson).ToList(),
                EvidenciasObservadas =
                    DeserializarLista(
                        resultado.EvidenciasObservadasJson).ToList(),
                EvidenciasNoObservadas =
                    DeserializarLista(
                        resultado.EvidenciasNoObservadasJson).ToList(),
                DiagnosticosAlternativos =
                    DeserializarLista(
                        resultado.DiagnosticosAlternativosJson).ToList(),
                InformacionFaltante =
                    DeserializarLista(
                        resultado.InformacionFaltanteJson).ToList(),
                RecomendacionesCaptura =
                    DeserializarLista(
                        resultado.RecomendacionesCapturaJson).ToList(),
                Advertencias =
                    DeserializarLista(
                        resultado.AdvertenciasJson).ToList(),
                FechaResultadoUtc = resultado.FechaResultadoUtc
            };

        private static DiagnosticoIARevisionDto CrearRevisionDto(
            DiagnosticoIARevision revision,
            IReadOnlyDictionary<int, string> usuarios) =>
            new()
            {
                DiagnosticoIARevisionId = revision.DiagnosticoIARevisionId,
                UsuarioAnalizadorId = revision.UsuarioClasificadorId,
                UsuarioAnalizador = usuarios.GetValueOrDefault(
                    revision.UsuarioClasificadorId,
                    $"Usuario {revision.UsuarioClasificadorId}"),
                RetroalimentacionAnalizador =
                    revision.RetroalimentacionClasificador,
                DiagnosticoPropuestoAnalizador =
                    revision.DiagnosticoPropuestoClasificador,
                FechaSolicitudRevisionUtc =
                    revision.FechaSolicitudRevisionUtc,
                FechaRespuestaRevisionUtc =
                    revision.FechaRespuestaRevisionUtc,
                Estado = revision.Estado,
                ImagenValida = revision.ImagenValida,
                ResultadoConcluyente = revision.ResultadoConcluyente,
                MantieneVeredictoOriginal =
                    revision.MantieneVeredictoOriginal,
                RelacionConCriterioTecnico =
                    revision.RelacionConCriterioTecnico,
                CalidadEvaluacion = revision.CalidadEvaluacion,
                EstadoGeneral = revision.EstadoGeneral,
                CategoriaPrincipal = revision.CategoriaPrincipal,
                CategoriasSecundarias =
                    DeserializarLista(
                        revision.CategoriasSecundariasJson).ToList(),
                DiagnosticoRevisado = revision.DiagnosticoRevisado,
                TipoDiagnostico = revision.TipoDiagnostico,
                SeveridadVisual = revision.SeveridadVisual,
                NivelCoincidencia = revision.NivelCoincidencia,
                ResumenRevision = revision.ResumenRevision,
                PartesAfectadas =
                    DeserializarLista(
                        revision.PartesAfectadasJson).ToList(),
                EvidenciasApoyo =
                    DeserializarLista(
                        revision.EvidenciasApoyoJson).ToList(),
                EvidenciasContradiccion =
                    DeserializarLista(
                        revision.EvidenciasContradiccionJson).ToList(),
                InformacionFaltante =
                    DeserializarLista(
                        revision.InformacionFaltanteJson).ToList(),
                RecomendacionesCaptura =
                    DeserializarLista(
                        revision.RecomendacionesCapturaJson).ToList(),
                Advertencias =
                    DeserializarLista(
                        revision.AdvertenciasJson).ToList(),
                ErrorRevision = revision.ErrorRevision
            };

        private static DiagnosticoIAAnalisisHumanoDto CrearAnalisisDto(
            DiagnosticoIAAnalisisHumano analisis,
            IReadOnlyDictionary<int, string> usuarios) =>
            new()
            {
                DiagnosticoIAAnalisisHumanoId =
                    analisis.DiagnosticoIAAnalisisHumanoId,
                UsuarioAnalizadorId = analisis.UsuarioAnalizadorId,
                UsuarioAnalizador = usuarios.GetValueOrDefault(
                    analisis.UsuarioAnalizadorId,
                    $"Usuario {analisis.UsuarioAnalizadorId}"),
                Version = analisis.Version,
                EstadoRegistro = analisis.EstadoRegistro,
                CalidadEvaluacion = analisis.CalidadEvaluacion,
                EstadoGeneral = analisis.EstadoGeneral,
                CategoriaPrincipal = analisis.CategoriaPrincipal,
                CategoriasSecundarias =
                    DeserializarLista(
                        analisis.CategoriasSecundariasJson).ToList(),
                DiagnosticoPropuesto = analisis.DiagnosticoPropuesto,
                TipoDiagnostico = analisis.TipoDiagnostico,
                SeveridadPropuesta = analisis.SeveridadPropuesta,
                NivelCerteza = analisis.NivelCerteza,
                PartesAfectadas =
                    DeserializarLista(
                        analisis.PartesAfectadasJson).ToList(),
                EvidenciasObservadas =
                    DeserializarLista(
                        analisis.EvidenciasObservadasJson).ToList(),
                Observaciones = analisis.Observaciones,
                FechaCreacionUtc = analisis.FechaCreacionUtc,
                FechaActualizacionUtc = analisis.FechaActualizacionUtc,
                FechaEnvioUtc = analisis.FechaEnvioUtc
            };

        private static DiagnosticoIAAprobacionDto CrearAprobacionDto(
            DiagnosticoIAAprobacion aprobacion,
            IReadOnlyDictionary<int, string> usuarios) =>
            new()
            {
                DiagnosticoIAAprobacionId =
                    aprobacion.DiagnosticoIAAprobacionId,
                DiagnosticoIAAnalisisHumanoId =
                    aprobacion.DiagnosticoIAAnalisisHumanoId,
                UsuarioAprobadorId = aprobacion.UsuarioAprobadorId,
                UsuarioAprobador = usuarios.GetValueOrDefault(
                    aprobacion.UsuarioAprobadorId,
                    $"Usuario {aprobacion.UsuarioAprobadorId}"),
                Decision = aprobacion.Decision,
                CalidadEvaluacionFinal =
                    aprobacion.CalidadEvaluacionFinal,
                EstadoGeneralFinal = aprobacion.EstadoGeneralFinal,
                CategoriaPrincipalFinal =
                    aprobacion.CategoriaPrincipalFinal,
                CategoriasSecundariasFinales =
                    DeserializarLista(
                        aprobacion.CategoriasSecundariasFinalJson).ToList(),
                DiagnosticoFinal = aprobacion.DiagnosticoFinal,
                TipoDiagnosticoFinal =
                    aprobacion.TipoDiagnosticoFinal,
                SeveridadFinal = aprobacion.SeveridadFinal,
                NivelCertezaFinal = aprobacion.NivelCertezaFinal,
                Observaciones = aprobacion.Observaciones,
                AutorizaPublicacionAlbum =
                    aprobacion.AutorizaPublicacionAlbum,
                MismoUsuarioQueAnalizo =
                    aprobacion.MismoUsuarioQueAnalizo,
                FechaAprobacionUtc = aprobacion.FechaAprobacionUtc,
                EvaluacionesImagen = aprobacion.EvaluacionesImagen
                    .Select(e => CrearEvaluacionDto(e, usuarios))
                    .ToList()
            };

        private static DiagnosticoIAImagenEvaluacionDto CrearEvaluacionDto(
            DiagnosticoIAImagenEvaluacion evaluacion,
            IReadOnlyDictionary<int, string> usuarios) =>
            new()
            {
                DiagnosticoIAImagenEvaluacionId =
                    evaluacion.DiagnosticoIAImagenEvaluacionId,
                DiagnosticoIAAprobacionId =
                    evaluacion.DiagnosticoIAAprobacionId,
                DiagnosticoIAImagenId =
                    evaluacion.DiagnosticoIAImagenId,
                UsuarioAprobadorId = evaluacion.UsuarioAprobadorId,
                UsuarioAprobador = usuarios.GetValueOrDefault(
                    evaluacion.UsuarioAprobadorId,
                    $"Usuario {evaluacion.UsuarioAprobadorId}"),
                CalidadTecnica = evaluacion.CalidadTecnica,
                EsEvidenciaValida = evaluacion.EsEvidenciaValida,
                AptaParaAlbum = evaluacion.AptaParaAlbum,
                Observacion = evaluacion.Observacion,
                FechaEvaluacionUtc = evaluacion.FechaEvaluacionUtc
            };

        private async Task<List<DiagnosticoIAAlbumPublicacionDto>>
            CrearPublicacionesDtoAsync(
                IEnumerable<DiagnosticoIAAlbumPublicacion> publicaciones,
                IReadOnlyDictionary<int, string> usuarios,
                CancellationToken cancellationToken)
        {
            List<DiagnosticoIAAlbumPublicacion> lista =
                publicaciones.ToList();

            int[] categoriasIds = lista
                .Select(p => p.CategoriaAlbumBotanicoId)
                .Distinct()
                .ToArray();

            int[] registrosIds = lista
                .Select(p => p.AlbumBotanicoCafeId)
                .Distinct()
                .ToArray();

            Dictionary<int, string> categorias =
                await diagnosticoDb.CategoriasAlbum
                    .AsNoTracking()
                    .Where(c => categoriasIds.Contains(
                        c.CategoriaAlbumBotanicoId))
                    .ToDictionaryAsync(
                        c => c.CategoriaAlbumBotanicoId,
                        c => c.NombreCategoria,
                        cancellationToken);

            Dictionary<int, string> registros =
                await diagnosticoDb.RegistrosAlbum
                    .AsNoTracking()
                    .Where(r => registrosIds.Contains(
                        r.AlbumBotanicoCafeId))
                    .ToDictionaryAsync(
                        r => r.AlbumBotanicoCafeId,
                        r => r.Titulo,
                        cancellationToken);

            return lista
                .OrderByDescending(p => p.FechaPublicacionUtc)
                .Select(p => new DiagnosticoIAAlbumPublicacionDto
                {
                    DiagnosticoIAAlbumPublicacionId =
                        p.DiagnosticoIAAlbumPublicacionId,
                    DiagnosticoIAImagenId = p.DiagnosticoIAImagenId,
                    CategoriaAlbumBotanicoId =
                        p.CategoriaAlbumBotanicoId,
                    CategoriaAlbum = categorias.GetValueOrDefault(
                        p.CategoriaAlbumBotanicoId,
                        "Categoría"),
                    AlbumBotanicoCafeId = p.AlbumBotanicoCafeId,
                    RegistroAlbum = registros.GetValueOrDefault(
                        p.AlbumBotanicoCafeId,
                        "Registro"),
                    AlbumBotanicoCafeFotoId =
                        p.AlbumBotanicoCafeFotoId,
                    UsuarioPublicacionId = p.UsuarioPublicacionId,
                    UsuarioPublicacion = usuarios.GetValueOrDefault(
                        p.UsuarioPublicacionId,
                        $"Usuario {p.UsuarioPublicacionId}"),
                    FechaPublicacionUtc = p.FechaPublicacionUtc,
                    DescripcionPublicacion =
                        p.DescripcionPublicacion,
                    RutaFotoAlbum = ConstruirUrlPublica(
                        p.RutaFotoAlbum),
                    Activo = p.Activo
                })
                .ToList();
        }

        private async Task<Dictionary<int, string>> ObtenerUsuariosAsync(
            IEnumerable<int> ids,
            CancellationToken cancellationToken)
        {
            int[] usuariosIds = ids
                .Where(id => id > 0)
                .Distinct()
                .ToArray();

            if (usuariosIds.Length == 0)
                return [];

            return await db.Usuarios
                .AsNoTracking()
                .Where(item => usuariosIds.Contains(item.UsuarioId))
                .ToDictionaryAsync(
                    item => item.UsuarioId,
                    item => item.nombreCompletoUsuario,
                    cancellationToken);
        }

        private async Task<bool> PuedeConsultarAsync(
            DiagnosticoIA diagnostico,
            int? usuarioId,
            CancellationToken cancellationToken)
        {
            if (usuarioId is null or <= 0)
                return false;

            if (diagnostico.UsuarioSolicitanteId == usuarioId &&
                await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazSolicitud,
                    TipoPermisoApi.Leer,
                    cancellationToken))
            {
                return true;
            }

            return await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAnalizador,
                    TipoPermisoApi.Leer,
                    cancellationToken) ||
                await TienePermisoAsync(
                    usuarioId,
                    DiagnosticoIAFlujo.InterfazAprobador,
                    TipoPermisoApi.Leer,
                    cancellationToken);
        }

        private async Task<DiagnosticoIAConfiguracion>
            ObtenerConfiguracionRevisionesAsync(
                CancellationToken cancellationToken)
        {
            DiagnosticoIAConfiguracion? configuracion =
                await diagnosticoDb.Configuraciones
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        item =>
                            item.DiagnosticoIAConfiguracionId == 1,
                        cancellationToken);

            return configuracion ??
                new DiagnosticoIAConfiguracion
                {
                    DiagnosticoIAConfiguracionId = 1,
                    MaximoRevisionesGemini = 2,
                    RevisionesIlimitadas = false,
                    FechaModificacionUtc = DateTime.UtcNow
                };
        }

        private async Task<bool> TieneAlgunPermisoLecturaAsync(
            int? usuarioId,
            CancellationToken cancellationToken) =>
            await TienePermisoAsync(
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

        private static string ResolverTipoFotografia(
            IReadOnlyList<string>? tipos,
            int indice)
        {
            if (tipos == null ||
                indice < 0 ||
                indice >= tipos.Count ||
                string.IsNullOrWhiteSpace(tipos[indice]))
            {
                return "EVIDENCIA";
            }

            string tipo = tipos[indice]
                .Trim()
                .ToUpperInvariant()
                .Replace(' ', '_');

            return Normalizar(tipo, 40);
        }

        private IActionResult? ValidarFotos(List<IFormFile> fotos)
        {
            int maximoFotos =
                gemini.ObtenerMaximoFotografiasPorInspeccion();

            if (fotos.Count is < 1 || fotos.Count > maximoFotos)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        $"Debe proporcionar entre 1 y {maximoFotos} fotografías por inspección."
                });
            }

            if (fotos.Any(item => item.Length > MaximoBytesPorFoto))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Cada fotografía debe pesar como máximo 12 MB."
                });
            }

            return null;
        }

        private async Task<(int? TerrenoId, string Codigo, IActionResult? Error)>
            ResolverTerrenoAsync(
                string? codigo,
                CancellationToken cancellationToken)
        {
            string codigoTerreno = Normalizar(codigo, 50);

            if (string.IsNullOrWhiteSpace(codigoTerreno))
                return (null, string.Empty, null);

            var terreno = await db.Terreno
                .AsNoTracking()
                .Where(item =>
                    item.activo &&
                    item.codigoTerreno == codigoTerreno)
                .Select(item => new
                {
                    item.terrenoId,
                    item.codigoTerreno
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (terreno == null)
            {
                return (
                    null,
                    codigoTerreno,
                    BadRequest(new
                    {
                        success = false,
                        message =
                            "No se encontró un terreno activo con el código indicado."
                    }));
            }

            return (terreno.terrenoId, terreno.codigoTerreno, null);
        }

        private async Task<string> CopiarImagenAlAlbumAsync(
            string rutaOrigen,
            int albumId,
            CancellationToken cancellationToken)
        {
            string origenFisico = storage.ResolverRutaPublica(rutaOrigen);

            if (!System.IO.File.Exists(origenFisico))
            {
                throw new FileNotFoundException(
                    "La fotografía original del diagnóstico no se encuentra.",
                    origenFisico);
            }

            string carpetaRelativa = $"album-botanico/{albumId}";
            string carpetaFisica = storage.ObtenerCarpeta(carpetaRelativa);
            Directory.CreateDirectory(carpetaFisica);

            string nombre = $"{Guid.NewGuid():N}.webp";
            string destinoFisico = Path.Combine(carpetaFisica, nombre);

            await using FileStream origen = new(
                origenFisico,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);

            await using FileStream destino = new(
                destinoFisico,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);

            await origen.CopyToAsync(destino, cancellationToken);

            return $"/resources/uploads/{carpetaRelativa}/{nombre}";
        }

        private static void AgregarHistorial(
            DiagnosticoIA diagnostico,
            int usuarioId,
            string estadoAnterior,
            string estadoNuevo,
            string accion,
            string detalle)
        {
            diagnostico.Historial.Add(
                new DiagnosticoIAHistorial
                {
                    UsuarioId = usuarioId,
                    EstadoAnterior = Normalizar(estadoAnterior, 40),
                    EstadoNuevo = Normalizar(estadoNuevo, 40),
                    Accion = Normalizar(accion, 80),
                    Detalle = Normalizar(detalle, 2000),
                    FechaUtc = DateTime.UtcNow
                });
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
            if (string.IsNullOrWhiteSpace(rutaRelativa))
                return string.Empty;

            if (Uri.TryCreate(
                    rutaRelativa,
                    UriKind.Absolute,
                    out _))
            {
                return rutaRelativa;
            }

            return $"{Request.Scheme}://{Request.Host}" +
                   $"{Request.PathBase}/{rutaRelativa.TrimStart('/')}";
        }

        private string ConstruirUrlVisible(
            string? urlGuardada,
            string rutaRelativa) =>
            string.IsNullOrWhiteSpace(rutaRelativa)
                ? urlGuardada ?? string.Empty
                : ConstruirUrlPublica(rutaRelativa);

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

        private IActionResult NoEncontrado() =>
            NotFound(new
            {
                success = false,
                message = "El diagnóstico no fue encontrado."
            });

        private static IReadOnlyList<string> DeserializarLista(
            string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            try
            {
                return JsonSerializer.Deserialize<List<string>>(
                        json,
                        JsonOptions) ??
                    [];
            }
            catch
            {
                return [];
            }
        }

        private static string SerializarLista(
            IEnumerable<string>? valores) =>
            JsonSerializer.Serialize(
                valores ?? [],
                JsonOptions);

        private static List<string> NormalizarCategoriasSecundarias(
            IEnumerable<string>? valores,
            string principal) =>
            (valores ?? [])
                .Select(item => DiagnosticoIAFlujo.Normalizar(
                    item,
                    DiagnosticoIAFlujo.Categoria.Todos,
                    string.Empty))
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item) &&
                    item != DiagnosticoIAFlujo.Categoria.NoAplica &&
                    !string.Equals(
                        item,
                        principal,
                        StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();

        private static List<string> NormalizarLista(
            IEnumerable<string>? valores,
            int maximo,
            int maximoCaracteres) =>
            (valores ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => Normalizar(item, maximoCaracteres))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maximo)
                .ToList();

        private static string Normalizar(
            string? valor,
            int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo
                ? texto
                : texto[..maximo];
        }

        private static (int StatusCode, string Mensaje)
            MapearErrorGemini(
                HttpStatusCode statusCode,
                bool esSegundaRevision)
        {
            string operacion = esSegundaRevision
                ? "la segunda revisión"
                : "el análisis";

            return statusCode switch
            {
                HttpStatusCode.TooManyRequests =>
                    (
                        StatusCodes.Status429TooManyRequests,
                        "Se alcanzó temporalmente el límite gratuito de Gemini. Las fotografías permanecen guardadas."
                    ),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    (
                        StatusCodes.Status503ServiceUnavailable,
                        "Gemini rechazó la clave configurada o sus permisos."
                    ),
                HttpStatusCode.NotFound =>
                    (
                        StatusCodes.Status503ServiceUnavailable,
                        "El modelo de Gemini configurado no está disponible para esta clave."
                    ),
                HttpStatusCode.BadRequest =>
                    (
                        StatusCodes.Status502BadGateway,
                        $"Gemini rechazó el formato de {operacion}."
                    ),
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout =>
                    (
                        StatusCodes.Status503ServiceUnavailable,
                        "Gemini se encuentra temporalmente fuera de servicio."
                    ),
                _ =>
                    (
                        StatusCodes.Status502BadGateway,
                        $"Gemini no pudo completar {operacion}."
                    )
            };
        }
    }
}
