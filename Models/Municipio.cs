using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{

    [Table("municipio")]
    public class Municipio
    {

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("municipioId")]
        public int MunicipioId { get; set; }

        [Required, MaxLength(80)]
        [Column("nombreMunicipio")]
        public string NombreMunicipio { get; set; } = string.Empty;

        [Required]
        [Column("activo")]
        public bool Activo { get; set; }

        // FK → departamento
        [Required]
        [Column("departamentoId")]
        public int DepartamentoId { get; set; }
        public Departamento Departamento { get; set; } = null!;
    }
}
