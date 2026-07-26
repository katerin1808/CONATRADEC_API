using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("unidadMedida", Schema = "dbo")]
    public class UnidadMedida
    {
        [Key]
        public int unidadMedidaId { get; set; }

        [Required]
        [MaxLength(50)]
        public string nombreUnidadMedida { get; set; } =
            null!;

        public bool activo { get; set; } = true;

        public ICollection<AnalisisSueloElementoQuimico>
            AnalisisSueloElementosQuimicos { get; set; } =
                new List<AnalisisSueloElementoQuimico>();

        public ICollection<RangoNutrimental>
            RangosNutrimentales { get; set; } =
                new List<RangoNutrimental>();

        /// <summary>
        /// Configuraciones que permiten usar esta unidad en elementos
        /// químicos del análisis.
        /// </summary>
        public ICollection<ElementoQuimicoUnidadMedida>
            ElementosQuimicosConfigurados { get; set; } =
                new List<ElementoQuimicoUnidadMedida>();

        /// <summary>
        /// Configuraciones que permiten usar esta unidad en materia orgánica.
        /// </summary>
        public ICollection<MateriaOrganicaUnidadMedida>
            MateriaOrganicaConfiguraciones { get; set; } =
                new List<MateriaOrganicaUnidadMedida>();
    }
}
