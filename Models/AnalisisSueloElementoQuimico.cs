using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("analisisSueloElementoQuimico", Schema = "dbo")]
    public class AnalisisSueloElementoQuimico
    {
        [Key]
        public int analisisSueloElementoQuimicoId { get; set; }

        [Column(TypeName = "decimal(10,4)")]
        public decimal cantidadElemento { get; set; }

        public bool activo { get; set; } = true;

        public int analisisSueloId { get; set; }

        public int elementoQuimicosId { get; set; }

        public int unidadMedidaId { get; set; }

        public AnalisisSuelo AnalisisSuelo { get; set; } = null!;

        public ElementoQuimico ElementoQuimico { get; set; } = null!;

        public UnidadMedida UnidadMedida { get; set; } = null!;
    }
}