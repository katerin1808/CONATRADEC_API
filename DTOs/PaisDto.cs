using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public class PaisDto
    {
        public class PaisRequest
        {
            [Required, MaxLength(80)]
            public string NombrePais { get; set; } = string.Empty;


            // ISO 3166-1 alpha-3: exactamente 3 letras
            [Required, RegularExpression(@"^[A-Za-z]{3}$", ErrorMessage = "El Código ISO debe tener exactamente 3 letras (A-Z).")]
            /*[StringLength(3, MinimumLength = 3, ErrorMessage = "El Código ISO debe tener exactamente 3 caracteres.")]*///lo deje comentariado porque repite el error -Ntreminio
            public string CodigoISOPais { get; set; } = string.Empty;

        }

        public class PaisResponse
        {
            public int PaisId { get; set; }
            public string NombrePais { get; set; } = string.Empty;
            public string CodigoISOPais { get; set; } = string.Empty;
            public bool Activo { get; set; }
        }
    }
}
