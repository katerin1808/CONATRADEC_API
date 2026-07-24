using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("publicacion", Schema = "dbo")]
    public sealed class Publicacion
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int publicacionId { get; set; }

        public int categoriaPublicacionId { get; set; }

        [Required, MaxLength(180)]
        public string titulo { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string resumen { get; set; } = string.Empty;

        [Required]
        public string contenido { get; set; } = string.Empty;

        [MaxLength(500)]
        public string rutaImagenPortada { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string enlaceExterno { get; set; } = string.Empty;

        [MaxLength(120)]
        public string textoEnlace { get; set; } = string.Empty;

        [MaxLength(300)]
        public string ubicacion { get; set; } = string.Empty;

        public DateTime? fechaEventoInicioUtc { get; set; }
        public DateTime? fechaEventoFinUtc { get; set; }
        public DateTime fechaInicioPublicacionUtc { get; set; }
        public DateTime? fechaFinPublicacionUtc { get; set; }

        [Required, MaxLength(20)]
        public string estadoPublicacion { get; set; } = "BORRADOR";

        public bool destacada { get; set; }

        public int usuarioCreacionId { get; set; }
        public int usuarioUltimaModificacionId { get; set; }

        public DateTime fechaCreacionUtc { get; set; }
        public DateTime fechaUltimaModificacionUtc { get; set; }

        public bool activo { get; set; } = true;

        public CategoriaPublicacion CategoriaPublicacion { get; set; } = null!;
    }
}
