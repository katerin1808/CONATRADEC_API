using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs;

public static class TerrenoPoligonoDto
{
    public sealed class VerticeDto
    {
        [Range(typeof(decimal), "-90", "90")]
        public decimal Latitud { get; set; }

        [Range(typeof(decimal), "-180", "180")]
        public decimal Longitud { get; set; }
    }

    public sealed class GuardarDto
    {
        [Required]
        public List<VerticeDto> Vertices { get; set; } = [];
    }

    public sealed class RespuestaDto
    {
        public int TerrenoId { get; set; }
        public string CodigoTerreno { get; set; } = string.Empty;
        public bool TienePoligono { get; set; }

        // El punto principal del terreno nunca es sustituido por el polígono.
        public decimal LatitudPunto { get; set; }
        public decimal LongitudPunto { get; set; }

        public decimal ExtensionRegistradaManzanas { get; set; }
        public List<VerticeDto> Vertices { get; set; } = [];
        public decimal AreaMetrosCuadrados { get; set; }
        public decimal AreaHectareas { get; set; }
        public decimal AreaManzanasCalculada { get; set; }
        public decimal DiferenciaManzanas { get; set; }
        public decimal? DiferenciaPorcentaje { get; set; }
        public bool PuntoDentroPoligono { get; set; }
        public DateTime? FechaActualizacionUtc { get; set; }
    }
}
