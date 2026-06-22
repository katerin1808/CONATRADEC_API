namespace CONATRADEC_API.DTOs
{
    public class FotoTerrenoDto
    {

        public class FotoTerrenoCrearDto
        {
            public int terrenoId { get; set; }
            public List<IFormFile> fotos { get; set; } = new();
        }

        public class FotoTerrenoEditarDto
        {
            public IFormFile? foto { get; set; }
        }

        public class FotoTerrenoListarDto
        {
            public int fotoTerrenoId { get; set; }

            public string urlFotoTerreno { get; set; } = string.Empty;

            public int terrenoId { get; set; }
        }

        public class FotoTerrenoDetalleDto
        {
            public int fotoTerrenoId { get; set; }

            public string urlFotoTerreno { get; set; } = string.Empty;

            public bool activo { get; set; }

            public int terrenoId { get; set; }

            public string codigoTerreno { get; set; } = string.Empty;

            public string nombrePropietarioTerreno { get; set; } = string.Empty;
        }
    }
}
