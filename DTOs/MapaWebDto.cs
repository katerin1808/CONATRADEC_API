namespace CONATRADEC_API.DTOs
{
    /// <summary>
    /// DTOs livianos para representar terrenos en el mapa administrativo.
    /// No incluyen fotografías ni objetos completos del análisis.
    /// </summary>
    public static class MapaWebDto
    {
        public sealed class TerrenoMapaDto
        {
            public int terrenoId { get; set; }
            public string codigo { get; set; } = string.Empty;

            /*
             * Actualmente Terreno no posee un campo separado para nombre de
             * finca. Se utiliza la dirección como descripción visible.
             */
            public string nombre { get; set; } = string.Empty;

            public string productor { get; set; } = string.Empty;
            public decimal latitud { get; set; }
            public decimal longitud { get; set; }
            public string departamento { get; set; } = string.Empty;
            public string municipio { get; set; } = string.Empty;
            public decimal extensionManzanas { get; set; }
            public string estado { get; set; } = "Registrado";
            public decimal? ultimoPh { get; set; }
            public DateTime? fechaUltimoAnalisis { get; set; }
        }
    }
}
