using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("interfaz", Schema = "dbo")]
    public class Interfaz
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int interfazId { get; set; }

        /// <summary>
        /// Código interno estable utilizado para validar permisos.
        /// Ejemplos: MainPage, userPage, terrenoPage.
        /// No debe mostrarse como título principal al usuario.
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string nombreInterfaz { get; set; } = string.Empty;

        /// <summary>
        /// Nombre legible mostrado en la matriz de permisos.
        /// Ejemplos: Análisis de suelo, Usuarios, Terrenos.
        /// </summary>
        [Required]
        [MaxLength(120)]
        public string nombreAmigableInterfaz { get; set; } = string.Empty;

        [MaxLength(250)]
        public string descripcionInterfaz { get; set; } = string.Empty;

        public bool activo { get; set; } = true;

        public ICollection<RolInterfaz> RolInterfaz { get; set; } =
            new List<RolInterfaz>();
    }

    [Table("rolInterfaz", Schema = "dbo")]
    public class RolInterfaz
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int rolInterfazId { get; set; }

        public bool? leer { get; set; }
        public bool? agregar { get; set; }
        public bool? actualizar { get; set; }
        public bool? eliminar { get; set; }

        public int rolId { get; set; }
        public int interfazId { get; set; }

        public Rol Rol { get; set; } = null!;
        public Interfaz Interfaz { get; set; } = null!;
    }
}
