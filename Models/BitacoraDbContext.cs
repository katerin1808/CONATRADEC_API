using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Models
{
    public sealed class BitacoraDbContext : DbContext
    {
        public BitacoraDbContext(
            DbContextOptions<BitacoraDbContext> options)
            : base(options)
        {
        }

        public DbSet<Bitacora> Bitacoras => Set<Bitacora>();
        public DbSet<BitacoraDetalle> BitacoraDetalles => Set<BitacoraDetalle>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Bitacora>(entity =>
            {
                entity.HasKey(x => x.bitacoraId);
                entity.Property(x => x.fechaHoraUtc).HasColumnType("datetime2(3)");
                entity.Property(x => x.parametros).HasColumnType("nvarchar(max)");
                entity.Property(x => x.error).HasColumnType("nvarchar(max)");
                entity.HasIndex(x => x.fechaHoraUtc);
                entity.HasIndex(x => x.usuarioId);
                entity.HasIndex(x => new { x.modulo, x.accion });
            });

            modelBuilder.Entity<BitacoraDetalle>(entity =>
            {
                entity.HasKey(x => x.bitacoraDetalleId);
                entity.Property(x => x.fechaHoraUtc).HasColumnType("datetime2(3)");
                entity.Property(x => x.valoresAnteriores).HasColumnType("nvarchar(max)");
                entity.Property(x => x.valoresNuevos).HasColumnType("nvarchar(max)");
                entity.Property(x => x.propiedadesModificadas).HasColumnType("nvarchar(max)");

                entity.HasOne(x => x.bitacora)
                    .WithMany(x => x.detalles)
                    .HasForeignKey(x => x.bitacoraId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
