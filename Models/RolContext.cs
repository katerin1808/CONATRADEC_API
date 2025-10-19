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

            modelBuilder.Entity<RolPermiso>(e =>
            {
                e.ToTable("rolPermiso", "dbo");
                e.HasKey(x => x.rolPermisoId);

                e.HasIndex(x => new { x.rolId, x.permisoId }).IsUnique();

                e.Property(x => x.leer).HasDefaultValue(false);
                e.Property(x => x.agregar).HasDefaultValue(false);
                e.Property(x => x.actualizar).HasDefaultValue(false);
                e.Property(x => x.eliminar).HasDefaultValue(false);

                e.HasOne(x => x.Rol).WithMany(r => r.rolPermisos).HasForeignKey(x => x.rolId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Permiso).WithMany(p => p.rolPermisos).HasForeignKey(x => x.permisoId).OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
    }

