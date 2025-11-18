using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("unidadMedida", Schema = "dbo")]
    public class UnidadMedida
    {
        [Key]
        public int unidadMedidaId { get; set; }

        [Required, MaxLength(50)]
        public string nombreUnidadMedida { get; set; } = null!;

        public bool activo { get; set; }

        // ==========================
        //  Relaciones de navegación
        // ==========================

        // 1 unidad -> muchos análisisSueloElementoQuimico
        public ICollection<AnalisisSueloElementoQuimico> AnalisisSueloElementosQuimicos { get; set; }
            = new List<AnalisisSueloElementoQuimico>();

        // 1 unidad -> muchos rangoNutrimental
        public ICollection<RangoNutrimental> RangosNutrimentales { get; set; }
            = new List<RangoNutrimental>();
    }
}