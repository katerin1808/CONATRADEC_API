using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs;

public sealed class ReglaAgricolaClimaDto
{
    public int ReglaAgricolaClimaId { get; set; }

    public string Clave { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;

    public string Descripcion { get; set; } = string.Empty;

    public string Icono { get; set; } = "fa-solid fa-seedling";

    public int Orden { get; set; }

    public bool Activo { get; set; }

    public int? ProbabilidadLluviaMaxima { get; set; }

    public decimal? PrecipitacionMaximaMm { get; set; }

    public decimal? VientoMaximoKmh { get; set; }

    public decimal? RafagaMaximaKmh { get; set; }

    public decimal? TemperaturaMinimaC { get; set; }

    public decimal? TemperaturaMaximaC { get; set; }

    public decimal? HumedadMinimaPct { get; set; }

    public decimal? HumedadMaximaPct { get; set; }

    public decimal? IndiceUvMaximo { get; set; }

    public bool BloquearTormentaMedia { get; set; }

    public int DuracionMinimaHoras { get; set; }

    public string MensajeFavorable { get; set; } = string.Empty;

    public string MensajeNoFavorable { get; set; } = string.Empty;

    public DateTime FechaRegistroUtc { get; set; }

    public DateTime? FechaActualizacionUtc { get; set; }
}

public sealed class ReglaAgricolaClimaGuardarDto
{
    [Required, MaxLength(60)]
    public string Clave { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Nombre { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Descripcion { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Icono { get; set; } = "fa-solid fa-seedling";

    [Range(0, 9999)]
    public int Orden { get; set; }

    public bool Activo { get; set; } = true;

    [Range(0, 100)]
    public int? ProbabilidadLluviaMaxima { get; set; }

    [Range(typeof(decimal), "0", "1000")]
    public decimal? PrecipitacionMaximaMm { get; set; }

    [Range(typeof(decimal), "0", "300")]
    public decimal? VientoMaximoKmh { get; set; }

    [Range(typeof(decimal), "0", "400")]
    public decimal? RafagaMaximaKmh { get; set; }

    [Range(typeof(decimal), "-20", "60")]
    public decimal? TemperaturaMinimaC { get; set; }

    [Range(typeof(decimal), "-20", "60")]
    public decimal? TemperaturaMaximaC { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? HumedadMinimaPct { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? HumedadMaximaPct { get; set; }

    [Range(typeof(decimal), "0", "20")]
    public decimal? IndiceUvMaximo { get; set; }

    public bool BloquearTormentaMedia { get; set; } = true;

    [Range(1, 24)]
    public int DuracionMinimaHoras { get; set; } = 3;

    [Required, MaxLength(300)]
    public string MensajeFavorable { get; set; } =
        "Condiciones adecuadas para realizar la labor.";

    [Required, MaxLength(300)]
    public string MensajeNoFavorable { get; set; } =
        "Conviene reprogramar la labor por las condiciones previstas.";
}
