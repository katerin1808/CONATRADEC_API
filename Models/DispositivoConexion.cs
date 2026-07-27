using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    /// <summary>
    /// Conserva el último estado conocido de una instalación de la app MAUI.
    /// La conexión real se calcula usando UltimoLatidoUtc y una tolerancia.
    /// </summary>
    [Table("dispositivoConexion", Schema = "dbo")]
    public sealed class DispositivoConexion
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int DispositivoConexionId { get; set; }

        [Required, MaxLength(64)]
        public string InstalacionId { get; set; } = string.Empty;

        [Required, MaxLength(64)]
        public string SesionId { get; set; } = string.Empty;

        public int UsuarioId { get; set; }

        [Required, MaxLength(150)]
        public string UsuarioNombre { get; set; } = string.Empty;

        [MaxLength(150)]
        public string CorreoUsuario { get; set; } = string.Empty;

        [MaxLength(100)]
        public string RolNombre { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string Plataforma { get; set; } = string.Empty;

        [MaxLength(30)]
        public string TipoDispositivo { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Fabricante { get; set; } = string.Empty;

        [MaxLength(150)]
        public string Modelo { get; set; } = string.Empty;

        [MaxLength(150)]
        public string NombreDispositivo { get; set; } = string.Empty;

        [MaxLength(100)]
        public string SistemaOperativo { get; set; } = string.Empty;

        [MaxLength(50)]
        public string VersionSistema { get; set; } = string.Empty;

        [MaxLength(50)]
        public string VersionApp { get; set; } = string.Empty;

        [MaxLength(50)]
        public string BuildApp { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Idioma { get; set; } = string.Empty;

        [MaxLength(100)]
        public string TipoConexion { get; set; } = string.Empty;

        [MaxLength(500)]
        public string PaginaActual { get; set; } = string.Empty;

        [MaxLength(100)]
        public string DireccionIp { get; set; } = string.Empty;

        [MaxLength(500)]
        public string UserAgent { get; set; } = string.Empty;

        public DateTime FechaRegistroUtc { get; set; }

        public DateTime FechaInicioSesionUtc { get; set; }

        public DateTime UltimoLatidoUtc { get; set; }

        public DateTime? FechaDesconexionUtc { get; set; }

        public bool ConectadoReportado { get; set; }

        public int CantidadSesiones { get; set; }

        public bool Activo { get; set; } = true;
    }
}
