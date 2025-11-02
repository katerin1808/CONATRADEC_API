using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("Pais", Schema = "dbo")]
    public class Pais
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PaisId { get; set; }

        [Required, MaxLength(80)]
        public string NombrePais { get; set; } = string.Empty;

        [Required, StringLength(3)]
        public string CodigoISOPais { get; set; } = string.Empty; // e.g., NIC, USA

        public bool Activo { get; set; } = true;

        public ICollection<Departamento> Departamentos { get; set; } = new List<Departamento>();
    }
}
