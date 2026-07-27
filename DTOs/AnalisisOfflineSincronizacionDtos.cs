namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// Envoltorio idempotente para sincronizar una operación guardada en un
    /// dispositivo. Una misma operaciónLocalId nunca debe crear dos análisis.
    /// </summary>
    public sealed class AnalisisOfflineSincronizarDto
    {
        public Guid operacionLocalId { get; set; }
        public string tipoOperacion { get; set; } = "CREAR";
        public int? analisisSueloCalculoId { get; set; }
        public GuardarTodoDto solicitud { get; set; } = new();
        public string versionMotor { get; set; } = string.Empty;
        public string hashPaquete { get; set; } = string.Empty;
        public DateTime fechaCalculoLocalUtc { get; set; }
    }
}
