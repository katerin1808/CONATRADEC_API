using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{

    [Table("departamento")]
    public class Departamento
    {


        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int DepartamentoId { get; set; }

        [Required, MaxLength(80)]
        public string NombreDepartamento { get; set; } = string.Empty;

        public bool Activo { get; set; } = true;

        // FK REQUERIDA → Pais
        [ForeignKey(nameof(Pais))]
        public int PaisId { get; set; }
        public Pais Pais { get; set; } = null!;

        public ICollection<Municipio> Municipios { get; set; } = new List<Municipio>();
    }
}
