using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    /// <summary>
    /// Contexto aislado para incorporar la jerarquía
    /// Categoría -> Subcategoría -> Ficha sin alterar la lógica existente
    /// del álbum ni del diagnóstico fitosanitario.
    /// </summary>
    public sealed class AlbumJerarquiaDbContext : DbContext
    {
        public AlbumJerarquiaDbContext(
            DbContextOptions<AlbumJerarquiaDbContext> options)
            : base(options)
        {
        }

        public DbSet<CategoriaAlbumJerarquia> Categorias =>
            Set<CategoriaAlbumJerarquia>();

        public DbSet<SubcategoriaAlbumBotanico> Subcategorias =>
            Set<SubcategoriaAlbumBotanico>();

        public DbSet<AlbumBotanicoCafeJerarquia> RegistrosAlbum =>
            Set<AlbumBotanicoCafeJerarquia>();

        public DbSet<AlbumBotanicoCafeFotoJerarquia> FotosAlbum =>
            Set<AlbumBotanicoCafeFotoJerarquia>();

        public DbSet<DiagnosticoIAJerarquiaReferencia> Diagnosticos =>
            Set<DiagnosticoIAJerarquiaReferencia>();

        public DbSet<DiagnosticoIAImagenJerarquiaReferencia> Fotografias =>
            Set<DiagnosticoIAImagenJerarquiaReferencia>();

        public DbSet<DiagnosticoIAImagenResultadoJerarquiaReferencia> ResultadosIA =>
            Set<DiagnosticoIAImagenResultadoJerarquiaReferencia>();

        public DbSet<DiagnosticoIAClasificacionJerarquia>
            ClasificacionesJerarquia =>
            Set<DiagnosticoIAClasificacionJerarquia>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CategoriaAlbumJerarquia>()
                .ToTable("CategoriaAlbumBotanico", "dbo");

            modelBuilder.Entity<SubcategoriaAlbumBotanico>()
                .ToTable("SubcategoriaAlbumBotanico", "dbo");

            modelBuilder.Entity<AlbumBotanicoCafeJerarquia>()
                .ToTable("AlbumBotanicoCafe", "dbo");

            modelBuilder.Entity<AlbumBotanicoCafeFotoJerarquia>()
                .ToTable("AlbumBotanicoCafeFoto", "dbo");

            modelBuilder.Entity<DiagnosticoIAJerarquiaReferencia>()
                .ToTable("diagnosticoIA", "dbo");

            modelBuilder.Entity<DiagnosticoIAImagenJerarquiaReferencia>()
                .ToTable("diagnosticoIAImagen", "dbo");

            modelBuilder.Entity<DiagnosticoIAImagenResultadoJerarquiaReferencia>()
                .ToTable("diagnosticoIAImagenResultadoIA", "dbo");

            modelBuilder.Entity<DiagnosticoIAClasificacionJerarquia>()
                .ToTable("diagnosticoIAClasificacionJerarquia", "dbo");

            modelBuilder.Entity<CategoriaAlbumJerarquia>()
                .HasMany(item => item.Subcategorias)
                .WithOne(item => item.Categoria)
                .HasForeignKey(item => item.CategoriaAlbumBotanicoId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SubcategoriaAlbumBotanico>()
                .HasIndex(item => new
                {
                    item.CategoriaAlbumBotanicoId,
                    item.NombreSubcategoria
                })
                .IsUnique();

            modelBuilder.Entity<AlbumBotanicoCafeJerarquia>()
                .HasOne(item => item.Categoria)
                .WithMany(item => item.Registros)
                .HasForeignKey(item => item.CategoriaAlbumBotanicoId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AlbumBotanicoCafeJerarquia>()
                .HasOne(item => item.Subcategoria)
                .WithMany(item => item.Registros)
                .HasForeignKey(item => item.SubcategoriaAlbumBotanicoId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AlbumBotanicoCafeFotoJerarquia>()
                .HasOne(item => item.Registro)
                .WithMany(item => item.Fotos)
                .HasForeignKey(item => item.AlbumBotanicoCafeId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIAJerarquiaReferencia>()
                .HasMany(item => item.Fotografias)
                .WithOne(item => item.Diagnostico)
                .HasForeignKey(item => item.DiagnosticoIAId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIAImagenJerarquiaReferencia>()
                .HasOne(item => item.ResultadoIA)
                .WithOne(item => item.Fotografia)
                .HasForeignKey<DiagnosticoIAImagenResultadoJerarquiaReferencia>(
                    item => item.DiagnosticoIAImagenId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIAClasificacionJerarquia>()
                .HasIndex(item => item.DiagnosticoIAImagenId)
                .IsUnique();

            modelBuilder.Entity<DiagnosticoIAClasificacionJerarquia>()
                .HasOne(item => item.Fotografia)
                .WithOne()
                .HasForeignKey<DiagnosticoIAClasificacionJerarquia>(
                    item => item.DiagnosticoIAImagenId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    [Table("CategoriaAlbumBotanico", Schema = "dbo")]
    public sealed class CategoriaAlbumJerarquia
    {
        [Key]
        [Column("categoriaAlbumBotanicoId")]
        public int CategoriaAlbumBotanicoId { get; set; }

        [Required, MaxLength(100)]
        [Column("nombreCategoria")]
        public string NombreCategoria { get; set; } = string.Empty;

        [MaxLength(500)]
        [Column("descripcion")]
        public string? Descripcion { get; set; }

        [Column("rutaImagenPortada")]
        public string? RutaImagenPortada { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

        public ICollection<SubcategoriaAlbumBotanico> Subcategorias { get; set; } =
            new List<SubcategoriaAlbumBotanico>();

        public ICollection<AlbumBotanicoCafeJerarquia> Registros { get; set; } =
            new List<AlbumBotanicoCafeJerarquia>();
    }

    [Table("SubcategoriaAlbumBotanico", Schema = "dbo")]
    public sealed class SubcategoriaAlbumBotanico
    {
        [Key]
        public int SubcategoriaAlbumBotanicoId { get; set; }

        public int CategoriaAlbumBotanicoId { get; set; }

        [Required, MaxLength(120)]
        public string NombreSubcategoria { get; set; } = string.Empty;

        [MaxLength(600)]
        public string? Descripcion { get; set; }

        public bool Activo { get; set; } = true;

        public DateTime FechaCreacionUtc { get; set; } = DateTime.UtcNow;

        public DateTime? FechaActualizacionUtc { get; set; }

        public CategoriaAlbumJerarquia Categoria { get; set; } = null!;

        public ICollection<AlbumBotanicoCafeJerarquia> Registros { get; set; } =
            new List<AlbumBotanicoCafeJerarquia>();
    }

    [Table("AlbumBotanicoCafe", Schema = "dbo")]
    public sealed class AlbumBotanicoCafeJerarquia
    {
        [Key]
        [Column("albumBotanicoCafeId")]
        public int AlbumBotanicoCafeId { get; set; }

        [Column("categoriaAlbumBotanicoId")]
        public int CategoriaAlbumBotanicoId { get; set; }

        [Column("subcategoriaAlbumBotanicoId")]
        public int? SubcategoriaAlbumBotanicoId { get; set; }

        [Required, MaxLength(200)]
        [Column("titulo")]
        public string Titulo { get; set; } = string.Empty;

        [MaxLength(200)]
        [Column("nombreCientifico")]
        public string? NombreCientifico { get; set; }

        [Required]
        [Column("descripcion")]
        public string Descripcion { get; set; } = string.Empty;

        [Column("caracteristicas")]
        public string? Caracteristicas { get; set; }

        [Column("sintomas")]
        public string? Sintomas { get; set; }

        [Column("causas")]
        public string? Causas { get; set; }

        [Column("recomendaciones")]
        public string? Recomendaciones { get; set; }

        [Column("observaciones")]
        public string? Observaciones { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

        [Column("fechaCreacion")]
        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public CategoriaAlbumJerarquia Categoria { get; set; } = null!;
        public SubcategoriaAlbumBotanico? Subcategoria { get; set; }
        public ICollection<AlbumBotanicoCafeFotoJerarquia> Fotos { get; set; } =
            new List<AlbumBotanicoCafeFotoJerarquia>();
    }

    [Table("AlbumBotanicoCafeFoto", Schema = "dbo")]
    public sealed class AlbumBotanicoCafeFotoJerarquia
    {
        [Key]
        [Column("albumBotanicoCafeFotoId")]
        public int AlbumBotanicoCafeFotoId { get; set; }

        [Column("albumBotanicoCafeId")]
        public int AlbumBotanicoCafeId { get; set; }

        [Column("rutaFoto")]
        public string RutaFoto { get; set; } = string.Empty;

        [Column("descripcionFoto")]
        public string? DescripcionFoto { get; set; }

        [Column("esPortada")]
        public bool EsPortada { get; set; }

        [Column("orden")]
        public int Orden { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

        public AlbumBotanicoCafeJerarquia Registro { get; set; } = null!;
    }

    [Table("diagnosticoIA", Schema = "dbo")]
    public sealed class DiagnosticoIAJerarquiaReferencia
    {
        [Key]
        public int DiagnosticoIAId { get; set; }

        [MaxLength(40)]
        public string Estado { get; set; } = string.Empty;

        public bool Activo { get; set; }

        public ICollection<DiagnosticoIAImagenJerarquiaReferencia> Fotografias { get; set; } =
            new List<DiagnosticoIAImagenJerarquiaReferencia>();
    }

    [Table("diagnosticoIAImagen", Schema = "dbo")]
    public sealed class DiagnosticoIAImagenJerarquiaReferencia
    {
        [Key]
        public int DiagnosticoIAImagenId { get; set; }

        public int DiagnosticoIAId { get; set; }

        public int Orden { get; set; }

        [MaxLength(40)]
        public string TipoFotografia { get; set; } = string.Empty;

        public DiagnosticoIAJerarquiaReferencia Diagnostico { get; set; } = null!;

        public DiagnosticoIAImagenResultadoJerarquiaReferencia? ResultadoIA { get; set; }
    }

    [Table("diagnosticoIAImagenResultadoIA", Schema = "dbo")]
    public sealed class DiagnosticoIAImagenResultadoJerarquiaReferencia
    {
        [Key]
        public int DiagnosticoIAImagenResultadoIAId { get; set; }

        public int DiagnosticoIAImagenId { get; set; }

        public bool ImagenValida { get; set; }
        public bool ParecePlantaCafe { get; set; }
        public bool ResultadoConcluyente { get; set; }

        [MaxLength(80)]
        public string PartePlanta { get; set; } = string.Empty;

        [MaxLength(40)]
        public string EstadoGeneral { get; set; } = string.Empty;

        [MaxLength(50)]
        public string CategoriaPrincipal { get; set; } = string.Empty;

        public string CategoriasSecundariasJson { get; set; } = "[]";

        [MaxLength(300)]
        public string DiagnosticoProbable { get; set; } = string.Empty;

        [MaxLength(80)]
        public string TipoDiagnostico { get; set; } = string.Empty;

        public int? CategoriaAlbumBotanicoIdSugerida { get; set; }
        public int? AlbumBotanicoCafeIdSugerido { get; set; }

        [MaxLength(150)]
        public string CategoriaAlbumSugerida { get; set; } = string.Empty;

        [MaxLength(200)]
        public string ClasificacionAlbumSugerida { get; set; } = string.Empty;

        [MaxLength(200)]
        public string NombreCientificoSugerido { get; set; } = string.Empty;

        public bool CoincideCatalogoAlbum { get; set; }
        public bool RequiereDecisionClasificacion { get; set; }

        [MaxLength(1000)]
        public string MotivoClasificacionAlbum { get; set; } = string.Empty;

        public int? CategoriaAlbumBotanicoIdSeleccionada { get; set; }
        public int? AlbumBotanicoCafeIdSeleccionado { get; set; }

        [MaxLength(150)]
        public string CategoriaAlbumSeleccionada { get; set; } = string.Empty;

        [MaxLength(200)]
        public string ClasificacionAlbumSeleccionada { get; set; } = string.Empty;

        [MaxLength(40)]
        public string EstadoClasificacionAlbum { get; set; } = string.Empty;

        public DiagnosticoIAImagenJerarquiaReferencia Fotografia { get; set; } = null!;
    }

    [Table("diagnosticoIAClasificacionJerarquia", Schema = "dbo")]
    public sealed class DiagnosticoIAClasificacionJerarquia
    {
        [Key]
        public int DiagnosticoIAClasificacionJerarquiaId { get; set; }

        public int DiagnosticoIAImagenId { get; set; }

        public int? CategoriaAlbumBotanicoIdSugerida { get; set; }
        public int? SubcategoriaAlbumBotanicoIdSugerida { get; set; }
        public int? AlbumBotanicoCafeIdSugerido { get; set; }

        [MaxLength(150)]
        public string CategoriaSugerida { get; set; } = string.Empty;

        [MaxLength(150)]
        public string SubcategoriaSugerida { get; set; } = string.Empty;

        [MaxLength(200)]
        public string FichaSugerida { get; set; } = string.Empty;

        [MaxLength(200)]
        public string NombreCientificoSugerido { get; set; } = string.Empty;

        [MaxLength(1200)]
        public string MotivoSugerencia { get; set; } = string.Empty;

        public int? CategoriaAlbumBotanicoIdSeleccionada { get; set; }
        public int? SubcategoriaAlbumBotanicoIdSeleccionada { get; set; }
        public int? AlbumBotanicoCafeIdSeleccionado { get; set; }

        [MaxLength(150)]
        public string CategoriaSeleccionada { get; set; } = string.Empty;

        [MaxLength(150)]
        public string SubcategoriaSeleccionada { get; set; } = string.Empty;

        [MaxLength(200)]
        public string FichaSeleccionada { get; set; } = string.Empty;

        public bool ProponeCategoria { get; set; }
        public bool ProponeSubcategoria { get; set; }
        public bool ProponeFicha { get; set; }

        [MaxLength(40)]
        public string Estado { get; set; } = "SUGERIDA_IA";

        public int? UsuarioActualizacionId { get; set; }
        public DateTime FechaActualizacionUtc { get; set; } = DateTime.UtcNow;

        public DiagnosticoIAImagenJerarquiaReferencia Fotografia { get; set; } = null!;
    }
}
