using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("elementoQuimico")]
    public sealed class ElementoQuimico
    {
        [Key]
        public int elementoQuimicosId { get; set; }

        [Required]
        [StringLength(10)]
        public string simboloElementoQuimico { get; set; } =
            string.Empty;

        [Required]
        [StringLength(100)]
        public string nombreElementoQuimico { get; set; } =
            string.Empty;

        [Column(TypeName = "decimal(10,2)")]
        public decimal pesoEquivalenteElementoQuimico { get; set; }

        public bool activo { get; set; } = true;

        public ICollection<FuenteNutrienteElementoQuimico>
            fuenteNutrienteElementoQuimico { get; set; } =
                new List<FuenteNutrienteElementoQuimico>();

        /// <summary>
        /// Unidades permitidas y reglas de conversión configuradas para
        /// este elemento químico.
        /// </summary>
        public ICollection<ElementoQuimicoUnidadMedida>
            unidadesMedidaPermitidas { get; set; } =
                new List<ElementoQuimicoUnidadMedida>();
    }
}
