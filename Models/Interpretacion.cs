using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("interpretacion", Schema = "dbo")]
    public class Interpretacion
    {
        [Key]
        public int interpretacionId { get; set; }

        [Required, MaxLength(50)]
        public string codigoInterpretacion { get; set; } = null!;

        [Required]
        public DateOnly fechaInterpretacion { get; set; }

        public bool activo { get; set; }

        // ===== Relaciones (FKs) =====
        public int terrenoId { get; set; }
        public Terreno Terreno { get; set; } = null!;

        public int analisisSueloId { get; set; }
        public AnalisisSuelo AnalisisSuelo { get; set; } = null!;

        public int usuarioId { get; set; }
        public Usuario Usuario { get; set; } = null!;

        public int interpretacionRangoNutrimentalId { get; set; }
        public RangoNutrimental InterpretacionRangoNutrimental { get; set; } = null!;
    }
}
