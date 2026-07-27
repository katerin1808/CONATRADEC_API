using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class CrearSeguimientoAlertaRequest
    {
        [Range(1, int.MaxValue)]
        public int terrenoId { get; set; }

        [Required, MaxLength(80)]
        public string tipoAlerta { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string nivel { get; set; } = string.Empty;

        public int? usuarioAsignadoId { get; set; }

        [MaxLength(1000)]
        public string observacion { get; set; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int usuarioAccionId { get; set; }
    }

    public sealed class ActualizarSeguimientoAlertaRequest
    {
        [Required, MaxLength(20)]
        public string estado { get; set; } = string.Empty;

        public int? usuarioAsignadoId { get; set; }

        [MaxLength(1000)]
        public string observacion { get; set; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int usuarioAccionId { get; set; }
    }

    public sealed class ActualizarUmbralAlertaRequest
    {
        public decimal valor { get; set; }

        [Range(1, int.MaxValue)]
        public int usuarioAccionId { get; set; }
    }

    public sealed class SeguimientoAlertaResponse
    {
        public int seguimientoAlertaAgricolaId { get; set; }
        public int terrenoId { get; set; }
        public string tipoAlerta { get; set; } = string.Empty;
        public string nivel { get; set; } = string.Empty;
        public string estado { get; set; } = string.Empty;
        public int? usuarioAsignadoId { get; set; }
        public string? usuarioAsignado { get; set; }
        public string observacion { get; set; } = string.Empty;
        public DateTime fechaCreacionUtc { get; set; }
        public DateTime fechaUltimaModificacionUtc { get; set; }
        public DateTime? fechaCierreUtc { get; set; }
    }

    public sealed class ConfiguracionAlertaResponse
    {
        public int configuracionAlertaAgricolaId { get; set; }
        public string clave { get; set; } = string.Empty;
        public string nombre { get; set; } = string.Empty;
        public decimal valor { get; set; }
        public string operador { get; set; } = string.Empty;
        public string unidad { get; set; } = string.Empty;
        public string descripcion { get; set; } = string.Empty;
    }

    public sealed class HistorialAlertaResponse
    {
        public int historialAlertaAgricolaId { get; set; }
        public string accion { get; set; } = string.Empty;
        public string detalle { get; set; } = string.Empty;
        public int usuarioId { get; set; }
        public string usuario { get; set; } = string.Empty;
        public DateTime fechaUtc { get; set; }
    }

    public sealed class TecnicoAlertaResponse
    {
        public int usuarioId { get; set; }
        public string nombreCompleto { get; set; } = string.Empty;
        public string nombreUsuario { get; set; } = string.Empty;
        public string rol { get; set; } = string.Empty;
        public string procedencia { get; set; } = string.Empty;
    }
}
