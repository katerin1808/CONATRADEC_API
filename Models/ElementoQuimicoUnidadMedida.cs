using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    /// <summary>
    /// Define una unidad permitida para un elemento químico y la fórmula
    /// utilizada para normalizar el valor reportado a lb/Mz.
    /// </summary>
    [Table("elementoQuimicoUnidadMedida", Schema = "dbo")]
    public sealed class ElementoQuimicoUnidadMedida
    {
        [Key]
        public int elementoQuimicoUnidadMedidaId { get; set; }

        [ForeignKey(nameof(ElementoQuimico))]
        public int elementoQuimicosId { get; set; }

        [ForeignKey(nameof(UnidadMedida))]
        public int unidadMedidaId { get; set; }

        [Required]
        [MaxLength(50)]
        public string codigoFormulaConversion { get; set; } =
            "LINEAL";

        [Column(TypeName = "decimal(18,8)")]
        public decimal factorPrincipal { get; set; } = 1m;

        [Column(TypeName = "decimal(18,8)")]
        public decimal factorSecundario { get; set; } = 1m;

        [Column(TypeName = "decimal(18,8)")]
        public decimal factorTerciario { get; set; } = 1m;

        [Column(TypeName = "decimal(18,8)")]
        public decimal divisor { get; set; } = 1m;

        [Column(TypeName = "decimal(18,8)")]
        public decimal desplazamiento { get; set; }

        public bool unidadPredeterminada { get; set; }

        /// <summary>
        /// Permite conservar conversiones internas, por ejemplo kg/ha para
        /// convertir rangos, sin mostrar esa unidad en el formulario.
        /// </summary>
        public bool visibleEnFormulario { get; set; } = true;

        public int orden { get; set; }

        [MaxLength(300)]
        public string? observacion { get; set; }

        public bool activo { get; set; } = true;

        public ElementoQuimico ElementoQuimico { get; set; } =
            null!;

        public UnidadMedida UnidadMedida { get; set; } =
            null!;
    }
}
