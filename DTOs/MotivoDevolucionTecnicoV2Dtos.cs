namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// Contratos de la versión auditada del catálogo. Las rutas históricas y
    /// sus DTO permanecen intactos para conservar compatibilidad.
    /// </summary>
    public sealed class MotivoDevolucionTecnicoV2Respuesta
    {
        public int MotivoDevolucionTecnicoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string InstruccionSugerida { get; set; } = string.Empty;
        public bool RequiereNuevaFotografia { get; set; }
        public bool PermiteCorregirMetadatos { get; set; }
        public int Orden { get; set; }
        public bool Activo { get; set; }
        public DateTime FechaCreacionUtc { get; set; }
        public DateTime FechaModificacionUtc { get; set; }
        public string RowVersion { get; set; } = string.Empty;
    }

    public class MotivoDevolucionTecnicoV2GuardarRequest
    {
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string InstruccionSugerida { get; set; } = string.Empty;
        public bool RequiereNuevaFotografia { get; set; }
        public bool PermiteCorregirMetadatos { get; set; } = true;
        public int Orden { get; set; } = 1;
    }

    public sealed class MotivoDevolucionTecnicoV2CrearRequest :
        MotivoDevolucionTecnicoV2GuardarRequest
    {
    }

    public sealed class MotivoDevolucionTecnicoV2ActualizarRequest :
        MotivoDevolucionTecnicoV2GuardarRequest
    {
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class MotivoDevolucionTecnicoV2EstadoRequest
    {
        public string RowVersion { get; set; } = string.Empty;
    }
}
