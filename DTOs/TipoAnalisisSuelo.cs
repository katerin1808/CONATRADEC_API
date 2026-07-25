using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("tipoAnalisisSuelo", Schema = "dbo")]
    public sealed class TipoAnalisisSuelo
    {
        [Key]
        public int tipoAnalisisSueloId { get; set; }

        /*
         * Código técnico inmutable.
         * Permite enlazar el catálogo con el módulo real sin depender
         * del nombre visible ni del identificador numérico.
         */
        [Required]
        [MaxLength(50)]
        public string codigoTipoAnalisisSuelo { get; set; } =
            string.Empty;

        [Required]
        [MaxLength(100)]
        public string nombreTipoAnalisisSuelo { get; set; } =
            string.Empty;

        [Required]
        [MaxLength(200)]
        public string descripcionTipoAnalisisSuelo { get; set; } =
            string.Empty;

        public bool activo { get; set; } = true;
    }

    public sealed class CrearTipoAnalisisSueloDto
    {
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [MaxLength(100)]
        public string nombreTipoAnalisisSuelo { get; set; } =
            string.Empty;

        [Required(ErrorMessage = "La descripción es obligatoria.")]
        [MaxLength(200)]
        public string descripcionTipoAnalisisSuelo { get; set; } =
            string.Empty;
    }

    public sealed class ActualizarTipoAnalisisSueloDto
    {
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [MaxLength(100)]
        public string nombreTipoAnalisisSuelo { get; set; } =
            string.Empty;

        [Required(ErrorMessage = "La descripción es obligatoria.")]
        [MaxLength(200)]
        public string descripcionTipoAnalisisSuelo { get; set; } =
            string.Empty;
    }

    public sealed class TipoAnalisisSueloRespuestaDto
    {
        public int tipoAnalisisSueloId { get; set; }

        public string codigoTipoAnalisisSuelo { get; set; } =
            string.Empty;

        public string nombreTipoAnalisisSuelo { get; set; } =
            string.Empty;

        public string descripcionTipoAnalisisSuelo { get; set; } =
            string.Empty;

        public bool activo { get; set; }

        public int cantidadAnalisis { get; set; }

        public bool esTipoSistema { get; set; }

        public bool puedeEliminar { get; set; }
    }

    public sealed class TipoAnalisisSueloPaginaResponse
    {
        public List<TipoAnalisisSueloRespuestaDto> Items { get; set; } =
            new();

        public int PaginaActual { get; set; }

        public int TamanoPagina { get; set; }

        public int TotalRegistros { get; set; }

        public int TotalPaginas { get; set; }
    }
}
