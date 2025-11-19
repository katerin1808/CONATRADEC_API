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

       
        public int fuenteNutrientesId { get; set; }

        [ForeignKey(nameof(fuenteNutrientesId))]
        public FuenteNutriente? FuenteNutriente { get; set; }

        // ===============================
        // FK -> ElementoQuimico
        // ===============================
        public int elementoQuimicosId { get; set; }

        [ForeignKey(nameof(elementoQuimicosId))]
        public ElementoQuimico? ElementoQuimico { get; set; }

        public bool activo { get; set; } = true;
    }

}

