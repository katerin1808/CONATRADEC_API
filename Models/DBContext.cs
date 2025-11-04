using Microsoft.EntityFrameworkCore;
using static CONATRADEC_API.Models.RolInterfaz;

namespace CONATRADEC_API.Models
{
    public class DBContext: DbContext  
    {
        public DBContext(DbContextOptions<DBContext> options) : base(options)
        {
        }

        public DbSet<Rol> Roles { get; set; }
     
        public DbSet<Interfaz>Interfaz{ get; set; } = null!;
        public DbSet<RolInterfaz> RolInterfaz { get; set; } = null!;

        public DbSet<Pais> Pais { get; set; }
        public DbSet<Departamento> Departamento { get; set; }

        public DbSet<Municipio> Municipios => Set<Municipio>();

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
                e.ToTable("interfaz", "dbo");
                e.HasKey(x => x.interfazId);
                e.HasIndex(x => x.nombreInterfaz).IsUnique(); // mismo estilo: índice único por nombre
                e.Property(x => x.descripcionInterfaz).HasMaxLength(100).IsRequired();
            });

            modelBuilder.Entity<RolInterfaz>(e =>
            {
                e.ToTable("rolInterfaz", "dbo");
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
                e.HasIndex(p => p.NombrePais).IsUnique(); ;                        // búsqueda rápida
            });

            modelBuilder.Entity<Departamento>(entity =>
            {
                entity.ToTable("departamento");

                entity.HasKey(d => d.DepartamentoId);
                entity.Property(d => d.DepartamentoId).HasColumnName("departamentoId");

                entity.Property(d => d.NombreDepartamento)
                      .IsRequired()
                      .HasMaxLength(80)
                      .HasColumnName("nombreDepartamento");

                entity.Property(d => d.Activo)
                      .IsRequired()
                      .HasColumnName("activo")
                      .HasDefaultValue(true);

                entity.Property(d => d.PaisId)
                      .IsRequired()
                      .HasColumnName("paisId");

                // Relación requerida: Departamento → Pais
                entity.HasOne(d => d.Pais)
                      .WithMany(p => p.Departamentos)
                      .HasForeignKey(d => d.PaisId)
                      .OnDelete(DeleteBehavior.Restrict)
                      .IsRequired();

                // Índice único por país
                entity.HasIndex(d => new { d.PaisId, d.NombreDepartamento })
                      .IsUnique();
            });


            // ====== MUNICIPIO ======
            modelBuilder.Entity<Municipio>(e =>
            {
                e.ToTable("municipio");

                e.HasKey(m => m.MunicipioId);
                e.Property(m => m.MunicipioId).HasColumnName("municipioId");

                e.Property(m => m.NombreMunicipio).IsRequired().HasMaxLength(80).HasColumnName("nombreMunicipio");
                e.Property(m => m.Activo).IsRequired().HasColumnName("activo");
                e.Property(m => m.DepartamentoId).IsRequired().HasColumnName("departamentoId");

                e.HasOne(m => m.Departamento)
                 .WithMany(d => d.Municipios)
                 .HasForeignKey(m => m.DepartamentoId)
                 .OnDelete(DeleteBehavior.Restrict)
                 .IsRequired();

                // Unicidad GLOBAL por nombre de municipio (coherente con depto)
                // Si lo quieres por departamento, cámbialo a:
                // e.HasIndex(m => new { m.DepartamentoId, m.NombreMunicipio }).IsUnique();
                e.HasIndex(m => m.NombreMunicipio).IsUnique();
            });

            modelBuilder.Entity<Usuario>(e =>
            {
                e.ToTable("usuario", "dbo");
                e.HasKey(x => x.UsuarioId);

                e.Property(x => x.NombreUsuario).IsRequired().HasMaxLength(100);
                e.Property(x => x.ClaveHashUsuario).IsRequired().HasMaxLength(400);
                e.Property(x => x.TelefonoUsuario).HasMaxLength(20);
                e.Property(x => x.CorreoUsuario).HasMaxLength(200);

                e.Property(x => x.Activo)
                 .IsRequired()
                 .HasDefaultValue(true); // <- default en SQL

                e.HasOne(x => x.Rol)
                 .WithMany(r => r.Usuarios)
                 .HasForeignKey(x => x.RolId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => x.NombreUsuario).IsUnique();
            });

        }
    }
    }

