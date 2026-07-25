using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public class PaisDto
    {
        public sealed class PaisRequest
        {
            [Required]
            [MaxLength(80)]
            public string NombrePais { get; set; } = string.Empty;

            [Required]
            [RegularExpression(
                @"^[A-Za-z]{3}$",
                ErrorMessage =
                    "El código ISO debe contener exactamente 3 letras.")]
            public string CodigoISOPais { get; set; } = string.Empty;
        }

        public sealed class PaisResponse
        {
            public int PaisId { get; set; }
            public string NombrePais { get; set; } = string.Empty;
            public string CodigoISOPais { get; set; } = string.Empty;
            public bool Activo { get; set; }
            public int CantidadDepartamentos { get; set; }
        }

        public sealed class PaisPaginaResponse
        {
            public List<PaisResponse> Items { get; set; } = new();
            public int PaginaActual { get; set; }
            public int TamanoPagina { get; set; }
            public int TotalRegistros { get; set; }
            public int TotalPaginas { get; set; }
        }
    }
}
