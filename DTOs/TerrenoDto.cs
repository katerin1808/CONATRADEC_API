namespace CONATRADEC_API.DTOs
{
    public class TerrenoDto
    {
        public class TerrenoCrearDto
        {
            // Se conserva opcional por compatibilidad con versiones anteriores
            // del frontend. La API no utiliza este valor al crear el terreno.
            public string? codigoTerreno { get; set; }

            public string identificacionPropietarioTerreno { get; set; } = null!;
            public string nombrePropietarioTerreno { get; set; } = null!;
            public int telefonoPropietario { get; set; }
            public string? correoPropietario { get; set; }
            public string direccionTerreno { get; set; } = null!;
            public decimal extensionManzanaTerreno { get; set; }
            public DateOnly fechaIngresoTerreno { get; set; }
            public int cantidadPlantasTerreno { get; set; }
            public int municipioId { get; set; }
            public decimal cantidadQuintalesOro { get; set; }
            public decimal latitud { get; set; }
            public decimal longitud { get; set; }
        }

        public class TerrenoEditarDto
        {
            // Se acepta para no romper clientes antiguos, pero nunca se aplica.
            // El código del terreno es inmutable una vez generado.
            public string? codigoTerreno { get; set; }

            public string identificacionPropietarioTerreno { get; set; } = null!;
            public string nombrePropietarioTerreno { get; set; } = null!;
            public int telefonoPropietario { get; set; }
            public string? correoPropietario { get; set; }
            public string direccionTerreno { get; set; } = null!;
            public decimal extensionManzanaTerreno { get; set; }
            public DateOnly fechaIngresoTerreno { get; set; }
            public int cantidadPlantasTerreno { get; set; }
            public int municipioId { get; set; }
            public decimal cantidadQuintalesOro { get; set; }
            public decimal latitud { get; set; }
            public decimal longitud { get; set; }
        }

        public class TerrenoUbicacionDto
        {
            public int paisId { get; set; }
            public string nombrePais { get; set; } = null!;
            public int departamentoId { get; set; }
            public string nombreDepartamento { get; set; } = null!;
            public int municipioId { get; set; }
            public string nombreMunicipio { get; set; } = null!;
        }

        public class TerrenoListarDto
        {
            public int terrenoId { get; set; }
            public string codigoTerreno { get; set; } = null!;
            public string identificacionPropietarioTerreno { get; set; } = null!;
            public string nombrePropietarioTerreno { get; set; } = null!;
            public int telefonoPropietario { get; set; }
            public string? correoPropietario { get; set; }
            public string direccionTerreno { get; set; } = null!;
            public decimal extensionManzanaTerreno { get; set; }
            public DateOnly fechaIngresoTerreno { get; set; }
            public int cantidadPlantasTerreno { get; set; }
            public int municipioId { get; set; }
            public decimal cantidadQuintalesOro { get; set; }
            public decimal latitud { get; set; }
            public decimal longitud { get; set; }
            public TerrenoUbicacionDto ubicacion { get; set; } = null!;
        }
    }
}
