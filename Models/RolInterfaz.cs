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
        public string nombreInterfaz { get; set; } = "";
        public string  descripcionInterfaz { get; set; } = "";
        public bool activo { get; set; } = true;

        //public ICollection<RolInteraz> rolInteraz { get; set; } = new List<RolInteraz>();
    }

    [Table("rolInteraz", Schema = "dbo")]
    public class RolInteraz
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]

        public int rolInterazId { get; set; }

        public bool? leer{ get; set; }

        public bool? agregar { get; set; }

        public bool? actualizar { get; set; }

        public bool? eliminar { get; set; } 

        public int rolId { get; set; }
        public int interfazId { get; set; }


    }

}

