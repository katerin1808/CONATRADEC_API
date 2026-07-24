using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class CategoriaPublicacionDto
    {
        public int CategoriaPublicacionId { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string ColorHex { get; set; } = "#3B655B";
        public int Orden { get; set; }
    }

    public sealed class PublicacionGuardarDto
    {
        public int PublicacionId { get; set; }

        [Range(1, int.MaxValue,
            ErrorMessage = "Debe seleccionar una categoría válida.")]
        public int CategoriaPublicacionId { get; set; }

        [Required(ErrorMessage = "El título es obligatorio.")]
        [MaxLength(180,
            ErrorMessage = "El título no puede superar los 180 caracteres.")]
        public string Titulo { get; set; } = string.Empty;

        [Required(ErrorMessage = "El resumen es obligatorio.")]
        [MaxLength(500,
            ErrorMessage = "El resumen no puede superar los 500 caracteres.")]
        public string Resumen { get; set; } = string.Empty;

        [Required(ErrorMessage = "El contenido es obligatorio.")]
        public string Contenido { get; set; } = string.Empty;

        [MaxLength(1000,
            ErrorMessage = "El enlace no puede superar los 1000 caracteres.")]
        public string? EnlaceExterno { get; set; }

        [MaxLength(120,
            ErrorMessage = "El texto del enlace no puede superar los 120 caracteres.")]
        public string? TextoEnlace { get; set; }

        [MaxLength(300,
            ErrorMessage = "La ubicación no puede superar los 300 caracteres.")]
        public string? Ubicacion { get; set; }

        public DateTimeOffset? FechaEventoInicio { get; set; }
        public DateTimeOffset? FechaEventoFin { get; set; }

        [Required(ErrorMessage = "La fecha de inicio de publicación es obligatoria.")]
        public DateTimeOffset FechaInicioPublicacion { get; set; }

        public DateTimeOffset? FechaFinPublicacion { get; set; }

        [Required(ErrorMessage = "El estado de la publicación es obligatorio.")]
        [MaxLength(20)]
        public string EstadoPublicacion { get; set; } = "BORRADOR";

        public bool Destacada { get; set; }
    }

    public sealed class CambiarEstadoPublicacionDto
    {
        [Required]
        [MaxLength(20)]
        public string EstadoPublicacion { get; set; } = string.Empty;
    }

    public sealed class CambiarDestacadaPublicacionDto
    {
        public bool Destacada { get; set; }
    }

    public sealed class SubirPortadaPublicacionDto
    {
        [Required(ErrorMessage = "Debe seleccionar una imagen.")]
        public IFormFile Archivo { get; set; } = null!;
    }

    public class PublicacionListadoDto
    {
        public int PublicacionId { get; set; }
        public int CategoriaPublicacionId { get; set; }
        public string Categoria { get; set; } = string.Empty;
        public string ColorCategoria { get; set; } = "#3B655B";
        public string Titulo { get; set; } = string.Empty;
        public string Resumen { get; set; } = string.Empty;
        public string RutaImagenPortada { get; set; } = string.Empty;
        public string Ubicacion { get; set; } = string.Empty;
        public DateTime? FechaEventoInicioUtc { get; set; }
        public DateTime? FechaEventoFinUtc { get; set; }
        public DateTime FechaInicioPublicacionUtc { get; set; }
        public DateTime? FechaFinPublicacionUtc { get; set; }
        public string EstadoPublicacion { get; set; } = string.Empty;
        public string EstadoVisual { get; set; } = string.Empty;
        public bool Destacada { get; set; }
        public DateTime FechaCreacionUtc { get; set; }
        public DateTime FechaUltimaModificacionUtc { get; set; }
        public int UsuarioCreacionId { get; set; }
        public int UsuarioUltimaModificacionId { get; set; }
        public string Autor { get; set; } = string.Empty;
        public string UltimoEditor { get; set; } = string.Empty;
    }

    public sealed class PublicacionDetalleDto : PublicacionListadoDto
    {
        public string Contenido { get; set; } = string.Empty;
        public string EnlaceExterno { get; set; } = string.Empty;
        public string TextoEnlace { get; set; } = string.Empty;
        public bool Activo { get; set; }
    }

    public sealed class PublicacionPaginadaDto
    {
        public List<PublicacionListadoDto> Items { get; set; } = new();
        public int Pagina { get; set; }
        public int TamanoPagina { get; set; }
        public int TotalRegistros { get; set; }
        public int TotalPaginas { get; set; }
    }

    public sealed class PublicacionCreadaDto
    {
        public int PublicacionId { get; set; }
    }

    public sealed class PortadaPublicacionDto
    {
        public int PublicacionId { get; set; }
        public string RutaImagenPortada { get; set; } = string.Empty;
    }
}
