using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.Models.RolPermiso;

namespace CONATRADEC_API.Models
{
    public class RolContext: DbContext  
    {
        public RolContext(DbContextOptions<RolContext> options) : base(options)
        {
        }

        public DbSet<Rol> Roles { get; set; }
        public DbSet<Cargo> Cargos { get; set; } // 👈 agregamos la tabla Cargo
        public DbSet<Permiso> Permisos { get; set; } = null!;
        public DbSet<RolPermiso> RolPermisos { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Rol>().ToTable("Rol", "dbo");

            // Si quieres mantener el índice único, puedes dejarlo
            modelBuilder.Entity<Rol>().HasIndex(c => c.nombreRol).IsUnique();



            // Tabla Cargo
            modelBuilder.Entity<Cargo>().ToTable("Cargo", "dbo");
            modelBuilder.Entity<Cargo>().HasIndex(c => c.nombreCargo).IsUnique();


              // === Permiso ===
            modelBuilder.Entity<Permiso>(e =>
            {
                e.ToTable("Permiso", "dbo");
                e.HasKey(x => x.permisoId);
                e.HasIndex(x => x.nombrePermiso).IsUnique(); // mismo estilo: índice único por nombre
                e.Property(x => x.nombrePermiso).HasMaxLength(100).IsRequired();
            });

            // === RolPermiso (tabla puente) ===
            modelBuilder.Entity<RolPermiso>(e =>
            {
                e.ToTable("RolPermiso", "dbo");

                // Clave compuesta (rolId + permisoId)
                e.HasKey(x => new { x.rolId, x.permisoId });

                // FK a Rol
                e.HasOne(x => x.Rol)
                 .WithMany()
                 .HasForeignKey(x => x.rolId)
                 .OnDelete(DeleteBehavior.Cascade);

                // FK a Permiso
                e.HasOne(x => x.Permiso)
                 .WithMany()
                 .HasForeignKey(x => x.permisoId)
                 .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
