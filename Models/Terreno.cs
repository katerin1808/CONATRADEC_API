using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("terreno", Schema = "dbo")]
    public class Terreno
    {
        [Key]
        public int terrenoId { get; set; }

        [Required, MaxLength(50)]
        public string codigoTerreno { get; set; } = null!;

        [Required, MaxLength(50)]
        public string identificacionPropietarioTerreno { get; set; } = null!;

        [Required, MaxLength(150)]
        public string nombrePropietarioTerreno { get; set; } = null!;

        public int telefonoPropietario { get; set; }
        public string? correoPropietario { get; set; }

        [Required, MaxLength(300)]
        public string direccionTerreno { get; set; } = null!;

        public decimal extensionManzanaTerreno { get; set; }
        public DateOnly fechaIngresoTerreno { get; set; }

        public bool activo { get; set; }

        public int municipioId { get; set; }
        public Municipio Municipio { get; set; } = null!;

        public decimal cantidadQuintalesOro { get; set; }
        public decimal latitud { get; set; }
        public decimal longitud { get; set; }
    }
}


