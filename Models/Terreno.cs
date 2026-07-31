using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("terreno", Schema = "dbo")]
    [Index(
        nameof(codigoTerreno),
        IsUnique = true,
        Name = "UX_terreno_codigoTerreno")]
    public class Terreno
    {
        [Key]
        public int terrenoId { get; set; }

        [Required, MaxLength(50)]
        public string codigoTerreno { get; set; } = null!;

        [Required, MaxLength(300)]
        public string direccionTerreno { get; set; } = null!;

        [Column(TypeName = "decimal(12,2)")]
        public decimal extensionManzanaTerreno { get; set; }

        public DateOnly fechaIngresoTerreno { get; set; }

        public int cantidadPlantasTerreno { get; set; }

        public bool activo { get; set; }

        public int municipioId { get; set; }

        public Municipio Municipio { get; set; } = null!;

        [Column(TypeName = "decimal(12,2)")]
        public decimal cantidadQuintalesOro { get; set; }

        [Column(TypeName = "decimal(20,17)")]
        public decimal latitud { get; set; }

        [Column(TypeName = "decimal(20,17)")]
        public decimal longitud { get; set; }

        public virtual ICollection<FotoTerreno> FotosTerreno { get; set; }
            = new List<FotoTerreno>();

        public ICollection<PropietarioTerreno> RelacionesPropietario { get; set; }
            = new List<PropietarioTerreno>();
    }
}
