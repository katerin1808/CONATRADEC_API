using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("categoriaPublicacion", Schema = "dbo")]
    public sealed class CategoriaPublicacion
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int categoriaPublicacionId { get; set; }

        [Required, MaxLength(80)]
        public string nombreCategoriaPublicacion { get; set; } = string.Empty;

        [MaxLength(250)]
        public string descripcionCategoriaPublicacion { get; set; } = string.Empty;

        [Required, MaxLength(7)]
        public string colorHex { get; set; } = "#3B655B";

        public int orden { get; set; }

        public bool activo { get; set; } = true;

        public ICollection<Publicacion> Publicaciones { get; set; } =
            new List<Publicacion>();
    }
}
