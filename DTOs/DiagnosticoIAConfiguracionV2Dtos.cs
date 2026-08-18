using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class DiagnosticoIAConfiguracionV2ActualizarRequest
    {
        [Range(1, 20)]
        public int MaximoRevisionesGemini { get; set; } = 2;

        public bool RevisionesIlimitadas { get; set; }

        [Required]
        public string RowVersion { get; set; } = string.Empty;
    }

    public sealed class DiagnosticoIAConfiguracionV2Dto
    {
        public int MaximoRevisionesGemini { get; set; } = 2;
        public bool RevisionesIlimitadas { get; set; }
        public DateTime FechaModificacionUtc { get; set; }
        public int? UsuarioModificacionId { get; set; }
        public string UsuarioModificacion { get; set; } = string.Empty;
        public string RowVersion { get; set; } = string.Empty;
        public List<DiagnosticoIAConfiguracionHistorialDto> Historial { get; set; } = [];
    }
}
