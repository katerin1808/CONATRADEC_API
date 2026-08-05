using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CONATRADEC_API.Services
{
    /// <summary>
    /// Cliente configurable desde el backend para Gemini nativo y proveedores
    /// compatibles con OpenAI, incluido OpenRouter. La API key se obtiene de
    /// appsettings o variables de entorno y nunca se devuelve al frontend.
    /// </summary>
    public sealed class ProveedorIAClienteService
    {
        public const string ProtocoloGeminiNativo = "GEMINI_NATIVO";
        public const string ProtocoloOpenAICompatible = "OPENAI_COMPATIBLE";

        private const string VariableApiKeyPredeterminada =
            "GEMINI_API_KEY";

        private readonly IHttpClientFactory httpClientFactory;
        private readonly IConfiguration configuration;
        private readonly ImageStoragePathService storage;
        private readonly DBContext db;
        private readonly ILogger logger;

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

        public ProveedorIAClienteService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ImageStoragePathService storage,
            DBContext db,
            ILogger logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.configuration = configuration;
            this.storage = storage;
            this.db = db;
            this.logger = logger;
        }

        /// <summary>
        /// Constructor de compatibilidad para controladores creados con la
        /// versión anterior del módulo. La inicialización de tablas ya no es
        /// responsabilidad del cliente de IA, por lo que el parámetro database
        /// se conserva únicamente para evitar romper esos controladores.
        /// </summary>
        public ProveedorIAClienteService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ImageStoragePathService storage,
            DBContext db,
            InspeccionFitosanitariaDatabase database,
            ILogger logger)
            : this(
                httpClientFactory,
                configuration,
                storage,
                db,
                logger)
        {
            ArgumentNullException.ThrowIfNull(database);
        }

        /// <summary>
        /// Devuelve una vista segura de la configuración cargada por el backend.
        /// La API key nunca se envía al cliente.
        /// </summary>
        public Task<ProveedorIAConfiguracionDto> ObtenerDtoAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProveedorEjecucion proveedor = CrearProveedorDesdeConfiguracion();
            return Task.FromResult(CrearDto(proveedor));
        }

        /// <summary>
        /// La configuración ya no se guarda desde MAUI. Se conserva la firma
        /// para que versiones anteriores del cliente reciban un mensaje claro.
        /// </summary>
        public Task<ProveedorIAConfiguracionDto> GuardarAsync(
            ProveedorIAConfiguracionActualizarRequest request,
            int usuarioId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                "El proveedor de IA se configura directamente en el backend mediante appsettings o variables de entorno.");
        }

        /// <summary>
        /// Prueba exclusivamente la configuración activa del servidor. Los
        /// valores enviados por el frontend se ignoran intencionalmente.
        /// </summary>
        public async Task<ProveedorIAPruebaDto> ProbarAsync(
            ProveedorIAConfiguracionActualizarRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            ProveedorEjecucion proveedor = CrearProveedorDesdeConfiguracion();

            if (!proveedor.Activo)
            {
                return new ProveedorIAPruebaDto
                {
                    Exitoso = false,
                    CodigoHttp = StatusCodes.Status503ServiceUnavailable,
                    Proveedor = proveedor.Proveedor,
                    Modelo = proveedor.ModeloPrincipal,
                    Mensaje = "El proveedor de IA está deshabilitado en el backend."
                };
            }

            if (string.IsNullOrWhiteSpace(proveedor.ApiKey))
            {
                return new ProveedorIAPruebaDto
                {
                    Exitoso = false,
                    CodigoHttp = StatusCodes.Status401Unauthorized,
                    Proveedor = proveedor.Proveedor,
                    Modelo = proveedor.ModeloPrincipal,
                    Mensaje =
                        $"No existe la variable de entorno {proveedor.ApiKeyVariableEntorno} o se encuentra vacía."
                };
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                await EnviarPruebaAsync(proveedor, cancellationToken);
                stopwatch.Stop();

                return new ProveedorIAPruebaDto
                {
                    Exitoso = true,
                    CodigoHttp = StatusCodes.Status200OK,
                    Proveedor = proveedor.Proveedor,
                    Modelo = proveedor.ModeloPrincipal,
                    Mensaje = "Conexión realizada correctamente.",
                    Milisegundos = stopwatch.ElapsedMilliseconds
                };
            }
            catch (ProveedorIAException ex)
            {
                stopwatch.Stop();

                return new ProveedorIAPruebaDto
                {
                    Exitoso = false,
                    CodigoHttp = (int)ex.StatusCode,
                    Proveedor = proveedor.Proveedor,
                    Modelo = proveedor.ModeloPrincipal,
                    Mensaje = ex.Message,
                    Milisegundos = stopwatch.ElapsedMilliseconds
                };
            }
        }

        public Task<ProveedorEjecucion> ObtenerEjecucionAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CrearProveedorDesdeConfiguracion());
        }

        public async Task<ProveedorIAResultadoFoto> AnalizarFotoAsync(
            DiagnosticoIAImagen imagen,
            string? observacionUsuario,
            string? retroalimentacion,
            string? diagnosticoPropuesto,
            CancellationToken cancellationToken = default)
        {
            ProveedorEjecucion proveedor =
                await ObtenerEjecucionAsync(cancellationToken);

            if (!proveedor.Activo)
            {
                throw new ProveedorIAException(
                    HttpStatusCode.ServiceUnavailable,
                    "El proveedor de IA está deshabilitado en la configuración.");
            }

            if (string.IsNullOrWhiteSpace(proveedor.ApiKey))
            {
                throw new ProveedorIAException(
                    HttpStatusCode.Unauthorized,
                    $"No existe la variable de entorno {proveedor.ApiKeyVariableEntorno} o se encuentra vacía.");
            }

            string rutaFisica = storage.ResolverRutaPublica(
                imagen.RutaRelativa);

            if (!File.Exists(rutaFisica))
            {
                throw new ProveedorIAException(
                    HttpStatusCode.NotFound,
                    "No se encontró el archivo físico de la fotografía.");
            }

            byte[] contenido = await File.ReadAllBytesAsync(
                rutaFisica,
                cancellationToken);

            string base64 = Convert.ToBase64String(contenido);
            string prompt = await ConstruirPromptAsync(
                imagen,
                observacionUsuario,
                retroalimentacion,
                diagnosticoPropuesto,
                cancellationToken);

            List<string> modelos = [proveedor.ModeloPrincipal];

            if (!string.IsNullOrWhiteSpace(proveedor.ModeloRespaldo) &&
                !string.Equals(
                    proveedor.ModeloRespaldo,
                    proveedor.ModeloPrincipal,
                    StringComparison.OrdinalIgnoreCase))
            {
                modelos.Add(proveedor.ModeloRespaldo);
            }

            ProveedorIAException? ultimoError = null;

            foreach (string modelo in modelos)
            {
                try
                {
                    string respuesta = proveedor.Protocolo ==
                        ProtocoloOpenAICompatible
                            ? await EnviarOpenAICompatibleAsync(
                                proveedor,
                                modelo,
                                prompt,
                                base64,
                                cancellationToken)
                            : await EnviarGeminiNativoAsync(
                                proveedor,
                                modelo,
                                prompt,
                                base64,
                                cancellationToken);

                    ProveedorIAResultadoFoto? resultado =
                        JsonSerializer.Deserialize<ProveedorIAResultadoFoto>(
                            LimpiarJson(respuesta),
                            JsonOptions);

                    if (resultado == null)
                    {
                        throw new ProveedorIAException(
                            HttpStatusCode.BadGateway,
                            "El proveedor devolvió una respuesta vacía.");
                    }

                    NormalizarResultado(resultado);
                    resultado.Proveedor = proveedor.Proveedor;
                    resultado.Modelo = modelo;
                    resultado.RespuestaOriginalJson = respuesta;
                    return resultado;
                }
                catch (ProveedorIAException ex)
                {
                    ultimoError = ex;

                    bool permiteFallback = ex.StatusCode is
                        HttpStatusCode.NotFound or
                        HttpStatusCode.TooManyRequests or
                        HttpStatusCode.BadGateway or
                        HttpStatusCode.ServiceUnavailable or
                        HttpStatusCode.GatewayTimeout;

                    if (!permiteFallback || modelo == modelos[^1])
                        throw;
                }
            }

            throw ultimoError ?? new ProveedorIAException(
                HttpStatusCode.ServiceUnavailable,
                "El proveedor de IA no pudo completar el análisis.");
        }

        private async Task<string> ConstruirPromptAsync(
            DiagnosticoIAImagen imagen,
            string? observacionUsuario,
            string? retroalimentacion,
            string? diagnosticoPropuesto,
            CancellationToken cancellationToken)
        {
            var categorias = await db.CategoriasAlbumBotanico
                .AsNoTracking()
                .Where(item => item.activo)
                .OrderBy(item => item.nombreCategoria)
                .Select(item => new
                {
                    id = item.categoriaAlbumBotanicoId,
                    nombre = item.nombreCategoria
                })
                .ToListAsync(cancellationToken);

            var fichas = await db.AlbumesBotanicosCafe
                .AsNoTracking()
                .Where(item => item.activo)
                .OrderBy(item => item.titulo)
                .Select(item => new
                {
                    id = item.albumBotanicoCafeId,
                    categoriaId = item.categoriaAlbumBotanicoId,
                    titulo = item.titulo,
                    nombreCientifico = item.nombreCientifico
                })
                .ToListAsync(cancellationToken);

            string catalogoJson = JsonSerializer.Serialize(
                new { categorias, fichas },
                JsonOptions);

            TipoFotografiaIAContexto contextoTipo =
                await ObtenerContextoTipoFotografiaAsync(
                    imagen.TipoFotografia,
                    cancellationToken);

            return $$"""
Actúa como apoyo preliminar para una inspección fitosanitaria de café.
No sustituyes al técnico, al analizador ni al aprobador humano.
Analiza solamente la fotografía adjunta y no inventes síntomas que no sean visibles.

Tipo declarado por el técnico: {{contextoTipo.Codigo}} - {{contextoTipo.Nombre}}.
Descripción del tipo: {{contextoTipo.Descripcion}}
Instrucción específica para esta fotografía:
{{contextoTipo.InstruccionIA}}
La instrucción específica orienta tu atención, pero no autoriza a inventar síntomas
ni a ignorar otras evidencias claramente visibles en la fotografía.

Observación de campo: {{Normalizar(observacionUsuario, 1000)}}
Retroalimentación para una revisión adicional: {{Normalizar(retroalimentacion, 2000)}}
Diagnóstico que el humano considera posible: {{Normalizar(diagnosticoPropuesto, 300)}}

Catálogo activo del Álbum Botánico de CONATRADEC:
{{catalogoJson}}

Reglas para esta primera versión:
1. Determina primero si la imagen permite evaluar una planta de café.
2. Si no corresponde a café, establece parecePlantaCafe=false,
   resultadoConcluyente=false y categoriaPrincipal=NO_APLICA. No intentes
   identificar otra especie ni registrarla para entrenamiento.
3. Cuando sí corresponda a café, prioriza la detección visual de PLAGAS y
   ENFERMEDADES; no inventes agentes causales que no sean visibles.
4. Usa una ficha existente solamente cuando la coincidencia visual sea razonable.
5. Cuando no exista una ficha segura, deja albumBotanicoCafeIdSugerido en null
   y propone una clasificación para revisión humana.
6. Nunca crees una ficha ni apruebes una publicación.
7. Si la fotografía está borrosa, oscura, demasiado lejana o la evidencia es
   insuficiente, usa calidadEvaluacion=NO_EVALUABLE o PARCIALMENTE_EVALUABLE
   y resultadoConcluyente=false.

Devuelve exclusivamente un objeto JSON con estas propiedades:
imagenValida, parecePlantaCafe, resultadoConcluyente, partePlanta,
calidadEvaluacion, estadoGeneral, categoriaPrincipal, categoriasSecundarias,
diagnosticoProbable, tipoDiagnostico, severidadVisual, nivelCerteza,
categoriaAlbumBotanicoIdSugerida, albumBotanicoCafeIdSugerido,
categoriaAlbumSugerida, clasificacionAlbumSugerida,
nombreCientificoSugerido, coincideCatalogoAlbum,
requiereDecisionClasificacion, motivoClasificacionAlbum, resumenImagen,
sintomasVisibles, evidenciasObservadas, evidenciasNoObservadas,
diagnosticosAlternativos, informacionFaltante,
recomendacionesCaptura y advertencias.

Valores controlados:
- calidadEvaluacion: EVALUABLE, PARCIALMENTE_EVALUABLE o NO_EVALUABLE.
- estadoGeneral: APARENTEMENTE_SANA, CON_AFECTACION o INDETERMINADA.
- categoriaPrincipal: ENFERMEDAD, PLAGA, ALTERACION_NUTRICIONAL,
  ESTRES_ABIOTICO, DANO_MECANICO, NO_APLICA o AFECTACION_NO_DETERMINADA.
- severidadVisual: LEVE, MODERADA, SEVERA, NO_APLICA o NO_EVALUABLE.
- nivelCerteza: ALTO, MEDIO, BAJO o NO_DETERMINADO.
""";
        }

        private async Task<TipoFotografiaIAContexto>
            ObtenerContextoTipoFotografiaAsync(
                string? codigoRecibido,
                CancellationToken cancellationToken)
        {
            string codigo = NormalizarCodigoTipo(codigoRecibido);

            const string sql = """
SELECT TOP (1)
    [Codigo], [Nombre], [Descripcion], [InstruccionIA]
FROM [dbo].[tipoFotografiaIA]
WHERE [Codigo] = @codigo
ORDER BY [Activo] DESC, [FechaModificacionUtc] DESC;
""";

            try
            {
                DbConnection connection = db.Database.GetDbConnection();
                await using DbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.CommandType = CommandType.Text;
                command.CommandTimeout = 30;

                DbParameter parameter = command.CreateParameter();
                parameter.ParameterName = "@codigo";
                parameter.Value = codigo;
                command.Parameters.Add(parameter);

                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync(cancellationToken);

                await using DbDataReader reader =
                    await command.ExecuteReaderAsync(cancellationToken);

                if (await reader.ReadAsync(cancellationToken))
                {
                    return new TipoFotografiaIAContexto(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3));
                }
            }
            catch (Exception ex)
            {
                /*
                 * Permite analizar registros anteriores aunque el catálogo aún
                 * no se haya inicializado. La selección de fotografías nuevas
                 * sí carga el catálogo desde su endpoint administrativo.
                 */
                logger.LogWarning(
                    ex,
                    "No fue posible cargar la instrucción del tipo de fotografía {Codigo}.",
                    codigo);
            }

            return CrearContextoPredeterminado(codigo);
        }

        private static TipoFotografiaIAContexto
            CrearContextoPredeterminado(string codigo) =>
            codigo switch
            {
                "HOJA" => new(
                    codigo,
                    "Hoja",
                    "Fotografía enfocada principalmente en hojas de café.",
                    "Prioriza manchas, clorosis, necrosis, perforaciones, galerías, pústulas, micelio, esporas, insectos, deformaciones y distribución de síntomas en el haz y el envés."),
                "FRUTO" => new(
                    codigo,
                    "Fruto",
                    "Fotografía enfocada en frutos del café.",
                    "Prioriza coloración, madurez anormal, lesiones, perforaciones, pudrición, momificación, deformaciones y presencia de broca u otros insectos."),
                "TALLO" => new(
                    codigo,
                    "Tallo",
                    "Fotografía enfocada en tallos del cafeto.",
                    "Prioriza lesiones, cancros, grietas, perforaciones, descortezamiento, exudados, pudrición y presencia de insectos."),
                "RAMA" => new(
                    codigo,
                    "Rama",
                    "Fotografía enfocada en una rama.",
                    "Prioriza lesiones, defoliación, marchitez, muerte regresiva, nudos, hojas o frutos asociados y presencia de insectos."),
                "PLANTA_COMPLETA" => new(
                    codigo,
                    "Planta completa",
                    "Fotografía general del cafeto.",
                    "Evalúa vigor, arquitectura, distribución de síntomas, marchitez, defoliación, coloración y daños generalizados."),
                "RAIZ" => new(
                    codigo,
                    "Raíz",
                    "Fotografía de raíces o cuello de la planta.",
                    "Prioriza pudrición, necrosis, deformaciones, agallas, pérdida de raíces finas, lesiones del cuello y plagas del suelo."),
                "OTRA" => new(
                    codigo,
                    "Otra evidencia",
                    "Evidencia diferente de los tipos comunes.",
                    "Describe primero el contenido visible y adapta el análisis a la evidencia y a la observación del técnico."),
                _ => new(
                    "EVIDENCIA",
                    "Evidencia general",
                    "Fotografía general de una inspección fitosanitaria.",
                    "Describe el contenido visible y revisa síntomas, plagas, enfermedades, daños mecánicos y condiciones anormales en cualquier parte del cafeto.")
            };

        private static string NormalizarCodigoTipo(string? valor)
        {
            string codigo = (valor ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace(' ', '_');

            return string.IsNullOrWhiteSpace(codigo)
                ? "EVIDENCIA"
                : codigo.Length <= 40
                    ? codigo
                    : codigo[..40];
        }

        private async Task<string> EnviarGeminiNativoAsync(
            ProveedorEjecucion proveedor,
            string modelo,
            string prompt,
            string base64,
            CancellationToken cancellationToken)
        {
            string endpoint = proveedor.Endpoint.Replace(
                "{model}",
                Uri.EscapeDataString(modelo),
                StringComparison.OrdinalIgnoreCase);

            Uri url = CrearUri(proveedor.BaseUrl, endpoint);

            object payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { text = prompt },
                            new
                            {
                                inlineData = new
                                {
                                    mimeType = "image/webp",
                                    data = base64
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    responseJsonSchema = CrearSchemaRespuesta(),
                    maxOutputTokens = 4000
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation(
                "x-goog-api-key",
                proveedor.ApiKey);
            request.Content = JsonContent.Create(payload, options: JsonOptions);

            string responseJson = await EnviarAsync(
                request,
                proveedor.TimeoutSegundos,
                cancellationToken);

            using JsonDocument document = JsonDocument.Parse(responseJson);

            if (!document.RootElement.TryGetProperty("candidates", out JsonElement candidates) ||
                candidates.GetArrayLength() == 0)
            {
                throw new ProveedorIAException(
                    HttpStatusCode.BadGateway,
                    "Gemini no devolvió candidatos válidos.");
            }

            JsonElement parts = candidates[0]
                .GetProperty("content")
                .GetProperty("parts");

            foreach (JsonElement part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out JsonElement text))
                    return text.GetString() ?? string.Empty;
            }

            throw new ProveedorIAException(
                HttpStatusCode.BadGateway,
                "Gemini no devolvió contenido JSON interpretable.");
        }

        private async Task<string> EnviarOpenAICompatibleAsync(
            ProveedorEjecucion proveedor,
            string modelo,
            string prompt,
            string base64,
            CancellationToken cancellationToken)
        {
            Uri url = CrearUri(proveedor.BaseUrl, proveedor.Endpoint);

            object payload = new
            {
                model = modelo,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:image/webp;base64,{base64}"
                                }
                            }
                        }
                    }
                },
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "diagnostico_fitosanitario",
                        strict = true,
                        schema = CrearSchemaRespuesta()
                    }
                },
                max_tokens = 4000,
                temperature = 0.1
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                proveedor.ApiKey);
            request.Headers.TryAddWithoutValidation(
                "X-Title",
                "CONATRADEC Diagnostico Fitosanitario");
            request.Content = JsonContent.Create(payload, options: JsonOptions);

            string responseJson = await EnviarAsync(
                request,
                proveedor.TimeoutSegundos,
                cancellationToken);

            using JsonDocument document = JsonDocument.Parse(responseJson);

            JsonElement message = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message");

            JsonElement content = message.GetProperty("content");

            if (content.ValueKind == JsonValueKind.String)
                return content.GetString() ?? string.Empty;

            if (content.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in content.EnumerateArray())
                {
                    if (item.TryGetProperty("text", out JsonElement text))
                        return text.GetString() ?? string.Empty;
                }
            }

            throw new ProveedorIAException(
                HttpStatusCode.BadGateway,
                "El proveedor compatible con OpenAI no devolvió contenido JSON interpretable.");
        }

        private async Task EnviarPruebaAsync(
            ProveedorEjecucion proveedor,
            CancellationToken cancellationToken)
        {
            if (proveedor.Protocolo == ProtocoloOpenAICompatible)
            {
                Uri url = CrearUri(proveedor.BaseUrl, proveedor.Endpoint);
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    proveedor.ApiKey);
                request.Content = JsonContent.Create(
                    new
                    {
                        model = proveedor.ModeloPrincipal,
                        messages = new[]
                        {
                            new { role = "user", content = "Responde únicamente OK." }
                        },
                        max_tokens = 8,
                        temperature = 0
                    },
                    options: JsonOptions);

                _ = await EnviarAsync(
                    request,
                    proveedor.TimeoutSegundos,
                    cancellationToken);
                return;
            }

            string endpoint = proveedor.Endpoint.Replace(
                "{model}",
                Uri.EscapeDataString(proveedor.ModeloPrincipal),
                StringComparison.OrdinalIgnoreCase);

            using var geminiRequest = new HttpRequestMessage(
                HttpMethod.Post,
                CrearUri(proveedor.BaseUrl, endpoint));

            geminiRequest.Headers.TryAddWithoutValidation(
                "x-goog-api-key",
                proveedor.ApiKey);
            geminiRequest.Content = JsonContent.Create(
                new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = "Responde únicamente OK." }
                            }
                        }
                    },
                    generationConfig = new { maxOutputTokens = 8 }
                },
                options: JsonOptions);

            _ = await EnviarAsync(
                geminiRequest,
                proveedor.TimeoutSegundos,
                cancellationToken);
        }

        private async Task<string> EnviarAsync(
            HttpRequestMessage request,
            int timeoutSegundos,
            CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(
                Math.Clamp(timeoutSegundos, 15, 600)));

            try
            {
                HttpClient client = httpClientFactory.CreateClient();
                using HttpResponseMessage response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);

                string content = await response.Content.ReadAsStringAsync(
                    timeout.Token);

                if (response.IsSuccessStatusCode)
                    return content;

                string detalle = ExtraerError(content);
                string mensaje = response.StatusCode switch
                {
                    HttpStatusCode.BadRequest =>
                        "El proveedor rechazó la solicitud. Revise el modelo, endpoint y formato configurado.",
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                        "La API key no fue aceptada por el proveedor.",
                    HttpStatusCode.NotFound =>
                        "No se encontró el endpoint o modelo configurado.",
                    HttpStatusCode.TooManyRequests =>
                        "El proveedor alcanzó su límite temporal de solicitudes.",
                    HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout =>
                        "El proveedor tardó demasiado en responder.",
                    _ when (int)response.StatusCode >= 500 =>
                        "El proveedor de IA se encuentra temporalmente no disponible.",
                    _ => "El proveedor de IA rechazó la solicitud."
                };

                logger.LogWarning(
                    "Proveedor IA respondió {Status}. Detalle: {Detalle}",
                    (int)response.StatusCode,
                    detalle);

                throw new ProveedorIAException(
                    response.StatusCode,
                    string.IsNullOrWhiteSpace(detalle)
                        ? mensaje
                        : $"{mensaje} {Limitar(detalle, 500)}");
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new ProveedorIAException(
                    HttpStatusCode.GatewayTimeout,
                    "El proveedor de IA superó el tiempo máximo configurado.");
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "No fue posible conectar con el proveedor IA.");
                throw new ProveedorIAException(
                    HttpStatusCode.ServiceUnavailable,
                    "No fue posible establecer comunicación con el proveedor de IA.");
            }
        }

        /// <summary>
        /// Obtiene exclusivamente la clave desde una variable de entorno.
        /// El nombre de la variable se puede cambiar desde ProveedorIA:
        /// ApiKeyVariableEntorno sin almacenar la clave en appsettings.
        /// </summary>
        private (string NombreVariable, string ApiKey)
            ObtenerApiKeyConfigurada()
        {
            string nombreVariable =
                configuration["ProveedorIA:ApiKeyVariableEntorno"]?.Trim() ??
                VariableApiKeyPredeterminada;

            if (string.IsNullOrWhiteSpace(nombreVariable))
                nombreVariable = VariableApiKeyPredeterminada;

            string apiKey =
                Environment.GetEnvironmentVariable(nombreVariable)?.Trim() ??
                configuration[nombreVariable]?.Trim() ??
                string.Empty;

            return (nombreVariable, apiKey);
        }

        private static object CrearSchemaRespuesta() => new
        {
            type = "object",
            additionalProperties = false,
            required = new[]
            {
                "imagenValida", "parecePlantaCafe", "resultadoConcluyente",
                "partePlanta", "calidadEvaluacion", "estadoGeneral",
                "categoriaPrincipal", "categoriasSecundarias",
                "diagnosticoProbable", "tipoDiagnostico",
                "severidadVisual", "nivelCerteza",
                "categoriaAlbumBotanicoIdSugerida",
                "albumBotanicoCafeIdSugerido", "categoriaAlbumSugerida",
                "clasificacionAlbumSugerida", "nombreCientificoSugerido",
                "coincideCatalogoAlbum", "requiereDecisionClasificacion",
                "motivoClasificacionAlbum", "resumenImagen",
                "sintomasVisibles", "evidenciasObservadas",
                "evidenciasNoObservadas", "diagnosticosAlternativos",
                "informacionFaltante", "recomendacionesCaptura",
                "advertencias"
            },
            properties = new Dictionary<string, object>
            {
                ["imagenValida"] = new { type = "boolean" },
                ["parecePlantaCafe"] = new { type = "boolean" },
                ["resultadoConcluyente"] = new { type = "boolean" },
                ["partePlanta"] = new { type = "string" },
                ["calidadEvaluacion"] = new { type = "string" },
                ["estadoGeneral"] = new { type = "string" },
                ["categoriaPrincipal"] = new { type = "string" },
                ["categoriasSecundarias"] = EsquemaLista(),
                ["diagnosticoProbable"] = new { type = "string" },
                ["tipoDiagnostico"] = new { type = "string" },
                ["severidadVisual"] = new { type = "string" },
                ["nivelCerteza"] = new { type = "string" },
                ["categoriaAlbumBotanicoIdSugerida"] =
                    new { type = new[] { "integer", "null" } },
                ["albumBotanicoCafeIdSugerido"] =
                    new { type = new[] { "integer", "null" } },
                ["categoriaAlbumSugerida"] = new { type = "string" },
                ["clasificacionAlbumSugerida"] = new { type = "string" },
                ["nombreCientificoSugerido"] = new { type = "string" },
                ["coincideCatalogoAlbum"] = new { type = "boolean" },
                ["requiereDecisionClasificacion"] = new { type = "boolean" },
                ["motivoClasificacionAlbum"] = new { type = "string" },
                ["resumenImagen"] = new { type = "string" },
                ["sintomasVisibles"] = EsquemaLista(),
                ["evidenciasObservadas"] = EsquemaLista(),
                ["evidenciasNoObservadas"] = EsquemaLista(),
                ["diagnosticosAlternativos"] = EsquemaLista(),
                ["informacionFaltante"] = EsquemaLista(),
                ["recomendacionesCaptura"] = EsquemaLista(),
                ["advertencias"] = EsquemaLista()
            }
        };

        private static object EsquemaLista() => new
        {
            type = "array",
            items = new { type = "string" }
        };

        private static void NormalizarResultado(
            ProveedorIAResultadoFoto resultado)
        {
            resultado.CalidadEvaluacion = NormalizarControlado(
                resultado.CalidadEvaluacion,
                ["EVALUABLE", "PARCIALMENTE_EVALUABLE", "NO_EVALUABLE"],
                "NO_EVALUABLE");
            resultado.EstadoGeneral = NormalizarControlado(
                resultado.EstadoGeneral,
                ["APARENTEMENTE_SANA", "CON_AFECTACION", "INDETERMINADA"],
                "INDETERMINADA");
            resultado.CategoriaPrincipal = NormalizarControlado(
                resultado.CategoriaPrincipal,
                [
                    "ENFERMEDAD", "PLAGA", "ALTERACION_NUTRICIONAL",
                    "ESTRES_ABIOTICO", "DANO_MECANICO", "NO_APLICA",
                    "AFECTACION_NO_DETERMINADA"
                ],
                "AFECTACION_NO_DETERMINADA");
            resultado.SeveridadVisual = NormalizarControlado(
                resultado.SeveridadVisual,
                ["LEVE", "MODERADA", "SEVERA", "NO_APLICA", "NO_EVALUABLE"],
                "NO_EVALUABLE");
            resultado.NivelCerteza = NormalizarControlado(
                resultado.NivelCerteza,
                ["ALTO", "MEDIO", "BAJO", "NO_DETERMINADO"],
                "NO_DETERMINADO");

            resultado.PartePlanta = Normalizar(resultado.PartePlanta, 80);
            resultado.DiagnosticoProbable = Normalizar(
                resultado.DiagnosticoProbable,
                300);
            resultado.TipoDiagnostico = Normalizar(
                resultado.TipoDiagnostico,
                80);
            resultado.CategoriaAlbumSugerida = Normalizar(
                resultado.CategoriaAlbumSugerida,
                150);
            resultado.ClasificacionAlbumSugerida = Normalizar(
                resultado.ClasificacionAlbumSugerida,
                200);
            resultado.NombreCientificoSugerido = Normalizar(
                resultado.NombreCientificoSugerido,
                200);
            resultado.MotivoClasificacionAlbum = Normalizar(
                resultado.MotivoClasificacionAlbum,
                1000);
            resultado.ResumenImagen = Normalizar(
                resultado.ResumenImagen,
                1600);

            resultado.CategoriasSecundarias = LimpiarLista(
                resultado.CategoriasSecundarias,
                6,
                80);
            resultado.SintomasVisibles = LimpiarLista(
                resultado.SintomasVisibles,
                10,
                400);
            resultado.EvidenciasObservadas = LimpiarLista(
                resultado.EvidenciasObservadas,
                10,
                400);
            resultado.EvidenciasNoObservadas = LimpiarLista(
                resultado.EvidenciasNoObservadas,
                10,
                400);
            resultado.DiagnosticosAlternativos = LimpiarLista(
                resultado.DiagnosticosAlternativos,
                8,
                300);
            resultado.InformacionFaltante = LimpiarLista(
                resultado.InformacionFaltante,
                8,
                400);
            resultado.RecomendacionesCaptura = LimpiarLista(
                resultado.RecomendacionesCaptura,
                8,
                400);
            resultado.Advertencias = LimpiarLista(
                resultado.Advertencias,
                8,
                400);
        }

        private static string NormalizarControlado(
            string? valor,
            IReadOnlyCollection<string> permitidos,
            string predeterminado)
        {
            string normalizado = (valor ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            return permitidos.Contains(normalizado)
                ? normalizado
                : predeterminado;
        }

        private static List<string> LimpiarLista(
            IEnumerable<string>? valores,
            int maximoElementos,
            int maximoCaracteres) =>
            (valores ?? [])
                .Select(item => Normalizar(item, maximoCaracteres))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maximoElementos)
                .ToList();

        private static string LimpiarJson(string valor)
        {
            string texto = (valor ?? string.Empty).Trim();

            if (texto.StartsWith("```", StringComparison.Ordinal))
            {
                int primeraLinea = texto.IndexOf('\n');
                if (primeraLinea >= 0)
                    texto = texto[(primeraLinea + 1)..];

                int cierre = texto.LastIndexOf("```", StringComparison.Ordinal);
                if (cierre >= 0)
                    texto = texto[..cierre];
            }

            int inicio = texto.IndexOf('{');
            int fin = texto.LastIndexOf('}');
            return inicio >= 0 && fin > inicio
                ? texto[inicio..(fin + 1)]
                : texto;
        }

        private static string ExtraerError(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);

                if (document.RootElement.TryGetProperty("error", out JsonElement error))
                {
                    if (error.ValueKind == JsonValueKind.String)
                        return error.GetString() ?? string.Empty;

                    if (error.TryGetProperty("message", out JsonElement message))
                        return message.GetString() ?? string.Empty;
                }

                if (document.RootElement.TryGetProperty("message", out JsonElement rootMessage))
                    return rootMessage.GetString() ?? string.Empty;
            }
            catch (JsonException)
            {
            }

            return Limitar(json, 500);
        }

        private static Uri CrearUri(string baseUrl, string endpoint)
        {
            string baseNormalizada = NormalizarBaseUrl(baseUrl);
            string endpointNormalizado = NormalizarEndpoint(endpoint);
            return new Uri(new Uri(baseNormalizada), endpointNormalizado);
        }

        private static string NormalizarBaseUrl(string valor)
        {
            string texto = valor.Trim();

            if (!Uri.TryCreate(texto, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme is not ("https" or "http"))
            {
                throw new ArgumentException(
                    "La URL base del proveedor no es válida.");
            }

            return texto.EndsWith('/') ? texto : $"{texto}/";
        }

        private static string NormalizarEndpoint(string valor) =>
            valor.Trim().TrimStart('/');

        private static void ValidarRequest(
            ProveedorIAConfiguracionActualizarRequest request)
        {
            string protocolo = request.Protocolo
                .Trim()
                .ToUpperInvariant();

            if (protocolo is not
                (ProtocoloGeminiNativo or ProtocoloOpenAICompatible))
            {
                throw new ArgumentException(
                    "El protocolo debe ser GEMINI_NATIVO u OPENAI_COMPATIBLE.");
            }

            _ = NormalizarBaseUrl(request.BaseUrl);

            if (string.IsNullOrWhiteSpace(request.Endpoint))
                throw new ArgumentException("El endpoint es obligatorio.");

            if (string.IsNullOrWhiteSpace(request.ModeloPrincipal))
                throw new ArgumentException("El modelo principal es obligatorio.");
        }

        private ProveedorEjecucion CrearProveedorDesdeConfiguracion()
        {
            string proveedor = (configuration["ProveedorIA:Proveedor"] ??
                "GEMINI").Trim().ToUpperInvariant();

            string protocoloPredeterminado = proveedor is
                "OPENROUTER" or "OPENAI"
                    ? ProtocoloOpenAICompatible
                    : ProtocoloGeminiNativo;

            string protocolo = (configuration["ProveedorIA:Protocolo"] ??
                protocoloPredeterminado).Trim().ToUpperInvariant();

            if (protocolo is not
                (ProtocoloGeminiNativo or ProtocoloOpenAICompatible))
            {
                throw new InvalidOperationException(
                    "ProveedorIA:Protocolo debe ser GEMINI_NATIVO u OPENAI_COMPATIBLE.");
            }

            bool esOpenAICompatible =
                protocolo == ProtocoloOpenAICompatible;

            string baseUrlPredeterminada = esOpenAICompatible
                ? "https://openrouter.ai/api/v1/"
                : "https://generativelanguage.googleapis.com/";

            string endpointPredeterminado = esOpenAICompatible
                ? "chat/completions"
                : "v1beta/models/{model}:generateContent";

            string modeloPredeterminado = esOpenAICompatible
                ? string.Empty
                : "gemini-3.6-flash";

            string respaldoPredeterminado = esOpenAICompatible
                ? string.Empty
                : "gemini-3.5-flash-lite";

            string baseUrl = NormalizarBaseUrl(
                configuration["ProveedorIA:BaseUrl"]?.Trim() ??
                baseUrlPredeterminada);

            string endpoint = NormalizarEndpoint(
                configuration["ProveedorIA:Endpoint"]?.Trim() ??
                endpointPredeterminado);

            string modeloPrincipal =
                configuration["ProveedorIA:ModeloPrincipal"]?.Trim() ??
                configuration["Gemini:Model"]?.Trim() ??
                modeloPredeterminado;

            string modeloRespaldo =
                configuration["ProveedorIA:ModeloRespaldo"]?.Trim() ??
                configuration["Gemini:FallbackModel"]?.Trim() ??
                respaldoPredeterminado;

            /*
             * Conserva compatibilidad con configuraciones anteriores, pero
             * evita enviar modelos retirados o restringidos para cuentas nuevas.
             * También acepta valores copiados como "models/gemini-...".
             */
            if (!esOpenAICompatible)
            {
                modeloPrincipal = NormalizarModeloGemini(
                    modeloPrincipal,
                    modeloPredeterminado);

                modeloRespaldo = NormalizarModeloGemini(
                    modeloRespaldo,
                    respaldoPredeterminado);
            }

            if (string.IsNullOrWhiteSpace(modeloPrincipal))
            {
                throw new InvalidOperationException(
                    "Debe configurar ProveedorIA:ModeloPrincipal en el backend.");
            }

            int timeout = Math.Clamp(
                configuration.GetValue<int?>(
                    "ProveedorIA:TimeoutSegundos") ?? 180,
                15,
                600);

            bool activo = configuration.GetValue<bool?>(
                "ProveedorIA:Activo") ?? true;

            (string nombreVariableApiKey, string apiKey) =
                ObtenerApiKeyConfigurada();

            return new ProveedorEjecucion(
                proveedor,
                protocolo,
                baseUrl,
                endpoint,
                apiKey,
                nombreVariableApiKey,
                Normalizar(modeloPrincipal, 160),
                Normalizar(modeloRespaldo, 160),
                timeout,
                activo);
        }

        private static string NormalizarModeloGemini(
            string? valor,
            string predeterminado)
        {
            string modelo = string.IsNullOrWhiteSpace(valor)
                ? predeterminado
                : valor.Trim();

            if (modelo.StartsWith(
                    "models/",
                    StringComparison.OrdinalIgnoreCase))
            {
                modelo = modelo["models/".Length..];
            }

            return modelo.ToLowerInvariant() switch
            {
                "gemini-2.0-flash" =>
                    "gemini-3.5-flash-lite",

                "gemini-2.0-flash-lite" =>
                    "gemini-3.5-flash-lite",

                "gemini-2.5-flash" =>
                    "gemini-3.6-flash",

                "gemini-2.5-flash-lite" =>
                    "gemini-3.5-flash-lite",

                _ => modelo
            };
        }

        private ProveedorIAConfiguracionDto CrearDto(
            ProveedorEjecucion proveedor)
        {
            bool tieneApiKey =
                !string.IsNullOrWhiteSpace(proveedor.ApiKey);

            return new ProveedorIAConfiguracionDto
            {
                Proveedor = proveedor.Proveedor,
                Protocolo = proveedor.Protocolo,
                BaseUrl = proveedor.BaseUrl,
                Endpoint = proveedor.Endpoint,
                ApiKeyMascara = tieneApiKey
                    ? CrearMascara(proveedor.ApiKey)
                    : string.Empty,
                TieneApiKey = tieneApiKey,
                ModeloPrincipal = proveedor.ModeloPrincipal,
                ModeloRespaldo = proveedor.ModeloRespaldo,
                TimeoutSegundos = proveedor.TimeoutSegundos,
                Activo = proveedor.Activo,
                FechaModificacionUtc = DateTime.MinValue,
                UsuarioModificacionId = null
            };
        }

        private static string CrearMascara(string apiKey)
        {
            if (apiKey.Length <= 8)
                return new string('*', apiKey.Length);

            return $"{apiKey[..4]}{new string('*', Math.Min(18, apiKey.Length - 8))}{apiKey[^4..]}";
        }

        private static string Normalizar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo ? texto : texto[..maximo];
        }

        private static string Limitar(string? valor, int maximo) =>
            Normalizar(valor, maximo);
    }

    public sealed record ProveedorEjecucion(
        string Proveedor,
        string Protocolo,
        string BaseUrl,
        string Endpoint,
        string ApiKey,
        string ApiKeyVariableEntorno,
        string ModeloPrincipal,
        string ModeloRespaldo,
        int TimeoutSegundos,
        bool Activo);

    public sealed class ProveedorIAResultadoFoto
    {
        public bool ImagenValida { get; set; }
        public bool ParecePlantaCafe { get; set; }
        public bool ResultadoConcluyente { get; set; }
        public string PartePlanta { get; set; } = string.Empty;
        public string CalidadEvaluacion { get; set; } = string.Empty;
        public string EstadoGeneral { get; set; } = string.Empty;
        public string CategoriaPrincipal { get; set; } = string.Empty;
        public List<string> CategoriasSecundarias { get; set; } = [];
        public string DiagnosticoProbable { get; set; } = string.Empty;
        public string TipoDiagnostico { get; set; } = string.Empty;
        public string SeveridadVisual { get; set; } = string.Empty;
        public string NivelCerteza { get; set; } = string.Empty;
        public int? CategoriaAlbumBotanicoIdSugerida { get; set; }
        public int? AlbumBotanicoCafeIdSugerido { get; set; }
        public string CategoriaAlbumSugerida { get; set; } = string.Empty;
        public string ClasificacionAlbumSugerida { get; set; } = string.Empty;
        public string NombreCientificoSugerido { get; set; } = string.Empty;
        public bool CoincideCatalogoAlbum { get; set; }
        public bool RequiereDecisionClasificacion { get; set; }
        public string MotivoClasificacionAlbum { get; set; } = string.Empty;
        public string ResumenImagen { get; set; } = string.Empty;
        public List<string> SintomasVisibles { get; set; } = [];
        public List<string> EvidenciasObservadas { get; set; } = [];
        public List<string> EvidenciasNoObservadas { get; set; } = [];
        public List<string> DiagnosticosAlternativos { get; set; } = [];
        public List<string> InformacionFaltante { get; set; } = [];
        public List<string> RecomendacionesCaptura { get; set; } = [];
        public List<string> Advertencias { get; set; } = [];

        [JsonIgnore]
        public string Proveedor { get; set; } = string.Empty;

        [JsonIgnore]
        public string Modelo { get; set; } = string.Empty;

        [JsonIgnore]
        public string RespuestaOriginalJson { get; set; } = string.Empty;
    }

    internal sealed record TipoFotografiaIAContexto(
        string Codigo,
        string Nombre,
        string Descripcion,
        string InstruccionIA);

    public sealed class ProveedorIAException : Exception
    {
        public ProveedorIAException(
            HttpStatusCode statusCode,
            string message)
            : base(message)
        {
            StatusCode = statusCode;
        }

        public HttpStatusCode StatusCode { get; }
    }
}
