namespace CONATRADEC_API.Models
{
    /// <summary>
    /// Estados del expediente individual de cada fotografía.
    /// La inspección general se calcula a partir de estos valores.
    /// </summary>
    public static class InspeccionFitosanitariaFlujo
    {
        public static class FotoEstados
        {
            public const string Borrador = "BORRADOR";
            public const string PendienteIA = "PENDIENTE_IA";
            public const string AnalizandoIA = "ANALIZANDO_IA";
            public const string ErrorIA = "ERROR_IA";
            public const string PendienteDecisionTecnico = "PENDIENTE_DECISION_TECNICO";
            public const string PendienteAnalizador = "PENDIENTE_ANALIZADOR";
            public const string EnAnalisisHumano = "EN_ANALISIS_HUMANO";
            public const string PendienteAprobacion = "PENDIENTE_APROBACION";
            public const string DevueltaAnalizador = "DEVUELTA_AL_ANALIZADOR";
            public const string Aprobada = "APROBADA";
            public const string AprobadaConCorreccion = "APROBADA_CON_CORRECCION";
            public const string Rechazada = "RECHAZADA";
            public const string NoConcluyente = "NO_CONCLUYENTE";
            public const string Descartada = "DESCARTADA";
            public const string PublicadaAlbum = "PUBLICADA_ALBUM";

            public static readonly HashSet<string> Todos =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    Borrador,
                    PendienteIA,
                    AnalizandoIA,
                    ErrorIA,
                    PendienteDecisionTecnico,
                    PendienteAnalizador,
                    EnAnalisisHumano,
                    PendienteAprobacion,
                    DevueltaAnalizador,
                    Aprobada,
                    AprobadaConCorreccion,
                    Rechazada,
                    NoConcluyente,
                    Descartada,
                    PublicadaAlbum
                };
        }

        public static class InspeccionEstados
        {
            public const string Borrador = "BORRADOR";
            public const string EnProceso = "EN_PROCESO";
            public const string EnProcesoConErrores = "EN_PROCESO_CON_ERRORES";
            public const string PendienteRevision = "PENDIENTE_REVISION";
            public const string PendienteAprobacion = "PENDIENTE_APROBACION";
            public const string Finalizada = "FINALIZADA";
            public const string FinalizadaParcialmente = "FINALIZADA_PARCIALMENTE";
        }

        public static class Acciones
        {
            public const string FotoRegistrada = "FOTO_REGISTRADA";
            public const string AnalisisIAIniciado = "ANALISIS_IA_INICIADO";
            public const string AnalisisIACompletado = "ANALISIS_IA_COMPLETADO";
            public const string AnalisisIAError = "ANALISIS_IA_ERROR";
            public const string TecnicoEnviaAnalizador = "TECNICO_ENVIA_ANALIZADOR";
            public const string TecnicoSolicitaRevision = "TECNICO_SOLICITA_REVISION";
            public const string AnalisisHumanoGuardado = "ANALISIS_HUMANO_GUARDADO";
            public const string AnalisisHumanoEnviado = "ANALISIS_HUMANO_ENVIADO";
            public const string AprobacionRegistrada = "APROBACION_REGISTRADA";
            public const string FotoDescartada = "FOTO_DESCARTADA";
            public const string FotoPublicadaAlbum = "FOTO_PUBLICADA_ALBUM";
        }

        public static class DecisionesAprobacion
        {
            public const string Aprobar = "APROBAR";
            public const string AprobarConCorreccion = "APROBAR_CON_CORRECCION";
            public const string Devolver = "DEVOLVER_AL_ANALIZADOR";
            public const string Rechazar = "RECHAZAR";
            public const string NoConcluyente = "NO_CONCLUYENTE";

            public static readonly HashSet<string> Todas =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    Aprobar,
                    AprobarConCorreccion,
                    Devolver,
                    Rechazar,
                    NoConcluyente
                };
        }

        public static string CalcularEstadoInspeccion(
            IEnumerable<string?> estados)
        {
            List<string> lista = estados
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.Trim().ToUpperInvariant())
                .ToList();

            if (lista.Count == 0 || lista.All(item =>
                    item is FotoEstados.Borrador or FotoEstados.PendienteIA))
            {
                return InspeccionEstados.Borrador;
            }

            if (lista.Any(item => item == FotoEstados.PendienteAprobacion))
                return InspeccionEstados.PendienteAprobacion;

            if (lista.Any(item => item is
                    FotoEstados.PendienteAnalizador or
                    FotoEstados.EnAnalisisHumano or
                    FotoEstados.DevueltaAnalizador))
            {
                return InspeccionEstados.PendienteRevision;
            }

            if (lista.Any(item => item is
                    FotoEstados.AnalizandoIA or
                    FotoEstados.PendienteDecisionTecnico))
            {
                return lista.Any(item => item == FotoEstados.ErrorIA)
                    ? InspeccionEstados.EnProcesoConErrores
                    : InspeccionEstados.EnProceso;
            }

            if (lista.Any(item => item == FotoEstados.ErrorIA))
            {
                bool existeResultadoFinal = lista.Any(EsEstadoFinal);
                return existeResultadoFinal
                    ? InspeccionEstados.FinalizadaParcialmente
                    : InspeccionEstados.EnProcesoConErrores;
            }

            if (lista.All(EsEstadoFinal))
            {
                bool todosExitosos = lista.All(item => item is
                    FotoEstados.Aprobada or
                    FotoEstados.AprobadaConCorreccion or
                    FotoEstados.PublicadaAlbum);

                return todosExitosos
                    ? InspeccionEstados.Finalizada
                    : InspeccionEstados.FinalizadaParcialmente;
            }

            return InspeccionEstados.EnProceso;
        }

        public static bool EsEstadoFinal(string? estado) =>
            estado is FotoEstados.Aprobada or
                FotoEstados.AprobadaConCorreccion or
                FotoEstados.Rechazada or
                FotoEstados.NoConcluyente or
                FotoEstados.Descartada or
                FotoEstados.PublicadaAlbum;

        public static string NormalizarEstadoFoto(string? estado) =>
            FotoEstados.Todos.Contains(estado ?? string.Empty)
                ? estado!.Trim().ToUpperInvariant()
                : FotoEstados.Borrador;
    }
}
