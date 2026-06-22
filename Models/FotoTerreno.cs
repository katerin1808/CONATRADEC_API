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

        public bool activo { get; set; } = true;

        public int terrenoId { get; set; }

        [ForeignKey(nameof(terrenoId))]
        public Terreno? Terreno { get; set; }
    }
}

