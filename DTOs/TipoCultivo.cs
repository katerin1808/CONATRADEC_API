using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("tipoCultivo", Schema = "dbo")]
    public sealed class TipoCultivo
    {
        [Key]
        public int tipoCultivoId { get; set; }

        [Required]
        [MaxLength(80)]
        public string nombreTipoCultivo { get; set; } =
            string.Empty;

        [Required]
        [MaxLength(150)]
        public string descripcionTipoCultivo { get; set; } =
            string.Empty;

        public bool activo { get; set; } = true;
    }

    public sealed class CrearTipoCultivoDto
    {
        [Required]
        [MaxLength(80)]
        public string nombreTipoCultivo { get; set; } =
            string.Empty;

        [MaxLength(150)]
        public string? descripcionTipoCultivo { get; set; }
    }

    public sealed class ActualizarTipoCultivoDto
    {
        [Required]
        [MaxLength(80)]
        public string nombreTipoCultivo { get; set; } =
            string.Empty;

        [MaxLength(150)]
        public string? descripcionTipoCultivo { get; set; }
    }

    public sealed class TipoCultivoRespuestaDto
    {
        public int tipoCultivoId { get; set; }

        public string nombreTipoCultivo { get; set; } =
            string.Empty;

        /*
         * Se conserva para las pantallas antiguas que recibían
         * el nombre del cultivo en la propiedad tipoCultivo.
         */
        public string tipoCultivo { get; set; } =
            string.Empty;

        public string descripcionTipoCultivo { get; set; } =
            string.Empty;

        public bool activo { get; set; }

        public int cantidadRangosActivos { get; set; }

        public int cantidadAnalisis { get; set; }
    }

    public sealed class TipoCultivoPaginaResponse
    {
        public List<TipoCultivoRespuestaDto> Items { get; set; } =
            new();

        public int PaginaActual { get; set; }

        public int TamanoPagina { get; set; }

        public int TotalRegistros { get; set; }

        public int TotalPaginas { get; set; }
    }
}
