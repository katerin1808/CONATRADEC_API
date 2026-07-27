using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    public sealed class AlertasAgricolasDbContext : DbContext
    {
        public AlertasAgricolasDbContext(
            DbContextOptions<AlertasAgricolasDbContext> options)
            : base(options)
        {
        }

        public DbSet<ConfiguracionAlertaAgricola> Configuraciones => Set<ConfiguracionAlertaAgricola>();
        public DbSet<SeguimientoAlertaAgricola> Seguimientos => Set<SeguimientoAlertaAgricola>();
        public DbSet<HistorialAlertaAgricola> Historial => Set<HistorialAlertaAgricola>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ConfiguracionAlertaAgricola>()
                .HasIndex(x => x.Clave)
                .IsUnique();

            modelBuilder.Entity<SeguimientoAlertaAgricola>()
                .HasIndex(x => new { x.TerrenoId, x.TipoAlerta, x.Activo });

            modelBuilder.Entity<HistorialAlertaAgricola>()
                .HasIndex(x => new { x.SeguimientoAlertaAgricolaId, x.FechaUtc });
        }
    }

    [Table("configuracionAlertaAgricola", Schema = "dbo")]
    public sealed class ConfiguracionAlertaAgricola
    {
        [Key]
        public int ConfiguracionAlertaAgricolaId { get; set; }

        [Required, MaxLength(80)]
        public string Clave { get; set; } = string.Empty;

        [Required, MaxLength(120)]
        public string Nombre { get; set; } = string.Empty;

        [Column(TypeName = "decimal(12,4)")]
        public decimal Valor { get; set; }

        [Required, MaxLength(30)]
        public string Operador { get; set; } = string.Empty;

        [MaxLength(30)]
        public string Unidad { get; set; } = string.Empty;

        [MaxLength(300)]
        public string Descripcion { get; set; } = string.Empty;

        public bool Activo { get; set; } = true;
        public DateTime FechaModificacionUtc { get; set; }
        public int? UsuarioModificacionId { get; set; }
    }

    [Table("seguimientoAlertaAgricola", Schema = "dbo")]
    public sealed class SeguimientoAlertaAgricola
    {
        [Key]
        public int SeguimientoAlertaAgricolaId { get; set; }

        public int TerrenoId { get; set; }

        [Required, MaxLength(80)]
        public string TipoAlerta { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string Nivel { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string Estado { get; set; } = "PENDIENTE";

        public int? UsuarioAsignadoId { get; set; }

        [MaxLength(1000)]
        public string Observacion { get; set; } = string.Empty;

        public DateTime FechaCreacionUtc { get; set; }
        public DateTime FechaUltimaModificacionUtc { get; set; }
        public DateTime? FechaCierreUtc { get; set; }

        public int UsuarioCreacionId { get; set; }
        public int UsuarioUltimaModificacionId { get; set; }
        public bool Activo { get; set; } = true;
    }

    [Table("historialAlertaAgricola", Schema = "dbo")]
    public sealed class HistorialAlertaAgricola
    {
        [Key]
        public int HistorialAlertaAgricolaId { get; set; }

        public int SeguimientoAlertaAgricolaId { get; set; }

        [Required, MaxLength(40)]
        public string Accion { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Detalle { get; set; } = string.Empty;

        public int UsuarioId { get; set; }
        public DateTime FechaUtc { get; set; }
    }
}
