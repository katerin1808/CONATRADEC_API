using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("parametroFuenteOrganicaAporte", Schema = "dbo")]
    public class ParametroFuenteOrganicaAporte
    {
        [Key]
        public int parametroFuenteOrganicaAporteId { get; set; }

        public int fuenteNutrientesId { get; set; }

        public int elementoQuimicosId { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal cantidadAportePorUnidad { get; set; }

        [Required]
        [MaxLength(30)]
        public string unidadEntrada { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string descripcionParametro { get; set; } = null!;

        public bool activo { get; set; } = true;

        public FuenteNutriente FuenteNutriente { get; set; } = null!;

        public ElementoQuimico ElementoQuimico { get; set; } = null!;
    }
}