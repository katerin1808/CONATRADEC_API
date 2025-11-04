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

        [Required, MaxLength(100)]
        public string descripcionInterfaz { get; set; } = null!;

        public bool activo { get; set; } = true;

        // Relación con RolInterfaz
        public ICollection<RolInterfaz> rolinterfaz { get; set; } = new List<RolInterfaz>();
    }

    [Table("rolInteraz", Schema = "dbo")]
    public class RolInterfaz
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int rolInterazId { get; set; }

        public bool leer { get; set; } = false;
        public bool agregar { get; set; } = false;
        public bool actualizar { get; set; } = false;
        public bool eliminar { get; set; } = false;

        public int rolId { get; set; }
        public int interfazId { get; set; }

        public Rol Rol { get; set; } = null!;
        public Interfaz Interfaz{ get; set; } = null!; // 👈 se mantiene como tú lo tenías
    }

}

