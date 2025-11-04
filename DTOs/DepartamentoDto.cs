using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CONATRADEC_API.DTOs
{
    public class DepartamentoDto
    {

        public class DepartamentoCreateRequest
        {
            [Required, MaxLength(80)]
            public string NombreDepartamento { get; set; } = string.Empty;

            [Required]
            public int PaisId { get; set; }
        }

        public class DepartamentoUpdateRequest
        {
            [Required, MaxLength(80)]
            public string NombreDepartamento { get; set; } = string.Empty;

           
        }

        public class DepartamentoResponse
        {
            public int DepartamentoId { get; set; }
            public string NombreDepartamento { get; set; } = string.Empty;
            [JsonIgnore] // 👈 Oculta el campo al serializar a JSON (Swagger, respuesta HTTP)
            public int PaisId { get; set; }
            public string NombrePais { get; set; } = string.Empty;
            public bool Activo { get; set; }
        }
    }
}
