using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{


    [Table("Permiso", Schema = "dbo")]
    public class Permiso
    {
        [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int permisoId { get; set; }

        [Required, MaxLength(100)]
        public string nombrePermiso { get; set; } = null!;

        public bool activo { get; set; } = true;

        public ICollection<RolPermiso> rolPermisos { get; set; } = new List<RolPermiso>();
    }

    [Table("RolPermiso", Schema = "dbo")]
    public class RolPermiso
    {
        // PK compuesta (se configura en OnModelCreating)
        [ForeignKey(nameof(Rol))] public int rolId { get; set; }
        [ForeignKey(nameof(Permiso))] public int permisoId { get; set; }

        public bool leer { get; set; }
        public bool agregar { get; set; }
        public bool actualizar { get; set; }
        public bool eliminar { get; set; }

        public Rol Rol { get; set; } = null!;
        public Permiso Permiso { get; set; } = null!;


    }
}
