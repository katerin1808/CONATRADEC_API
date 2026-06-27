using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("analisisSueloCalculo", Schema = "dbo")]
    public class AnalisisSueloCalculo
    {
        [Key]
        public int analisisSueloCalculoId { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal cantidadQuintalesOro { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal tamanoFinca { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal phAnalisisSuelo { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal? materiaOrganica { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal? acidezTotal { get; set; }

        [MaxLength(500)]
        public string? recomendacionGeneral { get; set; }

        [MaxLength(500)]
        public string? observacion { get; set; }

        public DateTime fechaCalculo { get; set; }

        public bool activo { get; set; } = true;

        public int analisisSueloId { get; set; }

        public int terrenoId { get; set; }

        public int tipoCultivoId { get; set; }

        public int tipoAnalisisSueloId { get; set; }

        public int? usuarioId { get; set; }

        public int? unidadMedidaMateriaOrganicaId { get; set; }

        [ForeignKey(nameof(unidadMedidaMateriaOrganicaId))]
        public UnidadMedida? unidadMedidaMateriaOrganica { get; set; }

        public ICollection<AnalisisSueloCalculoElementoQuimico> ElementosCalculados { get; set; }
            = new List<AnalisisSueloCalculoElementoQuimico>();
    }
}