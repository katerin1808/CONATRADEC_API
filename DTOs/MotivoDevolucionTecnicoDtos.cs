namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// Motivo administrable utilizado por el analizador para solicitar una
    /// corrección al técnico. Las banderas orientan la acción esperada sin
    /// impedir que el analizador agregue instrucciones específicas.
    /// </summary>
    public sealed class MotivoDevolucionTecnicoRespuesta
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
    }

    public class MotivoDevolucionTecnicoGuardarRequest
    {
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string InstruccionSugerida { get; set; } = string.Empty;
        public bool RequiereNuevaFotografia { get; set; }
        public bool PermiteCorregirMetadatos { get; set; } = true;
        public int Orden { get; set; } = 1;
    }

    public sealed class MotivoDevolucionTecnicoCrearRequest :
        MotivoDevolucionTecnicoGuardarRequest
    {
    }

    public sealed class MotivoDevolucionTecnicoActualizarRequest :
        MotivoDevolucionTecnicoGuardarRequest
    {
    }

    /// <summary>
    /// Solicitud individual del analizador. La fotografía no se elimina y el
    /// motivo queda registrado en el historial aunque el catálogo cambie.
    /// </summary>
    public sealed class DevolverFotografiaTecnicoRequest
    {
        public int FotografiaId { get; set; }
        public int MotivoDevolucionTecnicoId { get; set; }
        public string Instrucciones { get; set; } = string.Empty;
    }

    /// <summary>
    /// Respuesta del técnico para reabrir el análisis IA de la misma evidencia.
    /// Cuando el motivo exige otra toma, el técnico agrega la nueva fotografía
    /// con el flujo existente y puede descartar la evidencia devuelta.
    /// </summary>
    public sealed class ResolverDevolucionTecnicoRequest
    {
        public int FotografiaId { get; set; }
        public string TipoFotografia { get; set; } = string.Empty;
        public DateTime FechaIdentificacionCampo { get; set; }
        public string RespuestaTecnico { get; set; } = string.Empty;
    }

    public sealed class DevolucionTecnicoFotografiaDto
    {
        public int DevolucionTecnicoId { get; set; }
        public int FotografiaId { get; set; }
        public int MotivoDevolucionTecnicoId { get; set; }
        public string MotivoCodigo { get; set; } = string.Empty;
        public string MotivoNombre { get; set; } = string.Empty;
        public string MotivoDescripcion { get; set; } = string.Empty;
        public string InstruccionSugerida { get; set; } = string.Empty;
        public string InstruccionesAnalizador { get; set; } = string.Empty;
        public bool RequiereNuevaFotografia { get; set; }
        public bool PermiteCorregirMetadatos { get; set; }
        public int UsuarioAnalizadorId { get; set; }
        public DateTime FechaDevolucionUtc { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string RespuestaTecnico { get; set; } = string.Empty;
        public int? UsuarioTecnicoId { get; set; }
        public DateTime? FechaResolucionUtc { get; set; }
    }

    public sealed class ResumenRevisionAnalizadorDto
    {
        public int InspeccionId { get; set; }
        public int TotalRegistradas { get; set; }
        public int TotalEvaluables { get; set; }
        public int TotalDescartadasTecnico { get; set; }
        public int TotalRecibidasAnalizador { get; set; }
        public int TotalPendientesTecnico { get; set; }
        public int TotalDevueltasTecnico { get; set; }
        public int TotalErroresIA { get; set; }
        public int TotalProcesandoIA { get; set; }
        public int TotalPendienteDecisionTecnico { get; set; }
        public int TotalClasificadasHumano { get; set; }
        public int TotalPendientesClasificacionHumana { get; set; }
        public bool EtapaTecnicaFinalizada { get; set; }
        public bool EtapaAnalizadorFinalizada { get; set; }
        public DateTime? FechaFinEtapaAnalizadorUtc { get; set; }
        public bool PuedeFinalizarRevision { get; set; }
        public string MotivoNoPuedeFinalizarRevision { get; set; } = string.Empty;
    }

    public sealed class ContextoRevisionAnalizadorDto
    {
        public ResumenRevisionAnalizadorDto Resumen { get; set; } = new();
        public List<DevolucionTecnicoFotografiaDto> Devoluciones { get; set; } = [];
    }
}
