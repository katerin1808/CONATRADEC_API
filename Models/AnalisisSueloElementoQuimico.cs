using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("analisisSueloElementoQuimico", Schema = "dbo")]
    public class AnalisisSueloElementoQuimico
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int analisisSueloElementoQuimicoId { get; set; }

        [Column(TypeName = "decimal(10,0)")]
        public decimal cantidadElemento { get; set; }

        public bool activo { get; set; }

        // ===============================
        //       FK + Navegaciones
        // ===============================

        // FK -> AnalisisSuelo
        public int analisisSueloId { get; set; }
        public AnalisisSuelo AnalisisSuelo { get; set; } = null!;

        // FK -> ElementoQuimico
        public int elementoQuimicosId { get; set; }
        public ElementoQuimico ElementoQuimicos { get; set; } = null!;

        // FK -> UnidadMedida
        public int unidadMedidaId { get; set; }
        public UnidadMedida UnidadMedida { get; set; } = null!;
    }
}