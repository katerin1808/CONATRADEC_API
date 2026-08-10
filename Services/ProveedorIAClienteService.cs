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
    ///
    /// La fotografía continúa siendo la unidad del expediente, pero una misma
    /// valoración puede contener varios diagnósticos independientes y varias
    /// lesiones localizadas para cada uno.
    /// </summary>
    public sealed class ProveedorIAClienteService
    {
        public const string ProtocoloGeminiNativo = "GEMINI_NATIVO";
        public const string ProtocoloOpenAICompatible = "OPENAI_COMPATIBLE";

        private const string VariableApiKeyPredeterminada = "GEMINI_API_KEY";
        private const int MaximoDiagnosticos = 6;
        private const int MaximoLesionesPorDiagnostico = 25;
        private const int MaximoLesionesTotales = 80;

        private const string ColorMarcadorDiferencial = "#1E88E5";

        private static readonly string[] ColoresMarcadores =
        [
            "#E53935",
            "#43A047",
            "#FB8C00",
            "#8E24AA",
            "#00897B",
            "#6D4C41"
        ];

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
            : this(httpClientFactory, configuration, storage, db, logger)
        {
            ArgumentNullException.ThrowIfNull(database);
        }

        public Task<ProveedorIAConfiguracionDto> ObtenerDtoAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProveedorEjecucion proveedor = CrearProveedorDesdeConfiguracion();
            return Task.FromResult(CrearDto(proveedor));
        }

        public Task<ProveedorIAConfiguracionDto> GuardarAsync(
            ProveedorIAConfiguracionActualizarRequest request,
            int usuarioId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                "El proveedor de IA se configura directamente en el backend mediante appsettings o variables de entorno.");
        }

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

            string rutaFisica = storage.ResolverRutaPublica(imagen.RutaRelativa);

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
                    AplicarConsistenciaDiferenciales(resultado);
                    await AplicarTrazabilidadReevaluacionAsync(
                        resultado,
                        imagen,
                        retroalimentacion,
                        cancellationToken);
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

            string contextoValoracionAnterior =
                await ObtenerContextoValoracionAnteriorAsync(
                    imagen,
                    retroalimentacion,
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

CONTEXTO DE LA VALORACIÓN IA ANTERIOR (solo cuando existe una reevaluación):
{{contextoValoracionAnterior}}

Catálogo activo del Álbum Botánico de CONATRADEC:
{{catalogoJson}}

REGLAS PARA REEVALUACIONES:
A. Si existe una valoración anterior, úsala únicamente como referencia histórica y
   NO como una verdad obligatoria. Debes volver a observar toda la fotografía.
B. La retroalimentación del técnico es una pregunta o señal de atención; NO es una
   orden para reemplazar el diagnóstico anterior ni para mover sus regiones.
C. Compara explícitamente la nueva observación con los diagnósticos y box2d de la
   valoración anterior. Si una región anterior sigue sustentando el mismo diagnóstico,
   consérvala de forma razonable. No la abandones silenciosamente solo porque el
   técnico pidió revisar otras lesiones.
D. Si una región anteriormente atribuida a un diagnóstico ahora te parece otra
   afectación o un diferencial, puedes reclasificarla SOLO cuando exista evidencia
   visual suficiente. En ese caso explica el cambio en resumenImagen comenzando con
   "Cambio respecto a valoración anterior:" e indica qué cambió y por qué.
E. Si el diagnóstico principal anterior deja de ser el principal, no lo elimines
   silenciosamente: si todavía es plausible, mantenlo como diagnóstico adicional o
   diferencial; si ya no está sustentado, indícalo expresamente en advertencias.
F. Cuando el técnico mencione lesiones que quedaron sin marcar, revisa esos grupos
   de manera específica y clasifícalos como: mismo diagnóstico, diagnóstico adicional,
   diferencial o evidencia no concluyente. Si no puedes clasificarlos, dilo de forma
   explícita en advertencias o informacionFaltante. No ignores esas zonas.
G. Una reevaluación puede cambiar el resultado cuando la evidencia realmente lo
   justifique, pero debe ser trazable y explicada; evita cambios arbitrarios entre
   revisiones de la misma fotografía.
H. En toda reevaluación devuelve comparacionValoracionAnterior con una explicación
   breve y explícita. Debe indicar si el diagnóstico principal se mantiene o cambia,
   qué regiones anteriores se conservaron, cuáles se reclasificaron o dejaron de
   considerarse válidas y qué nuevas regiones fueron incorporadas. No uses frases
   vagas como "se reevaluó". Si es la primera valoración, devuelve cadena vacía.

REGLAS DE DIAGNÓSTICOS MÚLTIPLES Y LOCALIZACIÓN VISUAL:
1. La fotografía es un solo expediente. Puedes devolver de 0 a {{MaximoDiagnosticos}}
   diagnósticos independientes dentro de diagnosticos[].
2. Declara dos enfermedades/plagas simultáneas únicamente cuando existan grupos de
   evidencias visuales diferenciables que sustenten cada una.
3. Si una MISMA lesión admite varias explicaciones, registra una sola afectación
   probable y coloca las demás posibilidades en diagnosticosDiferenciales. NO las
   declares como enfermedades simultáneas.
4. Cuando exista evidencia visual razonable para un diferencial no confirmado,
   puedes registrarlo dentro de diferencialesLocalizados, pero SOLO si puedes señalar
   explícitamente una o más regiones que sustentan esa posibilidad. Cada región del
   diferencial debe provenir de tu propia observación visual de la fotografía.
   Si no puedes localizarlo con suficiente precisión, deja diferencialesLocalizados=[]
   y conserva únicamente su nombre en diagnosticosDiferenciales.
5. Nunca copies, reutilices ni inventes una caja del diagnóstico principal para crear
   una localización diferencial. Que una región sea ambigua no basta por sí solo:
   debes poder justificar visualmente por qué esa región también sustenta el
   diferencial indicado.
6. Primero localiza completamente el diagnóstico principal o confirmado tal como lo
   harías normalmente. No reduzcas, sustituyas ni empeores sus cajas por agregar
   diferenciales; los diferenciales se añaden después y nunca tienen prioridad sobre
   la calidad de la localización principal.
6A. Si una misma región queda localizada tanto para el diagnóstico principal como
   para un diferencial, trátala explícitamente como REGIÓN AMBIGUA. Explica qué
   rasgos sostienen el principal y qué rasgos justifican el diferencial. No afirmes
   simultáneamente que esa región es inequívocamente del principal y que también es
   un diferencial sin reconocer la ambigüedad.
6B. En una reevaluación, agregar, eliminar, mover o cambiar el tamaño de un box2d,
   agregar/quitar un diagnóstico diferencial o cambiar sus coordenadas CUENTA COMO
   CAMBIO respecto a la valoración anterior, aunque el nombre del diagnóstico
   principal permanezca igual. Nunca respondas "sin cambios" o "ningún cambio"
   cuando haya ocurrido cualquiera de esas modificaciones estructurales.
7. Cada diagnóstico visual que consideres presente y concluyente debe incluir una o
   más localizaciones visuales válidas. Cada localización usa box2d=[ymin,xmin,ymax,xmax]
   con enteros normalizados de 0 a 1000, donde ymin<ymax y xmin<xmax.
8. Cada localización puede representar una lesión puntual o una región visible que
   agrupe varias lesiones cercanas del mismo diagnóstico. Usa descripciones claras
   como "región principal", "foco secundario" o "dispersión adicional" cuando ayude.
9. Si existen múltiples focos dispersos del mismo diagnóstico, no marques cada pústula
   individual. Devuelve varias localizaciones que representen las regiones principales
   y también los focos secundarios o dispersos claramente visibles. No dejes sin
   representar grupos relevantes solo por estar fuera de la región principal.
10. Ajusta cada box2d lo más cerca posible al grupo visible de lesiones que realmente
   sustenta el diagnóstico. Evita incluir grandes áreas de tejido sano alrededor.
11. Evita solapamientos importantes entre localizaciones del mismo diagnóstico. Si dos
   cajas cubren casi la misma región, conserva la más precisa. Si hay grupos
   claramente separados, divide la evidencia en varias cajas más compactas.
12. Localiza únicamente zonas visibles que realmente sustenten ese diagnóstico. No
   marques hojas completas ni regiones arbitrariamente grandes cuando la evidencia
   sea una lesión puntual o un foco acotado.
13. Usa IDs estables dentro de esta respuesta: D1, D2... para diagnósticos y L1,
   L2... para lesiones. No repitas IDs.
14. Puede existir como máximo un diagnóstico con esPrincipal=true. Si no existe un
   predominio razonablemente claro, deja TODOS con esPrincipal=false y establece
   requiereDecisionClasificacion=true. No fuerces un principal arbitrario.
15. Si la imagen está aparentemente sana, diagnosticos debe ser [].
16. Si no es evaluable, diagnosticos debe ser [] y resultadoConcluyente=false.
17. Si no parece una planta de café, parecePlantaCafe=false, diagnosticos=[],
    resultadoConcluyente=false y categoriaPrincipal=NO_APLICA. No diagnostiques
    enfermedades de café en otra especie.
18. Nunca declares resultadoConcluyente=true para una afectación visual que no tenga
    ninguna lesión válida localizada.

REGLAS GENERALES:
19. Determina primero si la imagen permite evaluar una planta de café.
20. Cuando sí corresponda a café, prioriza la detección visual de PLAGAS y
    ENFERMEDADES; no inventes agentes causales que no sean visibles.
21. Usa una ficha existente solamente cuando la coincidencia visual sea razonable.
22. Cuando no exista una ficha segura, deja albumBotanicoCafeIdSugerido en null
    y propone una clasificación para revisión humana.
23. Nunca crees una ficha ni apruebes una publicación.
24. Si la fotografía está borrosa, oscura, demasiado lejana o la evidencia es
    insuficiente, usa calidadEvaluacion=NO_EVALUABLE o PARCIALMENTE_EVALUABLE
    y resultadoConcluyente=false.
25. Los campos superiores diagnosticoProbable, categoriaPrincipal,
    tipoDiagnostico, severidadVisual y nivelCerteza resumen el diagnóstico
    principal cuando exista. Se conservan por compatibilidad con clientes anteriores.

Devuelve exclusivamente un objeto JSON con estas propiedades:
imagenValida, parecePlantaCafe, resultadoConcluyente, partePlanta,
calidadEvaluacion, estadoGeneral, categoriaPrincipal, categoriasSecundarias,
diagnosticoProbable, tipoDiagnostico, severidadVisual, nivelCerteza,
categoriaAlbumBotanicoIdSugerida, albumBotanicoCafeIdSugerido,
categoriaAlbumSugerida, clasificacionAlbumSugerida,
nombreCientificoSugerido, coincideCatalogoAlbum,
requiereDecisionClasificacion, motivoClasificacionAlbum, resumenImagen,
comparacionValoracionAnterior, sintomasVisibles, evidenciasObservadas, evidenciasNoObservadas,
diagnosticosAlternativos, informacionFaltante,
recomendacionesCaptura, advertencias y diagnosticos.

Cada elemento de diagnosticos debe contener:
id, diagnostico, categoria, tipoDiagnostico, esPrincipal, nivelCerteza,
severidad, diagnosticosDiferenciales, diferencialesLocalizados y lesiones.

Cada elemento de diferencialesLocalizados debe contener:
diagnostico y lesiones.

Cada elemento de lesiones debe contener:
id, descripcion y box2d.

Valores controlados:
- calidadEvaluacion: EVALUABLE, PARCIALMENTE_EVALUABLE o NO_EVALUABLE.
- estadoGeneral: APARENTEMENTE_SANA, CON_AFECTACION o INDETERMINADA.
- categoriaPrincipal y diagnosticos[].categoria: ENFERMEDAD, PLAGA,
  ALTERACION_NUTRICIONAL, ESTRES_ABIOTICO, DANO_MECANICO, NO_APLICA o
  AFECTACION_NO_DETERMINADA.
- severidadVisual y diagnosticos[].severidad: LEVE, MODERADA, SEVERA,
  NO_APLICA o NO_EVALUABLE.
- nivelCerteza y diagnosticos[].nivelCerteza: ALTO, MEDIO, BAJO o
  NO_DETERMINADO.
""";
        }

        private async Task<string> ObtenerContextoValoracionAnteriorAsync(
            DiagnosticoIAImagen imagen,
            string? retroalimentacion,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(retroalimentacion))
                return "No aplica: esta es una valoración inicial.";

            var anterior = imagen.ResultadoIA;
            var partes = new List<string>();

            if (anterior != null)
            {
                partes.Add(
                    $"Diagnóstico principal anterior: {Normalizar(anterior.DiagnosticoProbable, 300)}.");
                partes.Add(
                    $"Categoría anterior: {Normalizar(anterior.CategoriaPrincipal, 50)}; " +
                    $"severidad: {Normalizar(anterior.SeveridadVisual, 30)}; " +
                    $"certeza: {Normalizar(anterior.NivelCerteza, 30)}.");

                string resumenAnterior = Normalizar(anterior.ResumenImagen, 1600);
                if (!string.IsNullOrWhiteSpace(resumenAnterior))
                    partes.Add($"Resumen anterior: {resumenAnterior}");
            }

            const string sql = """
SELECT TOP (1)
    [Revision],
    [DiagnosticosJson]
FROM [dbo].[diagnosticoIAImagenResultadoVisualV2]
WHERE [DiagnosticoIAImagenId] = @fotoId
  AND [EsVigente] = 1
ORDER BY [Revision] DESC,
         [DiagnosticoIAImagenResultadoVisualId] DESC;
""";

            try
            {
                DbConnection connection = db.Database.GetDbConnection();
                await using DbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.CommandType = CommandType.Text;
                command.CommandTimeout = 30;

                DbParameter parameter = command.CreateParameter();
                parameter.ParameterName = "@fotoId";
                parameter.Value = imagen.DiagnosticoIAImagenId;
                command.Parameters.Add(parameter);

                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync(cancellationToken);

                await using DbDataReader reader =
                    await command.ExecuteReaderAsync(cancellationToken);

                if (await reader.ReadAsync(cancellationToken))
                {
                    int revision = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                    string diagnosticosJson = reader.IsDBNull(1)
                        ? "[]"
                        : reader.GetString(1);

                    diagnosticosJson = Normalizar(diagnosticosJson, 12000);

                    partes.Add(
                        $"Revisión visual anterior: {revision}. " +
                        "Diagnósticos y regiones box2d anteriores (JSON): " +
                        diagnosticosJson);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "No fue posible cargar la valoración visual anterior de la fotografía {FotografiaId} para comparar la reevaluación.",
                    imagen.DiagnosticoIAImagenId);
            }

            return partes.Count == 0
                ? "Existe una reevaluación, pero no fue posible recuperar el detalle de la valoración anterior. Analiza toda la fotografía y explica cualquier cambio relevante."
                : string.Join(Environment.NewLine, partes);
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
                /*
                 * Gemini nativo recibe JSON forzado por MIME y por el prompt,
                 * pero no se le envía el responseJsonSchema completo. El
                 * esquema multidiagnóstico contiene objetos y arreglos
                 * anidados que algunas variantes de Gemini rechazan con
                 * INVALID_ARGUMENT antes de procesar la imagen.
                 *
                 * La respuesta sigue siendo validada y normalizada por el
                 * backend (diagnósticos, lesiones, coordenadas, límites y
                 * diagnóstico principal), por lo que retirar el esquema del
                 * request no elimina las reglas de seguridad del expediente.
                 */
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    maxOutputTokens = 7000
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("x-goog-api-key", proveedor.ApiKey);
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
                max_tokens = 7000,
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

                string content = await response.Content.ReadAsStringAsync(timeout.Token);

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

        private (string NombreVariable, string ApiKey) ObtenerApiKeyConfigurada()
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
                "comparacionValoracionAnterior", "sintomasVisibles", "evidenciasObservadas",
                "evidenciasNoObservadas", "diagnosticosAlternativos",
                "informacionFaltante", "recomendacionesCaptura",
                "advertencias", "diagnosticos"
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
                ["comparacionValoracionAnterior"] = new { type = "string" },
                ["sintomasVisibles"] = EsquemaLista(),
                ["evidenciasObservadas"] = EsquemaLista(),
                ["evidenciasNoObservadas"] = EsquemaLista(),
                ["diagnosticosAlternativos"] = EsquemaLista(),
                ["informacionFaltante"] = EsquemaLista(),
                ["recomendacionesCaptura"] = EsquemaLista(),
                ["advertencias"] = EsquemaLista(),
                ["diagnosticos"] = EsquemaDiagnosticos()
            }
        };

        private static object EsquemaLista() => new
        {
            type = "array",
            items = new { type = "string" }
        };

        private static object EsquemaDiagnosticos() => new
        {
            type = "array",
            maxItems = MaximoDiagnosticos,
            items = new
            {
                type = "object",
                additionalProperties = false,
                required = new[]
                {
                    "id", "diagnostico", "categoria", "tipoDiagnostico",
                    "esPrincipal", "nivelCerteza", "severidad",
                    "diagnosticosDiferenciales", "diferencialesLocalizados", "lesiones"
                },
                properties = new Dictionary<string, object>
                {
                    ["id"] = new { type = "string" },
                    ["diagnostico"] = new { type = "string" },
                    ["categoria"] = new { type = "string" },
                    ["tipoDiagnostico"] = new { type = "string" },
                    ["esPrincipal"] = new { type = "boolean" },
                    ["nivelCerteza"] = new { type = "string" },
                    ["severidad"] = new { type = "string" },
                    ["diagnosticosDiferenciales"] = EsquemaLista(),
                    ["diferencialesLocalizados"] = new
                    {
                        type = "array",
                        maxItems = 4,
                        items = new
                        {
                            type = "object",
                            additionalProperties = false,
                            required = new[] { "diagnostico", "lesiones" },
                            properties = new Dictionary<string, object>
                            {
                                ["diagnostico"] = new { type = "string" },
                                ["lesiones"] = new
                                {
                                    type = "array",
                                    maxItems = MaximoLesionesPorDiagnostico,
                                    items = new
                                    {
                                        type = "object",
                                        additionalProperties = false,
                                        required = new[] { "id", "descripcion", "box2d" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["id"] = new { type = "string" },
                                            ["descripcion"] = new { type = "string" },
                                            ["box2d"] = new
                                            {
                                                type = "array",
                                                minItems = 4,
                                                maxItems = 4,
                                                items = new
                                                {
                                                    type = "integer",
                                                    minimum = 0,
                                                    maximum = 1000
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    ["lesiones"] = new
                    {
                        type = "array",
                        maxItems = MaximoLesionesPorDiagnostico,
                        items = new
                        {
                            type = "object",
                            additionalProperties = false,
                            required = new[] { "id", "descripcion", "box2d" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "string" },
                                ["descripcion"] = new { type = "string" },
                                ["box2d"] = new
                                {
                                    type = "array",
                                    minItems = 4,
                                    maxItems = 4,
                                    items = new
                                    {
                                        type = "integer",
                                        minimum = 0,
                                        maximum = 1000
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        private static void NormalizarResultado(ProveedorIAResultadoFoto resultado)
        {
            resultado.CalidadEvaluacion = NormalizarControlado(
                resultado.CalidadEvaluacion,
                ["EVALUABLE", "PARCIALMENTE_EVALUABLE", "NO_EVALUABLE"],
                "NO_EVALUABLE");
            resultado.EstadoGeneral = NormalizarControlado(
                resultado.EstadoGeneral,
                ["APARENTEMENTE_SANA", "CON_AFECTACION", "INDETERMINADA"],
                "INDETERMINADA");
            resultado.CategoriaPrincipal = NormalizarCategoria(
                resultado.CategoriaPrincipal);
            resultado.SeveridadVisual = NormalizarSeveridad(
                resultado.SeveridadVisual);
            resultado.NivelCerteza = NormalizarCerteza(
                resultado.NivelCerteza);

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
            resultado.ComparacionValoracionAnterior = Normalizar(
                resultado.ComparacionValoracionAnterior,
                1800);

            resultado.CategoriasSecundarias = LimpiarLista(
                resultado.CategoriasSecundarias, 6, 80);
            resultado.SintomasVisibles = LimpiarLista(
                resultado.SintomasVisibles, 10, 400);
            resultado.EvidenciasObservadas = LimpiarLista(
                resultado.EvidenciasObservadas, 10, 400);
            resultado.EvidenciasNoObservadas = LimpiarLista(
                resultado.EvidenciasNoObservadas, 10, 400);
            resultado.DiagnosticosAlternativos = LimpiarLista(
                resultado.DiagnosticosAlternativos, 8, 300);
            resultado.InformacionFaltante = LimpiarLista(
                resultado.InformacionFaltante, 8, 400);
            resultado.RecomendacionesCaptura = LimpiarLista(
                resultado.RecomendacionesCaptura, 8, 400);
            resultado.Advertencias = LimpiarLista(
                resultado.Advertencias, 8, 400);

            NormalizarDiagnosticos(resultado);
        }

        private static void NormalizarDiagnosticos(ProveedorIAResultadoFoto resultado)
        {
            bool debeQuedarSinDiagnosticos =
                !resultado.ImagenValida ||
                !resultado.ParecePlantaCafe ||
                string.Equals(
                    resultado.CalidadEvaluacion,
                    "NO_EVALUABLE",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    resultado.EstadoGeneral,
                    "APARENTEMENTE_SANA",
                    StringComparison.OrdinalIgnoreCase);

            if (debeQuedarSinDiagnosticos)
            {
                resultado.Diagnosticos = [];
                resultado.ResultadoConcluyente = false;
                return;
            }

            var diagnosticos = new List<ProveedorIADiagnosticoFoto>();
            var idsDiagnosticos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var idsLesiones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int lesionesTotales = 0;

            foreach (ProveedorIADiagnosticoFoto diagnostico in
                     (resultado.Diagnosticos ?? []).Take(MaximoDiagnosticos))
            {
                string nombre = Normalizar(diagnostico.Diagnostico, 300);
                if (string.IsNullOrWhiteSpace(nombre))
                    continue;

                diagnostico.Id = CrearIdUnico(
                    diagnostico.Id,
                    "D",
                    diagnosticos.Count + 1,
                    idsDiagnosticos);
                diagnostico.Diagnostico = nombre;
                diagnostico.Categoria = NormalizarCategoria(diagnostico.Categoria);
                diagnostico.TipoDiagnostico = Normalizar(
                    diagnostico.TipoDiagnostico,
                    80);
                diagnostico.NivelCerteza = NormalizarCerteza(
                    diagnostico.NivelCerteza);
                diagnostico.Severidad = NormalizarSeveridad(
                    diagnostico.Severidad);
                diagnostico.DiagnosticosDiferenciales = LimpiarLista(
                    diagnostico.DiagnosticosDiferenciales,
                    6,
                    300);
                diagnostico.ColorMarcador = ColoresMarcadores[
                    diagnosticos.Count % ColoresMarcadores.Length];

                var lesionesValidas = new List<ProveedorIALesionFoto>();

                foreach (ProveedorIALesionFoto lesion in
                         (diagnostico.Lesiones ?? [])
                            .Take(MaximoLesionesPorDiagnostico))
                {
                    if (lesionesTotales >= MaximoLesionesTotales)
                        break;

                    if (!EsBoxValido(lesion.Box2d))
                        continue;

                    lesion.Id = CrearIdUnico(
                        lesion.Id,
                        "L",
                        lesionesTotales + 1,
                        idsLesiones);
                    lesion.Descripcion = Normalizar(lesion.Descripcion, 500);
                    lesion.Box2d = lesion.Box2d.Take(4).ToList();
                    lesionesValidas.Add(lesion);
                    lesionesTotales++;
                }

                diagnostico.Lesiones = CompactarLesiones(lesionesValidas);

                // Los diagnósticos principales conservan prioridad de localización.
                // Los diferenciales solo se aceptan después y únicamente cuando
                // Gemini devolvió coordenadas explícitas y válidas para ellos.
                diagnostico.DiferencialesLocalizados = NormalizarDiferencialesLocalizados(
                    diagnostico.DiferencialesLocalizados,
                    diagnostico.DiagnosticosDiferenciales,
                    idsLesiones,
                    ref lesionesTotales);

                diagnosticos.Add(diagnostico);
            }

            int principales = diagnosticos.Count(item => item.EsPrincipal);
            if (principales > 1)
            {
                foreach (ProveedorIADiagnosticoFoto item in diagnosticos)
                    item.EsPrincipal = false;

                resultado.RequiereDecisionClasificacion = true;
                resultado.Advertencias = AgregarAdvertencia(
                    resultado.Advertencias,
                    "La IA propuso más de un diagnóstico principal; se dejó la selección principal pendiente de decisión humana.");
            }
            else if (diagnosticos.Count == 1 && principales == 0)
            {
                diagnosticos[0].EsPrincipal = true;
            }
            else if (diagnosticos.Count > 1 && principales == 0)
            {
                resultado.RequiereDecisionClasificacion = true;
            }

            bool diagnosticoSinLocalizacion = diagnosticos.Any(item =>
                item.Lesiones.Count == 0);

            if (resultado.ResultadoConcluyente && diagnosticoSinLocalizacion)
            {
                resultado.ResultadoConcluyente = false;
                resultado.Advertencias = AgregarAdvertencia(
                    resultado.Advertencias,
                    "La valoración no se marcó como concluyente porque al menos una afectación visual no contiene lesiones localizadas válidas.");
            }

            if (diagnosticos.Count == 0 &&
                string.Equals(
                    resultado.EstadoGeneral,
                    "CON_AFECTACION",
                    StringComparison.OrdinalIgnoreCase))
            {
                resultado.ResultadoConcluyente = false;
            }

            resultado.Diagnosticos = diagnosticos;

            ProveedorIADiagnosticoFoto? principal =
                diagnosticos.FirstOrDefault(item => item.EsPrincipal);

            if (principal != null)
            {
                resultado.DiagnosticoProbable = principal.Diagnostico;
                resultado.CategoriaPrincipal = principal.Categoria;
                resultado.TipoDiagnostico = principal.TipoDiagnostico;
                resultado.SeveridadVisual = principal.Severidad;
                resultado.NivelCerteza = principal.NivelCerteza;
                resultado.CategoriasSecundarias = diagnosticos
                    .Where(item => !item.EsPrincipal)
                    .Select(item => item.Categoria)
                    .Where(item => !string.Equals(
                        item,
                        resultado.CategoriaPrincipal,
                        StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(6)
                    .ToList();
            }
        }

        private static List<string> AgregarAdvertencia(
            IEnumerable<string>? actuales,
            string advertencia) =>
            (actuales ?? [])
                .Append(advertencia)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => Normalizar(item, 400))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

        private static bool EsBoxValido(IReadOnlyList<int>? box)
        {
            if (box == null || box.Count != 4)
                return false;

            int ymin = box[0];
            int xmin = box[1];
            int ymax = box[2];
            int xmax = box[3];

            return ymin is >= 0 and <= 1000 &&
                   xmin is >= 0 and <= 1000 &&
                   ymax is >= 0 and <= 1000 &&
                   xmax is >= 0 and <= 1000 &&
                   ymin < ymax &&
                   xmin < xmax;
        }

        private static List<ProveedorIALesionFoto> CompactarLesiones(
            IEnumerable<ProveedorIALesionFoto> lesiones)
        {
            const double umbralSolapamientoSobreMenor = 0.75d;
            const double factorAreaDominante = 1.60d;

            var ordenadas = (lesiones ?? [])
                .Where(lesion => EsBoxValido(lesion.Box2d))
                .OrderBy(ObtenerAreaBox)
                .ToList();

            var compactadas = new List<ProveedorIALesionFoto>();

            foreach (ProveedorIALesionFoto actual in ordenadas)
            {
                int indiceSolapado = -1;

                for (int i = 0; i < compactadas.Count; i++)
                {
                    ProveedorIALesionFoto existente = compactadas[i];
                    double interseccion = ObtenerAreaInterseccion(
                        actual.Box2d,
                        existente.Box2d);

                    if (interseccion <= 0d)
                        continue;

                    double areaActual = ObtenerAreaBox(actual);
                    double areaExistente = ObtenerAreaBox(existente);
                    double areaMenor = Math.Min(areaActual, areaExistente);

                    if (areaMenor <= 0d)
                        continue;

                    double coberturaMenor = interseccion / areaMenor;
                    double areaMayor = Math.Max(areaActual, areaExistente);
                    double factorArea = areaMenor <= 0d
                        ? 1d
                        : areaMayor / areaMenor;

                    if (coberturaMenor >= umbralSolapamientoSobreMenor &&
                        factorArea >= factorAreaDominante)
                    {
                        indiceSolapado = i;
                        break;
                    }
                }

                if (indiceSolapado < 0)
                {
                    compactadas.Add(actual);
                    continue;
                }

                ProveedorIALesionFoto solapada = compactadas[indiceSolapado];
                if (ObtenerAreaBox(actual) < ObtenerAreaBox(solapada))
                    compactadas[indiceSolapado] = actual;
            }

            return compactadas
                .OrderBy(lesion => lesion.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static double ObtenerAreaBox(ProveedorIALesionFoto lesion) =>
            ObtenerAreaBox(lesion.Box2d);

        private static double ObtenerAreaBox(IReadOnlyList<int>? box)
        {
            if (!EsBoxValido(box))
                return 0d;

            return (box![2] - box[0]) * (box[3] - box[1]);
        }

        private static double ObtenerAreaInterseccion(
            IReadOnlyList<int>? a,
            IReadOnlyList<int>? b)
        {
            if (!EsBoxValido(a) || !EsBoxValido(b))
                return 0d;

            int ymin = Math.Max(a![0], b![0]);
            int xmin = Math.Max(a[1], b[1]);
            int ymax = Math.Min(a[2], b[2]);
            int xmax = Math.Min(a[3], b[3]);

            if (ymin >= ymax || xmin >= xmax)
                return 0d;

            return (ymax - ymin) * (xmax - xmin);
        }

        private static string CrearIdUnico(
            string? solicitado,
            string prefijo,
            int consecutivo,
            ISet<string> usados)
        {
            string candidato = Normalizar(solicitado, 20);

            if (string.IsNullOrWhiteSpace(candidato) || usados.Contains(candidato))
                candidato = $"{prefijo}{consecutivo}";

            int sufijo = consecutivo;
            while (usados.Contains(candidato))
                candidato = $"{prefijo}{++sufijo}";

            usados.Add(candidato);
            return candidato;
        }

        private static string NormalizarCategoria(string? valor) =>
            NormalizarControlado(
                valor,
                [
                    "ENFERMEDAD", "PLAGA", "ALTERACION_NUTRICIONAL",
                    "ESTRES_ABIOTICO", "DANO_MECANICO", "NO_APLICA",
                    "AFECTACION_NO_DETERMINADA"
                ],
                "AFECTACION_NO_DETERMINADA");

        private static string NormalizarSeveridad(string? valor) =>
            NormalizarControlado(
                valor,
                ["LEVE", "MODERADA", "SEVERA", "NO_APLICA", "NO_EVALUABLE"],
                "NO_EVALUABLE");

        private static string NormalizarCerteza(string? valor) =>
            NormalizarControlado(
                valor,
                ["ALTO", "MEDIO", "BAJO", "NO_DETERMINADO"],
                "NO_DETERMINADO");

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
                throw new ArgumentException("La URL base del proveedor no es válida.");
            }

            return texto.EndsWith('/') ? texto : $"{texto}/";
        }

        private static string NormalizarEndpoint(string valor) =>
            valor.Trim().TrimStart('/');

        private static void ValidarRequest(
            ProveedorIAConfiguracionActualizarRequest request)
        {
            string protocolo = request.Protocolo.Trim().ToUpperInvariant();

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

            bool esOpenAICompatible = protocolo == ProtocoloOpenAICompatible;

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
                configuration.GetValue<int?>("ProveedorIA:TimeoutSegundos") ?? 180,
                15,
                600);

            bool activo = configuration.GetValue<bool?>("ProveedorIA:Activo") ?? true;

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

            if (modelo.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                modelo = modelo["models/".Length..];

            return modelo.ToLowerInvariant() switch
            {
                "gemini-2.0-flash" => "gemini-3.5-flash-lite",
                "gemini-2.0-flash-lite" => "gemini-3.5-flash-lite",
                "gemini-2.5-flash" => "gemini-3.6-flash",
                "gemini-2.5-flash-lite" => "gemini-3.5-flash-lite",
                _ => modelo
            };
        }

        private ProveedorIAConfiguracionDto CrearDto(ProveedorEjecucion proveedor)
        {
            bool tieneApiKey = !string.IsNullOrWhiteSpace(proveedor.ApiKey);

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

        private static List<ProveedorIADiagnosticoDiferencialFoto>
            NormalizarDiferencialesLocalizados(
                IEnumerable<ProveedorIADiagnosticoDiferencialFoto>? diferenciales,
                IList<string> diagnosticosDiferenciales,
                HashSet<string> idsLesiones,
                ref int lesionesTotales)
        {
            var resultado = new List<ProveedorIADiagnosticoDiferencialFoto>();

            foreach (ProveedorIADiagnosticoDiferencialFoto diferencial in
                     (diferenciales ?? []).Take(4))
            {
                string nombre = Normalizar(diferencial.Diagnostico, 300);
                if (string.IsNullOrWhiteSpace(nombre))
                    continue;

                var lesiones = new List<ProveedorIALesionFoto>();
                foreach (ProveedorIALesionFoto lesion in
                         (diferencial.Lesiones ?? [])
                            .Take(MaximoLesionesPorDiagnostico))
                {
                    if (lesionesTotales >= MaximoLesionesTotales)
                        break;

                    if (!EsBoxValido(lesion.Box2d))
                        continue;

                    lesion.Id = CrearIdUnico(
                        lesion.Id,
                        "DL",
                        lesionesTotales + 1,
                        idsLesiones);
                    lesion.Descripcion = Normalizar(
                        lesion.Descripcion,
                        500);
                    lesion.Box2d = lesion.Box2d.Take(4).ToList();
                    lesiones.Add(lesion);
                    lesionesTotales++;
                }

                lesiones = CompactarLesiones(lesiones);
                if (lesiones.Count == 0)
                    continue;

                if (!diagnosticosDiferenciales.Any(item =>
                        string.Equals(
                            item,
                            nombre,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    diagnosticosDiferenciales.Add(nombre);
                }

                diferencial.Diagnostico = nombre;
                diferencial.Lesiones = lesiones;
                diferencial.ColorMarcador = ColorMarcadorDiferencial;
                resultado.Add(diferencial);
            }

            return resultado;
        }



        private static void AplicarConsistenciaDiferenciales(
            ProveedorIAResultadoFoto resultado)
        {
            const double umbralAmbiguedad = 0.55d;

            foreach (ProveedorIADiagnosticoFoto diagnostico in
                     resultado.Diagnosticos ?? [])
            {
                foreach (ProveedorIADiagnosticoDiferencialFoto diferencial in
                         diagnostico.DiferencialesLocalizados ?? [])
                {
                    foreach (ProveedorIALesionFoto lesionDiferencial in
                             diferencial.Lesiones ?? [])
                    {
                        if (!EsBoxValido(lesionDiferencial.Box2d))
                            continue;

                        ProveedorIALesionFoto? lesionPrincipal =
                            (diagnostico.Lesiones ?? [])
                                .Where(item => EsBoxValido(item.Box2d))
                                .FirstOrDefault(item =>
                                {
                                    double interseccion = ObtenerAreaInterseccion(
                                        item.Box2d,
                                        lesionDiferencial.Box2d);
                                    double areaMenor = Math.Min(
                                        ObtenerAreaBox(item.Box2d),
                                        ObtenerAreaBox(lesionDiferencial.Box2d));

                                    return areaMenor > 0d &&
                                           interseccion / areaMenor >=
                                               umbralAmbiguedad;
                                });

                        if (lesionPrincipal == null)
                            continue;

                        string descripcion = Normalizar(
                            lesionDiferencial.Descripcion,
                            500);

                        if (!descripcion.Contains(
                                "región ambigua",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            lesionDiferencial.Descripcion = Normalizar(
                                $"Región ambigua: esta zona también forma parte de la evidencia localizada para {diagnostico.Diagnostico}, pero presenta rasgos compatibles con el diferencial {diferencial.Diagnostico}. {descripcion}",
                                500);
                        }

                        resultado.Advertencias = AgregarAdvertencia(
                            resultado.Advertencias,
                            $"Región ambigua: una zona localizada para {diagnostico.Diagnostico} también presenta rasgos compatibles con {diferencial.Diagnostico} como diferencial no confirmado.");
                    }
                }
            }
        }

        private async Task AplicarTrazabilidadReevaluacionAsync(
            ProveedorIAResultadoFoto resultado,
            DiagnosticoIAImagen imagen,
            string? retroalimentacion,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(retroalimentacion))
                return;

            string comparacionModelo = Normalizar(
                resultado.ComparacionValoracionAnterior,
                1800);

            List<ProveedorIADiagnosticoFoto> anteriores =
                await ObtenerDiagnosticosVisualesAnterioresAsync(
                    imagen.DiagnosticoIAImagenId,
                    cancellationToken);

            string comparacionEstructural = CrearComparacionEstructural(
                anteriores,
                resultado.Diagnosticos ?? [],
                out bool huboCambioEstructural);

            string comparacion;

            if (huboCambioEstructural)
            {
                comparacion = comparacionEstructural;

                if (!string.IsNullOrWhiteSpace(comparacionModelo) &&
                    !AfirmaAusenciaDeCambios(comparacionModelo))
                {
                    comparacion = Normalizar(
                        comparacion + " Explicación de Gemini: " +
                        comparacionModelo,
                        1800);
                }
            }
            else if (!string.IsNullOrWhiteSpace(comparacionModelo))
            {
                comparacion = comparacionModelo;
            }
            else if (!string.IsNullOrWhiteSpace(comparacionEstructural))
            {
                comparacion = comparacionEstructural;
            }
            else
            {
                string diagnosticoAnterior = Normalizar(
                    imagen.ResultadoIA?.DiagnosticoProbable,
                    300);
                string diagnosticoActual = Normalizar(
                    resultado.DiagnosticoProbable,
                    300);

                if (!string.IsNullOrWhiteSpace(diagnosticoAnterior) &&
                    !string.IsNullOrWhiteSpace(diagnosticoActual))
                {
                    comparacion = string.Equals(
                        diagnosticoAnterior,
                        diagnosticoActual,
                        StringComparison.OrdinalIgnoreCase)
                            ? $"Se mantiene el diagnóstico principal '{diagnosticoActual}'. La reevaluación revisó las zonas indicadas por el técnico y no se detectó un cambio estructural verificable en los datos visuales disponibles."
                            : $"El diagnóstico principal cambió de '{diagnosticoAnterior}' a '{diagnosticoActual}'. Revise las nuevas localizaciones y diferenciales de esta valoración.";
                }
                else
                {
                    comparacion =
                        "La reevaluación fue comparada con la valoración previa disponible. Revise las regiones conservadas, nuevas o reclasificadas en la imagen marcada.";
                }
            }

            resultado.ComparacionValoracionAnterior = Normalizar(
                comparacion,
                1800);

            string bloqueComparacion =
                "Comparación con valoración anterior: " +
                resultado.ComparacionValoracionAnterior;

            if (string.IsNullOrWhiteSpace(resultado.ResumenImagen))
            {
                resultado.ResumenImagen = Normalizar(
                    bloqueComparacion,
                    1600);
                return;
            }

            string resumen = resultado.ResumenImagen;
            int indiceComparacion = resumen.IndexOf(
                "Comparación con valoración anterior:",
                StringComparison.OrdinalIgnoreCase);
            int indiceCambio = resumen.IndexOf(
                "Cambio respecto a valoración anterior:",
                StringComparison.OrdinalIgnoreCase);

            if (indiceComparacion < 0 && indiceCambio < 0)
            {
                resultado.ResumenImagen = Normalizar(
                    bloqueComparacion + " " + resumen,
                    1600);
                return;
            }

            // Si Gemini ya había escrito una comparación pero la verificación
            // estructural detectó cambios, reemplazamos el encabezado previo para
            // evitar frases contradictorias como "ningún cambio".
            if (huboCambioEstructural &&
                (AfirmaAusenciaDeCambios(resumen) || indiceComparacion >= 0))
            {
                string descripcionActual = resumen;

                if (indiceComparacion >= 0)
                {
                    int fin = BuscarFinBloqueComparacion(
                        resumen,
                        indiceComparacion);
                    descripcionActual =
                        (resumen[..indiceComparacion] + resumen[fin..]).Trim();
                }
                else if (indiceCambio >= 0)
                {
                    int fin = BuscarFinBloqueComparacion(
                        resumen,
                        indiceCambio);
                    descripcionActual =
                        (resumen[..indiceCambio] + resumen[fin..]).Trim();
                }

                resultado.ResumenImagen = Normalizar(
                    bloqueComparacion + " " + descripcionActual,
                    1600);
            }
        }

        private async Task<List<ProveedorIADiagnosticoFoto>>
            ObtenerDiagnosticosVisualesAnterioresAsync(
                int fotografiaId,
                CancellationToken cancellationToken)
        {
            const string sql = """
SELECT TOP (1) [DiagnosticosJson]
FROM [dbo].[diagnosticoIAImagenResultadoVisualV2]
WHERE [DiagnosticoIAImagenId] = @fotoId
  AND [EsVigente] = 1
ORDER BY [Revision] DESC,
         [DiagnosticoIAImagenResultadoVisualId] DESC;
""";

            try
            {
                DbConnection connection = db.Database.GetDbConnection();
                await using DbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.CommandType = CommandType.Text;
                command.CommandTimeout = 30;

                DbParameter parameter = command.CreateParameter();
                parameter.ParameterName = "@fotoId";
                parameter.Value = fotografiaId;
                command.Parameters.Add(parameter);

                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync(cancellationToken);

                object? valor = await command.ExecuteScalarAsync(cancellationToken);
                string json = valor?.ToString() ?? "[]";

                return JsonSerializer.Deserialize<
                           List<ProveedorIADiagnosticoFoto>>(
                               json,
                               JsonOptions) ?? [];
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "No fue posible obtener el JSON visual anterior de la fotografía {FotografiaId} para verificar los cambios de reevaluación.",
                    fotografiaId);
                return [];
            }
        }

        private static string CrearComparacionEstructural(
            IReadOnlyCollection<ProveedorIADiagnosticoFoto> anteriores,
            IReadOnlyCollection<ProveedorIADiagnosticoFoto> actuales,
            out bool huboCambio)
        {
            huboCambio = false;
            if (anteriores.Count == 0)
                return string.Empty;

            ProveedorIADiagnosticoFoto? principalAnterior =
                anteriores.FirstOrDefault(item => item.EsPrincipal) ??
                anteriores.FirstOrDefault();
            ProveedorIADiagnosticoFoto? principalActual =
                actuales.FirstOrDefault(item => item.EsPrincipal) ??
                actuales.FirstOrDefault();

            var partes = new List<string>();

            string nombreAnterior = Normalizar(
                principalAnterior?.Diagnostico,
                300);
            string nombreActual = Normalizar(
                principalActual?.Diagnostico,
                300);

            if (!string.Equals(
                    nombreAnterior,
                    nombreActual,
                    StringComparison.OrdinalIgnoreCase))
            {
                huboCambio = true;
                partes.Add(
                    $"El diagnóstico principal cambió de '{nombreAnterior}' a '{nombreActual}'.");
            }
            else if (!string.IsNullOrWhiteSpace(nombreActual))
            {
                partes.Add(
                    $"El diagnóstico principal se mantiene como '{nombreActual}'.");
            }

            string firmaAnterior = CrearFirmaDiagnosticos(anteriores);
            string firmaActual = CrearFirmaDiagnosticos(actuales);

            if (!string.Equals(
                    firmaAnterior,
                    firmaActual,
                    StringComparison.Ordinal))
            {
                huboCambio = true;
                partes.Add(
                    "Las localizaciones diagnósticas cambiaron respecto a la valoración previa: se agregaron, retiraron, movieron o ajustaron una o más regiones.");
            }
            else
            {
                partes.Add(
                    "Las localizaciones diagnósticas se mantienen sin cambios estructurales.");
            }

            string diferencialesAnteriores =
                CrearFirmaDiferenciales(anteriores);
            string diferencialesActuales =
                CrearFirmaDiferenciales(actuales);

            if (!string.Equals(
                    diferencialesAnteriores,
                    diferencialesActuales,
                    StringComparison.Ordinal))
            {
                huboCambio = true;
                partes.Add(
                    "Los diagnósticos diferenciales o sus localizaciones cambiaron respecto a la valoración anterior.");
            }
            else if (!string.IsNullOrWhiteSpace(diferencialesActuales))
            {
                partes.Add(
                    "Los diferenciales localizados se mantienen estructuralmente iguales.");
            }

            List<string> ambiguas = ObtenerRegionesAmbiguas(actuales);
            if (ambiguas.Count > 0)
            {
                partes.Add(
                    "Regiones ambiguas: " + string.Join("; ", ambiguas) + ".");
            }

            return Normalizar(string.Join(" ", partes), 1800);
        }

        private static string CrearFirmaDiagnosticos(
            IEnumerable<ProveedorIADiagnosticoFoto> diagnosticos) =>
            string.Join(
                "||",
                diagnosticos
                    .OrderBy(item => Normalizar(item.Diagnostico, 300),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(item =>
                    {
                        string nombre = Normalizar(item.Diagnostico, 300)
                            .ToUpperInvariant();
                        string cajas = string.Join(
                            ";",
                            (item.Lesiones ?? [])
                                .Where(lesion => EsBoxValido(lesion.Box2d))
                                .Select(lesion => string.Join(",", lesion.Box2d))
                                .OrderBy(valor => valor, StringComparer.Ordinal));
                        return nombre + "=" + cajas;
                    }));

        private static string CrearFirmaDiferenciales(
            IEnumerable<ProveedorIADiagnosticoFoto> diagnosticos)
        {
            var firmas = new List<string>();

            foreach (ProveedorIADiagnosticoFoto diagnostico in diagnosticos)
            {
                foreach (string nombre in
                         diagnostico.DiagnosticosDiferenciales ?? [])
                {
                    string normalizado = Normalizar(nombre, 300)
                        .ToUpperInvariant();
                    if (!string.IsNullOrWhiteSpace(normalizado))
                        firmas.Add(normalizado + "=SIN_LOCALIZACION");
                }

                foreach (ProveedorIADiagnosticoDiferencialFoto diferencial in
                         diagnostico.DiferencialesLocalizados ?? [])
                {
                    string nombre = Normalizar(
                        diferencial.Diagnostico,
                        300).ToUpperInvariant();
                    string cajas = string.Join(
                        ";",
                        (diferencial.Lesiones ?? [])
                            .Where(lesion => EsBoxValido(lesion.Box2d))
                            .Select(lesion => string.Join(",", lesion.Box2d))
                            .OrderBy(valor => valor, StringComparer.Ordinal));

                    firmas.Add(nombre + "=" + cajas);
                }
            }

            return string.Join(
                "||",
                firmas
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(valor => valor, StringComparer.Ordinal));
        }

        private static List<string> ObtenerRegionesAmbiguas(
            IEnumerable<ProveedorIADiagnosticoFoto> diagnosticos)
        {
            const double umbralAmbiguedad = 0.55d;
            var resultado = new List<string>();

            foreach (ProveedorIADiagnosticoFoto diagnostico in diagnosticos)
            {
                foreach (ProveedorIADiagnosticoDiferencialFoto diferencial in
                         diagnostico.DiferencialesLocalizados ?? [])
                {
                    bool seSolapa = (diferencial.Lesiones ?? []).Any(
                        lesionDiferencial =>
                            (diagnostico.Lesiones ?? []).Any(
                                lesionPrincipal =>
                                {
                                    double interseccion =
                                        ObtenerAreaInterseccion(
                                            lesionPrincipal.Box2d,
                                            lesionDiferencial.Box2d);
                                    double areaMenor = Math.Min(
                                        ObtenerAreaBox(
                                            lesionPrincipal.Box2d),
                                        ObtenerAreaBox(
                                            lesionDiferencial.Box2d));

                                    return areaMenor > 0d &&
                                           interseccion / areaMenor >=
                                               umbralAmbiguedad;
                                }));

                    if (seSolapa)
                    {
                        resultado.Add(
                            $"{diagnostico.Diagnostico} / {diferencial.Diagnostico}");
                    }
                }
            }

            return resultado.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool AfirmaAusenciaDeCambios(string texto)
        {
            string normalizado = (texto ?? string.Empty).ToLowerInvariant();
            return normalizado.Contains("ningún cambio") ||
                   normalizado.Contains("ningun cambio") ||
                   normalizado.Contains("sin cambios") ||
                   normalizado.Contains("ninguno en el diagnóstico") ||
                   normalizado.Contains("ninguno en el diagnostico");
        }

        private static int BuscarFinBloqueComparacion(
            string resumen,
            int inicio)
        {
            int punto = resumen.IndexOf('.', inicio);
            return punto < 0
                ? resumen.Length
                : Math.Min(resumen.Length, punto + 1);
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
        public string ComparacionValoracionAnterior { get; set; } = string.Empty;
        public List<string> SintomasVisibles { get; set; } = [];
        public List<string> EvidenciasObservadas { get; set; } = [];
        public List<string> EvidenciasNoObservadas { get; set; } = [];
        public List<string> DiagnosticosAlternativos { get; set; } = [];
        public List<string> InformacionFaltante { get; set; } = [];
        public List<string> RecomendacionesCaptura { get; set; } = [];
        public List<string> Advertencias { get; set; } = [];
        public List<ProveedorIADiagnosticoFoto> Diagnosticos { get; set; } = [];

        [JsonIgnore]
        public string Proveedor { get; set; } = string.Empty;

        [JsonIgnore]
        public string Modelo { get; set; } = string.Empty;

        [JsonIgnore]
        public string RespuestaOriginalJson { get; set; } = string.Empty;
    }

    public sealed class ProveedorIADiagnosticoFoto
    {
        public string Id { get; set; } = string.Empty;
        public string Diagnostico { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public string TipoDiagnostico { get; set; } = string.Empty;
        public bool EsPrincipal { get; set; }
        public string NivelCerteza { get; set; } = string.Empty;
        public string Severidad { get; set; } = string.Empty;
        public List<string> DiagnosticosDiferenciales { get; set; } = [];
        public List<ProveedorIADiagnosticoDiferencialFoto> DiferencialesLocalizados { get; set; } = [];
        public List<ProveedorIALesionFoto> Lesiones { get; set; } = [];

        /// <summary>
        /// Se asigna en backend después de validar la respuesta. No se solicita
        /// al proveedor para impedir que la semántica visual dependa del modelo.
        /// </summary>
        public string ColorMarcador { get; set; } = string.Empty;
    }

    public sealed class ProveedorIADiagnosticoDiferencialFoto
    {
        public string Diagnostico { get; set; } = string.Empty;
        public List<ProveedorIALesionFoto> Lesiones { get; set; } = [];

        [JsonIgnore]
        public string ColorMarcador { get; set; } = "#1E88E5";
    }

    public sealed class ProveedorIALesionFoto
    {
        public string Id { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public List<int> Box2d { get; set; } = [];
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
