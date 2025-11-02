using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.Models.RolInterfaz;

namespace CONATRADEC_API.Models
{
    public class RolContext: DbContext  
    {
        public RolContext(DbContextOptions<RolContext> options) : base(options)
        {
        }

        public DbSet<Rol> Roles { get; set; }
     
        public DbSet<Interfaz>Interfaz{ get; set; } = null!;
        public DbSet<RolInterfaz> RolInterfaz { get; set; } = null!;

        public DbSet<Pais> Pais { get; set; }
        public DbSet<Departamento> Departamento { get; set; }

        public DbSet<Municipio> Municipio { get; set; } // 👈 agregamos la tabla Cargo

        public DbSet<Usuario> Usuarios => Set<Usuario>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Rol>().ToTable("Rol", "dbo");

            // Si quieres mantener el índice único, puedes dejarlo
            modelBuilder.Entity<Rol>().HasIndex(c => c.nombreRol).IsUnique();




              // === Permiso ===
            modelBuilder.Entity<Interfaz>(e =>
            {
                e.ToTable("Permiso", "dbo");
                e.HasKey(x => x.interfazId);
                e.HasIndex(x => x.nombreInterfaz).IsUnique(); // mismo estilo: índice único por nombre
                e.Property(x => x.descripcionInterfaz).HasMaxLength(100).IsRequired();
            });

            modelBuilder.Entity<RolInterfaz>(e =>
            {
                e.ToTable("rolPermiso", "dbo");
                e.HasKey(x => x.rolInterfazId);

                e.HasIndex(x => new { x.rolId, x.interfazId }).IsUnique();

                e.Property(x => x.leer).HasDefaultValue(false);
                e.Property(x => x.agregar).HasDefaultValue(false);
                e.Property(x => x.actualizar).HasDefaultValue(false);
                e.Property(x => x.eliminar).HasDefaultValue(false);

                e.HasOne(x => x.Rol).WithMany(r => r.rolInterfaz).HasForeignKey(x => x.rolId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Interfaces).WithMany(p => p.rolinterfaz).HasForeignKey(x => x.interfazId).OnDelete(DeleteBehavior.Cascade);
            });



            modelBuilder.Entity<Pais>(e =>
            {
                e.Property(p => p.NombrePais).HasMaxLength(80).IsRequired();
                e.Property(p => p.CodigoISOPais).HasMaxLength(3).IsRequired();

                // Únicos / índices
                e.HasIndex(p => p.CodigoISOPais).IsUnique();          // ISO único
                e.HasIndex(p => p.NombrePais);                        // búsqueda rápida
            });

            modelBuilder.Entity<Departamento>(e =>
            {
                e.Property(d => d.NombreDepartamento).HasMaxLength(80).IsRequired();
                e.Property(d => d.PaisId).IsRequired();

                // Relación requerida: Departamento -> Pais
                e.HasOne(d => d.Pais)
                 .WithMany(p => p.Departamentos)
                 .HasForeignKey(d => d.PaisId)
                 .IsRequired()
                 .OnDelete(DeleteBehavior.Restrict); // sin cascada

                // Nombre único dentro de un país
                e.HasIndex(d => new { d.PaisId, d.NombreDepartamento }).IsUnique();
            });


            modelBuilder.Entity<Municipio>(e =>
            {
                e.Property(m => m.NombreMunicipio).HasMaxLength(80).IsRequired();
                e.Property(m => m.DepartamentoId).IsRequired();

                // Relación requerida: Municipio -> Departamento
                e.HasOne(m => m.Departamento)
                 .WithMany(d => d.Municipios)
                 .HasForeignKey(m => m.DepartamentoId)
                 .IsRequired()
                 .OnDelete(DeleteBehavior.Restrict);

                // Nombre único dentro de un departamento
                e.HasIndex(m => new { m.DepartamentoId, m.NombreMunicipio }).IsUnique();
            });

            modelBuilder.Entity<Usuario>(e =>
            {
                e.ToTable("usuario", "dbo");
                e.HasKey(x => x.UsuarioId);

                e.Property(x => x.nombreUsuario).IsRequired().HasMaxLength(100);
                e.Property(x => x.claveHashUsuario).IsRequired().HasMaxLength(400);
                e.Property(x => x.telefonoUsuario).HasMaxLength(20);
                e.Property(x => x.correoUsuario).HasMaxLength(200);

                e.Property(x => x.activo)
                 .IsRequired()
                 .HasDefaultValue(true); // <- default en SQL

                e.HasOne(x => x.Rol)
                 .WithMany(r => r.Usuarios)
                 .HasForeignKey(x => x.rolId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => x.nombreUsuario).IsUnique();
            });

        }
    }
    }

