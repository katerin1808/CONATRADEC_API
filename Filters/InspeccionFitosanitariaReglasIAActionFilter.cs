using CONATRADEC_API.DTOs;
using CONATRADEC_API.Infrastructure;
using CONATRADEC_API.Models;
using CONATRADEC_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CONATRADEC_API.Filters
{
    /// <summary>
    /// Aplica en backend las reglas que separan el análisis IA inicial de las
    /// reevaluaciones del técnico. No depende de triggers ni procedimientos de
    /// base de datos.
    /// </summary>
    public sealed class InspeccionFitosanitariaReglasIAActionFilter :
        IAsyncActionFilter,
        IOrderedFilter
    {
        private const string Controlador = "InspeccionFitosanitaria";
        private const string AccionAnalisisInicial = "ProcesarFotografias";
        private const string AccionReevaluacion = "SolicitarRevisionIA";

        private readonly DiagnosticoIADbContext diagnosticoDb;
        private readonly PermisoApiService permisos;
        private readonly ILogger<InspeccionFitosanitariaReglasIAActionFilter>
            logger;

        public InspeccionFitosanitariaReglasIAActionFilter(
            DiagnosticoIADbContext diagnosticoDb,
            PermisoApiService permisos,
            ILogger<InspeccionFitosanitariaReglasIAActionFilter> logger)
        {
            this.diagnosticoDb = diagnosticoDb;
            this.permisos = permisos;
            this.logger = logger;
        }

        /// <summary>
        /// Se ejecuta después de las validaciones transversales generales del
        /// flujo fitosanitario y antes de entrar al controlador.
        /// </summary>
        public int Order => 100;

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            context.ActionDescriptor.RouteValues.TryGetValue(
                "controller",
                out string? controladorValor);
            context.ActionDescriptor.RouteValues.TryGetValue(
                "action",
                out string? accionValor);

            string controlador = controladorValor ?? string.Empty;
            string accion = accionValor ?? string.Empty;

            bool esAnalisisInicial = string.Equals(
                accion,
                AccionAnalisisInicial,
                StringComparison.OrdinalIgnoreCase);
            bool esReevaluacion = string.Equals(
                accion,
                AccionReevaluacion,
                StringComparison.OrdinalIgnoreCase);

            if (!string.Equals(
                    controlador,
                    Controlador,
                    StringComparison.OrdinalIgnoreCase) ||
                (!esAnalisisInicial && !esReevaluacion))
            {
                await next();
                return;
            }

            CancellationToken cancellationToken =
                context.HttpContext.RequestAborted;

            int[] fotografiaIds = ObtenerFotografiaIds(context);
            int? inspeccionId = ObtenerInspeccionId(context);
            int? usuarioId = ObtenerUsuarioId(context.HttpContext.User);

            if (fotografiaIds.Length == 0 ||
                !inspeccionId.HasValue ||
                !usuarioId.HasValue)
            {
                await next();
                return;
            }

            int? usuarioSolicitanteId = await diagnosticoDb.Diagnosticos
                .AsNoTracking()
                .Where(item =>
                    item.DiagnosticoIAId == inspeccionId.Value &&
                    item.Activo)
                .Select(item => (int?)item.UsuarioSolicitanteId)
                .FirstOrDefaultAsync(cancellationToken);

            if (usuarioSolicitanteId != usuarioId.Value)
            {
                await next();
                return;
            }

            ResultadoPermisoApi permiso = await permisos.ValidarAsync(
                usuarioId.Value,
                DiagnosticoIAFlujo.InterfazSolicitud,
                TipoPermisoApi.Agregar,
                cancellationToken);

            if (!permiso.Permitido)
            {
                await next();
                return;
            }

            int[] fotografiasDeInspeccion = await diagnosticoDb.Imagenes
                .AsNoTracking()
                .Where(item =>
                    item.DiagnosticoIAId == inspeccionId.Value &&
                    fotografiaIds.Contains(item.DiagnosticoIAImagenId))
                .Select(item => item.DiagnosticoIAImagenId)
                .ToArrayAsync(cancellationToken);

            if (fotografiasDeInspeccion.Length != fotografiaIds.Length)
            {
                await next();
                return;
            }

            Dictionary<int, DiagnosticoIAImagenResultadoIA> resultadosPrevios =
                await diagnosticoDb.ResultadosImagenIA
                    .AsNoTracking()
                    .Where(item =>
                        item.Imagen.DiagnosticoIAId == inspeccionId.Value &&
                        fotografiaIds.Contains(item.DiagnosticoIAImagenId))
                    .ToDictionaryAsync(
                        item => item.DiagnosticoIAImagenId,
                        cancellationToken);

            HashSet<int> fotografiasConResultadoPrevio =
                resultadosPrevios.Keys.ToHashSet();

            if (esAnalisisInicial &&
                fotografiasConResultadoPrevio.Count > 0)
            {
                context.Result = new ConflictObjectResult(new
                {
                    success = false,
                    message =
                        "Una o más fotografías ya cuentan con un análisis IA. Para analizarlas nuevamente debe solicitar una nueva evaluación IA.",
                    data = new
                    {
                        fotografiaIds = fotografiasConResultadoPrevio
                            .OrderBy(item => item)
                            .ToArray()
                    }
                });
                return;
            }

            if (esReevaluacion &&
                fotografiasConResultadoPrevio.Count != fotografiaIds.Length)
            {
                int[] sinResultado = fotografiaIds
                    .Where(item =>
                        !fotografiasConResultadoPrevio.Contains(item))
                    .OrderBy(item => item)
                    .ToArray();

                context.Result = new BadRequestObjectResult(new
                {
                    success = false,
                    message =
                        "La nueva evaluación IA solo puede solicitarse para fotografías que ya tengan un resultado IA previo.",
                    data = new
                    {
                        fotografiaIds = sinResultado
                    }
                });
                return;
            }

            try
            {
                await next();
            }
            finally
            {
                /*
                 * Una reevaluación fallida no invalida el último resultado IA
                 * que ya había sido aceptado por el sistema. El controlador
                 * registra el intento y su error; esta capa transversal deja la
                 * fotografía nuevamente pendiente de la decisión del técnico.
                 */
                if (esReevaluacion &&
                    fotografiasConResultadoPrevio.Count > 0)
                {
                    await RestaurarResultadoAnteriorSiCorrespondeAsync(
                        resultadosPrevios,
                        usuarioId.Value);
                }
            }
        }

        private async Task RestaurarResultadoAnteriorSiCorrespondeAsync(
            IReadOnlyDictionary<int, DiagnosticoIAImagenResultadoIA>
                resultadosPrevios,
            int usuarioId)
        {
            var database = new InspeccionFitosanitariaDatabase(diagnosticoDb);

            foreach ((int fotografiaId, DiagnosticoIAImagenResultadoIA previo)
                     in resultadosPrevios)
            {
                try
                {
                    FotoMetadatos? foto = await database.ObtenerFotoAsync(
                        fotografiaId,
                        CancellationToken.None);

                    if (foto == null ||
                        !foto.Activo ||
                        foto.Descartada ||
                        !string.Equals(
                            foto.Estado,
                            InspeccionFitosanitariaFlujo.FotoEstados.ErrorIA,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    await RestaurarResultadoPersistidoAsync(
                        fotografiaId,
                        previo);

                    string detalle = string.IsNullOrWhiteSpace(
                            foto.ErrorProcesamiento)
                        ? "La reevaluación IA no pudo completarse. Se conservó el último resultado IA válido y la fotografía vuelve a quedar pendiente de la decisión del técnico."
                        : "La reevaluación IA no pudo completarse. Se conservó el último resultado IA válido. Error registrado: " +
                          Limitar(foto.ErrorProcesamiento, 1200);

                    await database.CambiarEstadoFotoAsync(
                        fotografiaId,
                        usuarioId,
                        InspeccionFitosanitariaFlujo.FotoEstados
                            .PendienteDecisionTecnico,
                        "REEVALUACION_IA_ERROR_RESULTADO_ANTERIOR_CONSERVADO",
                        detalle,
                        error: foto.ErrorProcesamiento,
                        modeloIA: foto.ModeloIAUtilizado,
                        cancellationToken: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "No fue posible restaurar el estado técnico de la fotografía {FotografiaId} después de una reevaluación IA fallida.",
                        fotografiaId);
                }
            }
        }

        private async Task RestaurarResultadoPersistidoAsync(
            int fotografiaId,
            DiagnosticoIAImagenResultadoIA resultadoPrevio)
        {
            DiagnosticoIAImagenResultadoIA? actual =
                await diagnosticoDb.ResultadosImagenIA
                    .FirstOrDefaultAsync(
                        item => item.DiagnosticoIAImagenId == fotografiaId,
                        CancellationToken.None);

            if (actual == null)
                return;

            /*
             * Si la reevaluación alcanzó a modificar la entidad antes de que
             * una operación posterior fallara, se restauran los valores que
             * estaban persistidos al iniciar la solicitud. Así una revisión
             * con error nunca reemplaza el último resultado válido.
             */
            diagnosticoDb.Entry(actual)
                .CurrentValues
                .SetValues(resultadoPrevio);

            await diagnosticoDb.SaveChangesAsync(CancellationToken.None);
        }

        private static int[] ObtenerFotografiaIds(
            ActionExecutingContext context)
        {
            InspeccionFotosSeleccionadasRequest? request = context
                .ActionArguments
                .Values
                .OfType<InspeccionFotosSeleccionadasRequest>()
                .FirstOrDefault();

            return (request?.FotografiaIds ?? [])
                .Where(item => item > 0)
                .Distinct()
                .ToArray();
        }

        private static int? ObtenerInspeccionId(
            ActionExecutingContext context)
        {
            if (context.ActionArguments.TryGetValue(
                    "id",
                    out object? valor) &&
                valor is int id &&
                id > 0)
            {
                return id;
            }

            return null;
        }

        private static int? ObtenerUsuarioId(ClaimsPrincipal user)
        {
            string? valor =
                user.FindFirstValue(ClaimTypes.NameIdentifier) ??
                user.FindFirstValue("usuarioId") ??
                user.FindFirstValue("uid") ??
                user.FindFirstValue("sub");

            return int.TryParse(valor, out int usuarioId) && usuarioId > 0
                ? usuarioId
                : null;
        }

        private static string Limitar(string? valor, int maximo)
        {
            string texto = (valor ?? string.Empty).Trim();
            return texto.Length <= maximo ? texto : texto[..maximo];
        }
    }
}
