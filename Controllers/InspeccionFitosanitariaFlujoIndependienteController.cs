using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace CONATRADEC_API.Controllers
{
    /// <summary>
    /// Operaciones del flujo independiente por fotografía. El analizador puede
    /// trabajar con las evidencias que el técnico ya envió, aunque todavía
    /// existan otras fotografías bajo responsabilidad del técnico.
    ///
    /// Una fotografía puede contener varios diagnósticos, pero todos pertenecen
    /// al mismo expediente y comparten un solo estado de flujo.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/inspecciones-fitosanitarias-flujo")]
    public sealed class InspeccionFitosanitariaFlujoIndependienteController :
        ControllerBase
    {
        private const int MaximoDiagnosticosPorFoto = 8;
        private const int MaximoLesionesPorDiagnostico = 25;
        private const int MaximoLesionesPorFoto = 80;

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };

        private readonly DBContext db;
        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly PermisoApiService permisos;
        private readonly InspeccionFitosanitariaDatabase database;
        private readonly InspeccionFitosanitariaControlDatabaseInitializer control;
        private readonly InspeccionFitosanitariaAsignacionDatabase asignaciones;
        private readonly InspeccionFitosanitariaBloqueoDatabase bloqueos;

        public InspeccionFitosanitariaFlujoIndependienteController(
            DBContext db,
            DiagnosticoIADbContext diagnosticoDb,
            PermisoApiService permisos,
            InspeccionFitosanitariaControlDatabaseInitializer control)
        {
            this.db = db;
            this.diagnosticoDb = diagnosticoDb;
            this.permisos = permisos;
            this.control = control;
            database = new InspeccionFitosanitariaDatabase(diagnosticoDb);
            asignaciones = new InspeccionFitosanitariaAsignacionDatabase(diagnosticoDb);
            bloqueos = new InspeccionFitosanitariaBloqueoDatabase(diagnosticoDb);
        }

        /// <summary>
        /// Guarda la clasificación humana de una fotografía como borrador. El
        /// envío al aprobador se realiza desde el flujo de revisión por fotografía.
        /// </summary>
        [HttpPost("{id:int}/analisis-humano-individual")]
        public async Task<IActionResult> GuardarAnalisisHumanoIndividual(
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

            if (request.Fotografias.Count != 1)
                return OperacionDebeSerIndividual();

            IActionResult? cierre = await ValidarInspeccionAbiertaAsync(
                id,
                cancellationToken);

            if (cierre != null)
                return cierre;

            InspeccionFotoAnalisisHumanoItemRequest item =
                request.Fotografias[0];

            IActionResult? validacionDiagnosticos =
                ValidarConjuntoDiagnosticos(
                    item.Diagnosticos,
                    permitirAccionesHumanas: true);
            if (validacionDiagnosticos != null)
                return validacionDiagnosticos;

            List<InspeccionDiagnosticoVisualDto> diagnosticosHumanos =
                NormalizarDiagnosticosHumanos(item.Diagnosticos);

            List<InspeccionDiagnosticoVisualDto> diagnosticosActivos =
                diagnosticosHumanos
                    .Where(diag => !string.Equals(
                        diag.AccionHumana,
                        "DESCARTAR",
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (string.IsNullOrWhiteSpace(item.Diagnostico) &&
                diagnosticosActivos.Count == 0 &&
                item.Diagnosticos.Count == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "Debe registrar al menos un diagnóstico humano o enviar explícitamente el conjunto revisado por el analizador."
                });
            }

            if (item.Diagnosticos.Count > 0 &&
                diagnosticosActivos.Count == 0)
            {
                /*
                 * El analizador puede descartar todas las afectaciones IA sin
                 * convertirlas en estados separados. El resumen legado queda
                 * explícitamente sin diagnóstico para no conservar como vigente
                 * una enfermedad que el humano descartó.
                 */
                item.Diagnostico = string.Empty;
                item.CategoriaPrincipal = "NO_APLICA";
                item.CategoriasSecundarias = [];
                item.TipoDiagnostico = string.Empty;
                item.Severidad = "NO_EVALUABLE";
                item.NivelCerteza = "NO_DETERMINADO";
            }
            else
            {
                AplicarResumenDesdeDiagnosticos(
                    diagnosticosActivos,
                    item,
                    soloSiExistePrincipal: true);
            }

            await database.InicializarAsync(cancellationToken);
            await asignaciones.InicializarAsync(cancellationToken);

            await using var transaccion =
                await diagnosticoDb.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                DiagnosticoIA? inspeccion = await CargarInspeccionAsync(
                    id,
                    cancellationToken);

                if (inspeccion == null)
                    return NoEncontrado();

                DiagnosticoIAImagen? imagen = inspeccion.Imagenes
                    .FirstOrDefault(value =>
                        value.DiagnosticoIAImagenId == item.FotografiaId);

                if (imagen == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "La fotografía no pertenece a la inspección."
                    });
                }

                /*
                 * Compatibilidad progresiva con la interfaz MAUI actual: si la
                 * pantalla todavía envía únicamente el resumen humano, se parte
                 * del último conjunto IA vigente para no perder diagnósticos
                 * secundarios. El diagnóstico principal se marca como CORREGIR
                 * solo cuando el analizador cambió su resumen; los secundarios
                 * permanecen CONFIRMAR hasta que la nueva interfaz permita
                 * decidirlos explícitamente uno por uno.
                 */
                if (diagnosticosHumanos.Count == 0)
                {
                    ResultadoVisualRegistro? visual =
                        await database.ObtenerResultadoVisualVigenteAsync(
                            item.FotografiaId,
                            cancellationToken);

                    diagnosticosHumanos = DeserializarDiagnosticos(
                        visual?.DiagnosticosJson);

                    foreach (InspeccionDiagnosticoVisualDto diagnosticoVisual
                             in diagnosticosHumanos)
                    {
                        diagnosticoVisual.IdOrigenIA =
                            string.IsNullOrWhiteSpace(diagnosticoVisual.IdOrigenIA)
                                ? diagnosticoVisual.Id
                                : diagnosticoVisual.IdOrigenIA;
                        diagnosticoVisual.AccionHumana = "CONFIRMAR";
                    }

                    InspeccionDiagnosticoVisualDto? principal =
                        diagnosticosHumanos.FirstOrDefault(value =>
                            value.EsPrincipal) ??
                        (diagnosticosHumanos.Count == 1
                            ? diagnosticosHumanos[0]
                            : null);

                    if (principal != null &&
                        !string.IsNullOrWhiteSpace(item.Diagnostico) &&
                        !string.Equals(
                            principal.Diagnostico?.Trim(),
                            item.Diagnostico.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        principal.Diagnostico = item.Diagnostico.Trim();
                        principal.Categoria = item.CategoriaPrincipal;
                        principal.TipoDiagnostico = item.TipoDiagnostico;
                        principal.Severidad = item.Severidad;
                        principal.NivelCerteza = item.NivelCerteza;
                        principal.AccionHumana = "CORREGIR";
                    }

                    diagnosticosActivos = diagnosticosHumanos
                        .Where(diag => !string.Equals(
                            diag.AccionHumana,
                            "DESCARTAR",
                            StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                FotoMetadatos? meta = await database.ObtenerFotoAsync(
                    item.FotografiaId,
                    cancellationToken);

                if (meta == null || meta.Descartada || !meta.Activo)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "La fotografía no se encuentra disponible."
                    });
                }

                if (NormalizarEstado(meta.Estado) is not
                    (InspeccionFitosanitariaFlujo.FotoEstados.PendienteAnalizador or
                     InspeccionFitosanitariaFlujo.FotoEstados.EnAnalisisHumano or
                     InspeccionFitosanitariaFlujo.FotoEstados.DevueltaAnalizador))
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "La fotografía no está disponible para análisis humano."
                    });
                }

                IActionResult? bloqueoAnalizador = await ValidarBloqueoRevisionAsync(
                    id,
                    usuarioId!.Value,
                    "ANALIZADOR",
                    cancellationToken);

                if (bloqueoAnalizador != null)
                    return bloqueoAnalizador;

                ResultadoAsignacionFlujo asignacionAnalizador =
                    await asignaciones.TomarAnalizadorAsync(
                        id,
                        usuarioId!.Value,
                        cancellationToken);

                if (!asignacionAnalizador.Exitoso)
                {
                    return Conflict(new
                    {
                        success = false,
                        message = asignacionAnalizador.Mensaje
                    });
                }

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
                    JsonSerializer.Serialize(diagnosticosHumanos, JsonOptions),
                    enviar: false,
                    cancellationToken);

                const string mensaje =
                    "La clasificación humana quedó guardada como borrador. Puede enviarla al aprobador cuando esté lista.";

                await database.CambiarEstadoFotoAsync(
                    item.FotografiaId,
                    usuarioId.Value,
                    InspeccionFitosanitariaFlujo.FotoEstados.EnAnalisisHumano,
                    InspeccionFitosanitariaFlujo.Acciones.AnalisisHumanoGuardado,
                    mensaje,
                    fechaAnalisisHumanoUtc: DateTime.UtcNow,
                    cancellationToken: cancellationToken);

                await ActualizarEstadoInspeccionAsync(
                    inspeccion,
                    cancellationToken);

                await transaccion.CommitAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = mensaje,
                    data = CrearResultadoOperacion(
                        item.FotografiaId,
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .EnAnalisisHumano,
                        mensaje)
                });
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        /// <summary>
        /// Registra la decisión final de una sola fotografía. La aprobación de
        /// una evidencia no cambia ni completa automáticamente las demás.
        /// </summary>
        [HttpPost("{id:int}/aprobacion-individual")]
        public async Task<IActionResult> RegistrarAprobacionIndividual(
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

            if (request.Fotografias.Count != 1)
                return OperacionDebeSerIndividual();

            IActionResult? cierre = await ValidarInspeccionAbiertaAsync(
                id,
                cancellationToken);

            if (cierre != null)
                return cierre;

            InspeccionFotoAprobacionItemRequest item =
                request.Fotografias[0];

            if (!InspeccionFitosanitariaFlujo.DecisionesAprobacion.Todas
                    .Contains(item.Decision ?? string.Empty))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "La decisión de aprobación no es válida."
                });
            }

            IActionResult? validacionDiagnosticos =
                ValidarConjuntoDiagnosticos(
                    item.DiagnosticosFinales,
                    permitirAccionesHumanas: false);
            if (validacionDiagnosticos != null)
                return validacionDiagnosticos;

            await database.InicializarAsync(cancellationToken);
            await asignaciones.InicializarAsync(cancellationToken);

            await using var transaccion =
                await diagnosticoDb.Database.BeginTransactionAsync(
                    cancellationToken);

            try
            {
                DiagnosticoIA? inspeccion = await CargarInspeccionAsync(
                    id,
                    cancellationToken);

                if (inspeccion == null)
                    return NoEncontrado();

                DiagnosticoIAImagen? imagen = inspeccion.Imagenes
                    .FirstOrDefault(value =>
                        value.DiagnosticoIAImagenId == item.FotografiaId);

                if (imagen == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "La fotografía no pertenece a la inspección."
                    });
                }

                FotoMetadatos? meta = await database.ObtenerFotoAsync(
                    item.FotografiaId,
                    cancellationToken);

                if (meta == null || NormalizarEstado(meta.Estado) !=
                    InspeccionFitosanitariaFlujo.FotoEstados
                        .PendienteAprobacion)
                {
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "La fotografía no está pendiente de aprobación."
                    });
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
                    return Conflict(new
                    {
                        success = false,
                        message =
                            "No existe un análisis humano enviado para esta fotografía."
                    });
                }

                bool mismoUsuarioQueAnalizo =
                    analisis.UsuarioAnalizadorId == usuarioId.Value;

                IActionResult? bloqueoAprobador = await ValidarBloqueoRevisionAsync(
                    id,
                    usuarioId.Value,
                    "APROBADOR",
                    cancellationToken);

                if (bloqueoAprobador != null)
                    return bloqueoAprobador;

                ResultadoAsignacionFlujo asignacionAprobador =
                    await asignaciones.TomarAprobadorAsync(
                        id,
                        usuarioId.Value,
                        cancellationToken);

                if (!asignacionAprobador.Exitoso)
                {
                    return Conflict(new
                    {
                        success = false,
                        message = asignacionAprobador.Mensaje
                    });
                }

                string decision = item.Decision.Trim().ToUpperInvariant();
                string estadoNuevo = decision switch
                {
                    InspeccionFitosanitariaFlujo.DecisionesAprobacion.Aprobar =>
                        InspeccionFitosanitariaFlujo.FotoEstados.Aprobada,
                    InspeccionFitosanitariaFlujo.DecisionesAprobacion
                        .AprobarConCorreccion =>
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .AprobadaConCorreccion,
                    InspeccionFitosanitariaFlujo.DecisionesAprobacion.Devolver =>
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .DevueltaAnalizador,
                    InspeccionFitosanitariaFlujo.DecisionesAprobacion.Rechazar =>
                        InspeccionFitosanitariaFlujo.FotoEstados.Rechazada,
                    _ => InspeccionFitosanitariaFlujo.FotoEstados.NoConcluyente
                };

                bool decisionPositiva = estadoNuevo is
                    InspeccionFitosanitariaFlujo.FotoEstados.Aprobada or
                    InspeccionFitosanitariaFlujo.FotoEstados
                        .AprobadaConCorreccion;

                List<InspeccionDiagnosticoVisualDto> diagnosticosFinales =
                    item.DiagnosticosFinales.Count > 0
                        ? NormalizarDiagnosticosHumanos(item.DiagnosticosFinales)
                        : DeserializarDiagnosticos(analisis.DiagnosticosJson)
                            .Where(diag => !string.Equals(
                                diag.AccionHumana,
                                "DESCARTAR",
                                StringComparison.OrdinalIgnoreCase))
                            .Select(diag =>
                            {
                                diag.AccionHumana = string.Empty;
                                return diag;
                            })
                            .ToList();

                /*
                 * La pantalla actual del aprobador permite corregir el nombre
                 * principal mediante el resumen. Ese cambio debe reflejarse
                 * también dentro del conjunto final para que no exista una
                 * discrepancia entre DiagnosticoFinal y DiagnosticosFinales.
                 */
                if (string.Equals(
                        decision,
                        InspeccionFitosanitariaFlujo.DecisionesAprobacion
                            .AprobarConCorreccion,
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.DiagnosticoFinal))
                {
                    InspeccionDiagnosticoVisualDto? principalFinal =
                        diagnosticosFinales.FirstOrDefault(value =>
                            value.EsPrincipal) ??
                        (diagnosticosFinales.Count == 1
                            ? diagnosticosFinales[0]
                            : null);

                    if (principalFinal != null)
                    {
                        principalFinal.Diagnostico =
                            Limitar(item.DiagnosticoFinal, 300);

                        if (!string.IsNullOrWhiteSpace(
                                item.CategoriaPrincipalFinal))
                        {
                            principalFinal.Categoria = Limitar(
                                item.CategoriaPrincipalFinal,
                                50).ToUpperInvariant();
                        }

                        if (!string.IsNullOrWhiteSpace(
                                item.TipoDiagnosticoFinal))
                        {
                            principalFinal.TipoDiagnostico = Limitar(
                                item.TipoDiagnosticoFinal,
                                80);
                        }

                        if (!string.IsNullOrWhiteSpace(item.SeveridadFinal))
                        {
                            principalFinal.Severidad = Limitar(
                                item.SeveridadFinal,
                                30).ToUpperInvariant();
                        }

                        if (!string.IsNullOrWhiteSpace(item.NivelCertezaFinal))
                        {
                            principalFinal.NivelCerteza = Limitar(
                                item.NivelCertezaFinal,
                                30).ToUpperInvariant();
                        }
                    }
                }

                AplicarResumenDesdeDiagnosticos(
                    diagnosticosFinales,
                    item,
                    soloSiExistePrincipal: true);

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
                        ? SerializarLista(
                            item.CategoriasSecundariasFinales)
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
                    JsonSerializer.Serialize(diagnosticosFinales, JsonOptions),
                    decisionPositiva && item.AutorizaPublicacionAlbum,
                    mismoUsuario: mismoUsuarioQueAnalizo,
                    cancellationToken);

                await database.CambiarEstadoFotoAsync(
                    item.FotografiaId,
                    usuarioId.Value,
                    estadoNuevo,
                    InspeccionFitosanitariaFlujo.Acciones
                        .AprobacionRegistrada,
                    $"El aprobador registró la decisión individual {decision}.",
                    fechaAprobacionUtc: DateTime.UtcNow,
                    cancellationToken: cancellationToken);

                if (estadoNuevo ==
                    InspeccionFitosanitariaFlujo.FotoEstados
                        .DevueltaAnalizador)
                {
                    await asignaciones.ReabrirEtapaAnalizadorAsync(
                        id,
                        cancellationToken);
                }

                await ActualizarEstadoInspeccionAsync(
                    inspeccion,
                    cancellationToken);

                await transaccion.CommitAsync(cancellationToken);

                return Ok(new
                {
                    success = true,
                    message =
                        "La decisión de la fotografía fue registrada correctamente.",
                    data = CrearResultadoOperacion(
                        item.FotografiaId,
                        estadoNuevo,
                        "Decisión registrada correctamente.")
                });
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private IActionResult? ValidarConjuntoDiagnosticos(
            IReadOnlyCollection<InspeccionDiagnosticoVisualDto>? diagnosticos,
            bool permitirAccionesHumanas)
        {
            if (diagnosticos == null || diagnosticos.Count == 0)
                return null;

            if (diagnosticos.Count > MaximoDiagnosticosPorFoto)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        $"Una fotografía admite como máximo {MaximoDiagnosticosPorFoto} diagnósticos durante la revisión humana."
                });
            }

            int totalLesiones = diagnosticos.Sum(item =>
                item.Lesiones?.Count ?? 0);

            if (totalLesiones > MaximoLesionesPorFoto ||
                diagnosticos.Any(item =>
                    (item.Lesiones?.Count ?? 0) > MaximoLesionesPorDiagnostico))
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        $"Una fotografía admite como máximo {MaximoLesionesPorFoto} lesiones en total y {MaximoLesionesPorDiagnostico} por diagnóstico."
                });
            }

            int principales = diagnosticos.Count(item =>
                item.EsPrincipal &&
                !string.Equals(
                    item.AccionHumana,
                    "DESCARTAR",
                    StringComparison.OrdinalIgnoreCase));

            if (principales > 1)
            {
                return BadRequest(new
                {
                    success = false,
                    message =
                        "La fotografía puede tener como máximo un diagnóstico principal."
                });
            }

            string[] accionesPermitidas =
                ["", "CONFIRMAR", "CORREGIR", "DESCARTAR", "AGREGAR"];

            foreach (InspeccionDiagnosticoVisualDto item in diagnosticos)
            {
                if (!permitirAccionesHumanas &&
                    string.Equals(
                        item.AccionHumana,
                        "DESCARTAR",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Los diagnósticos finales del aprobador no pueden contener elementos descartados."
                    });
                }

                if (permitirAccionesHumanas &&
                    !accionesPermitidas.Contains(
                        (item.AccionHumana ?? string.Empty)
                            .Trim()
                            .ToUpperInvariant()))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "La acción del diagnóstico debe ser CONFIRMAR, CORREGIR, DESCARTAR o AGREGAR."
                    });
                }

                if (!string.Equals(
                        item.AccionHumana,
                        "DESCARTAR",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(item.Diagnostico))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Cada diagnóstico activo debe indicar su nombre."
                    });
                }

                if ((item.Lesiones ?? []).Any(lesion =>
                        lesion.Box2d == null ||
                        lesion.Box2d.Count != 4 ||
                        lesion.Box2d.Any(value => value is < 0 or > 1000) ||
                        lesion.Box2d[0] >= lesion.Box2d[2] ||
                        lesion.Box2d[1] >= lesion.Box2d[3]))
                {
                    return BadRequest(new
                    {
                        success = false,
                        message =
                            "Una lesión contiene coordenadas box2d inválidas."
                    });
                }
            }

            return null;
        }

        private static List<InspeccionDiagnosticoVisualDto>
            NormalizarDiagnosticosHumanos(
                IEnumerable<InspeccionDiagnosticoVisualDto>? diagnosticos)
        {
            int consecutivo = 0;
            return (diagnosticos ?? [])
                .Take(MaximoDiagnosticosPorFoto)
                .Select(item =>
                {
                    consecutivo++;
                    item.Id = Limitar(
                        string.IsNullOrWhiteSpace(item.Id)
                            ? $"H{consecutivo}"
                            : item.Id,
                        20);
                    item.IdOrigenIA = Limitar(
                        string.IsNullOrWhiteSpace(item.IdOrigenIA) &&
                        !string.Equals(
                            item.AccionHumana,
                            "AGREGAR",
                            StringComparison.OrdinalIgnoreCase)
                            ? item.Id
                            : item.IdOrigenIA,
                        20);
                    item.AccionHumana = Limitar(
                        item.AccionHumana,
                        30).ToUpperInvariant();
                    item.Diagnostico = Limitar(item.Diagnostico, 300);
                    item.Categoria = Limitar(item.Categoria, 50)
                        .ToUpperInvariant();
                    item.TipoDiagnostico = Limitar(item.TipoDiagnostico, 80);
                    item.NivelCerteza = Limitar(item.NivelCerteza, 30)
                        .ToUpperInvariant();
                    item.Severidad = Limitar(item.Severidad, 30)
                        .ToUpperInvariant();
                    item.ColorMarcador = Limitar(item.ColorMarcador, 9);
                    item.DiagnosticosDiferenciales =
                        (item.DiagnosticosDiferenciales ?? [])
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(value => Limitar(value, 300))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(6)
                            .ToList();
                    item.Lesiones = (item.Lesiones ?? [])
                        .Where(lesion => lesion.Box2d?.Count == 4)
                        .Take(MaximoLesionesPorDiagnostico)
                        .ToList();
                    return item;
                })
                .ToList();
        }

        private static void AplicarResumenDesdeDiagnosticos(
            IReadOnlyCollection<InspeccionDiagnosticoVisualDto> diagnosticos,
            InspeccionFotoAnalisisHumanoItemRequest destino,
            bool soloSiExistePrincipal)
        {
            InspeccionDiagnosticoVisualDto? principal =
                diagnosticos.FirstOrDefault(item => item.EsPrincipal);

            if (principal == null && soloSiExistePrincipal)
                return;

            principal ??= diagnosticos.FirstOrDefault();
            if (principal == null)
                return;

            destino.Diagnostico = principal.Diagnostico;
            destino.CategoriaPrincipal = principal.Categoria;
            destino.TipoDiagnostico = principal.TipoDiagnostico;
            destino.Severidad = principal.Severidad;
            destino.NivelCerteza = principal.NivelCerteza;
            destino.CategoriasSecundarias = diagnosticos
                .Where(item => !ReferenceEquals(item, principal))
                .Select(item => item.Categoria)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AplicarResumenDesdeDiagnosticos(
            IReadOnlyCollection<InspeccionDiagnosticoVisualDto> diagnosticos,
            InspeccionFotoAprobacionItemRequest destino,
            bool soloSiExistePrincipal)
        {
            InspeccionDiagnosticoVisualDto? principal =
                diagnosticos.FirstOrDefault(item => item.EsPrincipal);

            if (principal == null && soloSiExistePrincipal)
                return;

            principal ??= diagnosticos.FirstOrDefault();
            if (principal == null)
                return;

            destino.DiagnosticoFinal = principal.Diagnostico;
            destino.CategoriaPrincipalFinal = principal.Categoria;
            destino.TipoDiagnosticoFinal = principal.TipoDiagnostico;
            destino.SeveridadFinal = principal.Severidad;
            destino.NivelCertezaFinal = principal.NivelCerteza;
            destino.CategoriasSecundariasFinales = diagnosticos
                .Where(item => !ReferenceEquals(item, principal))
                .Select(item => item.Categoria)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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

        private static string NormalizarEstado(string? estado) =>
            (estado ?? string.Empty).Trim().ToUpperInvariant();

        private async Task<DiagnosticoIA?> CargarInspeccionAsync(
            int id,
            CancellationToken cancellationToken) =>
            await diagnosticoDb.Diagnosticos
                .Include(item => item.Imagenes)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(item =>
                    item.DiagnosticoIAId == id && item.Activo,
                    cancellationToken);

        private async Task ActualizarEstadoInspeccionAsync(
            DiagnosticoIA inspeccion,
            CancellationToken cancellationToken)
        {
            List<FotoMetadatos> fotos = await database.ObtenerFotosAsync(
                inspeccion.DiagnosticoIAId,
                cancellationToken);

            InspeccionFitosanitariaControlRegistro? controlActual =
                await control.ObtenerAsync(
                    inspeccion.DiagnosticoIAId,
                    cancellationToken);

            bool cerradaDefinitiva =
                controlActual?.CerradaDefinitiva == true;

            string estadoNuevo =
                InspeccionFitosanitariaFlujo.CalcularEstadoInspeccion(
                    fotos.Where(item => item.Activo)
                        .Select(item => item.Estado),
                    cerradaDefinitiva);

            if (!string.Equals(
                    inspeccion.Estado,
                    estadoNuevo,
                    StringComparison.OrdinalIgnoreCase))
            {
                inspeccion.Estado = estadoNuevo;
                await diagnosticoDb.SaveChangesAsync(cancellationToken);
            }
        }

        private async Task<IActionResult?> ValidarInspeccionAbiertaAsync(
            int id,
            CancellationToken cancellationToken)
        {
            InspeccionFitosanitariaControlRegistro? registro =
                await control.ObtenerAsync(id, cancellationToken);

            if (registro == null || !registro.Activo)
                return NoEncontrado();

            if (!registro.CerradaDefinitiva)
                return null;

            return Conflict(new
            {
                success = false,
                message =
                    "La inspección está cerrada definitivamente y solo puede consultarse."
            });
        }

        private async Task<IActionResult?> ValidarPermisoAsync(
            int? usuarioId,
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            if (!usuarioId.HasValue)
                return Forbid();

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                tipo,
                cancellationToken);

            if (permiso.Permitido)
                return null;

            return StatusCode(
                permiso.CodigoEstado,
                new
                {
                    success = false,
                    message = permiso.Mensaje
                });
        }

        private async Task<bool> TienePermisoAsync(
            int usuarioId,
            string interfaz,
            TipoPermisoApi tipo,
            CancellationToken cancellationToken)
        {
            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId,
                interfaz,
                tipo,
                cancellationToken);

            return permiso.Permitido;
        }

        private async Task<IActionResult?> ValidarBloqueoRevisionAsync(
            int inspeccionId,
            int usuarioId,
            string etapa,
            CancellationToken cancellationToken)
        {
            ResultadoValidacionBloqueoInspeccion resultado =
                await bloqueos.ValidarPropietarioActivoAsync(
                    inspeccionId,
                    usuarioId,
                    etapa,
                    cancellationToken);

            if (resultado.Permitido)
                return null;

            return Conflict(new
            {
                success = false,
                message = resultado.Mensaje
            });
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

        private IActionResult NoEncontrado() =>
            NotFound(new
            {
                success = false,
                message = "No se encontró la inspección indicada."
            });

        private IActionResult OperacionDebeSerIndividual() =>
            BadRequest(new
            {
                success = false,
                message =
                    "La operación debe contener exactamente una fotografía. Cada evidencia mantiene sus propios datos y decisiones."
            });

        private static InspeccionOperacionMasivaDto CrearResultadoOperacion(
            int fotografiaId,
            string estado,
            string mensaje) =>
            new()
            {
                TotalSolicitadas = 1,
                TotalExitosas = 1,
                TotalConError = 0,
                Resultados =
                [
                    new InspeccionOperacionItemDto
                    {
                        FotografiaId = fotografiaId,
                        Exitoso = true,
                        Estado = estado,
                        Mensaje = mensaje
                    }
                ]
            };

        private static string SerializarLista(IEnumerable<string>? valores) =>
            JsonSerializer.Serialize(
                (valores ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());

        private static string ValorOAnterior(
            string? nuevo,
            string anterior) =>
            string.IsNullOrWhiteSpace(nuevo)
                ? anterior
                : nuevo.Trim();

        private static string Limitar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo ? texto : texto[..maximo];
        }
    }

    /// <summary>
    /// Respuesta compatible con las bandejas MAUI, enriquecida con el nombre
    /// de la inspección y los contadores de recepción progresiva.
    /// </summary>
    public sealed class InspeccionFitosanitariaFlujoListaDto
    {
        public int InspeccionId { get; set; }
        public string NombreInspeccion { get; set; } = string.Empty;
        public string CodigoTerreno { get; set; } = string.Empty;
        public int UsuarioTecnicoId { get; set; }
        public string TecnicoNombreCompleto { get; set; } = string.Empty;
        public string TecnicoUsuario { get; set; } = string.Empty;
        public DateTime FechaRegistroSistemaUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public bool EtapaTecnicaFinalizada { get; set; }
        public DateTime? FechaFinEtapaTecnicaUtc { get; set; }
        public int TotalFotografias { get; set; }
        public int Pendientes { get; set; }
        public int ConError { get; set; }
        public int Finalizadas { get; set; }
        public string UrlMiniatura { get; set; } = string.Empty;
    }
}
