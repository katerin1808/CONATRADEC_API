using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{

    [Table("fuenteNutrienteElementoQuimico", Schema = "dbo")]
    public class FuenteNutrienteElementoQuimico
    {

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int fuenteNutrienteElementoQuimicoId { get; set; }

        public decimal cantidadAporte { get; set; }

        // FK obligatorias (igual que rolId / interfazId en tu ejemplo)
        public int? fuenteNutrientesId { get; set; }
        public int? elementoQuimicosId { get; set; }

        // Navegaciones estilo “null!”
        public FuenteNutriente FuenteNutriente { get; set; } = null!;
        public ElementoQuimico ElementoQuimico { get; set; } = null!;
    }
}
