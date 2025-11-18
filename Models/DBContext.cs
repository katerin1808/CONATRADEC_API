using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using static CONATRADEC_API.Models.RolInteraz;

namespace CONATRADEC_API.Models
{
    public class DBContext : DbContext
    {
        public DBContext(DbContextOptions<DBContext> options) : base(options) { }

        // ==========================
        // TABLAS BASE
        // ==========================
        public DbSet<Rol> Roles { get; set; } = null!;
        public DbSet<Interfaz> Interfaz { get; set; } = null!;
        public DbSet<RolInteraz> RolInteraz { get; set; } = null!;
        public DbSet<Pais> Pais { get; set; } = null!;
        public DbSet<Departamento> Departamento { get; set; } = null!;
        public DbSet<Municipio> Municipios { get; set; } = null!;
        public DbSet<Procedencia> Procedencia { get; set; } = null!;
        public DbSet<Usuario> Usuarios { get; set; } = null!;
        public DbSet<Terreno> Terreno { get; set; } = null!;
        public DbSet<FuenteNutriente> FuenteNutrientes { get; set; } = null!;
        public DbSet<ElementoQuimico> ElementoQuimicos { get; set; } = null!;

        // ==========================
        // NUEVO MÓDULO: ANÁLISIS DE SUELO
        // ==========================
        public DbSet<AnalisisSuelo> AnalisisSuelos { get; set; } = null!;
        public DbSet<AnalisisSueloElementoQuimico> AnalisisSueloElementos { get; set; } = null!;
        public DbSet<UnidadMedida> UnidadesMedida { get; set; } = null!;
        public DbSet<RangoNutrimental> RangoNutrimentales { get; set; } = null!;
        public DbSet<Interpretacion> Interpretaciones { get; set; } = null!;
        public DbSet<InterpretacionFuenteNutriente> InterpretacionFuentes { get; set; } = null!;
        public DbSet<ControlAplicacion> ControlAplicaciones { get; set; } = null!;
        public DbSet<FuenteNutrienteControlAplicacion> FuenteNutrienteControlAplicaciones { get; set; } = null!;

        // =============================================
        // CONFIGURACIÓN FLUENTE
        // =============================================
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ===================================================
            // 🧩 CONFIGURACIÓN EXISTENTE (NO SE TOCA)
            // ===================================================
            modelBuilder.Entity<Rol>().ToTable("Rol", "dbo");
            modelBuilder.Entity<Rol>().HasIndex(c => c.nombreRol).IsUnique();

            modelBuilder.Entity<Interfaz>(e =>
            {
                e.ToTable("interfaz", "dbo");
                e.HasKey(x => x.interfazId);
                e.HasIndex(x => x.nombreInterfaz).IsUnique();
                e.Property(x => x.nombreInterfaz).HasMaxLength(100).IsRequired();
            });

            modelBuilder.Entity<RolInteraz>(e =>
            {
                e.ToTable("rolInteraz", "dbo");
                e.HasKey(x => x.rolInterazId);
                e.Property(x => x.rolInterazId).ValueGeneratedOnAdd();
                e.Property(x => x.leer).HasDefaultValue(false).IsRequired();
                e.Property(x => x.agregar).HasDefaultValue(false).IsRequired();
                e.Property(x => x.actualizar).HasDefaultValue(false).IsRequired();
                e.Property(x => x.eliminar).HasDefaultValue(false).IsRequired();
            });

            // ... (se mantiene toda tu configuración actual: País, Departamento, Municipio, etc.)

            // ===================================================
            // 🔬 NUEVO MÓDULO: ANÁLISIS DE SUELO
            // ===================================================

            // === analisisSuelo ===
            modelBuilder.Entity<AnalisisSuelo>(e =>
            {
                e.ToTable("analisisSuelo", "dbo");
                e.HasKey(x => x.analisisSueloId);
                e.Property(x => x.fechaAnalisisSuelo).HasColumnType("date").IsRequired();
                e.Property(x => x.laboratorioAnalasisSuelo).HasMaxLength(80).IsRequired();
                e.Property(x => x.identificadorAnalisisSuelo).HasMaxLength(50).IsRequired();
                e.Property(x => x.activo).HasDefaultValue(true).IsRequired();
            });

            // === analisisSueloElementoQuimico ===
            modelBuilder.Entity<AnalisisSueloElementoQuimico>(e =>
            {
                e.ToTable("analisisSueloElementoQuimico", "dbo");
                e.HasKey(x => x.analisisSueloElementoQuimicoId);
                e.Property(x => x.cantidadElemento).HasPrecision(10, 4).IsRequired();
                e.Property(x => x.activo).HasDefaultValue(true).IsRequired();

                e.HasOne(x => x.AnalisisSuelo)
                 .WithMany()
                 .HasForeignKey(x => x.analisisSueloId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.ElementoQuimicos)
                 .WithMany()
                 .HasForeignKey(x => x.elementoQuimicosId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.UnidadMedida)
                 .WithMany()
                 .HasForeignKey(x => x.unidadMedidaId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // === unidadMedida ===
            modelBuilder.Entity<UnidadMedida>(e =>
            {
                e.ToTable("unidadMedida", "dbo");
                e.HasKey(x => x.unidadMedidaId);
                e.Property(x => x.nombreUnidadMedida).HasMaxLength(50).IsRequired();
                e.Property(x => x.activo).HasDefaultValue(true);
            });

            // === rangoNutrimental ===
            modelBuilder.Entity<RangoNutrimental>(e =>
            {
                e.ToTable("rangoNutrimental", "dbo");
                e.HasKey(x => x.rangoNutrimentalId);
                e.Property(x => x.minimoRangoNutrimental).IsRequired();
                e.Property(x => x.maximoRangoNutrimental).IsRequired();
                e.Property(x => x.activo).HasDefaultValue(true).IsRequired();
            });

            // === interpretacion ===
            modelBuilder.Entity<Interpretacion>(e =>
            {
                e.ToTable("interpretacion", "dbo");
                e.HasKey(x => x.interpretacionId);
                e.Property(x => x.codigoInterpretacion).HasMaxLength(50).IsRequired();
                e.Property(x => x.fechaInterpretacion).HasColumnType("date").IsRequired();
                e.Property(x => x.activo).HasDefaultValue(true).IsRequired();
            });

            // === interpretacionFuenteNutriente ===
            modelBuilder.Entity<InterpretacionFuenteNutriente>(e =>
            {
                e.ToTable("interpretacionFuenteNutriente", "dbo");
                e.HasKey(x => x.interpretacionFuenteNutrienteId);
                e.Property(x => x.precioHistorico).HasPrecision(10, 4);
                e.Property(x => x.activo).HasDefaultValue(true).IsRequired();
            });

            // === controlAplicacion ===
            modelBuilder.Entity<ControlAplicacion>(e =>
            {
                e.ToTable("controlAplicacion", "dbo");
                e.HasKey(x => x.controlAplicacionId);
                e.Property(x => x.fechaControlAplicacion).HasColumnType("date").IsRequired();
                e.Property(x => x.numeroControlAplicacion).IsRequired();
                e.Property(x => x.activo).HasDefaultValue(true).IsRequired();
            });

            // === fuenteNutrienteControlAplicacion ===
            modelBuilder.Entity<FuenteNutrienteControlAplicacion>(e =>
            {
                e.ToTable("fuenteNutrienteControlAplicacion", "dbo");
                e.HasKey(x => x.fuenteNutrienteControlAplicacionId);
                e.Property(x => x.cantidadAplicado).HasPrecision(10, 4).IsRequired();
                e.Property(x => x.fechaAplicado).HasColumnType("date").IsRequired();
                e.Property(x => x.cantidadPendiente).HasPrecision(10, 4).IsRequired();
                e.Property(x => x.activo).HasDefaultValue(true).IsRequired();
            });
        }
    }
}
