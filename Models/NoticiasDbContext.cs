using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Models
{
    public sealed class NoticiasDbContext : DbContext
    {
        public NoticiasDbContext(
            DbContextOptions<NoticiasDbContext> options)
            : base(options)
        {
        }

        public DbSet<CategoriaPublicacion> CategoriasPublicacion =>
            Set<CategoriaPublicacion>();

        public DbSet<Publicacion> Publicaciones =>
            Set<Publicacion>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<CategoriaPublicacion>(entity =>
            {
                entity.ToTable("categoriaPublicacion", "dbo");
                entity.HasKey(x => x.categoriaPublicacionId);
                entity.HasIndex(x => x.nombreCategoriaPublicacion)
                    .IsUnique();

                entity.Property(x => x.nombreCategoriaPublicacion)
                    .HasMaxLength(80)
                    .IsRequired();

                entity.Property(x => x.descripcionCategoriaPublicacion)
                    .HasMaxLength(250);

                entity.Property(x => x.colorHex)
                    .HasMaxLength(7)
                    .IsRequired();

                entity.Property(x => x.activo)
                    .HasDefaultValue(true)
                    .IsRequired();
            });

            modelBuilder.Entity<Publicacion>(entity =>
            {
                entity.ToTable("publicacion", "dbo");
                entity.HasKey(x => x.publicacionId);

                entity.HasIndex(x => new
                {
                    x.activo,
                    x.estadoPublicacion,
                    x.fechaInicioPublicacionUtc
                });

                entity.HasIndex(x => new
                {
                    x.destacada,
                    x.fechaInicioPublicacionUtc
                });

                entity.Property(x => x.titulo)
                    .HasMaxLength(180)
                    .IsRequired();

                entity.Property(x => x.resumen)
                    .HasMaxLength(500)
                    .IsRequired();

                entity.Property(x => x.contenido)
                    .HasColumnType("nvarchar(max)")
                    .IsRequired();

                entity.Property(x => x.rutaImagenPortada)
                    .HasMaxLength(500);

                entity.Property(x => x.enlaceExterno)
                    .HasMaxLength(1000);

                entity.Property(x => x.textoEnlace)
                    .HasMaxLength(120);

                entity.Property(x => x.ubicacion)
                    .HasMaxLength(300);

                entity.Property(x => x.estadoPublicacion)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.activo)
                    .HasDefaultValue(true)
                    .IsRequired();

                entity.HasOne(x => x.CategoriaPublicacion)
                    .WithMany(x => x.Publicaciones)
                    .HasForeignKey(x => x.categoriaPublicacionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
