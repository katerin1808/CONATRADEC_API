using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public class MunicipioDto
    {
        public sealed class MunicipioCreateRequest
        {
            [Required]
            [MaxLength(80)]
            public string NombreMunicipio { get; set; } = string.Empty;

            [Required]
            [Range(
                1,
                int.MaxValue,
                ErrorMessage = "Debe indicar un departamento válido.")]
            public int DepartamentoId { get; set; }
        }

        public sealed class MunicipioUpdateRequest
        {
            [Required]
            [MaxLength(80)]
            public string NombreMunicipio { get; set; } = string.Empty;
        }

        public sealed class MunicipioResponse
        {
            public int MunicipioId { get; set; }
            public string NombreMunicipio { get; set; } = string.Empty;
            public int DepartamentoId { get; set; }
            public string NombreDepartamento { get; set; } = string.Empty;
            public int PaisId { get; set; }
            public string NombrePais { get; set; } = string.Empty;
            public bool Activo { get; set; }
            public int CantidadTerrenos { get; set; }
            public int CantidadUsuarios { get; set; }
        }

        public sealed class MunicipioPaginaResponse
        {
            public List<MunicipioResponse> Items { get; set; } = new();
            public int PaginaActual { get; set; }
            public int TamanoPagina { get; set; }
            public int TotalRegistros { get; set; }
            public int TotalPaginas { get; set; }
            public int DepartamentoId { get; set; }
            public string NombreDepartamento { get; set; } = string.Empty;
            public int PaisId { get; set; }
            public string NombrePais { get; set; } = string.Empty;
        }

        internal sealed class DepartamentoUbicacionResponse
        {
            public int DepartamentoId { get; set; }
            public string NombreDepartamento { get; set; } = string.Empty;
            public int PaisId { get; set; }
            public string NombrePais { get; set; } = string.Empty;
        }
    }
}
