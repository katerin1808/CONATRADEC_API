using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("propietarioTerreno", Schema = "dbo")]
    public sealed class PropietarioTerreno
    {
        [Key]
        public int propietarioTerrenoId { get; set; }

        public int propietarioId { get; set; }

        public int terrenoId { get; set; }

        public bool activo { get; set; } = true;

        public DateTime fechaAsignacionUtc { get; set; }

        public DateTime? fechaDesasignacionUtc { get; set; }

        public int? asignadoPorUsuarioId { get; set; }

        public int? desasignadoPorUsuarioId { get; set; }

        public Propietario Propietario { get; set; } = null!;

        public Terreno Terreno { get; set; } = null!;
    }
}
