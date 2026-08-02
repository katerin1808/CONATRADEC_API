using Microsoft.AspNetCore.Http;
namespace CONATRADEC_API.DTOs
{
    public class FotoTerrenoDto
    {
        public class FotoTerrenoCrearDto
        {
            public int terrenoId { get; set; }
            public List<IFormFile> fotos { get; set; } = new();
            public string? titulo { get; set; }
            public string? descripcion { get; set; }
            public DateTime? fechaCaptura { get; set; }
            public bool establecerComoPortada { get; set; }
        }

        public class FotoTerrenoEditarDto
        {
            public IFormFile? foto { get; set; }
            public string? titulo { get; set; }
            public string? descripcion { get; set; }
            public DateTime? fechaCaptura { get; set; }
            public bool? establecerComoPortada { get; set; }
        }

        public class FotoTerrenoListarDto
        {
            public int fotoTerrenoId { get; set; }
            public string urlFotoTerreno { get; set; } = string.Empty;
            public int terrenoId { get; set; }
            public string titulo { get; set; } = string.Empty;
            public string descripcion { get; set; } = string.Empty;
            public string nombreArchivoOriginal { get; set; } = string.Empty;
            public DateTime fechaRegistroUtc { get; set; }
            public DateTime? fechaCaptura { get; set; }
            public bool esPortada { get; set; }
            public bool activo { get; set; }
        }

        public class FotoTerrenoDetalleDto
        {
            public int fotoTerrenoId { get; set; }
            public string urlFotoTerreno { get; set; } = string.Empty;
            public bool activo { get; set; }
            public int terrenoId { get; set; }
            public string codigoTerreno { get; set; } = string.Empty;
            public string titulo { get; set; } = string.Empty;
            public string descripcion { get; set; } = string.Empty;
            public DateTime fechaRegistroUtc { get; set; }
            public DateTime? fechaCaptura { get; set; }
            public bool esPortada { get; set; }
            public TerrenoDto.TerrenoPropietarioDto? propietario { get; set; }
        }
    }
}
