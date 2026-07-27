using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Models
{
    /// <summary>
    /// Contexto aislado para que los latidos no participen en la auditoría
    /// de cambios de los módulos funcionales del sistema.
    /// </summary>
    public sealed class DispositivosConexionDbContext : DbContext
    {
        public DispositivosConexionDbContext(
            DbContextOptions<DispositivosConexionDbContext> options)
            : base(options)
        {
        }

        public DbSet<DispositivoConexion> DispositivosConexion =>
            Set<DispositivoConexion>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DispositivoConexion>(entity =>
            {
                entity.ToTable("dispositivoConexion", "dbo");
                entity.HasKey(x => x.DispositivoConexionId);

                entity.HasIndex(x => x.InstalacionId)
                    .IsUnique()
                    .HasDatabaseName("UX_dispositivoConexion_instalacionId");

                entity.HasIndex(x => x.UltimoLatidoUtc)
                    .HasDatabaseName("IX_dispositivoConexion_ultimoLatidoUtc");

                entity.HasIndex(x => new
                    {
                        x.UsuarioId,
                        x.UltimoLatidoUtc
                    })
                    .HasDatabaseName(
                        "IX_dispositivoConexion_usuario_ultimoLatido");

                entity.HasIndex(x => x.FechaUbicacionUtc)
                    .HasDatabaseName(
                        "IX_dispositivoConexion_fechaUbicacionUtc");

                entity.Property(x => x.Latitud)
                    .HasPrecision(9, 6);

                entity.Property(x => x.Longitud)
                    .HasPrecision(9, 6);

                entity.Property(x => x.PrecisionMetros)
                    .HasPrecision(10, 2);

                entity.Property(x => x.FechaUbicacionUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.OrigenUbicacion)
                    .HasDefaultValue(string.Empty);

                entity.Property(x => x.EstadoPermisoUbicacion)
                    .HasDefaultValue("NO_REPORTADO");

                entity.Property(x => x.FechaRegistroUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.FechaInicioSesionUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.UltimoLatidoUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.FechaDesconexionUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.ConectadoReportado)
                    .HasDefaultValue(false);

                entity.Property(x => x.CantidadSesiones)
                    .HasDefaultValue(1);

                entity.Property(x => x.Activo)
                    .HasDefaultValue(true);
            });
        }
    }
}
