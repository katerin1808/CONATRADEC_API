using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.ComponentModel;
using static CONATRADEC_API.Models.RolInteraz;

namespace CONATRADEC_API.Models
{
    public class DBContext: DbContext  
    {
        public DBContext(DbContextOptions<DBContext> options) : base(options)
        {
        }

        public DbSet<Rol> Roles { get; set; }
     
        public DbSet<Interfaz>Interfaz{ get; set; } = null!;
        public DbSet<RolInteraz> RolInteraz { get; set; } = null!;

        public DbSet<Pais> Pais { get; set; }
        public DbSet<Departamento> Departamento { get; set; }

        public DbSet<Municipio> Municipios => Set<Municipio>();

        public DbSet<Procedencia> Procedencia { get; set; } = null!;
        public DbSet<Usuario> Usuarios { get; set; } = null!;


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Rol>().ToTable("Rol", "dbo");

            // Si quieres mantener el índice único, puedes dejarlo
            modelBuilder.Entity<Rol>().HasIndex(c => c.nombreRol).IsUnique();

            // === Interfaz ===
          
            modelBuilder.Entity<Interfaz>(e =>
            {
                e.ToTable("interfaz", "dbo");
                e.HasKey(x => x.interfazId);
                e.HasIndex(x => x.nombreInterfaz).IsUnique(); // mismo estilo: índice único por nombre
                e.Property(x => x.nombreInterfaz).HasMaxLength(100).IsRequired();
            });

            modelBuilder.Entity<RolInteraz>(e =>
            {
                e.ToTable("rolInteraz", "dbo");
                e.HasKey(x => x.rolInterazId);
                e.Property(x => x.rolInterazId).ValueGeneratedOnAdd();

                e.Property(x => x.leer)
                    .IsRequired()
                    .HasDefaultValue(false);

                e.Property(x => x.agregar)
                    .IsRequired()
                    .HasDefaultValue(false);

                e.Property(x => x.actualizar)
                    .IsRequired()
                    .HasDefaultValue(false);

                e.Property(x => x.eliminar)
                    .IsRequired()
                    .HasDefaultValue(false);
            });



            modelBuilder.Entity<Pais>(e =>
            {
                e.ToTable("pais", "dbo");
                e.HasKey(p => p.PaisId);

                e.Property(p => p.PaisId).HasColumnName("paisId");
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

            modelBuilder.Entity<Procedencia>(e =>
            {
                e.ToTable("procedencia");
                e.HasKey(x => x.procedenciaId);
                e.Property(x => x.procedenciaId).HasColumnName("procedenciaId");
                e.Property(x => x.nombreProcedencia).HasColumnName("nombreProcedencia").HasMaxLength(100).IsRequired();
                e.Property(x => x.descripcionProcedencia).HasColumnName("descripcionProcedencia").HasMaxLength(200);
                e.Property(x => x.activo).HasColumnName("activo").HasDefaultValue(true);

                e.HasData(
                    new Procedencia { procedenciaId = 1, nombreProcedencia = "Interno", descripcionProcedencia = "Usuario interno", activo = true },
                    new Procedencia { procedenciaId = 2, nombreProcedencia = "Externo", descripcionProcedencia = "Usuario externo", activo = true }
                );
            });


            modelBuilder.Entity<Usuario>(e =>
            {
                e.ToTable("usuario", "dbo");
                e.HasKey(x => x.UsuarioId);

                e.Property(x => x.nombreUsuario).IsRequired().HasMaxLength(100);
                e.Property(x => x.claveHashUsuario).IsRequired().HasMaxLength(512);
                e.Property(x => x.identificacionUsuario).HasMaxLength(50);
                e.Property(x => x.nombreCompletoUsuario).IsRequired().HasMaxLength(150);
                e.Property(x => x.correoUsuario).IsRequired().HasMaxLength(150);
                e.Property(x => x.telefonoUsuario).HasMaxLength(25);

                // Mapear DateOnly -> date
                e.Property(x => x.fechaNacimientoUsuario)
                 .HasColumnType("date");

                e.Property(x => x.activo).IsRequired().HasDefaultValue(true);

                // Índices únicos (evita duplicados)
                e.HasIndex(x => x.nombreUsuario).IsUnique();
                e.HasIndex(x => x.correoUsuario).IsUnique();

                // FKs
                e.HasOne(x => x.Rol)
                 .WithMany(r => r.Usuarios)
                 .HasForeignKey(x => x.rolId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Procedencia)
                 .WithMany(p => p.Usuarios)
                 .HasForeignKey(x => x.procedenciaId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Municipio)
                 .WithMany(m => m.Usuarios)
                 .HasForeignKey(x => x.municipioId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

        }


    }
    
    }

