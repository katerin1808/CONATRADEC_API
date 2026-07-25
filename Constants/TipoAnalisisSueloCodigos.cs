namespace CONATRADEC_API.Constants
{
    /// <summary>
    /// Códigos internos estables de los módulos de cálculo.
    /// No deben depender del nombre visible que puede editar el usuario.
    /// </summary>
    public static class TipoAnalisisSueloCodigos
    {
        public const string RequerimientoAnual =
            "REQUERIMIENTO_ANUAL";

        public const string BalanceFormula =
            "BALANCE_FORMULA";

        public const string EnmiendaCalcarea =
            "ENMIENDA_CALCAREA";

        public const string FertilizacionMixta =
            "FERTILIZACION_MIXTA";

        public static bool EsTipoSistema(
            string? codigo) =>
            codigo is
                RequerimientoAnual or
                BalanceFormula or
                EnmiendaCalcarea or
                FertilizacionMixta;

        public static string CrearCodigoPersonalizado() =>
            $"PERSONALIZADO_{Guid.NewGuid():N}"
                .ToUpperInvariant();
    }
}
