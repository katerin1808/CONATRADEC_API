using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CONATRADEC_API.DTOs
{
    public class MunicipioDto
    {

        public class MunicipioCreateRequest
        {
            [Required, MaxLength(80)]
            public string NombreMunicipio { get; set; } = string.Empty;

            [Required]
            public int DepartamentoId { get; set; }
        }

        public class MunicipioUpdateRequest
        {
            [Required, MaxLength(80)]
            public string NombreMunicipio { get; set; } = string.Empty;

            [Required]
            public int DepartamentoId { get; set; }
        }

        public class MunicipioResponse
        {
            public int MunicipioId { get; set; }
            public string NombreMunicipio { get; set; } = string.Empty;

            [JsonIgnore] // 👈 sigue existiendo internamente pero no se muestra
            public int DepartamentoId { get; set; }

            public string NombreDepartamento { get; set; } = string.Empty;
            public string NombrePais { get; set; } = string.Empty;

            public bool Activo { get; set; }
        }
    }
}
