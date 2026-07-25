using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public sealed class CrearElementoQuimicoDto
    {
        [Required]
        [MaxLength(10)]
        public string simboloElementoQuimico { get; set; } =
            string.Empty;

        [Required]
        [MaxLength(100)]
        public string nombreElementoQuimico { get; set; } =
            string.Empty;

        [Range(
            typeof(decimal),
            "0.01",
            "99999999.99",
            ErrorMessage =
                "El peso equivalente debe estar entre 0.01 y 99999999.99.")]
        public decimal pesoEquivalenteElementoQuimico { get; set; }
    }

    public sealed class EditarElementoQuimicoDto
    {
        [Range(1, int.MaxValue)]
        public int elementoQuimicosId { get; set; }

        [Required]
        [MaxLength(10)]
        public string simboloElementoQuimico { get; set; } =
            string.Empty;

        [Required]
        [MaxLength(100)]
        public string nombreElementoQuimico { get; set; } =
            string.Empty;

        [Range(
            typeof(decimal),
            "0.01",
            "99999999.99",
            ErrorMessage =
                "El peso equivalente debe estar entre 0.01 y 99999999.99.")]
        public decimal pesoEquivalenteElementoQuimico { get; set; }
    }

    public sealed class ElementoQuimicoRespuestaDto
    {
        public int elementoQuimicosId { get; set; }

        public string simboloElementoQuimico { get; set; } =
            string.Empty;

        public string nombreElementoQuimico { get; set; } =
            string.Empty;

        public decimal pesoEquivalenteElementoQuimico { get; set; }

        public bool activo { get; set; }
    }

    public sealed class ElementoQuimicoPaginaResponse
    {
        public List<ElementoQuimicoRespuestaDto> Items { get; set; } =
            new();

        public int PaginaActual { get; set; }

        public int TamanoPagina { get; set; }

        public int TotalRegistros { get; set; }

        public int TotalPaginas { get; set; }
    }
}
