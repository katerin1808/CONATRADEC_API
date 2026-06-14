namespace CONATRADEC_API.DTOs
{
    public class FotoTerrenoDto
    {

        public class FotoTerrenoCrearDto
        {
            public int terrenoId { get; set; }
            public List<string> urlsFotoTerreno { get; set; } = new();
        }

        public class FotoTerrenoEditarDto
        {
            public string urlFotoTerreno { get; set; } = null!;
        }

        public class FotoTerrenoListarDto
        {
            public int fotoTerrenoId { get; set; }
            public string urlFotoTerreno { get; set; } = null!;
            public int terrenoId { get; set; }
        }

        public class FotoTerrenoDetalleDto
        {
            public int fotoTerrenoId { get; set; }
            public string urlFotoTerreno { get; set; } = null!;
            public bool activo { get; set; }
            public int terrenoId { get; set; }

            public string codigoTerreno { get; set; } = null!;
            public string nombrePropietarioTerreno { get; set; } = null!;
        }
    }
}
