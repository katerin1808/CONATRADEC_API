using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        private const int MaximoFotos = 4;
        private const int MaximoSegundasRevisiones = 3;
        private const long MaximoBytesPorFoto = 12L * 1024L * 1024L;

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly DBContext db;
        private readonly ImageService imageService;
        private readonly PermisoApiService permisos;
        private readonly GeminiDiagnosticoService gemini;
        private readonly ILogger<DiagnosticoIAController> logger;

        public DiagnosticoIAController(
            DiagnosticoIADbContext diagnosticoDb,
            DBContext db,
            ImageService imageService,
            PermisoApiService permisos,
            GeminiDiagnosticoService gemini,
            ILogger<DiagnosticoIAController> logger)
        {
            this.diagnosticoDb = diagnosticoDb;
            this.db = db;
            this.imageService = imageService;
            this.permisos = permisos;
            this.gemini = gemini;
            this.logger = logger;
        }

        /// <summary>
        /// Guarda las evidencias en el almacenamiento persistente y solicita
        /// el veredicto preliminar a Gemini. Solo funciona en línea.
        /// </summary>
        [HttpPost("analizar")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(52L * 1024L * 1024L)]
        public async Task<IActionResult> Analizar(
            [FromForm] DiagnosticoIACrearRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            List<IFormFile> fotos = (request.Fotos ?? [])
                .Where(item => item != null && item.Length > 0)
                .ToList();

            if (fotos.Count is < 1 or > MaximoFotos)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        $"Debe proporcionar entre 1 y {MaximoFotos} fotografías."
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

            int? terrenoId = null;
            string codigoTerreno =
                Normalizar(request.CodigoTerreno, 50);

            if (!string.IsNullOrWhiteSpace(codigoTerreno))
            {
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
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "No se encontró un terreno activo con el código indicado."
                    });
                }

                terrenoId = terreno.terrenoId;
                codigoTerreno = terreno.codigoTerreno;
            }

            var diagnostico = new DiagnosticoIA
            {
                TerrenoId = terrenoId,
                CodigoTerreno = codigoTerreno,
                UsuarioSolicitanteId = usuarioId!.Value,
                FechaSolicitudUtc = DateTime.UtcNow,
                Estado = "ANALIZANDO_IA",
                ModeloGemini = gemini.ObtenerModeloConfigurado(),
                ObservacionUsuario =
                    Normalizar(request.Observacion, 1000),
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
                            TipoFotografia = "EVIDENCIA",
                            Orden = indice + 1,
                            FechaRegistroUtc = DateTime.UtcNow
                        });
                }

                await diagnosticoDb.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                foreach (string ruta in rutasGuardadas)
                    EliminarImagenSeguro(ruta);

                diagnosticoDb.Diagnosticos.Remove(diagnostico);
                await diagnosticoDb.SaveChangesAsync(
                    CancellationToken.None);

                throw;
            }

            try
            {
                GeminiDiagnosticoResultado resultado =
                    await gemini.AnalizarAsync(
                        diagnostico.Imagenes.ToList(),
                        diagnostico.ObservacionUsuario,
                        cancellationToken);

                AplicarResultadoGemini(
                    diagnostico,
                    resultado);

                await diagnosticoDb.SaveChangesAsync(cancellationToken);

                DiagnosticoIADetalleDto detalle =
                    await ConstruirDetalleAsync(
                        diagnostico,
                        cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Gemini completó el análisis preliminar. El resultado quedó pendiente de validación humana.",
                    data = detalle
                });
            }
            catch (GeminiApiException ex)
            {
                diagnostico.Estado = "ERROR_ANALISIS";
                diagnostico.ErrorAnalisis =
                    Normalizar(ex.Message, 2000);
                diagnostico.FechaRespuestaIAUtc = DateTime.UtcNow;

                await diagnosticoDb.SaveChangesAsync(
                    CancellationToken.None);

                int statusCode;
                string mensajeUsuario;

                switch (ex.StatusCode)
                {
                    case HttpStatusCode.TooManyRequests:
                        statusCode =
                            StatusCodes.Status429TooManyRequests;
                        mensajeUsuario =
                            "Se alcanzó temporalmente el límite gratuito de Gemini. Intente nuevamente más tarde.";
                        break;

                    case HttpStatusCode.Unauthorized:
                    case HttpStatusCode.Forbidden:
                        statusCode =
                            StatusCodes.Status503ServiceUnavailable;
                        mensajeUsuario =
                            "Gemini rechazó la clave configurada o sus permisos.";
                        break;

                    case HttpStatusCode.NotFound:
                        statusCode =
                            StatusCodes.Status503ServiceUnavailable;
                        mensajeUsuario =
                            "El modelo de Gemini configurado no está disponible para esta clave.";
                        break;

                    case HttpStatusCode.BadRequest:
                        statusCode =
                            StatusCodes.Status502BadGateway;
                        mensajeUsuario =
                            "Gemini rechazó el formato de la solicitud.";
                        break;

                    case HttpStatusCode.ServiceUnavailable:
                    case HttpStatusCode.GatewayTimeout:
                        statusCode =
                            StatusCodes.Status503ServiceUnavailable;
                        mensajeUsuario =
                            "Gemini se encuentra temporalmente fuera de servicio.";
                        break;

                    default:
                        statusCode =
                            StatusCodes.Status502BadGateway;
                        mensajeUsuario =
                            "Las fotografías se guardaron, pero Gemini no pudo completar el análisis.";
                        break;
                }

                return StatusCode(
                    statusCode,
                    new
                    {
                        success = false,
                        message = mensajeUsuario,
                        diagnosticoIAId =
                            diagnostico.DiagnosticoIAId,
                        detail = diagnostico.ErrorAnalisis
                    });
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains(
                    "GEMINI_API_KEY",
                    StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError(
                    ex,
                    "La API no puede leer la configuración de Gemini.");

                diagnostico.Estado = "ERROR_ANALISIS";
                diagnostico.ErrorAnalisis =
                    Normalizar(ex.Message, 2000);
                diagnostico.FechaRespuestaIAUtc = DateTime.UtcNow;

                await diagnosticoDb.SaveChangesAsync(
                    CancellationToken.None);

                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        success = false,
                        message =
                            "La clave de Gemini no está disponible para el proceso del servidor.",
                        diagnosticoIAId =
                            diagnostico.DiagnosticoIAId,
                        detail = diagnostico.ErrorAnalisis
                    });
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                diagnostico.Estado = "ERROR_ANALISIS";
                diagnostico.ErrorAnalisis =
                    "La solicitud fue cancelada antes de recibir la respuesta de Gemini.";
                diagnostico.FechaRespuestaIAUtc = DateTime.UtcNow;

                await diagnosticoDb.SaveChangesAsync(
                    CancellationToken.None);

                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error al analizar el diagnóstico IA {DiagnosticoIAId}.",
                    diagnostico.DiagnosticoIAId);

                diagnostico.Estado = "ERROR_ANALISIS";
                diagnostico.ErrorAnalisis =
                    "Ocurrió un error inesperado al procesar el análisis con Gemini.";
                diagnostico.FechaRespuestaIAUtc = DateTime.UtcNow;

                await diagnosticoDb.SaveChangesAsync(
                    CancellationToken.None);

                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new
                    {
                        success = false,
                        message =
                            "Las fotografías se guardaron, pero no fue posible completar el análisis con Gemini.",
                        diagnosticoIAId =
                            diagnostico.DiagnosticoIAId,
                        detail =
                            "Revise el registro del backend para conocer la excepción interna."
                    });
            }
        }

        [HttpGet("mis-diagnosticos")]
        public async Task<IActionResult> MisDiagnosticos(
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 20,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                TipoPermisoApi.Leer,
                cancellationToken);

            if (acceso != null)
                return acceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 1, 50);

            IQueryable<DiagnosticoIA> query = diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .Include(item => item.Imagenes)
                .Include(item => item.Validaciones)
                .Include(item => item.Revisiones)
                .Where(item =>
                    item.Activo &&
                    item.UsuarioSolicitanteId == usuarioId!.Value)
                .OrderByDescending(item => item.FechaSolicitudUtc);

            int total = await query.CountAsync(cancellationToken);

            List<DiagnosticoIA> elementos = await query
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .ToListAsync(cancellationToken);

            IReadOnlyList<DiagnosticoIADetalleDto> datos =
                await ConstruirDetallesAsync(
                    elementos,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                pagina,
                tamanoPagina,
                total,
                data = datos
            });
        }

        /// <summary>
        /// Cola visible únicamente para usuarios cuyo rol tenga Actualizar.
        /// </summary>
        [HttpGet("pendientes")]
        public async Task<IActionResult> Pendientes(
            [FromQuery] int pagina = 1,
            [FromQuery] int tamanoPagina = 20,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            pagina = Math.Max(1, pagina);
            tamanoPagina = Math.Clamp(tamanoPagina, 1, 50);

            IQueryable<DiagnosticoIA> query = diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .Include(item => item.Imagenes)
                .Include(item => item.Validaciones)
                .Include(item => item.Revisiones)
                .Where(item =>
                    item.Activo &&
                    item.Estado == "PENDIENTE_VALIDACION")
                .OrderBy(item => item.FechaSolicitudUtc);

            int total = await query.CountAsync(cancellationToken);

            List<DiagnosticoIA> elementos = await query
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .ToListAsync(cancellationToken);

            IReadOnlyList<DiagnosticoIADetalleDto> datos =
                await ConstruirDetallesAsync(
                    elementos,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                pagina,
                tamanoPagina,
                total,
                data = datos
            });
        }

        [HttpGet("{diagnosticoIAId:int}")]
        public async Task<IActionResult> ObtenerDetalle(
            int diagnosticoIAId,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            DiagnosticoIA? diagnostico = await diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .Include(item => item.Imagenes)
                .Include(item => item.Validaciones)
                .Include(item => item.Revisiones)
                .FirstOrDefaultAsync(
                    item =>
                        item.DiagnosticoIAId == diagnosticoIAId &&
                        item.Activo,
                    cancellationToken);

            if (diagnostico == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El diagnóstico solicitado no existe."
                });
            }

            bool esPropietario =
                usuarioId.HasValue &&
                diagnostico.UsuarioSolicitanteId == usuarioId.Value;

            ResultadoPermisoApi permisoLectura =
                await permisos.ValidarAsync(
                    usuarioId,
                    DiagnosticoIADatabaseInitializer.PermisoDiagnosticoIA,
                    TipoPermisoApi.Leer,
                    cancellationToken);

            ResultadoPermisoApi permisoClasificar =
                await permisos.ValidarAsync(
                    usuarioId,
                    DiagnosticoIADatabaseInitializer.PermisoDiagnosticoIA,
                    TipoPermisoApi.Actualizar,
                    cancellationToken);

            if ((!esPropietario || !permisoLectura.Permitido) &&
                !permisoClasificar.Permitido)
            {
                return Forbid();
            }

            return Ok(new
            {
                success = true,
                data = await ConstruirDetalleAsync(
                    diagnostico,
                    cancellationToken)
            });
        }

        /// <summary>
        /// Solicita una segunda opinión a Gemini usando las mismas imágenes,
        /// el veredicto original y la retroalimentación del clasificador.
        /// El caso continúa pendiente hasta que una persona guarde la decisión
        /// humana final.
        /// </summary>
        [HttpPost("{diagnosticoIAId:int}/segunda-revision")]
        public async Task<IActionResult> SolicitarSegundaRevision(
            int diagnosticoIAId,
            [FromBody] DiagnosticoIASegundaRevisionRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DiagnosticoIA? diagnostico = await diagnosticoDb.Diagnosticos
                .Include(item => item.Imagenes)
                .Include(item => item.Validaciones)
                .Include(item => item.Revisiones)
                .FirstOrDefaultAsync(
                    item =>
                        item.DiagnosticoIAId == diagnosticoIAId &&
                        item.Activo,
                    cancellationToken);

            if (diagnostico == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El diagnóstico solicitado no existe."
                });
            }

            if (diagnostico.Estado != "PENDIENTE_VALIDACION")
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Solo se puede solicitar otra revisión mientras el diagnóstico esté pendiente de validación."
                });
            }

            if (diagnostico.Imagenes.Count == 0 ||
                string.IsNullOrWhiteSpace(
                    diagnostico.RespuestaOriginalJson))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "El diagnóstico no contiene las imágenes o el primer veredicto necesarios para una segunda revisión."
                });
            }

            string retroalimentacion =
                Normalizar(
                    request.RetroalimentacionClasificador,
                    2000);

            if (retroalimentacion.Length < 8)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Describa con más detalle qué observó o por qué duda del veredicto de Gemini."
                });
            }

            if (diagnostico.Revisiones.Any(item =>
                    item.Estado == "ANALIZANDO_IA"))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Ya existe una segunda revisión en proceso para este diagnóstico."
                });
            }

            int revisionesCompletadas =
                diagnostico.Revisiones.Count(item =>
                    item.Estado == "COMPLETADA");

            if (revisionesCompletadas >=
                MaximoSegundasRevisiones)
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        $"Este diagnóstico ya alcanzó el máximo de {MaximoSegundasRevisiones} revisiones con Gemini. La decisión debe registrarse mediante la validación humana."
                });
            }

            var revision = new DiagnosticoIARevision
            {
                DiagnosticoIAId = diagnostico.DiagnosticoIAId,
                UsuarioClasificadorId = usuarioId!.Value,
                RetroalimentacionClasificador =
                    retroalimentacion,
                DiagnosticoPropuestoClasificador =
                    Normalizar(
                        request.DiagnosticoPropuestoClasificador,
                        300),
                FechaSolicitudRevisionUtc = DateTime.UtcNow,
                Estado = "ANALIZANDO_IA"
            };

            diagnostico.Revisiones.Add(revision);
            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            try
            {
                GeminiRevisionResultado resultado =
                    await gemini.RevisarAsync(
                        diagnostico.Imagenes.ToList(),
                        diagnostico,
                        revision.RetroalimentacionClasificador,
                        revision.DiagnosticoPropuestoClasificador,
                        cancellationToken);

                AplicarResultadoRevision(
                    revision,
                    resultado);

                await diagnosticoDb.SaveChangesAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "Gemini completó la segunda revisión. El caso continúa pendiente de la decisión humana final.",
                    data = await ConstruirDetalleAsync(
                        diagnostico,
                        cancellationToken)
                });
            }
            catch (GeminiApiException ex)
            {
                revision.Estado = "ERROR_REVISION";
                revision.ErrorRevision =
                    Normalizar(ex.Message, 2000);
                revision.FechaRespuestaRevisionUtc =
                    DateTime.UtcNow;

                await diagnosticoDb.SaveChangesAsync(
                    CancellationToken.None);

                (int statusCode, string mensajeUsuario) =
                    MapearErrorGemini(
                        ex.StatusCode,
                        esSegundaRevision: true);

                return StatusCode(
                    statusCode,
                    new
                    {
                        success = false,
                        message = mensajeUsuario,
                        diagnosticoIAId,
                        detail = revision.ErrorRevision,
                        data = await ConstruirDetalleAsync(
                            diagnostico,
                            CancellationToken.None)
                    });
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains(
                    "GEMINI_API_KEY",
                    StringComparison.OrdinalIgnoreCase))
            {
                revision.Estado = "ERROR_REVISION";
                revision.ErrorRevision =
                    Normalizar(ex.Message, 2000);
                revision.FechaRespuestaRevisionUtc =
                    DateTime.UtcNow;

                await diagnosticoDb.SaveChangesAsync(
                    CancellationToken.None);

                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new
                    {
                        success = false,
                        message =
                            "La clave de Gemini no está disponible para el proceso del servidor.",
                        diagnosticoIAId,
                        detail = revision.ErrorRevision
                    });
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                revision.Estado = "ERROR_REVISION";
                revision.ErrorRevision =
                    "La segunda revisión fue cancelada antes de recibir la respuesta de Gemini.";
                revision.FechaRespuestaRevisionUtc =
                    DateTime.UtcNow;

                await diagnosticoDb.SaveChangesAsync(
                    CancellationToken.None);

                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error en la segunda revisión IA del diagnóstico {DiagnosticoIAId}.",
                    diagnosticoIAId);

                revision.Estado = "ERROR_REVISION";
                revision.ErrorRevision =
                    "Ocurrió un error inesperado durante la segunda revisión con Gemini.";
                revision.FechaRespuestaRevisionUtc =
                    DateTime.UtcNow;

                await diagnosticoDb.SaveChangesAsync(
                    CancellationToken.None);

                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new
                    {
                        success = false,
                        message =
                            "No fue posible completar la segunda revisión con Gemini.",
                        diagnosticoIAId,
                        detail = revision.ErrorRevision
                    });
            }
        }

        [HttpPut("{diagnosticoIAId:int}/clasificar")]
        public async Task<IActionResult> Clasificar(
            int diagnosticoIAId,
            [FromBody] DiagnosticoIAClasificarRequest request,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            IActionResult? acceso = await ValidarPermisoAsync(
                usuarioId,
                TipoPermisoApi.Actualizar,
                cancellationToken);

            if (acceso != null)
                return acceso;

            DiagnosticoIA? diagnostico = await diagnosticoDb.Diagnosticos
                .Include(item => item.Imagenes)
                .Include(item => item.Validaciones)
                .Include(item => item.Revisiones)
                .FirstOrDefaultAsync(
                    item =>
                        item.DiagnosticoIAId == diagnosticoIAId &&
                        item.Activo,
                    cancellationToken);

            if (diagnostico == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "El diagnóstico solicitado no existe."
                });
            }

            if (diagnostico.Estado != "PENDIENTE_VALIDACION")
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Este diagnóstico ya no se encuentra pendiente de validación."
                });
            }

            string decision = (request.Decision ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            string diagnosticoFinal =
                Normalizar(request.DiagnosticoFinal, 300);

            bool? coincide;
            string nuevoEstado;

            DiagnosticoIARevision? revisionVigente =
                diagnostico.Revisiones
                    .Where(item =>
                        item.Estado == "COMPLETADA" &&
                        !string.IsNullOrWhiteSpace(
                            item.DiagnosticoRevisado))
                    .OrderByDescending(item =>
                        item.FechaRespuestaRevisionUtc ??
                        item.FechaSolicitudRevisionUtc)
                    .FirstOrDefault();

            string veredictoVigente =
                revisionVigente?.DiagnosticoRevisado ??
                diagnostico.DiagnosticoSugerido;

            switch (decision)
            {
                case "CONFIRMAR":
                    coincide = true;
                    nuevoEstado = "CONFIRMADO";
                    diagnosticoFinal =
                        veredictoVigente;
                    break;

                case "CORREGIR":
                    if (string.IsNullOrWhiteSpace(diagnosticoFinal))
                    {
                        return BadRequest(new
                        {
                            success = false,
                            message =
                                "Debe indicar el diagnóstico correcto."
                        });
                    }

                    coincide = false;
                    nuevoEstado = "CORREGIDO";
                    break;

                case "NO_CONCLUYENTE":
                    coincide = null;
                    nuevoEstado = "NO_CONCLUYENTE";
                    diagnosticoFinal = "NO_DETERMINADO";
                    break;

                case "IMAGEN_RECHAZADA":
                    coincide = null;
                    nuevoEstado = "IMAGEN_RECHAZADA";
                    diagnosticoFinal = string.Empty;
                    break;

                default:
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "La decisión debe ser CONFIRMAR, CORREGIR, NO_CONCLUYENTE o IMAGEN_RECHAZADA."
                    });
            }

            var validacion = new DiagnosticoIAValidacion
            {
                DiagnosticoIAId = diagnostico.DiagnosticoIAId,
                UsuarioClasificadorId = usuarioId!.Value,
                Decision = decision,
                DiagnosticoFinal = diagnosticoFinal,
                CoincideConGemini = coincide,
                Observaciones =
                    Normalizar(request.Observaciones, 2000),
                FechaValidacionUtc = DateTime.UtcNow
            };

            diagnostico.Estado = nuevoEstado;
            diagnostico.RequiereValidacionHumana = false;
            diagnostico.Validaciones.Add(validacion);

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = decision switch
                {
                    "CONFIRMAR" =>
                        "El veredicto vigente de Gemini fue confirmado.",
                    "CORREGIR" =>
                        "El veredicto de Gemini fue corregido.",
                    "NO_CONCLUYENTE" =>
                        "El caso fue marcado como no concluyente.",
                    _ =>
                        "Las imágenes fueron rechazadas para clasificación."
                },
                data = await ConstruirDetalleAsync(
                    diagnostico,
                    cancellationToken)
            });
        }

        private void AplicarResultadoGemini(
            DiagnosticoIA diagnostico,
            GeminiDiagnosticoResultado resultado)
        {
            diagnostico.ImagenValida = resultado.ImagenValida;
            diagnostico.ParecePlantaCafe = resultado.ParecePlantaCafe;
            diagnostico.ResultadoConcluyente =
                resultado.ResultadoConcluyente;
            diagnostico.PosibleDanoNoBiotico =
                resultado.PosibleDanoNoBiotico;
            diagnostico.DiagnosticoSugerido =
                resultado.DiagnosticoSugerido;
            diagnostico.NivelCoincidencia =
                resultado.NivelCoincidencia;
            diagnostico.Resumen = resultado.Resumen;
            diagnostico.PosibleCausaNoBiotica =
                resultado.PosibleCausaNoBiotica;
            diagnostico.SintomasVisiblesJson =
                JsonSerializer.Serialize(
                    resultado.SintomasVisibles,
                    JsonOptions);
            diagnostico.DiagnosticosAlternativosJson =
                JsonSerializer.Serialize(
                    resultado.DiagnosticosAlternativos,
                    JsonOptions);
            diagnostico.RecomendacionesCapturaJson =
                JsonSerializer.Serialize(
                    resultado.RecomendacionesCaptura,
                    JsonOptions);
            diagnostico.AdvertenciasJson =
                JsonSerializer.Serialize(
                    resultado.Advertencias,
                    JsonOptions);
            diagnostico.RespuestaOriginalJson =
                resultado.RespuestaOriginalJson;
            diagnostico.ErrorAnalisis = string.Empty;
            diagnostico.FechaRespuestaIAUtc = DateTime.UtcNow;
            diagnostico.Estado = "PENDIENTE_VALIDACION";
            diagnostico.RequiereValidacionHumana = true;
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
            revision.DiagnosticoRevisado =
                resultado.DiagnosticoRevisado;
            revision.NivelCoincidencia =
                resultado.NivelCoincidencia;
            revision.ResumenRevision =
                resultado.ResumenRevision;
            revision.EvidenciasApoyoJson =
                JsonSerializer.Serialize(
                    resultado.EvidenciasApoyo,
                    JsonOptions);
            revision.EvidenciasContradiccionJson =
                JsonSerializer.Serialize(
                    resultado.EvidenciasContradiccion,
                    JsonOptions);
            revision.InformacionFaltanteJson =
                JsonSerializer.Serialize(
                    resultado.InformacionFaltante,
                    JsonOptions);
            revision.RecomendacionesCapturaJson =
                JsonSerializer.Serialize(
                    resultado.RecomendacionesCaptura,
                    JsonOptions);
            revision.AdvertenciasJson =
                JsonSerializer.Serialize(
                    resultado.Advertencias,
                    JsonOptions);
            revision.RespuestaOriginalJson =
                resultado.RespuestaOriginalJson;
            revision.ErrorRevision = string.Empty;
            revision.FechaRespuestaRevisionUtc =
                DateTime.UtcNow;
            revision.Estado = "COMPLETADA";
        }

        private async Task<IReadOnlyList<DiagnosticoIADetalleDto>>
            ConstruirDetallesAsync(
                IReadOnlyCollection<DiagnosticoIA> diagnosticos,
                CancellationToken cancellationToken)
        {
            int[] usuariosIds = diagnosticos
                .Select(item => item.UsuarioSolicitanteId)
                .Concat(
                    diagnosticos.SelectMany(item =>
                        item.Validaciones.Select(validacion =>
                            validacion.UsuarioClasificadorId)))
                .Concat(
                    diagnosticos.SelectMany(item =>
                        item.Revisiones.Select(revision =>
                            revision.UsuarioClasificadorId)))
                .Distinct()
                .ToArray();

            Dictionary<int, string> usuarios = await db.Usuarios
                .AsNoTracking()
                .Where(item => usuariosIds.Contains(item.UsuarioId))
                .ToDictionaryAsync(
                    item => item.UsuarioId,
                    item => item.nombreCompletoUsuario,
                    cancellationToken);

            return diagnosticos
                .Select(item => CrearDto(item, usuarios))
                .ToList();
        }

        private async Task<DiagnosticoIADetalleDto>
            ConstruirDetalleAsync(
                DiagnosticoIA diagnostico,
                CancellationToken cancellationToken)
        {
            IReadOnlyList<DiagnosticoIADetalleDto> detalles =
                await ConstruirDetallesAsync(
                    new[] { diagnostico },
                    cancellationToken);

            return detalles[0];
        }

        private DiagnosticoIADetalleDto CrearDto(
            DiagnosticoIA item,
            IReadOnlyDictionary<int, string> usuarios)
        {
            DiagnosticoIAValidacion? ultimaValidacion =
                item.Validaciones
                    .OrderByDescending(validacion =>
                        validacion.FechaValidacionUtc)
                    .FirstOrDefault();

            List<DiagnosticoIARevisionDto> revisiones =
                item.Revisiones
                    .OrderByDescending(revision =>
                        revision.FechaSolicitudRevisionUtc)
                    .Select(revision =>
                        CrearRevisionDto(
                            revision,
                            usuarios))
                    .ToList();

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
                PosibleDanoNoBiotico = item.PosibleDanoNoBiotico,
                DiagnosticoSugerido = item.DiagnosticoSugerido,
                NivelCoincidencia = item.NivelCoincidencia,
                Resumen = item.Resumen,
                PosibleCausaNoBiotica = item.PosibleCausaNoBiotica,
                SintomasVisibles =
                    DeserializarLista(item.SintomasVisiblesJson),
                DiagnosticosAlternativos =
                    DeserializarLista(item.DiagnosticosAlternativosJson),
                RecomendacionesCaptura =
                    DeserializarLista(item.RecomendacionesCapturaJson),
                Advertencias =
                    DeserializarLista(item.AdvertenciasJson),
                ErrorAnalisis = item.ErrorAnalisis,
                RequiereValidacionHumana =
                    item.RequiereValidacionHumana,
                Imagenes = item.Imagenes
                    .OrderBy(imagen => imagen.Orden)
                    .Select(imagen =>
                        new DiagnosticoIAImagenDto
                        {
                            DiagnosticoIAImagenId =
                                imagen.DiagnosticoIAImagenId,
                            UrlImagen = ConstruirUrlVisible(
                                imagen.UrlImagen,
                                imagen.RutaRelativa),
                            TipoFotografia =
                                imagen.TipoFotografia,
                            Orden = imagen.Orden
                        })
                    .ToList(),
                RevisionesIA = revisiones,
                UltimaRevisionIA = revisiones.FirstOrDefault(),
                RevisionVigenteIA = revisiones.FirstOrDefault(revision =>
                    revision.Estado == "COMPLETADA"),
                UltimaValidacion = ultimaValidacion == null
                    ? null
                    : new DiagnosticoIAValidacionDto
                    {
                        DiagnosticoIAValidacionId =
                            ultimaValidacion.DiagnosticoIAValidacionId,
                        UsuarioClasificadorId =
                            ultimaValidacion.UsuarioClasificadorId,
                        UsuarioClasificador = usuarios.GetValueOrDefault(
                            ultimaValidacion.UsuarioClasificadorId,
                            $"Usuario {ultimaValidacion.UsuarioClasificadorId}"),
                        Decision = ultimaValidacion.Decision,
                        DiagnosticoFinal =
                            ultimaValidacion.DiagnosticoFinal,
                        CoincideConGemini =
                            ultimaValidacion.CoincideConGemini,
                        Observaciones =
                            ultimaValidacion.Observaciones,
                        FechaValidacionUtc =
                            ultimaValidacion.FechaValidacionUtc
                    }
            };
        }

        private DiagnosticoIARevisionDto CrearRevisionDto(
            DiagnosticoIARevision revision,
            IReadOnlyDictionary<int, string> usuarios) =>
            new()
            {
                DiagnosticoIARevisionId =
                    revision.DiagnosticoIARevisionId,
                UsuarioClasificadorId =
                    revision.UsuarioClasificadorId,
                UsuarioClasificador = usuarios.GetValueOrDefault(
                    revision.UsuarioClasificadorId,
                    $"Usuario {revision.UsuarioClasificadorId}"),
                RetroalimentacionClasificador =
                    revision.RetroalimentacionClasificador,
                DiagnosticoPropuestoClasificador =
                    revision.DiagnosticoPropuestoClasificador,
                FechaSolicitudRevisionUtc =
                    revision.FechaSolicitudRevisionUtc,
                FechaRespuestaRevisionUtc =
                    revision.FechaRespuestaRevisionUtc,
                Estado = revision.Estado,
                ImagenValida = revision.ImagenValida,
                ResultadoConcluyente =
                    revision.ResultadoConcluyente,
                MantieneVeredictoOriginal =
                    revision.MantieneVeredictoOriginal,
                RelacionConCriterioTecnico =
                    revision.RelacionConCriterioTecnico,
                DiagnosticoRevisado =
                    revision.DiagnosticoRevisado,
                NivelCoincidencia =
                    revision.NivelCoincidencia,
                ResumenRevision =
                    revision.ResumenRevision,
                EvidenciasApoyo =
                    DeserializarLista(
                        revision.EvidenciasApoyoJson),
                EvidenciasContradiccion =
                    DeserializarLista(
                        revision.EvidenciasContradiccionJson),
                InformacionFaltante =
                    DeserializarLista(
                        revision.InformacionFaltanteJson),
                RecomendacionesCaptura =
                    DeserializarLista(
                        revision.RecomendacionesCapturaJson),
                Advertencias =
                    DeserializarLista(
                        revision.AdvertenciasJson),
                ErrorRevision =
                    revision.ErrorRevision
            };

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
                        "Se alcanzó temporalmente el límite gratuito de Gemini. Intente nuevamente más tarde."
                    ),

                HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden =>
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

        private async Task<IActionResult?> ValidarPermisoAsync(
            int? usuarioId,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi resultado = await permisos.ValidarAsync(
                usuarioId,
                DiagnosticoIADatabaseInitializer.PermisoDiagnosticoIA,
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

        private string ConstruirUrlPublica(string rutaRelativa) =>
            $"{Request.Scheme}://{Request.Host}" +
            $"{Request.PathBase}{rutaRelativa}";

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

        private static string Normalizar(
            string? valor,
            int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();

            return texto.Length <= maximo
                ? texto
                : texto[..maximo];
        }
    }
}
