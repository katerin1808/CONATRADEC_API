using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Models
{
    public sealed class ActualizacionesDbContext : DbContext
    {
        public ActualizacionesDbContext(
            DbContextOptions<ActualizacionesDbContext> options)
            : base(options)
        {
        }

        public DbSet<ActualizacionAplicacion> ActualizacionesAplicacion =>
            Set<ActualizacionAplicacion>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ActualizacionAplicacion>(entity =>
            {
                entity.ToTable("actualizacionAplicacion", "dbo");

                entity.HasKey(x => x.ActualizacionAplicacionId);

                entity.Property(x => x.Plataforma)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.Canal)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.VersionNombre)
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.NotasVersion)
                    .HasMaxLength(4000)
                    .IsRequired();

                entity.Property(x => x.Estado)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.NombreArchivo)
                    .HasMaxLength(260)
                    .IsRequired();

                entity.Property(x => x.NombreArchivoAlmacenado)
                    .HasMaxLength(260)
                    .IsRequired();

                entity.Property(x => x.RutaArchivo)
                    .HasMaxLength(700)
                    .IsRequired();

                entity.Property(x => x.TipoContenido)
                    .HasMaxLength(150)
                    .IsRequired();

                entity.Property(x => x.HashSha256)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(x => x.FechaCreacionUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.FechaUltimaModificacionUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.FechaPublicacionUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.Activo)
                    .HasDefaultValue(true)
                    .IsRequired();

                entity.HasIndex(x => new
                {
                    x.Plataforma,
                    x.Canal,
                    x.VersionCodigo
                }).IsUnique();

                entity.HasIndex(x => new
                {
                    x.Plataforma,
                    x.Canal,
                    x.Estado,
                    x.Activo
                });
            });
        }
    }
}
