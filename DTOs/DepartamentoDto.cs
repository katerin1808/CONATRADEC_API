using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public class DepartamentoDto
    {
        public sealed class DepartamentoCreateRequest
        {
            [Required]
            [MaxLength(80)]
            public string NombreDepartamento { get; set; } =
                string.Empty;

            [Required]
            [Range(
                1,
                int.MaxValue,
                ErrorMessage =
                    "Debe indicar un país válido.")]
            public int PaisId { get; set; }
        }

        public sealed class DepartamentoUpdateRequest
        {
            [Required]
            [MaxLength(80)]
            public string NombreDepartamento { get; set; } =
                string.Empty;
        }

        public sealed class DepartamentoResponse
        {
            public int DepartamentoId { get; set; }

            public string NombreDepartamento { get; set; } =
                string.Empty;

            public int PaisId { get; set; }

            public string NombrePais { get; set; } =
                string.Empty;

            public bool Activo { get; set; }

            public int CantidadMunicipios { get; set; }
        }

        public sealed class DepartamentoPaginaResponse
        {
            public List<DepartamentoResponse> Items { get; set; } =
                new();

            public int PaginaActual { get; set; }

            public int TamanoPagina { get; set; }

            public int TotalRegistros { get; set; }

            public int TotalPaginas { get; set; }

            public int PaisId { get; set; }

            public string NombrePais { get; set; } =
                string.Empty;
        }

        public sealed class ConteoPaginadoRequest
        {
            public bool ContarIntervalo { get; set; }

            public int Inicio { get; set; }

            public int Fin { get; set; }

            public int PageSize { get; set; } = 20;
        }
    }
}
