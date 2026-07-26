using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    /// <summary>
    /// Configura cómo convertir una unidad de materia orgánica a porcentaje.
    /// </summary>
    [Table("materiaOrganicaUnidadMedida", Schema = "dbo")]
    public sealed class MateriaOrganicaUnidadMedida
    {
        [Key]
        public int materiaOrganicaUnidadMedidaId { get; set; }

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

        public bool visibleEnFormulario { get; set; } = true;

        public int orden { get; set; }

        [MaxLength(300)]
        public string? observacion { get; set; }

        public bool activo { get; set; } = true;

        public UnidadMedida UnidadMedida { get; set; } =
            null!;
    }
}
