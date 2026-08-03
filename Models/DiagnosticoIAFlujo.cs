namespace CONATRADEC_API.Models
{
    /// <summary>
    /// Códigos internos del flujo de diagnóstico. Se mantienen centralizados
    /// para impedir estados o clasificaciones escritos de forma diferente
    /// entre Gemini, el controlador, el inicializador y los clientes.
    /// </summary>
    public static class DiagnosticoIAFlujo
    {
        public const string InterfazSolicitud =
            "diagnosticoIASolicitudPage";

        public const string InterfazAnalizador =
            "diagnosticoIAAnalizadorPage";

        public const string InterfazAprobador =
            "diagnosticoIAAprobadorPage";

        public const string InterfazConfiguracion =
            "diagnosticoIAConfiguracionPage";

        public const string InterfazAlbum =
            "albumFotosPage";

        public static class Estados
        {
            public const string AnalizandoIA = "ANALIZANDO_IA";
            public const string ErrorAnalisis = "ERROR_ANALISIS";
            public const string PendienteDecisionTecnico =
                "PENDIENTE_DECISION_TECNICO";
            public const string CanceladoPorTecnico =
                "CANCELADO_POR_TECNICO";
            public const string PendienteAnalizador = "PENDIENTE_ANALIZADOR";
            public const string EnAnalisisHumano = "EN_ANALISIS_HUMANO";
            public const string PendienteAprobacion = "PENDIENTE_APROBACION";
            public const string DevueltoCorreccion = "DEVUELTO_PARA_CORRECCION";
            public const string Aprobado = "APROBADO";
            public const string AprobadoConCorreccion =
                "APROBADO_CON_CORRECCION";
            public const string Rechazado = "RECHAZADO";
            public const string NoConcluyente = "NO_CONCLUYENTE";
            public const string PublicadoAlbum = "PUBLICADO_EN_ALBUM";
            public const string Anulado = "ANULADO";
        }

        public static class CalidadEvaluacion
        {
            public const string Evaluable = "EVALUABLE";
            public const string Parcial = "PARCIALMENTE_EVALUABLE";
            public const string NoEvaluable = "NO_EVALUABLE";

            public static readonly HashSet<string> Todos =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    Evaluable,
                    Parcial,
                    NoEvaluable
                };
        }

        public static class EstadoGeneral
        {
            public const string Sana = "APARENTEMENTE_SANA";
            public const string Afectada = "CON_AFECTACION";
            public const string Indeterminada = "INDETERMINADA";

            public static readonly HashSet<string> Todos =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    Sana,
                    Afectada,
                    Indeterminada
                };
        }

        public static class Categoria
        {
            public const string Enfermedad = "ENFERMEDAD";
            public const string Plaga = "PLAGA";
            public const string AlteracionNutricional =
                "ALTERACION_NUTRICIONAL";
            public const string EstresAbiotico = "ESTRES_ABIOTICO";
            public const string DanoMecanico = "DANO_MECANICO";
            public const string NoDeterminada =
                "AFECTACION_NO_DETERMINADA";
            public const string NoAplica = "NO_APLICA";

            public static readonly HashSet<string> Todos =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    Enfermedad,
                    Plaga,
                    AlteracionNutricional,
                    EstresAbiotico,
                    DanoMecanico,
                    NoDeterminada,
                    NoAplica
                };
        }

        public static class Severidad
        {
            public const string Leve = "LEVE";
            public const string Moderada = "MODERADA";
            public const string Severa = "SEVERA";
            public const string NoEvaluable = "NO_EVALUABLE";
            public const string NoAplica = "NO_APLICA";

            public static readonly HashSet<string> Todos =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    Leve,
                    Moderada,
                    Severa,
                    NoEvaluable,
                    NoAplica
                };
        }

        public static class Certeza
        {
            public const string Alto = "ALTO";
            public const string Medio = "MEDIO";
            public const string Bajo = "BAJO";
            public const string NoDeterminado = "NO_DETERMINADO";

            public static readonly HashSet<string> Todos =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    Alto,
                    Medio,
                    Bajo,
                    NoDeterminado
                };
        }

        /// <summary>
        /// Estado de la clasificación que vincula un resultado de Gemini con
        /// la estructura oficial del Álbum Botánico. Gemini nunca crea
        /// registros: únicamente selecciona uno existente o propone uno nuevo.
        /// </summary>
        public static class ClasificacionAlbum
        {
            public const string NoAplica = "NO_APLICA";
            public const string ResueltaAutomatica =
                "RESUELTA_AUTOMATICA";
            public const string PendienteDecisionTecnico =
                "PENDIENTE_DECISION_TECNICO";
            public const string ResueltaPorTecnico =
                "RESUELTA_POR_TECNICO";
            public const string CreadaDesdeInspeccion =
                "CREADA_DESDE_INSPECCION";

            public static bool EstaPendiente(string? estado) =>
                string.Equals(
                    estado,
                    PendienteDecisionTecnico,
                    StringComparison.OrdinalIgnoreCase);

            public static bool EstaResuelta(string? estado) =>
                string.Equals(estado, ResueltaAutomatica, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(estado, ResueltaPorTecnico, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(estado, CreadaDesdeInspeccion, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(estado, NoAplica, StringComparison.OrdinalIgnoreCase);
        }

        public static class EstadoAnalisisHumano
        {
            public const string Borrador = "BORRADOR";
            public const string Enviado = "ENVIADO";
            public const string Devuelto = "DEVUELTO";
            public const string Superado = "SUPERADO";
        }

        public static class DecisionAprobacion
        {
            public const string AprobarSinCambios =
                "APROBAR_SIN_CAMBIOS";

            public const string AprobarConCorreccion =
                "APROBAR_CON_CORRECCION";

            public const string Devolver =
                "DEVOLVER_AL_ANALIZADOR";

            public const string Rechazar =
                "RECHAZAR_DIAGNOSTICO";

            public const string NoConcluyente =
                "MARCAR_NO_CONCLUYENTE";

            public static readonly HashSet<string> Todos =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    AprobarSinCambios,
                    AprobarConCorreccion,
                    Devolver,
                    Rechazar,
                    NoConcluyente
                };
        }

        public static class CalidadImagen
        {
            public const string Alta = "ALTA";
            public const string Media = "MEDIA";
            public const string Baja = "BAJA";
            public const string NoEvaluable = "NO_EVALUABLE";

            public static readonly HashSet<string> Todos =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    Alta,
                    Media,
                    Baja,
                    NoEvaluable
                };
        }

        public static string Normalizar(
            string? valor,
            HashSet<string> permitidos,
            string predeterminado)
        {
            string normalizado = (valor ?? string.Empty)
                .Trim()
                .ToUpperInvariant();

            return permitidos.Contains(normalizado)
                ? normalizado
                : predeterminado;
        }

        public static bool EsEstadoAprobado(string? estado) =>
            string.Equals(
                estado,
                Estados.Aprobado,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                estado,
                Estados.AprobadoConCorreccion,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                estado,
                Estados.PublicadoAlbum,
                StringComparison.OrdinalIgnoreCase);
    }
}
