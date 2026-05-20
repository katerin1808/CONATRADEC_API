using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("analisisSueloCalculoElementoQuimico", Schema = "dbo")]
    public class AnalisisSueloCalculoElementoQuimico
    {
        [Key]
        public int analisisSueloCalculoElementoQuimicoId { get; set; }

        public int analisisSueloCalculoId { get; set; }

        public int elementoQuimicosId { get; set; }

        public int? unidadMedidaId { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal cantidadIngresada { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal? requerimientoCalculado { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal? cantidadConvertidaLbMz { get; set; }

        [MaxLength(50)]
        public string? clasificacion { get; set; }

        [MaxLength(500)]
        public string? observacion { get; set; }

        public bool activo { get; set; } = true;

        public AnalisisSueloCalculo AnalisisSueloCalculo { get; set; } = null!;

        public ElementoQuimico ElementoQuimico { get; set; } = null!;

        public UnidadMedida? UnidadMedida { get; set; }
    }
}