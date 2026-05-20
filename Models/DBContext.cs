using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using static CONATRADEC_API.Models.RolInterfaz;

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
        public DbSet<RolInterfaz> RolInterfaz { get; set; } = null!;
        public DbSet<Pais> Pais { get; set; } = null!;
        public DbSet<Departamento> Departamento { get; set; } = null!;
        public DbSet<Municipio> Municipios { get; set; } = null!;
        public DbSet<Procedencia> Procedencia { get; set; } = null!;
        public DbSet<Usuario> Usuarios { get; set; } = null!;
        public DbSet<Terreno> Terreno { get; set; } = null!;
        public DbSet<FuenteNutriente> fuenteNutriente { get; set; }
        public DbSet<ElementoQuimico> elementoQuimico { get; set; }
        public DbSet<FuenteNutrienteElementoQuimico> fuenteNutrienteElementoQuimico { get; set; }
        public DbSet<AnalisisSueloCalculoElementoQuimico> AnalisisSueloCalculoElementoQuimicos { get; set; } = null!;

        // ==========================
        // 🔬 ANÁLISIS DE SUELOS
        // ==========================
        public DbSet<AnalisisSuelo> AnalisisSuelos { get; set; } = null!;
        public DbSet<AnalisisSueloElementoQuimico> AnalisisSueloElementos { get; set; } = null!;
        public DbSet<TipoCultivo> TipoCultivos { get; set; } = null!;
        public DbSet<TipoAnalisisSuelo> TipoAnalisisSuelos { get; set; } = null!;

        public DbSet<UnidadMedida> UnidadMedidas { get; set; } = null!;
        public DbSet<RangoNutrimental> RangoNutrimentales { get; set; } = null!;
        public DbSet<Interpretacion> Interpretaciones { get; set; } = null!;
        public DbSet<InterpretacionFuenteNutriente> InterpretacionFuenteNutrientes { get; set; } = null!;
        public DbSet<ControlAplicacion> ControlAplicaciones { get; set; } = null!;
        public DbSet<FuenteNutrienteControlAplicacion> FuenteNutrienteControlAplicaciones { get; set; } = null!;
        public DbSet<AnalisisSueloCalculo> AnalisisSueloCalculos { get; set; } = null!;
        public DbSet<AnalisisSueloCalculoElementoQuimico> AnalisisSueloCalculoElementos { get; set; } = null!;
        public DbSet<ParametroExtraccionNutrienteCafe> ParametroExtraccionNutrienteCafe { get; set; } = null!;
        public DbSet<ParametroRangoNutrienteCultivo> ParametroRangoNutrienteCultivo { get; set; } = null!;
        public DbSet<ParametroEnmiendaCalcarea> ParametroEnmiendaCalcarea { get; set; } = null!;
        public DbSet<ParametroFuenteOrganicaAporte> ParametroFuenteOrganicaAporte { get; set; } = null!;

        // ==========================
        // CONFIGURACIONES
        // ==========================
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configuraciones que ya tenías 
            // (las dejo intactas)

            // Interfaz
            modelBuilder.Entity<Interfaz>(e =>
            {
                e.ToTable("interfaz", "dbo");
                e.HasKey(x => x.interfazId);
                e.HasIndex(x => x.nombreInterfaz).IsUnique();
                e.Property(x => x.nombreInterfaz).HasMaxLength(100).IsRequired();
            });

            // RolInteraz
            modelBuilder.Entity<RolInterfaz>(e =>
            {
                e.ToTable("rolInterfaz", "dbo");
                e.HasKey(x => x.rolInterfazId);
                e.Property(x => x.leer).HasDefaultValue(false).IsRequired();
                e.Property(x => x.agregar).HasDefaultValue(false).IsRequired();
                e.Property(x => x.actualizar).HasDefaultValue(false).IsRequired();
                e.Property(x => x.eliminar).HasDefaultValue(false).IsRequired();
            });

            // analisisSuelo
            modelBuilder.Entity<AnalisisSuelo>(e =>
            {
                e.ToTable("analisisSuelo", "dbo");
                e.HasKey(x => x.analisisSueloId);
                e.Property(x => x.fechaAnalisisSuelo).HasColumnType("date").IsRequired();
                e.Property(x => x.laboratorioAnalasisSuelo).HasMaxLength(80).IsRequired();
                e.Property(x => x.identificadorAnalisisSuelo).HasMaxLength(50).IsRequired();
                e.Property(x => x.activo).HasDefaultValue(true).IsRequired();
            });
            // ============================
            // analisisSueloElementoQuimico
            // ============================
            modelBuilder.Entity<AnalisisSueloElementoQuimico>(entity =>
            {
                entity.ToTable("analisisSueloElementoQuimico", "dbo");

                entity.HasKey(e => e.analisisSueloElementoQuimicoId);

                entity.Property(e => e.cantidadElemento)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.activo)
                    .HasDefaultValue(true);

                entity.HasOne(e => e.AnalisisSuelo)
                    .WithMany(e => e.Elementos)
                    .HasForeignKey(e => e.analisisSueloId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.ElementoQuimico)
                    .WithMany()
                    .HasForeignKey(e => e.elementoQuimicosId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.UnidadMedida)
                    .WithMany(u => u.AnalisisSueloElementosQuimicos)
                    .HasForeignKey(e => e.unidadMedidaId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // unidadMedida
            modelBuilder.Entity<UnidadMedida>(e =>
            {
                e.ToTable("unidadMedida", "dbo");
                e.HasKey(x => x.unidadMedidaId);
                e.Property(x => x.nombreUnidadMedida).HasMaxLength(50).IsRequired();
                e.Property(x => x.activo).HasDefaultValue(true);
            });

            // rangoNutrimental
            modelBuilder.Entity<RangoNutrimental>(e =>
            {
                e.ToTable("rangoNutrimental", "dbo");
                e.HasKey(x => x.rangoNutrimentalId);
                e.Property(x => x.minimoRangoNutrimental).IsRequired();
                e.Property(x => x.maximoRangoNutrimental).IsRequired();
                e.Property(x => x.activo).HasDefaultValue(true);
            });

            // interpretacion
            modelBuilder.Entity<Interpretacion>(e =>
            {
                e.ToTable("interpretacion", "dbo");
                e.HasKey(x => x.interpretacionId);
                e.Property(x => x.codigoInterpretacion).HasMaxLength(50).IsRequired();
                e.Property(x => x.fechaInterpretacion).HasColumnType("date").IsRequired();
                e.Property(x => x.activo).HasDefaultValue(true);
            });

            // interpretacionFuenteNutriente
            modelBuilder.Entity<InterpretacionFuenteNutriente>(e =>
            {
                e.ToTable("interpretacionFuenteNutriente", "dbo");
                e.HasKey(x => x.interpretacionFuenteNutrienteId);
                e.Property(x => x.precioHistorico).HasPrecision(10, 4);
                e.Property(x => x.activo).HasDefaultValue(true);
            });

            // controlAplicacion
            modelBuilder.Entity<ControlAplicacion>(e =>
            {
                e.ToTable("controlAplicacion", "dbo");
                e.HasKey(x => x.controlAplicacionId);
                e.Property(x => x.fechaControlAplicacion).HasColumnType("date").IsRequired();
                e.Property(x => x.numeroControlAplicacion).IsRequired();
                e.Property(x => x.activo).HasDefaultValue(true);
            });

            // fuenteNutrienteControlAplicacion
            modelBuilder.Entity<FuenteNutrienteControlAplicacion>(e =>
            {
                e.ToTable("fuenteNutrienteControlAplicacion", "dbo");
                e.HasKey(x => x.fuenteNutrienteControlAplicacionId);
                e.Property(x => x.cantidadAplicado).HasPrecision(10, 4).IsRequired();
                e.Property(x => x.fechaAplicado).HasColumnType("date").IsRequired();
                e.Property(x => x.cantidadPendiente).HasPrecision(10, 4).IsRequired();
                e.Property(x => x.activo).HasDefaultValue(true);
            });

            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<FuenteNutrienteElementoQuimico>()
                .HasOne(x => x.fuenteNutriente)
                .WithMany(x => x.fuenteNutrienteElementoQuimico)
                .HasForeignKey(x => x.fuenteNutrientesId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FuenteNutrienteElementoQuimico>()
                .HasOne(x => x.elementoQuimico)
                .WithMany(x => x.fuenteNutrienteElementoQuimico)
                .HasForeignKey(x => x.elementoQuimicosId)
                .OnDelete(DeleteBehavior.Restrict);

            // ============================
            // analisisSueloCalculo
            // ============================
            modelBuilder.Entity<AnalisisSueloCalculo>(entity =>
            {
                entity.ToTable("analisisSueloCalculo", "dbo");

                entity.HasKey(e => e.analisisSueloCalculoId);

                entity.Property(e => e.cantidadQuintalesOro)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.tamanoFinca)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.phAnalisisSuelo)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.materiaOrganica)
                    .HasPrecision(10, 4);

                entity.Property(e => e.acidezTotal)
                    .HasPrecision(10, 4);

                entity.Property(e => e.recomendacionGeneral)
                    .HasMaxLength(500);

                entity.Property(e => e.observacion)
                    .HasMaxLength(500);

                entity.Property(e => e.fechaCalculo)
                    .HasColumnType("datetime");

                entity.Property(e => e.activo)
                    .HasDefaultValue(true);
            });


            // ============================
            // analisisSueloCalculoElementoQuimico
            // ============================
            modelBuilder.Entity<AnalisisSueloCalculoElementoQuimico>(entity =>
            {
                entity.ToTable("analisisSueloCalculoElementoQuimico", "dbo");

                entity.HasKey(e => e.analisisSueloCalculoElementoQuimicoId);

                entity.Property(e => e.cantidadIngresada)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.requerimientoCalculado)
                    .HasPrecision(10, 4);

                entity.Property(e => e.cantidadConvertidaLbMz)
                    .HasPrecision(10, 4);

                entity.Property(e => e.clasificacion)
                    .HasMaxLength(50);

                entity.Property(e => e.observacion)
                    .HasMaxLength(500);

                entity.Property(e => e.activo)
                    .HasDefaultValue(true);

                entity.HasOne(e => e.AnalisisSueloCalculo)
                    .WithMany(e => e.ElementosCalculados)
                    .HasForeignKey(e => e.analisisSueloCalculoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.ElementoQuimico)
                    .WithMany()
                    .HasForeignKey(e => e.elementoQuimicosId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.UnidadMedida)
                    .WithMany()
                    .HasForeignKey(e => e.unidadMedidaId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ============================
            // parametroExtraccionNutrienteCafe
            // ============================
            modelBuilder.Entity<ParametroExtraccionNutrienteCafe>(entity =>
            {
                entity.ToTable("parametroExtraccionNutrienteCafe", "dbo");

                entity.HasKey(e => e.parametroExtraccionNutrienteCafeId);

                entity.Property(e => e.cantidadExtraidaPorQQOro)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.descripcionParametro)
                    .HasMaxLength(150)
                    .IsRequired();

                entity.Property(e => e.activo)
                    .HasDefaultValue(true);

                entity.HasOne(e => e.ElementoQuimico)
                    .WithMany()
                    .HasForeignKey(e => e.elementoQuimicosId)
                    .OnDelete(DeleteBehavior.Restrict);
            });


            // ============================
            // parametroRangoNutrienteCultivo
            // ============================
            modelBuilder.Entity<ParametroRangoNutrienteCultivo>(entity =>
            {
                entity.ToTable("parametroRangoNutrienteCultivo", "dbo");

                entity.HasKey(e => e.parametroRangoNutrienteCultivoId);

                entity.Property(e => e.valorMinimo)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.valorMaximo)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.unidadBase)
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(e => e.descripcionParametro)
                    .HasMaxLength(150)
                    .IsRequired();

                entity.Property(e => e.activo)
                    .HasDefaultValue(true);

                entity.HasOne(e => e.TipoCultivo)
                    .WithMany()
                    .HasForeignKey(e => e.tipoCultivoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.ElementoQuimico)
                    .WithMany()
                    .HasForeignKey(e => e.elementoQuimicosId)
                    .OnDelete(DeleteBehavior.Restrict);
            });


            // ============================
            // parametroEnmiendaCalcarea
            // ============================
            modelBuilder.Entity<ParametroEnmiendaCalcarea>(entity =>
            {
                entity.ToTable("parametroEnmiendaCalcarea", "dbo");

                entity.HasKey(e => e.parametroEnmiendaCalcareaId);

                entity.Property(e => e.saturacionBasesDeseada)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.prnt)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.factorTonHaALbHa)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.factorHaAMz)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.factorTonHaAKgHa)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.descripcionParametro)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(e => e.activo)
                    .HasDefaultValue(true);

                entity.HasOne(e => e.FuenteNutriente)
                    .WithMany()
                    .HasForeignKey(e => e.fuenteNutrientesId)
                    .OnDelete(DeleteBehavior.Restrict);
            });


            // ============================
            // parametroFuenteOrganicaAporte
            // ============================
            modelBuilder.Entity<ParametroFuenteOrganicaAporte>(entity =>
            {
                entity.ToTable("parametroFuenteOrganicaAporte", "dbo");

                entity.HasKey(e => e.parametroFuenteOrganicaAporteId);

                entity.Property(e => e.cantidadAportePorUnidad)
                    .HasPrecision(10, 4)
                    .IsRequired();

                entity.Property(e => e.unidadEntrada)
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(e => e.descripcionParametro)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(e => e.activo)
                    .HasDefaultValue(true);

                entity.HasOne(e => e.FuenteNutriente)
                    .WithMany()
                    .HasForeignKey(e => e.fuenteNutrientesId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.ElementoQuimico)
                    .WithMany()
                    .HasForeignKey(e => e.elementoQuimicosId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

        }

    }
}
