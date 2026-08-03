using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;

namespace CONATRADEC_API.Services
{
    /// <summary>
    /// Ejecuta el trabajo prolongado fuera de la solicitud HTTP. Toda salida
    /// válida se guarda por fotografía antes de avanzar al analizador humano.
    /// </summary>
    public sealed class DiagnosticoIAProcesamientoService
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        private readonly DiagnosticoIADbContext db;
        private readonly GeminiDiagnosticoService gemini;
        private readonly DiagnosticoIAProcesamientoEstadoStore estadoStore;
        private readonly ILogger<DiagnosticoIAProcesamientoService> logger;

        public DiagnosticoIAProcesamientoService(
            DiagnosticoIADbContext db,
            GeminiDiagnosticoService gemini,
            DiagnosticoIAProcesamientoEstadoStore estadoStore,
            ILogger<DiagnosticoIAProcesamientoService> logger)
        {
            this.db = db;
            this.gemini = gemini;
            this.estadoStore = estadoStore;
            this.logger = logger;
        }

        public Task ProcesarAsync(
            DiagnosticoIAProcesamientoTrabajo trabajo,
            CancellationToken cancellationToken) =>
            string.Equals(
                trabajo.TipoOperacion,
                DiagnosticoIAProcesamientoOperaciones.Revision,
                StringComparison.OrdinalIgnoreCase)
                ? ProcesarRevisionAsync(trabajo, cancellationToken)
                : ProcesarAnalisisAsync(trabajo, cancellationToken);

        private async Task ProcesarAnalisisAsync(
            DiagnosticoIAProcesamientoTrabajo trabajo,
            CancellationToken cancellationToken)
        {
            DiagnosticoIA? diagnostico = await db.Diagnosticos
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.ResultadoIA)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(
                    item =>
                        item.DiagnosticoIAId == trabajo.DiagnosticoIAId &&
                        item.Activo,
                    cancellationToken);

            if (diagnostico == null)
                return;

            int total = diagnostico.Imagenes.Count;

            if (total == 0)
            {
                await RegistrarErrorAsync(
                    diagnostico,
                    trabajo.UsuarioId,
                    "El diagnóstico no contiene fotografías para procesar.",
                    CancellationToken.None);
                return;
            }

            try
            {
                string estadoAnterior = diagnostico.Estado;
                diagnostico.Estado = DiagnosticoIAFlujo.Estados.AnalizandoIA;
                diagnostico.ErrorAnalisis = string.Empty;
                diagnostico.FechaRespuestaIAUtc = null;
                diagnostico.ModeloGemini = gemini.ObtenerModeloConfigurado();

                EliminarResultadosAnteriores(diagnostico);

                AgregarHistorial(
                    diagnostico,
                    trabajo.UsuarioId,
                    estadoAnterior,
                    diagnostico.Estado,
                    trabajo.EsReintento
                        ? "REINTENTO_IA_INICIADO"
                        : "PROCESAMIENTO_IA_INICIADO",
                    $"Se inició el procesamiento en segundo plano de {total} fotografías.");

                await db.SaveChangesAsync(cancellationToken);

                estadoStore.Actualizar(
                    diagnostico.DiagnosticoIAId,
                    diagnostico.Estado,
                    "PREPARANDO",
                    $"Preparando {total} fotografías para Gemini...",
                    0,
                    total);

                IProgress<GeminiDiagnosticoProgreso> progreso =
                    new ProgresoEnLinea<GeminiDiagnosticoProgreso>(valor =>
                    {
                        estadoStore.Actualizar(
                            diagnostico.DiagnosticoIAId,
                            diagnostico.Estado,
                            valor.Etapa,
                            valor.Mensaje,
                            valor.FotografiasProcesadas,
                            valor.TotalFotografias);
                    });

                GeminiDiagnosticoResultado resultado =
                    await gemini.AnalizarConProgresoAsync(
                        diagnostico.Imagenes.ToList(),
                        diagnostico.ObservacionUsuario,
                        progreso,
                        cancellationToken);

                ValidarResultadoCompleto(diagnostico, resultado);

                estadoStore.Actualizar(
                    diagnostico.DiagnosticoIAId,
                    diagnostico.Estado,
                    "GUARDANDO_RESULTADOS",
                    "Guardando los resultados individuales y el resumen general...",
                    total,
                    total);

                AplicarResultadoGemini(diagnostico, resultado);

                estadoAnterior = diagnostico.Estado;
                diagnostico.Estado =
                    DiagnosticoIAFlujo.Estados.PendienteDecisionTecnico;
                diagnostico.FechaRespuestaIAUtc = DateTime.UtcNow;
                diagnostico.ErrorAnalisis = string.Empty;

                AgregarHistorial(
                    diagnostico,
                    trabajo.UsuarioId,
                    estadoAnterior,
                    diagnostico.Estado,
                    "IA_COMPLETADA",
                    "Gemini registró un resultado válido por cada fotografía. El técnico solicitante debe decidir si lo envía al analizador, solicita otra evaluación o cancela la solicitud.");

                await db.SaveChangesAsync(cancellationToken);

                estadoStore.Actualizar(
                    diagnostico.DiagnosticoIAId,
                    diagnostico.Estado,
                    "COMPLETADO",
                    "Gemini completó el análisis preliminar. La solicitud quedó pendiente de la decisión del técnico.",
                    total,
                    total,
                    finalizado: true);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                // La API se está deteniendo. El HostedService recuperará el
                // diagnóstico, cuyo estado permanece ANALIZANDO_IA.
                throw;
            }
            catch (GeminiApiException ex)
            {
                string mensaje = ResolverMensajeGemini(ex);

                logger.LogWarning(
                    ex,
                    "Gemini no completó el diagnóstico {DiagnosticoIAId}: {Mensaje}",
                    diagnostico.DiagnosticoIAId,
                    mensaje);

                await RegistrarErrorAsync(
                    diagnostico,
                    trabajo.UsuarioId,
                    mensaje,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error procesando el diagnóstico IA {DiagnosticoIAId}.",
                    diagnostico.DiagnosticoIAId);

                await RegistrarErrorAsync(
                    diagnostico,
                    trabajo.UsuarioId,
                    "Ocurrió un error inesperado durante el análisis. Las fotografías permanecen guardadas y puede reintentar.",
                    CancellationToken.None);
            }
        }

        private async Task ProcesarRevisionAsync(
            DiagnosticoIAProcesamientoTrabajo trabajo,
            CancellationToken cancellationToken)
        {
            DiagnosticoIA? diagnostico = await db.Diagnosticos
                .Include(item => item.Imagenes)
                    .ThenInclude(item => item.ResultadoIA)
                .Include(item => item.RevisionesIA)
                .Include(item => item.Historial)
                .FirstOrDefaultAsync(
                    item =>
                        item.DiagnosticoIAId == trabajo.DiagnosticoIAId &&
                        item.Activo,
                    cancellationToken);

            if (diagnostico == null ||
                !trabajo.DiagnosticoIARevisionId.HasValue)
            {
                return;
            }

            DiagnosticoIARevision? revision = diagnostico.RevisionesIA
                .FirstOrDefault(item =>
                    item.DiagnosticoIARevisionId ==
                        trabajo.DiagnosticoIARevisionId.Value);

            if (revision == null)
                return;

            int total = diagnostico.Imagenes.Count;

            try
            {
                estadoStore.Actualizar(
                    diagnostico.DiagnosticoIAId,
                    diagnostico.Estado,
                    "REVISION_GEMINI",
                    $"Gemini analizará nuevamente {total} fotografía(s), una por una...",
                    0,
                    total);

                string diagnosticoAnterior =
                    diagnostico.DiagnosticoSugerido;
                string resumenAnterior =
                    diagnostico.Resumen;
                string respuestaAnterior =
                    diagnostico.RespuestaOriginalJson;

                string observacionRevision =
                    string.Join(
                        Environment.NewLine,
                        new[]
                        {
                            diagnostico.ObservacionUsuario,
                            "EVALUACIÓN ADICIONAL SOLICITADA POR EL TÉCNICO:",
                            revision.RetroalimentacionClasificador,
                            string.IsNullOrWhiteSpace(
                                revision.DiagnosticoPropuestoClasificador)
                                ? string.Empty
                                : "Diagnóstico que el técnico considera posible: " +
                                  revision.DiagnosticoPropuestoClasificador
                        }
                        .Where(item =>
                            !string.IsNullOrWhiteSpace(item)));

                IProgress<GeminiDiagnosticoProgreso> progreso =
                    new ProgresoEnLinea<GeminiDiagnosticoProgreso>(valor =>
                    {
                        estadoStore.Actualizar(
                            diagnostico.DiagnosticoIAId,
                            diagnostico.Estado,
                            valor.Etapa,
                            valor.Mensaje,
                            valor.FotografiasProcesadas,
                            valor.TotalFotografias);
                    });

                GeminiDiagnosticoResultado resultado =
                    await gemini.AnalizarConProgresoAsync(
                        diagnostico.Imagenes.ToList(),
                        observacionRevision,
                        progreso,
                        cancellationToken);

                ValidarResultadoCompleto(
                    diagnostico,
                    resultado);

                EliminarResultadosAnteriores(diagnostico);
                AplicarResultadoGemini(
                    diagnostico,
                    resultado);

                AplicarResultadoRevisionDesdeAnalisis(
                    revision,
                    resultado,
                    diagnosticoAnterior,
                    resumenAnterior,
                    respuestaAnterior);

                revision.Estado = "COMPLETADA";
                revision.FechaRespuestaRevisionUtc = DateTime.UtcNow;

                string estadoAnterior = diagnostico.Estado;
                diagnostico.Estado =
                    DiagnosticoIAFlujo.Estados.PendienteDecisionTecnico;
                diagnostico.ErrorAnalisis = string.Empty;
                diagnostico.FechaRespuestaIAUtc = DateTime.UtcNow;

                AgregarHistorial(
                    diagnostico,
                    trabajo.UsuarioId,
                    estadoAnterior,
                    diagnostico.Estado,
                    "REVISION_IA_COMPLETADA",
                    "Gemini completó la evaluación adicional, analizando cada fotografía de manera independiente. El técnico debe revisar el nuevo criterio y decidir si envía el caso al analizador.");

                await db.SaveChangesAsync(cancellationToken);

                estadoStore.Actualizar(
                    diagnostico.DiagnosticoIAId,
                    diagnostico.Estado,
                    "REVISION_COMPLETADA",
                    "Gemini completó la evaluación adicional. La solicitud quedó pendiente de la decisión del técnico.",
                    total,
                    total,
                    finalizado: true);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (GeminiApiException ex)
            {
                await RegistrarErrorRevisionAsync(
                    diagnostico,
                    revision,
                    trabajo.UsuarioId,
                    ResolverMensajeGemini(ex),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error procesando la revisión IA {RevisionId} del diagnóstico {DiagnosticoId}.",
                    revision.DiagnosticoIARevisionId,
                    diagnostico.DiagnosticoIAId);

                await RegistrarErrorRevisionAsync(
                    diagnostico,
                    revision,
                    trabajo.UsuarioId,
                    "Ocurrió un error inesperado durante la revisión adicional de Gemini.",
                    CancellationToken.None);
            }
        }

        private async Task RegistrarErrorRevisionAsync(
            DiagnosticoIA diagnostico,
            DiagnosticoIARevision revision,
            int usuarioId,
            string mensaje,
            CancellationToken cancellationToken)
        {
            revision.Estado = "ERROR";
            revision.ErrorRevision = Normalizar(mensaje, 2000);
            revision.FechaRespuestaRevisionUtc = DateTime.UtcNow;

            string estadoAnterior = diagnostico.Estado;
            diagnostico.Estado =
                DiagnosticoIAFlujo.Estados.PendienteDecisionTecnico;

            AgregarHistorial(
                diagnostico,
                usuarioId,
                estadoAnterior,
                diagnostico.Estado,
                "ERROR_REVISION_IA",
                revision.ErrorRevision);

            await db.SaveChangesAsync(cancellationToken);

            estadoStore.Actualizar(
                diagnostico.DiagnosticoIAId,
                diagnostico.Estado,
                "ERROR_REVISION",
                revision.ErrorRevision,
                0,
                0,
                finalizado: true,
                tieneError: true);
        }

        private static void AplicarResultadoRevisionDesdeAnalisis(
            DiagnosticoIARevision revision,
            GeminiDiagnosticoResultado resultado,
            string diagnosticoAnterior,
            string resumenAnterior,
            string respuestaAnterior)
        {
            bool mantiene = string.Equals(
                (diagnosticoAnterior ?? string.Empty).Trim(),
                (resultado.DiagnosticoSugerido ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);

            revision.ImagenValida = resultado.ImagenValida;
            revision.ResultadoConcluyente = resultado.ResultadoConcluyente;
            revision.MantieneVeredictoOriginal = mantiene;
            revision.RelacionConCriterioTecnico =
                string.IsNullOrWhiteSpace(
                    revision.DiagnosticoPropuestoClasificador)
                    ? "NO_EVALUABLE"
                    : resultado.DiagnosticoSugerido.Contains(
                        revision.DiagnosticoPropuestoClasificador,
                        StringComparison.OrdinalIgnoreCase)
                        ? "COINCIDE"
                        : "NO_COINCIDE";
            revision.CalidadEvaluacion = resultado.CalidadEvaluacion;
            revision.EstadoGeneral = resultado.EstadoGeneral;
            revision.CategoriaPrincipal = resultado.CategoriaPrincipal;
            revision.CategoriasSecundariasJson =
                SerializarLista(resultado.CategoriasSecundarias);
            revision.DiagnosticoRevisado =
                Normalizar(resultado.DiagnosticoSugerido, 300);
            revision.TipoDiagnostico =
                Normalizar(resultado.TipoDiagnostico, 80);
            revision.SeveridadVisual = resultado.SeveridadVisual;
            revision.NivelCoincidencia = resultado.NivelCoincidencia;
            revision.ResumenRevision =
                Normalizar(resultado.Resumen, 2000);
            revision.PartesAfectadasJson =
                SerializarLista(resultado.PartesAfectadas);
            revision.EvidenciasApoyoJson =
                SerializarLista(resultado.SintomasVisibles);
            revision.EvidenciasContradiccionJson =
                SerializarLista(resultado.EvidenciasNoObservadas);
            revision.InformacionFaltanteJson =
                SerializarLista(resultado.InformacionFaltante);
            revision.RecomendacionesCapturaJson =
                SerializarLista(resultado.RecomendacionesCaptura);
            revision.AdvertenciasJson =
                SerializarLista(resultado.Advertencias);
            revision.RespuestaOriginalJson = JsonSerializer.Serialize(
                new
                {
                    diagnosticoAnterior,
                    resumenAnterior,
                    respuestaAnterior,
                    diagnosticoNuevo =
                        resultado.DiagnosticoSugerido,
                    resumenNuevo =
                        resultado.Resumen,
                    respuestaNueva =
                        resultado.RespuestaOriginalJson
                },
                JsonOptions);
            revision.ErrorRevision = string.Empty;
        }

        private static void AplicarResultadoRevision(
            DiagnosticoIARevision revision,
            GeminiRevisionResultado resultado)
        {
            revision.ImagenValida = resultado.ImagenValida;
            revision.ResultadoConcluyente = resultado.ResultadoConcluyente;
            revision.MantieneVeredictoOriginal =
                resultado.MantieneVeredictoOriginal;
            revision.RelacionConCriterioTecnico =
                Normalizar(resultado.RelacionConCriterioTecnico, 30);
            revision.CalidadEvaluacion = resultado.CalidadEvaluacion;
            revision.EstadoGeneral = resultado.EstadoGeneral;
            revision.CategoriaPrincipal = resultado.CategoriaPrincipal;
            revision.CategoriasSecundariasJson =
                SerializarLista(resultado.CategoriasSecundarias);
            revision.DiagnosticoRevisado =
                Normalizar(resultado.DiagnosticoRevisado, 300);
            revision.TipoDiagnostico =
                Normalizar(resultado.TipoDiagnostico, 80);
            revision.SeveridadVisual = resultado.SeveridadVisual;
            revision.NivelCoincidencia = resultado.NivelCoincidencia;
            revision.ResumenRevision =
                Normalizar(resultado.ResumenRevision, 2000);
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
            revision.RespuestaOriginalJson = resultado.RespuestaOriginalJson;
            revision.ErrorRevision = string.Empty;
        }

        private async Task RegistrarErrorAsync(
            DiagnosticoIA diagnostico,
            int usuarioId,
            string mensaje,
            CancellationToken cancellationToken)
        {
            string anterior = diagnostico.Estado;
            diagnostico.Estado = DiagnosticoIAFlujo.Estados.ErrorAnalisis;
            diagnostico.ErrorAnalisis = Normalizar(mensaje, 2000);
            diagnostico.FechaRespuestaIAUtc = DateTime.UtcNow;

            AgregarHistorial(
                diagnostico,
                usuarioId,
                anterior,
                diagnostico.Estado,
                "ERROR_IA",
                diagnostico.ErrorAnalisis);

            await db.SaveChangesAsync(cancellationToken);

            estadoStore.Actualizar(
                diagnostico.DiagnosticoIAId,
                diagnostico.Estado,
                "ERROR",
                diagnostico.ErrorAnalisis,
                diagnostico.Imagenes.Count(item => item.ResultadoIA != null),
                diagnostico.Imagenes.Count,
                finalizado: true,
                tieneError: true);
        }

        private void EliminarResultadosAnteriores(
            DiagnosticoIA diagnostico)
        {
            foreach (DiagnosticoIAImagen imagen in diagnostico.Imagenes)
            {
                if (imagen.ResultadoIA == null)
                    continue;

                db.ResultadosImagenIA.Remove(imagen.ResultadoIA);
                imagen.ResultadoIA = null;
            }

            diagnostico.ImagenValida = false;
            diagnostico.ParecePlantaCafe = false;
            diagnostico.ResultadoConcluyente = false;
            diagnostico.CalidadEvaluacionIA =
                DiagnosticoIAFlujo.CalidadEvaluacion.NoEvaluable;
            diagnostico.EstadoGeneralIA =
                DiagnosticoIAFlujo.EstadoGeneral.Indeterminada;
            diagnostico.CategoriaPrincipalIA =
                DiagnosticoIAFlujo.Categoria.NoAplica;
            diagnostico.CategoriasSecundariasIAJson = "[]";
            diagnostico.DiagnosticoSugerido = string.Empty;
            diagnostico.TipoDiagnosticoIA = string.Empty;
            diagnostico.SeveridadVisualIA =
                DiagnosticoIAFlujo.Severidad.NoEvaluable;
            diagnostico.NivelCoincidencia =
                DiagnosticoIAFlujo.Certeza.NoDeterminado;
            diagnostico.Resumen = string.Empty;
            diagnostico.PartesAfectadasJson = "[]";
            diagnostico.SintomasVisiblesJson = "[]";
            diagnostico.EvidenciasNoObservadasJson = "[]";
            diagnostico.DiagnosticosAlternativosJson = "[]";
            diagnostico.InformacionFaltanteJson = "[]";
            diagnostico.RecomendacionesCapturaJson = "[]";
            diagnostico.AdvertenciasJson = "[]";
            diagnostico.PosibleDanoNoBiotico = false;
            diagnostico.PosibleCausaNoBiotica = string.Empty;
            diagnostico.RespuestaOriginalJson = string.Empty;
        }

        private static void ValidarResultadoCompleto(
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

            bool duplicadas = resultado.ResultadosPorImagen
                .GroupBy(item => item.Orden)
                .Any(group => group.Count() > 1);

            bool mensajesTecnicos = resultado.ResultadosPorImagen.Any(item =>
                item.ResumenImagen.Contains(
                    "Gemini no devolvió",
                    StringComparison.OrdinalIgnoreCase) ||
                item.DiagnosticoProbable.Equals(
                    "ERROR_RESPUESTA_IA",
                    StringComparison.OrdinalIgnoreCase));

            if (esperadas.SequenceEqual(recibidas) &&
                resultado.ResultadosPorImagen.Count == esperadas.Count &&
                !duplicadas &&
                !mensajesTecnicos)
            {
                return;
            }

            throw new GeminiApiException(
                HttpStatusCode.BadGateway,
                "Gemini devolvió una respuesta incompleta por fotografía. El caso no avanzó al analizador.",
                $"Esperadas: {string.Join(", ", esperadas)}. " +
                $"Recibidas: {string.Join(", ", recibidas)}.");
        }

        private static void AplicarResultadoGemini(
            DiagnosticoIA diagnostico,
            GeminiDiagnosticoResultado resultado)
        {
            diagnostico.ImagenValida = resultado.ImagenValida;
            diagnostico.ParecePlantaCafe = resultado.ParecePlantaCafe;
            diagnostico.ResultadoConcluyente = resultado.ResultadoConcluyente;
            diagnostico.CalidadEvaluacionIA = resultado.CalidadEvaluacion;
            diagnostico.EstadoGeneralIA = resultado.EstadoGeneral;
            diagnostico.CategoriaPrincipalIA = resultado.CategoriaPrincipal;
            diagnostico.CategoriasSecundariasIAJson =
                SerializarLista(resultado.CategoriasSecundarias);
            diagnostico.DiagnosticoSugerido =
                Normalizar(resultado.DiagnosticoSugerido, 300);
            diagnostico.TipoDiagnosticoIA =
                Normalizar(resultado.TipoDiagnostico, 80);
            diagnostico.SeveridadVisualIA = resultado.SeveridadVisual;
            diagnostico.NivelCoincidencia = resultado.NivelCoincidencia;
            diagnostico.Resumen = Normalizar(resultado.Resumen, 2000);
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
            diagnostico.PosibleDanoNoBiotico = resultado.PosibleDanoNoBiotico;
            diagnostico.PosibleCausaNoBiotica =
                Normalizar(resultado.PosibleCausaNoBiotica, 500);
            diagnostico.RespuestaOriginalJson = resultado.RespuestaOriginalJson;

            foreach (GeminiImagenResultado origen in
                     resultado.ResultadosPorImagen)
            {
                DiagnosticoIAImagen? imagen = diagnostico.Imagenes
                    .FirstOrDefault(item => item.Orden == origen.Orden);

                if (imagen == null)
                    continue;

                imagen.ResultadoIA = new DiagnosticoIAImagenResultadoIA
                {
                    ImagenValida = origen.ImagenValida,
                    ParecePlantaCafe = origen.ParecePlantaCafe,
                    ResultadoConcluyente = origen.ResultadoConcluyente,
                    PartePlanta = Normalizar(origen.PartePlanta, 80),
                    CalidadEvaluacion = origen.CalidadEvaluacion,
                    EstadoGeneral = origen.EstadoGeneral,
                    CategoriaPrincipal = origen.CategoriaPrincipal,
                    CategoriasSecundariasJson =
                        SerializarLista(origen.CategoriasSecundarias),
                    DiagnosticoProbable =
                        Normalizar(origen.DiagnosticoProbable, 300),
                    TipoDiagnostico =
                        Normalizar(origen.TipoDiagnostico, 80),
                    SeveridadVisual = origen.SeveridadVisual,
                    NivelCerteza = origen.NivelCerteza,
                    ResumenImagen = Normalizar(origen.ResumenImagen, 1600),
                    SintomasVisiblesJson =
                        SerializarLista(origen.SintomasVisibles),
                    EvidenciasObservadasJson =
                        SerializarLista(origen.EvidenciasObservadas),
                    EvidenciasNoObservadasJson =
                        SerializarLista(origen.EvidenciasNoObservadas),
                    DiagnosticosAlternativosJson =
                        SerializarLista(origen.DiagnosticosAlternativos),
                    InformacionFaltanteJson =
                        SerializarLista(origen.InformacionFaltante),
                    RecomendacionesCapturaJson =
                        SerializarLista(origen.RecomendacionesCaptura),
                    AdvertenciasJson =
                        SerializarLista(origen.Advertencias),
                    FechaResultadoUtc = DateTime.UtcNow
                };
            }
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

        private static string ResolverMensajeGemini(
            GeminiApiException ex) =>
            ex.StatusCode switch
            {
                HttpStatusCode.TooManyRequests =>
                    "Gemini alcanzó temporalmente su límite de solicitudes. Las fotografías quedaron guardadas y puede reintentar más tarde.",
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout =>
                    "Gemini está temporalmente saturado. Las fotografías quedaron guardadas y puede reintentar en unos minutos.",
                HttpStatusCode.BadRequest =>
                    "Gemini rechazó el formato del análisis. Revise el registro del backend antes de reintentar.",
                HttpStatusCode.BadGateway => ex.Message,
                HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden =>
                    "Gemini rechazó la clave configurada o sus permisos.",
                HttpStatusCode.NotFound =>
                    "El modelo de Gemini configurado no está disponible.",
                _ =>
                    "Gemini no pudo completar el análisis. Las fotografías permanecen guardadas."
            };

        private static string SerializarLista(
            IEnumerable<string>? valores) =>
            JsonSerializer.Serialize(
                (valores ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                JsonOptions);

        private sealed class ProgresoEnLinea<T> : IProgress<T>
        {
            private readonly Action<T> accion;

            public ProgresoEnLinea(Action<T> accion)
            {
                this.accion = accion;
            }

            public void Report(T value) => accion(value);
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
