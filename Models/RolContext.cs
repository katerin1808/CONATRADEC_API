using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Models
{
    public class RolContext: DbContext  
    {
        public RolContext(DbContextOptions<RolContext> options) : base(options)
        {
        }

        public DbSet<Rol> Roles { get; set; }
        public DbSet<Cargo> Cargos { get; set; } // 👈 agregamos la tabla Cargo
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Rol>().ToTable("Rol", "dbo");

            // Si quieres mantener el índice único, puedes dejarlo
            modelBuilder.Entity<Rol>().HasIndex(c => c.nombreRol).IsUnique();



            // Tabla Cargo
            modelBuilder.Entity<Cargo>().ToTable("Cargo", "dbo");
            modelBuilder.Entity<Cargo>().HasIndex(c => c.nombreCargo).IsUnique();
        }
    }
}
