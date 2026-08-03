using CONATRADEC_API.DTOs;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Endpoints de inicio y seguimiento del análisis prolongado. La solicitud
    /// HTTP termina después de guardar las fotografías; Gemini continúa en la
    /// cola del servidor.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/diagnostico-ia-procesamiento")]
    public sealed class DiagnosticoIAProcesamientoController : ControllerBase
    {
        private const long MaximoBytesPorFoto = 12L * 1024L * 1024L;

        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly DBContext db;
        private readonly ImageService imageService;
        private readonly PermisoApiService permisos;
        private readonly GeminiDiagnosticoService gemini;
        private readonly DiagnosticoIAProcesamientoQueue queue;
        private readonly DiagnosticoIAProcesamientoEstadoStore estadoStore;

        public DiagnosticoIAProcesamientoController(
            DiagnosticoIADbContext diagnosticoDb,
            DBContext db,
            ImageService imageService,
            PermisoApiService permisos,
            GeminiDiagnosticoService gemini,
            DiagnosticoIAProcesamientoQueue queue,
            DiagnosticoIAProcesamientoEstadoStore estadoStore)
        {
            this.diagnosticoDb = diagnosticoDb;
            this.db = db;
            this.imageService = imageService;
            this.permisos = permisos;
            this.gemini = gemini;
            this.queue = queue;
            this.estadoStore = estadoStore;
        }

        [HttpPost("crear")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(700L * 1024L * 1024L)]
        public async Task<IActionResult> Crear(
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

            IActionResult? errorFotos = ValidarFotos(fotos);
            if (errorFotos != null)
                return errorFotos;

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
                    diagnostico.Estado,
                    "SOLICITUD_CREADA",
                    $"Se guardaron {fotos.Count} fotografías y el análisis fue enviado a la cola del servidor.");

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

            DiagnosticoIAProcesamientoEstado estado = estadoStore.Actualizar(
                diagnostico.DiagnosticoIAId,
                diagnostico.Estado,
                "EN_COLA",
                "Las fotografías fueron guardadas. Esperando turno para analizar con Gemini...",
                0,
                fotos.Count);

            await queue.EncolarAsync(
                new DiagnosticoIAProcesamientoTrabajo(
                    diagnostico.DiagnosticoIAId,
                    usuarioId.Value,
                    false),
                CancellationToken.None);

            return Accepted(new
            {
                success = true,
                message =
                    "Las fotografías fueron guardadas. Gemini continuará el análisis en el servidor.",
                data = estado
            });
        }

        [HttpPost("{id:int}/reintentar")]
        public async Task<IActionResult> Reintentar(
            int id,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            DiagnosticoIA? diagnostico = await diagnosticoDb.Diagnosticos
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.ResultadoIA)
                .Include(item => item.RevisionesIA)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(
                    item => item.DiagnosticoIAId == id && item.Activo,
                    cancellationToken);

            if (diagnostico == null)
                return NoEncontrado();

            bool esPropietario =
                usuarioId.HasValue &&
                diagnostico.UsuarioSolicitanteId == usuarioId.Value;

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

            if (diagnostico.Estado == DiagnosticoIAFlujo.Estados.AnalizandoIA)
            {
                return Conflict(new
                {
                    success = false,
                    message = "Este diagnóstico ya se encuentra en procesamiento."
                });
            }

            if (diagnostico.RevisionesIA.Any(item =>
                    item.Estado == "ANALIZANDO"))
            {
                return Conflict(new
                {
                    success = false,
                    message =
                        "Existe una revisión adicional de Gemini en proceso para este diagnóstico."
                });
            }

            bool resultadosIncompletos =
                diagnostico.Imagenes.Count > 0 &&
                diagnostico.Imagenes.Any(item =>
                    item.ResultadoIA == null ||
                    item.ResultadoIA.ResumenImagen.Contains(
                        "Gemini no devolvió",
                        StringComparison.OrdinalIgnoreCase));

            if (diagnostico.Estado !=
                    DiagnosticoIAFlujo.Estados.ErrorAnalisis &&
                !resultadosIncompletos)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Solo se puede reintentar un diagnóstico con error o con resultados individuales incompletos."
                });
            }

            string anterior = diagnostico.Estado;
            diagnostico.Estado = DiagnosticoIAFlujo.Estados.AnalizandoIA;
            diagnostico.ErrorAnalisis = string.Empty;
            diagnostico.FechaRespuestaIAUtc = null;
            diagnostico.ModeloGemini = gemini.ObtenerModeloConfigurado();

            AgregarHistorial(
                diagnostico,
                usuarioId!.Value,
                anterior,
                diagnostico.Estado,
                "REINTENTO_IA_ENCOLADO",
                "Se solicitó nuevamente el análisis de las fotografías existentes.");

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            DiagnosticoIAProcesamientoEstado estado = estadoStore.Actualizar(
                diagnostico.DiagnosticoIAId,
                diagnostico.Estado,
                "EN_COLA",
                "Reintento guardado. Esperando turno para analizar con Gemini...",
                0,
                diagnostico.Imagenes.Count);

            await queue.EncolarAsync(
                new DiagnosticoIAProcesamientoTrabajo(
                    diagnostico.DiagnosticoIAId,
                    usuarioId.Value,
                    true),
                CancellationToken.None);

            return Accepted(new
            {
                success = true,
                message = "El reintento fue enviado a la cola del servidor.",
                data = estado
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

            DiagnosticoIA? diagnostico = await diagnosticoDb.Diagnosticos
                .Include(item => item.Imagenes)
                .Include(item => item.RevisionesIA)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(
                    item => item.DiagnosticoIAId == id && item.Activo,
                    cancellationToken);

            if (diagnostico == null)
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
                        "La revisión adicional solo está disponible durante el análisis humano."
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

            string retroalimentacion = Normalizar(
                request.RetroalimentacionAnalizador,
                2000);

            if (retroalimentacion.Length < 8)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Describa con más detalle qué debe revisar Gemini."
                });
            }

            int completadas = diagnostico.RevisionesIA.Count(item =>
                item.Estado == "COMPLETADA");

            DiagnosticoIAConfiguracion? configuracion =
                await diagnosticoDb.Configuraciones
                    .AsNoTracking()
                    .OrderBy(item => item.DiagnosticoIAConfiguracionId)
                    .FirstOrDefaultAsync(cancellationToken);

            int maximo = Math.Clamp(
                configuracion?.MaximoRevisionesGemini ?? 2,
                1,
                20);

            bool ilimitadas =
                configuracion?.RevisionesIlimitadas ?? false;

            if (!ilimitadas && completadas >= maximo)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        $"Este diagnóstico ya alcanzó el máximo de {maximo} revisiones adicionales."
                });
            }

            var revision = new DiagnosticoIARevision
            {
                DiagnosticoIAId = diagnostico.DiagnosticoIAId,
                UsuarioClasificadorId = usuarioId!.Value,
                RetroalimentacionClasificador = retroalimentacion,
                DiagnosticoPropuestoClasificador = Normalizar(
                    request.DiagnosticoPropuestoAnalizador,
                    300),
                FechaSolicitudRevisionUtc = DateTime.UtcNow,
                Estado = "ANALIZANDO"
            };

            diagnostico.RevisionesIA.Add(revision);

            AgregarHistorial(
                diagnostico,
                usuarioId.Value,
                diagnostico.Estado,
                diagnostico.Estado,
                "REVISION_IA_ENCOLADA",
                $"Se solicitó la revisión adicional {completadas + 1} a Gemini.");

            await diagnosticoDb.SaveChangesAsync(cancellationToken);

            DiagnosticoIAProcesamientoEstado estado = estadoStore.Actualizar(
                diagnostico.DiagnosticoIAId,
                diagnostico.Estado,
                "EN_COLA_REVISION",
                "La revisión fue guardada. Esperando turno para consultar Gemini...",
                0,
                0);

            await queue.EncolarAsync(
                new DiagnosticoIAProcesamientoTrabajo(
                    diagnostico.DiagnosticoIAId,
                    usuarioId.Value,
                    false,
                    DiagnosticoIAProcesamientoOperaciones.Revision,
                    revision.DiagnosticoIARevisionId),
                CancellationToken.None);

            return Accepted(new
            {
                success = true,
                message =
                    "La revisión adicional fue enviada a la cola del servidor.",
                data = estado
            });
        }

        [HttpGet("{id:int}/estado")]
        public async Task<IActionResult> Estado(
            int id,
            [FromQuery] string? operacion = null,
            CancellationToken cancellationToken = default)
        {
            int? usuarioId = ObtenerUsuarioId();

            var diagnostico = await diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .Where(item => item.DiagnosticoIAId == id && item.Activo)
                .Select(item => new
                {
                    item.DiagnosticoIAId,
                    item.UsuarioSolicitanteId,
                    item.Estado,
                    item.ErrorAnalisis,
                    Total = item.Imagenes.Count,
                    Procesadas = item.Imagenes.Count(imagen =>
                        imagen.ResultadoIA != null),
                    UltimaRevision = item.RevisionesIA
                        .OrderByDescending(revision =>
                            revision.FechaSolicitudRevisionUtc)
                        .Select(revision => new
                        {
                            revision.Estado,
                            revision.ErrorRevision
                        })
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (diagnostico == null)
                return NoEncontrado();

            bool permitido =
                diagnostico.UsuarioSolicitanteId == usuarioId ||
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

            if (!permitido)
                return Forbid();

            if (!estadoStore.IntentarObtener(
                    id,
                    out DiagnosticoIAProcesamientoEstado estado))
            {
                bool esRevision = string.Equals(
                    operacion,
                    DiagnosticoIAProcesamientoOperaciones.Revision,
                    StringComparison.OrdinalIgnoreCase);

                bool tieneError;
                bool finalizado;
                string mensaje;
                string etapa;

                if (esRevision && diagnostico.UltimaRevision != null)
                {
                    tieneError = diagnostico.UltimaRevision.Estado == "ERROR";
                    finalizado = diagnostico.UltimaRevision.Estado != "ANALIZANDO";
                    mensaje = tieneError
                        ? diagnostico.UltimaRevision.ErrorRevision
                        : finalizado
                            ? "La revisión adicional terminó."
                            : "Gemini continúa realizando la revisión adicional...";
                    etapa = finalizado
                        ? (tieneError ? "ERROR_REVISION" : "REVISION_COMPLETADA")
                        : "REVISION_GEMINI";
                }
                else
                {
                    tieneError = diagnostico.Estado ==
                        DiagnosticoIAFlujo.Estados.ErrorAnalisis;
                    finalizado = diagnostico.Estado !=
                        DiagnosticoIAFlujo.Estados.AnalizandoIA;
                    mensaje = tieneError
                        ? diagnostico.ErrorAnalisis
                        : finalizado
                            ? "El procesamiento terminó."
                            : "Gemini continúa analizando las fotografías en el servidor...";
                    etapa = finalizado
                        ? (tieneError ? "ERROR" : "COMPLETADO")
                        : "ANALIZANDO";
                }

                estado = estadoStore.Actualizar(
                    id,
                    diagnostico.Estado,
                    etapa,
                    mensaje,
                    esRevision ? 0 : diagnostico.Procesadas,
                    esRevision ? 0 : diagnostico.Total,
                    finalizado,
                    tieneError);
            }

            return Ok(new
            {
                success = true,
                message = estado.Mensaje,
                data = estado
            });
        }

        private IActionResult? ValidarFotos(List<IFormFile> fotos)
        {
            int maximo = gemini.ObtenerMaximoFotografiasPorInspeccion();

            if (fotos.Count is < 1 || fotos.Count > maximo)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        $"Debe proporcionar entre 1 y {maximo} fotografías por inspección."
                });
            }

            if (fotos.Any(item => item.Length > MaximoBytesPorFoto))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Cada fotografía debe pesar como máximo 12 MB."
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
            if (tipos == null ||
                indice < 0 ||
                indice >= tipos.Count ||
                string.IsNullOrWhiteSpace(tipos[indice]))
            {
                return "EVIDENCIA";
            }

            return Normalizar(
                tipos[indice]
                    .Trim()
                    .ToUpperInvariant()
                    .Replace(' ', '_'),
                40);
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

        private static string Normalizar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo
                ? texto
                : texto[..maximo];
        }

        private IActionResult NoEncontrado() =>
            NotFound(new
            {
                success = false,
                message = "El diagnóstico solicitado no existe."
            });
    }
}
