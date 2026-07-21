using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("bitacoraDetalle", Schema = "dbo")]
    public sealed class BitacoraDetalle
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long bitacoraDetalleId { get; set; }

        public Guid bitacoraId { get; set; }
        public DateTime fechaHoraUtc { get; set; }

        [MaxLength(150)]
        public string entidad { get; set; } = string.Empty;

        [MaxLength(300)]
        public string entidadId { get; set; } = string.Empty;

        [MaxLength(30)]
        public string operacion { get; set; } = string.Empty;

        public string valoresAnteriores { get; set; } = string.Empty;
        public string valoresNuevos { get; set; } = string.Empty;
        public string propiedadesModificadas { get; set; } = string.Empty;

        public Bitacora bitacora { get; set; } = null!;
    }
}
