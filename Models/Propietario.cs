using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("propietario", Schema = "dbo")]
    public sealed class Propietario
    {
        [Key]
        public int propietarioId { get; set; }

        [Required, MaxLength(50)]
        public string identificacion { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string identificacionNormalizada { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string nombreCompleto { get; set; } = string.Empty;

        [MaxLength(25)]
        public string? telefono { get; set; }

        [MaxLength(150)]
        public string? correo { get; set; }

        [MaxLength(300)]
        public string? direccion { get; set; }

        public bool activo { get; set; } = true;

        public DateTime fechaRegistroUtc { get; set; }

        public DateTime? fechaActualizacionUtc { get; set; }

        public int? usuarioRegistroId { get; set; }

        public int? usuarioActualizacionId { get; set; }

        public ICollection<PropietarioTerreno> RelacionesTerreno { get; set; }
            = new List<PropietarioTerreno>();
    }
}
