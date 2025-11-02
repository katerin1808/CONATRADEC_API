using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{



    [Table("interfaz", Schema = "dbo")]

    public class Interfaz
    {


        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int interfazId { get; set; }

        [Required, MaxLength(100)]
        public string nombreInterfaz { get; set; } = null!;
        public string descripcionInterfaz { get; set; } = null!;

        public bool activo { get; set; } = true;

        public ICollection<RolInterfaz> rolinterfaz{ get; set; } = new List<RolInterfaz>();



    }


    [Table("RolInterfaz", Schema = "dbo")]
    public class RolInterfaz
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int rolInterfazId { get; set; }

        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }

        public int rolId { get; set; }
        public int interfazId { get; set; }

        public Rol Rol { get; set; } = null!;
        public Interfaz Interfaces { get; set; } = null!;  

    }
}
