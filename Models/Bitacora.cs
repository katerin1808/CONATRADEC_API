using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    [Table("bitacora", Schema = "dbo")]
    public sealed class Bitacora
    {
        [Key]
        public Guid bitacoraId { get; set; }

        public DateTime fechaHoraUtc { get; set; }
        public int? usuarioId { get; set; }

        [MaxLength(150)]
        public string usuarioNombre { get; set; } = string.Empty;

        [MaxLength(100)]
        public string rolNombre { get; set; } = string.Empty;

        [MaxLength(120)]
        public string modulo { get; set; } = string.Empty;

        [MaxLength(50)]
        public string accion { get; set; } = string.Empty;

        [MaxLength(10)]
        public string metodoHttp { get; set; } = string.Empty;

        [MaxLength(500)]
        public string endpoint { get; set; } = string.Empty;

        [MaxLength(500)]
        public string paginaOrigen { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string descripcion { get; set; } = string.Empty;

        public string parametros { get; set; } = string.Empty;

        [MaxLength(100)]
        public string direccionIp { get; set; } = string.Empty;

        [MaxLength(200)]
        public string dispositivo { get; set; } = string.Empty;

        [MaxLength(100)]
        public string plataforma { get; set; } = string.Empty;

        [MaxLength(50)]
        public string versionApp { get; set; } = string.Empty;

        [MaxLength(100)]
        public string correlationId { get; set; } = string.Empty;

        public int codigoEstado { get; set; }
        public bool exitoso { get; set; }
        public long duracionMs { get; set; }
        public string error { get; set; } = string.Empty;

        public ICollection<BitacoraDetalle> detalles { get; set; } =
            new List<BitacoraDetalle>();
    }
}
