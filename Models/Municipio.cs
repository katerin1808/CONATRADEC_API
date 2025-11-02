using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{

    [Table("municipio")]
    public class Municipio
    {

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int MunicipioId { get; set; }

        [Required, MaxLength(80)]
        public string NombreMunicipio { get; set; } = string.Empty;

        public bool Activo { get; set; } = true;

        // FK REQUERIDA → Departamento
        [ForeignKey(nameof(Departamento))]
        public int DepartamentoId { get; set; }
        public Departamento Departamento{ get; set; } = null!;
    }
}
