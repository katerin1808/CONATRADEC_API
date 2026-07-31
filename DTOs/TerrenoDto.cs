using System.ComponentModel.DataAnnotations;

namespace CONATRADEC_API.DTOs
{
    public class TerrenoDto
    {
        public abstract class TerrenoGuardarBaseDto
        {
            [Range(1, int.MaxValue)]
            public int propietarioId { get; set; }

            [Required, MaxLength(300)]
            public string direccionTerreno { get; set; } = string.Empty;

            [Range(typeof(decimal), "0.01", "9999999999")]
            public decimal extensionManzanaTerreno { get; set; }

            [Range(1, int.MaxValue)]
            public int municipioId { get; set; }

            [Range(typeof(decimal), "0", "9999999999")]
            public decimal cantidadQuintalesOro { get; set; }

            [Range(0, int.MaxValue)]
            public int cantidadPlantasTerreno { get; set; }

            [Range(typeof(decimal), "-90", "90")]
            public decimal latitud { get; set; }

            [Range(typeof(decimal), "-180", "180")]
            public decimal longitud { get; set; }
        }

        public sealed class TerrenoCrearDto : TerrenoGuardarBaseDto
        {
            public string? codigoTerreno { get; set; }
            public DateOnly? fechaIngresoTerreno { get; set; }
        }

        public sealed class TerrenoEditarDto : TerrenoGuardarBaseDto
        {
            public string? codigoTerreno { get; set; }
            public DateOnly? fechaIngresoTerreno { get; set; }
        }

        public sealed class TerrenoUbicacionDto
        {
            public int paisId { get; set; }
            public string nombrePais { get; set; } = string.Empty;
            public int departamentoId { get; set; }
            public string nombreDepartamento { get; set; } = string.Empty;
            public int municipioId { get; set; }
            public string nombreMunicipio { get; set; } = string.Empty;
        }

        public sealed class TerrenoPropietarioDto
        {
            public int propietarioId { get; set; }
            public string identificacion { get; set; } = string.Empty;
            public string nombreCompleto { get; set; } = string.Empty;
            public string? telefono { get; set; }
            public string? correo { get; set; }
            public string? direccion { get; set; }
        }

        public sealed class TerrenoListarDto
        {
            public int terrenoId { get; set; }
            public string codigoTerreno { get; set; } = string.Empty;
            public int? propietarioId { get; set; }
            public TerrenoPropietarioDto? propietario { get; set; }
            public string direccionTerreno { get; set; } = string.Empty;
            public decimal extensionManzanaTerreno { get; set; }
            public DateOnly fechaIngresoTerreno { get; set; }
            public int cantidadPlantasTerreno { get; set; }
            public int municipioId { get; set; }
            public decimal cantidadQuintalesOro { get; set; }
            public decimal latitud { get; set; }
            public decimal longitud { get; set; }
            public bool activo { get; set; }
            public TerrenoUbicacionDto ubicacion { get; set; } = new();
        }
    }
}
