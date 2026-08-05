namespace CONATRADEC_API.Models
{
    /// <summary>
    /// Estados del expediente individual de cada fotografía y reglas del ciclo
    /// de vida general de la inspección.
    ///
    /// La inspección permanece bajo control del técnico hasta que éste la
    /// cierra expresamente. Antes del cierre no aparece en la bandeja del
    /// analizador, aunque algunas fotografías ya estén listas para revisión.
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
            /// <summary>
            /// Al menos una fotografía presentó un error de IA y el técnico
            /// todavía debe corregir o reintentar antes de cerrar.
            /// </summary>
            public const string Borrador = "BORRADOR";

            /// <summary>
            /// Inspección abierta, sin fotografías procesadas todavía.
            /// Se pueden agregar o descartar fotografías.
            /// </summary>
            public const string EnProceso = "EN_PROCESO";

            /// <summary>
            /// El técnico ya comenzó a procesar o preparar fotografías, pero
            /// aún no ha cerrado la inspección. No aparece al analizador.
            /// </summary>
            public const string Parcial = "PARCIAL";

            public const string PendienteRevision = "PENDIENTE_REVISION";
            public const string PendienteAprobacion = "PENDIENTE_APROBACION";
            public const string Finalizada = "FINALIZADA";
            public const string FinalizadaParcialmente = "FINALIZADA_PARCIALMENTE";

            // Compatibilidad con registros y clientes de versiones anteriores.
            public const string EnProcesoConErrores = Borrador;
        }

        public static class Acciones
        {
            public const string FotoRegistrada = "FOTO_REGISTRADA";
            public const string AnalisisIAIniciado = "ANALISIS_IA_INICIADO";
            public const string AnalisisIACompletado = "ANALISIS_IA_COMPLETADO";
            public const string AnalisisIAError = "ANALISIS_IA_ERROR";
            public const string TecnicoEnviaAnalizador = "TECNICO_ENVIA_ANALIZADOR";
            public const string TecnicoCierraInspeccion = "TECNICO_CIERRA_INSPECCION";
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
            IEnumerable<string?> estados,
            bool cerradaPorTecnico = false)
        {
            List<string> lista = estados
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.Trim().ToUpperInvariant())
                .ToList();

            if (lista.Count == 0)
                return InspeccionEstados.EnProceso;

            if (!cerradaPorTecnico)
            {
                // Un error mantiene la inspección en borrador para que el
                // técnico lo corrija antes de habilitar el siguiente paso.
                if (lista.Any(item => item == FotoEstados.ErrorIA))
                    return InspeccionEstados.Borrador;

                bool ningunaProcesada = lista.All(item => item is
                    FotoEstados.Borrador or
                    FotoEstados.PendienteIA or
                    FotoEstados.Descartada);

                return ningunaProcesada
                    ? InspeccionEstados.EnProceso
                    : InspeccionEstados.Parcial;
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

            if (lista.All(EsEstadoFinal))
            {
                bool todosExitosos = lista.All(item => item is
                    FotoEstados.Aprobada or
                    FotoEstados.AprobadaConCorreccion or
                    FotoEstados.PublicadaAlbum or
                    FotoEstados.Descartada);

                return todosExitosos
                    ? InspeccionEstados.Finalizada
                    : InspeccionEstados.FinalizadaParcialmente;
            }

            // Una inspección cerrada nunca debe volver a una fase editable del
            // técnico. Si queda un estado inesperado, se conserva pendiente de
            // revisión para que sea visible y auditable.
            return InspeccionEstados.PendienteRevision;
        }

        public static bool PuedeCerrarInspeccion(
            IEnumerable<string?> estados)
        {
            List<string> lista = estados
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.Trim().ToUpperInvariant())
                .ToList();

            return lista.Any(item => item == FotoEstados.PendienteAnalizador) &&
                lista.All(item => item is
                    FotoEstados.PendienteAnalizador or
                    FotoEstados.Descartada);
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
