using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("parametroRangoNutrienteCultivo", Schema = "dbo")]
    public class ParametroRangoNutrienteCultivo
    {
        [Key]
        public int parametroRangoNutrienteCultivoId { get; set; }

        public int tipoCultivoId { get; set; }

        public int elementoQuimicosId { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal valorMinimo { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal valorMaximo { get; set; }

        [Required]
        [MaxLength(30)]
        public string unidadBase { get; set; } = null!;

        [Required]
        [MaxLength(150)]
        public string descripcionParametro { get; set; } = null!;

        public bool activo { get; set; } = true;

        public TipoCultivo TipoCultivo { get; set; } = null!;

        public ElementoQuimico ElementoQuimico { get; set; } = null!;
    }
}