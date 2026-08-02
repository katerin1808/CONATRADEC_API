using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("FotoTerreno")]
    public class FotoTerreno
    {
        [Key]
        public int fotoTerrenoId { get; set; }

        [Required]
        [MaxLength(500)]
        public string urlFotoTerreno { get; set; } = string.Empty;

        [MaxLength(150)]
        public string tituloFotoTerreno { get; set; } = string.Empty;

        [MaxLength(600)]
        public string descripcionFotoTerreno { get; set; } = string.Empty;

        [MaxLength(255)]
        public string nombreArchivoOriginal { get; set; } = string.Empty;

        public DateTime fechaRegistroUtc { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "date")]
        public DateTime? fechaCaptura { get; set; }

        public bool esPortada { get; set; }

        public bool activo { get; set; } = true;

        public int terrenoId { get; set; }

        [ForeignKey(nameof(terrenoId))]
        public Terreno? Terreno { get; set; }
    }
}
