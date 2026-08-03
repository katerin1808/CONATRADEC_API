using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
    /// <summary>
    /// Contexto aislado del módulo. Comparte la misma base de datos del
    /// sistema, pero evita modificar el DBContext principal.
    /// </summary>
    public sealed class DiagnosticoIADbContext : DbContext
    {
        public DiagnosticoIADbContext(
            DbContextOptions<DiagnosticoIADbContext> options)
            : base(options)
        {
        }

        public DbSet<DiagnosticoIA> Diagnosticos =>
            Set<DiagnosticoIA>();

        public DbSet<DiagnosticoIAImagen> Imagenes =>
            Set<DiagnosticoIAImagen>();

        public DbSet<DiagnosticoIARevision> RevisionesIA =>
            Set<DiagnosticoIARevision>();

        public DbSet<DiagnosticoIAValidacion> ValidacionesLegadas =>
            Set<DiagnosticoIAValidacion>();

        public DbSet<DiagnosticoIAAnalisisHumano> AnalisisHumanos =>
            Set<DiagnosticoIAAnalisisHumano>();

        public DbSet<DiagnosticoIAAprobacion> Aprobaciones =>
            Set<DiagnosticoIAAprobacion>();

        public DbSet<DiagnosticoIAImagenResultadoIA> ResultadosImagenIA =>
            Set<DiagnosticoIAImagenResultadoIA>();

        public DbSet<DiagnosticoIAImagenEvaluacion> EvaluacionesImagen =>
            Set<DiagnosticoIAImagenEvaluacion>();

        public DbSet<DiagnosticoIAAlbumPublicacion> PublicacionesAlbum =>
            Set<DiagnosticoIAAlbumPublicacion>();

        public DbSet<DiagnosticoIAHistorial> Historial =>
            Set<DiagnosticoIAHistorial>();

        public DbSet<DiagnosticoIAConfiguracion> Configuraciones =>
            Set<DiagnosticoIAConfiguracion>();

        public DbSet<DiagnosticoIAConfiguracionHistorial>
            ConfiguracionHistorial =>
            Set<DiagnosticoIAConfiguracionHistorial>();

        /*
         * Referencias de solo integración con el álbum existente.
         * No cambian sus tablas ni sustituyen sus modelos actuales.
         */
        public DbSet<CategoriaAlbumBotanicoReferencia> CategoriasAlbum =>
            Set<CategoriaAlbumBotanicoReferencia>();

        public DbSet<AlbumBotanicoCafeReferencia> RegistrosAlbum =>
            Set<AlbumBotanicoCafeReferencia>();

        public DbSet<AlbumBotanicoCafeFotoReferencia> FotosAlbum =>
            Set<AlbumBotanicoCafeFotoReferencia>();

        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DiagnosticoIA>()
                .HasIndex(item => new
                {
                    item.UsuarioSolicitanteId,
                    item.FechaSolicitudUtc
                });

            modelBuilder.Entity<DiagnosticoIA>()
                .HasIndex(item => new
                {
                    item.Estado,
                    item.Activo,
                    item.FechaSolicitudUtc
                });

            modelBuilder.Entity<DiagnosticoIAImagen>()
                .HasIndex(item => new
                {
                    item.DiagnosticoIAId,
                    item.Orden
                });

            modelBuilder.Entity<DiagnosticoIARevision>()
                .HasIndex(item => new
                {
                    item.DiagnosticoIAId,
                    item.FechaSolicitudRevisionUtc
                });

            modelBuilder.Entity<DiagnosticoIAAnalisisHumano>()
                .HasIndex(item => new
                {
                    item.DiagnosticoIAId,
                    item.Version
                })
                .IsUnique();

            modelBuilder.Entity<DiagnosticoIAAprobacion>()
                .HasIndex(item => new
                {
                    item.DiagnosticoIAId,
                    item.FechaAprobacionUtc
                });

            modelBuilder.Entity<DiagnosticoIAImagenResultadoIA>()
                .HasIndex(item => item.DiagnosticoIAImagenId)
                .IsUnique();

            modelBuilder.Entity<DiagnosticoIAImagenEvaluacion>()
                .HasIndex(item => new
                {
                    item.DiagnosticoIAAprobacionId,
                    item.DiagnosticoIAImagenId
                })
                .IsUnique();

            modelBuilder.Entity<DiagnosticoIAAlbumPublicacion>()
                .HasIndex(item => new
                {
                    item.DiagnosticoIAImagenId,
                    item.Activo
                });

            modelBuilder.Entity<DiagnosticoIAHistorial>()
                .HasIndex(item => new
                {
                    item.DiagnosticoIAId,
                    item.FechaUtc
                });

            modelBuilder.Entity<DiagnosticoIAConfiguracion>()
                .Property(item => item.RowVersion)
                .IsRowVersion();

            modelBuilder.Entity<DiagnosticoIAConfiguracionHistorial>()
                .HasIndex(item => item.FechaUtc);

            modelBuilder.Entity<DiagnosticoIAImagen>()
                .HasOne(item => item.Diagnostico)
                .WithMany(item => item.Imagenes)
                .HasForeignKey(item => item.DiagnosticoIAId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIARevision>()
                .HasOne(item => item.Diagnostico)
                .WithMany(item => item.RevisionesIA)
                .HasForeignKey(item => item.DiagnosticoIAId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIAValidacion>()
                .HasOne(item => item.Diagnostico)
                .WithMany(item => item.ValidacionesLegadas)
                .HasForeignKey(item => item.DiagnosticoIAId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIAAnalisisHumano>()
                .HasOne(item => item.Diagnostico)
                .WithMany(item => item.AnalisisHumanos)
                .HasForeignKey(item => item.DiagnosticoIAId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIAAprobacion>()
                .HasOne(item => item.Diagnostico)
                .WithMany(item => item.Aprobaciones)
                .HasForeignKey(item => item.DiagnosticoIAId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIAAprobacion>()
                .HasOne(item => item.AnalisisHumano)
                .WithMany(item => item.Aprobaciones)
                .HasForeignKey(item =>
                    item.DiagnosticoIAAnalisisHumanoId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<DiagnosticoIAImagenResultadoIA>()
                .HasOne(item => item.Imagen)
                .WithOne(item => item.ResultadoIA)
                .HasForeignKey<DiagnosticoIAImagenResultadoIA>(
                    item => item.DiagnosticoIAImagenId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIAImagenEvaluacion>()
                .HasOne(item => item.Imagen)
                .WithMany(item => item.Evaluaciones)
                .HasForeignKey(item => item.DiagnosticoIAImagenId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIAImagenEvaluacion>()
                .HasOne(item => item.Aprobacion)
                .WithMany(item => item.EvaluacionesImagen)
                .HasForeignKey(item => item.DiagnosticoIAAprobacionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIAAlbumPublicacion>()
                .HasOne(item => item.Diagnostico)
                .WithMany(item => item.PublicacionesAlbum)
                .HasForeignKey(item => item.DiagnosticoIAId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIAAlbumPublicacion>()
                .HasOne(item => item.Imagen)
                .WithMany(item => item.PublicacionesAlbum)
                .HasForeignKey(item => item.DiagnosticoIAImagenId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<DiagnosticoIAHistorial>()
                .HasOne(item => item.Diagnostico)
                .WithMany(item => item.Historial)
                .HasForeignKey(item => item.DiagnosticoIAId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CategoriaAlbumBotanicoReferencia>()
                .ToTable("CategoriaAlbumBotanico", "dbo");

            modelBuilder.Entity<AlbumBotanicoCafeReferencia>()
                .ToTable("AlbumBotanicoCafe", "dbo");

            modelBuilder.Entity<AlbumBotanicoCafeFotoReferencia>()
                .ToTable("AlbumBotanicoCafeFoto", "dbo");
        }
    }

    [Table("diagnosticoIA", Schema = "dbo")]
    public sealed class DiagnosticoIA
    {
        [Key]
        public int DiagnosticoIAId { get; set; }

        public int? TerrenoId { get; set; }

        [MaxLength(50)]
        public string CodigoTerreno { get; set; } =
            string.Empty;

        public int UsuarioSolicitanteId { get; set; }

        public DateTime FechaSolicitudUtc { get; set; }

        public DateTime? FechaRespuestaIAUtc { get; set; }

        [Required, MaxLength(40)]
        public string Estado { get; set; } =
            DiagnosticoIAFlujo.Estados.AnalizandoIA;

        [Required, MaxLength(80)]
        public string ModeloGemini { get; set; } =
            string.Empty;

        [MaxLength(1000)]
        public string ObservacionUsuario { get; set; } =
            string.Empty;

        public bool ImagenValida { get; set; }

        public bool ParecePlantaCafe { get; set; }

        public bool ResultadoConcluyente { get; set; }

        [MaxLength(30)]
        public string CalidadEvaluacionIA { get; set; } =
            DiagnosticoIAFlujo.CalidadEvaluacion.NoEvaluable;

        [MaxLength(40)]
        public string EstadoGeneralIA { get; set; } =
            DiagnosticoIAFlujo.EstadoGeneral.Indeterminada;

        [MaxLength(50)]
        public string CategoriaPrincipalIA { get; set; } =
            DiagnosticoIAFlujo.Categoria.NoAplica;

        public string CategoriasSecundariasIAJson { get; set; } =
            "[]";

        [MaxLength(300)]
        public string DiagnosticoSugerido { get; set; } =
            string.Empty;

        [MaxLength(80)]
        public string TipoDiagnosticoIA { get; set; } =
            string.Empty;

        [MaxLength(30)]
        public string SeveridadVisualIA { get; set; } =
            DiagnosticoIAFlujo.Severidad.NoEvaluable;

        [MaxLength(30)]
        public string NivelCoincidencia { get; set; } =
            DiagnosticoIAFlujo.Certeza.NoDeterminado;

        [MaxLength(2000)]
        public string Resumen { get; set; } =
            string.Empty;

        public string PartesAfectadasJson { get; set; } =
            "[]";

        public string SintomasVisiblesJson { get; set; } =
            "[]";

        public string EvidenciasNoObservadasJson { get; set; } =
            "[]";

        public string DiagnosticosAlternativosJson { get; set; } =
            "[]";

        public string InformacionFaltanteJson { get; set; } =
            "[]";

        public string RecomendacionesCapturaJson { get; set; } =
            "[]";

        public string AdvertenciasJson { get; set; } =
            "[]";

        public bool PosibleDanoNoBiotico { get; set; }

        [MaxLength(500)]
        public string PosibleCausaNoBiotica { get; set; } =
            string.Empty;

        public string RespuestaOriginalJson { get; set; } =
            string.Empty;

        [MaxLength(2000)]
        public string ErrorAnalisis { get; set; } =
            string.Empty;

        public bool RequiereValidacionHumana { get; set; } =
            true;

        public bool Activo { get; set; } = true;

        public ICollection<DiagnosticoIAImagen> Imagenes { get; set; } =
            new List<DiagnosticoIAImagen>();

        public ICollection<DiagnosticoIARevision> RevisionesIA { get; set; } =
            new List<DiagnosticoIARevision>();

        public ICollection<DiagnosticoIAValidacion> ValidacionesLegadas { get; set; } =
            new List<DiagnosticoIAValidacion>();

        public ICollection<DiagnosticoIAAnalisisHumano> AnalisisHumanos { get; set; } =
            new List<DiagnosticoIAAnalisisHumano>();

        public ICollection<DiagnosticoIAAprobacion> Aprobaciones { get; set; } =
            new List<DiagnosticoIAAprobacion>();

        public ICollection<DiagnosticoIAAlbumPublicacion> PublicacionesAlbum { get; set; } =
            new List<DiagnosticoIAAlbumPublicacion>();

        public ICollection<DiagnosticoIAHistorial> Historial { get; set; } =
            new List<DiagnosticoIAHistorial>();
    }

    [Table("diagnosticoIAImagen", Schema = "dbo")]
    public sealed class DiagnosticoIAImagen
    {
        [Key]
        public int DiagnosticoIAImagenId { get; set; }

        public int DiagnosticoIAId { get; set; }

        [Required, MaxLength(1000)]
        public string UrlImagen { get; set; } =
            string.Empty;

        [Required, MaxLength(600)]
        public string RutaRelativa { get; set; } =
            string.Empty;

        [MaxLength(255)]
        public string NombreArchivoOriginal { get; set; } =
            string.Empty;

        [MaxLength(40)]
        public string TipoFotografia { get; set; } =
            "EVIDENCIA";

        public int Orden { get; set; }

        public DateTime FechaRegistroUtc { get; set; }

        public DiagnosticoIA Diagnostico { get; set; } =
            null!;

        public DiagnosticoIAImagenResultadoIA? ResultadoIA { get; set; }

        public ICollection<DiagnosticoIAImagenEvaluacion> Evaluaciones { get; set; } =
            new List<DiagnosticoIAImagenEvaluacion>();

        public ICollection<DiagnosticoIAAlbumPublicacion> PublicacionesAlbum { get; set; } =
            new List<DiagnosticoIAAlbumPublicacion>();
    }


    [Table("diagnosticoIAImagenResultadoIA", Schema = "dbo")]
    public sealed class DiagnosticoIAImagenResultadoIA
    {
        [Key]
        public int DiagnosticoIAImagenResultadoIAId { get; set; }

        public int DiagnosticoIAImagenId { get; set; }

        public bool ImagenValida { get; set; }

        public bool ParecePlantaCafe { get; set; }

        public bool ResultadoConcluyente { get; set; }

        [MaxLength(80)]
        public string PartePlanta { get; set; } = string.Empty;

        [MaxLength(30)]
        public string CalidadEvaluacion { get; set; } =
            DiagnosticoIAFlujo.CalidadEvaluacion.NoEvaluable;

        [MaxLength(40)]
        public string EstadoGeneral { get; set; } =
            DiagnosticoIAFlujo.EstadoGeneral.Indeterminada;

        [MaxLength(50)]
        public string CategoriaPrincipal { get; set; } =
            DiagnosticoIAFlujo.Categoria.NoAplica;

        public string CategoriasSecundariasJson { get; set; } = "[]";

        [MaxLength(300)]
        public string DiagnosticoProbable { get; set; } = string.Empty;

        [MaxLength(80)]
        public string TipoDiagnostico { get; set; } = string.Empty;

        [MaxLength(30)]
        public string SeveridadVisual { get; set; } =
            DiagnosticoIAFlujo.Severidad.NoEvaluable;

        [MaxLength(30)]
        public string NivelCerteza { get; set; } =
            DiagnosticoIAFlujo.Certeza.NoDeterminado;

        // Clasificación oficial compartida con el Álbum Botánico.
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
        public string EstadoClasificacionAlbum { get; set; } =
            DiagnosticoIAFlujo.ClasificacionAlbum.NoAplica;

        [MaxLength(1600)]
        public string ResumenImagen { get; set; } = string.Empty;

        public string SintomasVisiblesJson { get; set; } = "[]";

        public string EvidenciasObservadasJson { get; set; } = "[]";

        public string EvidenciasNoObservadasJson { get; set; } = "[]";

        public string DiagnosticosAlternativosJson { get; set; } = "[]";

        public string InformacionFaltanteJson { get; set; } = "[]";

        public string RecomendacionesCapturaJson { get; set; } = "[]";

        public string AdvertenciasJson { get; set; } = "[]";

        public DateTime FechaResultadoUtc { get; set; }

        public DiagnosticoIAImagen Imagen { get; set; } = null!;
    }

    /// <summary>
    /// Tabla anterior del módulo. Se conserva para no perder diagnósticos
    /// creados con la primera versión.
    /// </summary>
    [Table("diagnosticoIAValidacion", Schema = "dbo")]
    public sealed class DiagnosticoIAValidacion
    {
        [Key]
        public int DiagnosticoIAValidacionId { get; set; }

        public int DiagnosticoIAId { get; set; }

        public int UsuarioClasificadorId { get; set; }

        [Required, MaxLength(30)]
        public string Decision { get; set; } =
            string.Empty;

        [MaxLength(300)]
        public string DiagnosticoFinal { get; set; } =
            string.Empty;

        public bool? CoincideConGemini { get; set; }

        [MaxLength(2000)]
        public string Observaciones { get; set; } =
            string.Empty;

        public DateTime FechaValidacionUtc { get; set; }

        public DiagnosticoIA Diagnostico { get; set; } =
            null!;
    }

    [Table("diagnosticoIARevision", Schema = "dbo")]
    public sealed class DiagnosticoIARevision
    {
        [Key]
        public int DiagnosticoIARevisionId { get; set; }

        public int DiagnosticoIAId { get; set; }

        public int UsuarioClasificadorId { get; set; }

        [MaxLength(2000)]
        public string RetroalimentacionClasificador { get; set; } =
            string.Empty;

        [MaxLength(300)]
        public string DiagnosticoPropuestoClasificador { get; set; } =
            string.Empty;

        public DateTime FechaSolicitudRevisionUtc { get; set; }

        public DateTime? FechaRespuestaRevisionUtc { get; set; }

        [Required, MaxLength(30)]
        public string Estado { get; set; } =
            DiagnosticoIAFlujo.Estados.AnalizandoIA;

        public bool ImagenValida { get; set; }

        public bool ResultadoConcluyente { get; set; }

        public bool MantieneVeredictoOriginal { get; set; }

        [MaxLength(30)]
        public string RelacionConCriterioTecnico { get; set; } =
            "NO_EVALUABLE";

        [MaxLength(30)]
        public string CalidadEvaluacion { get; set; } =
            DiagnosticoIAFlujo.CalidadEvaluacion.NoEvaluable;

        [MaxLength(40)]
        public string EstadoGeneral { get; set; } =
            DiagnosticoIAFlujo.EstadoGeneral.Indeterminada;

        [MaxLength(50)]
        public string CategoriaPrincipal { get; set; } =
            DiagnosticoIAFlujo.Categoria.NoAplica;

        public string CategoriasSecundariasJson { get; set; } =
            "[]";

        [MaxLength(300)]
        public string DiagnosticoRevisado { get; set; } =
            string.Empty;

        [MaxLength(80)]
        public string TipoDiagnostico { get; set; } =
            string.Empty;

        [MaxLength(30)]
        public string SeveridadVisual { get; set; } =
            DiagnosticoIAFlujo.Severidad.NoEvaluable;

        [MaxLength(30)]
        public string NivelCoincidencia { get; set; } =
            DiagnosticoIAFlujo.Certeza.NoDeterminado;

        [MaxLength(2000)]
        public string ResumenRevision { get; set; } =
            string.Empty;

        public string PartesAfectadasJson { get; set; } =
            "[]";

        public string EvidenciasApoyoJson { get; set; } =
            "[]";

        public string EvidenciasContradiccionJson { get; set; } =
            "[]";

        public string InformacionFaltanteJson { get; set; } =
            "[]";

        public string RecomendacionesCapturaJson { get; set; } =
            "[]";

        public string AdvertenciasJson { get; set; } =
            "[]";

        public string RespuestaOriginalJson { get; set; } =
            string.Empty;

        [MaxLength(2000)]
        public string ErrorRevision { get; set; } =
            string.Empty;

        public DiagnosticoIA Diagnostico { get; set; } =
            null!;
    }

    [Table("diagnosticoIAAnalisisHumano", Schema = "dbo")]
    public sealed class DiagnosticoIAAnalisisHumano
    {
        [Key]
        public int DiagnosticoIAAnalisisHumanoId { get; set; }

        public int DiagnosticoIAId { get; set; }

        public int UsuarioAnalizadorId { get; set; }

        public int Version { get; set; }

        [Required, MaxLength(30)]
        public string EstadoRegistro { get; set; } =
            DiagnosticoIAFlujo.EstadoAnalisisHumano.Borrador;

        [MaxLength(30)]
        public string CalidadEvaluacion { get; set; } =
            DiagnosticoIAFlujo.CalidadEvaluacion.NoEvaluable;

        [MaxLength(40)]
        public string EstadoGeneral { get; set; } =
            DiagnosticoIAFlujo.EstadoGeneral.Indeterminada;

        [MaxLength(50)]
        public string CategoriaPrincipal { get; set; } =
            DiagnosticoIAFlujo.Categoria.NoAplica;

        public string CategoriasSecundariasJson { get; set; } =
            "[]";

        [MaxLength(300)]
        public string DiagnosticoPropuesto { get; set; } =
            string.Empty;

        [MaxLength(80)]
        public string TipoDiagnostico { get; set; } =
            string.Empty;

        [MaxLength(30)]
        public string SeveridadPropuesta { get; set; } =
            DiagnosticoIAFlujo.Severidad.NoEvaluable;

        [MaxLength(30)]
        public string NivelCerteza { get; set; } =
            DiagnosticoIAFlujo.Certeza.NoDeterminado;

        public string PartesAfectadasJson { get; set; } =
            "[]";

        public string EvidenciasObservadasJson { get; set; } =
            "[]";

        [MaxLength(3000)]
        public string Observaciones { get; set; } =
            string.Empty;

        public DateTime FechaCreacionUtc { get; set; }

        public DateTime FechaActualizacionUtc { get; set; }

        public DateTime? FechaEnvioUtc { get; set; }

        public DiagnosticoIA Diagnostico { get; set; } =
            null!;

        public ICollection<DiagnosticoIAAprobacion> Aprobaciones { get; set; } =
            new List<DiagnosticoIAAprobacion>();
    }

    [Table("diagnosticoIAAprobacion", Schema = "dbo")]
    public sealed class DiagnosticoIAAprobacion
    {
        [Key]
        public int DiagnosticoIAAprobacionId { get; set; }

        public int DiagnosticoIAId { get; set; }

        public int DiagnosticoIAAnalisisHumanoId { get; set; }

        public int UsuarioAprobadorId { get; set; }

        [Required, MaxLength(40)]
        public string Decision { get; set; } =
            string.Empty;

        [MaxLength(30)]
        public string CalidadEvaluacionFinal { get; set; } =
            DiagnosticoIAFlujo.CalidadEvaluacion.NoEvaluable;

        [MaxLength(40)]
        public string EstadoGeneralFinal { get; set; } =
            DiagnosticoIAFlujo.EstadoGeneral.Indeterminada;

        [MaxLength(50)]
        public string CategoriaPrincipalFinal { get; set; } =
            DiagnosticoIAFlujo.Categoria.NoAplica;

        public string CategoriasSecundariasFinalJson { get; set; } =
            "[]";

        [MaxLength(300)]
        public string DiagnosticoFinal { get; set; } =
            string.Empty;

        [MaxLength(80)]
        public string TipoDiagnosticoFinal { get; set; } =
            string.Empty;

        [MaxLength(30)]
        public string SeveridadFinal { get; set; } =
            DiagnosticoIAFlujo.Severidad.NoEvaluable;

        [MaxLength(30)]
        public string NivelCertezaFinal { get; set; } =
            DiagnosticoIAFlujo.Certeza.NoDeterminado;

        [MaxLength(3000)]
        public string Observaciones { get; set; } =
            string.Empty;

        public bool AutorizaPublicacionAlbum { get; set; }

        public bool MismoUsuarioQueAnalizo { get; set; }

        public DateTime FechaAprobacionUtc { get; set; }

        public DiagnosticoIA Diagnostico { get; set; } =
            null!;

        public DiagnosticoIAAnalisisHumano AnalisisHumano { get; set; } =
            null!;

        public ICollection<DiagnosticoIAImagenEvaluacion> EvaluacionesImagen { get; set; } =
            new List<DiagnosticoIAImagenEvaluacion>();
    }

    [Table("diagnosticoIAImagenEvaluacion", Schema = "dbo")]
    public sealed class DiagnosticoIAImagenEvaluacion
    {
        [Key]
        public int DiagnosticoIAImagenEvaluacionId { get; set; }

        public int DiagnosticoIAAprobacionId { get; set; }

        public int DiagnosticoIAImagenId { get; set; }

        public int UsuarioAprobadorId { get; set; }

        [MaxLength(30)]
        public string CalidadTecnica { get; set; } =
            DiagnosticoIAFlujo.CalidadImagen.NoEvaluable;

        public bool EsEvidenciaValida { get; set; }

        public bool AptaParaAlbum { get; set; }

        [MaxLength(1000)]
        public string Observacion { get; set; } =
            string.Empty;

        public DateTime FechaEvaluacionUtc { get; set; }

        public DiagnosticoIAAprobacion Aprobacion { get; set; } =
            null!;

        public DiagnosticoIAImagen Imagen { get; set; } =
            null!;
    }

    [Table("diagnosticoIAAlbumPublicacion", Schema = "dbo")]
    public sealed class DiagnosticoIAAlbumPublicacion
    {
        [Key]
        public int DiagnosticoIAAlbumPublicacionId { get; set; }

        public int DiagnosticoIAId { get; set; }

        public int DiagnosticoIAImagenId { get; set; }

        public int CategoriaAlbumBotanicoId { get; set; }

        public int AlbumBotanicoCafeId { get; set; }

        public int AlbumBotanicoCafeFotoId { get; set; }

        public int UsuarioPublicacionId { get; set; }

        public DateTime FechaPublicacionUtc { get; set; }

        [MaxLength(1000)]
        public string DescripcionPublicacion { get; set; } =
            string.Empty;

        [MaxLength(50)]
        public string ClasificacionFinal { get; set; } =
            string.Empty;

        [MaxLength(300)]
        public string DiagnosticoFinal { get; set; } =
            string.Empty;

        [MaxLength(600)]
        public string RutaFotoAlbum { get; set; } =
            string.Empty;

        public bool Activo { get; set; } = true;

        public DiagnosticoIA Diagnostico { get; set; } =
            null!;

        public DiagnosticoIAImagen Imagen { get; set; } =
            null!;
    }

    [Table("diagnosticoIAHistorial", Schema = "dbo")]
    public sealed class DiagnosticoIAHistorial
    {
        [Key]
        public int DiagnosticoIAHistorialId { get; set; }

        public int DiagnosticoIAId { get; set; }

        public int UsuarioId { get; set; }

        [MaxLength(40)]
        public string EstadoAnterior { get; set; } =
            string.Empty;

        [MaxLength(40)]
        public string EstadoNuevo { get; set; } =
            string.Empty;

        [MaxLength(80)]
        public string Accion { get; set; } =
            string.Empty;

        [MaxLength(2000)]
        public string Detalle { get; set; } =
            string.Empty;

        public DateTime FechaUtc { get; set; }

        public DiagnosticoIA Diagnostico { get; set; } =
            null!;
    }

    [Table("CategoriaAlbumBotanico", Schema = "dbo")]
    public sealed class CategoriaAlbumBotanicoReferencia
    {
        [Key]
        [Column("categoriaAlbumBotanicoId")]
        public int CategoriaAlbumBotanicoId { get; set; }

        [Column("nombreCategoria"), MaxLength(150)]
        public string NombreCategoria { get; set; } =
            string.Empty;

        [Column("activo")]
        public bool Activo { get; set; }
    }

    [Table("AlbumBotanicoCafe", Schema = "dbo")]
    public sealed class AlbumBotanicoCafeReferencia
    {
        [Key]
        [Column("albumBotanicoCafeId")]
        public int AlbumBotanicoCafeId { get; set; }

        [Column("categoriaAlbumBotanicoId")]
        public int CategoriaAlbumBotanicoId { get; set; }

        [Column("titulo"), MaxLength(200)]
        public string Titulo { get; set; } =
            string.Empty;

        [Column("nombreCientifico"), MaxLength(200)]
        public string? NombreCientifico { get; set; }

        [Column("descripcion")]
        public string Descripcion { get; set; } = string.Empty;

        [Column("sintomas")]
        public string? Sintomas { get; set; }

        [Column("activo")]
        public bool Activo { get; set; }
    }

    [Table("AlbumBotanicoCafeFoto", Schema = "dbo")]
    public sealed class AlbumBotanicoCafeFotoReferencia
    {
        [Key]
        [Column("albumBotanicoCafeFotoId")]
        public int AlbumBotanicoCafeFotoId { get; set; }

        [Column("albumBotanicoCafeId")]
        public int AlbumBotanicoCafeId { get; set; }

        [Column("rutaFoto"), MaxLength(500)]
        public string RutaFoto { get; set; } =
            string.Empty;

        [Column("descripcionFoto"), MaxLength(500)]
        public string? DescripcionFoto { get; set; }

        [Column("esPortada")]
        public bool EsPortada { get; set; }

        [Column("orden")]
        public int Orden { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;
    }

    [Table("diagnosticoIAConfiguracion", Schema = "dbo")]
    public sealed class DiagnosticoIAConfiguracion
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int DiagnosticoIAConfiguracionId { get; set; } = 1;

        [Range(1, 20)]
        public int MaximoRevisionesGemini { get; set; } = 2;

        public bool RevisionesIlimitadas { get; set; }

        public DateTime FechaModificacionUtc { get; set; }

        public int? UsuarioModificacionId { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = [];
    }

    [Table("diagnosticoIAConfiguracionHistorial", Schema = "dbo")]
    public sealed class DiagnosticoIAConfiguracionHistorial
    {
        [Key]
        public int DiagnosticoIAConfiguracionHistorialId { get; set; }

        public int DiagnosticoIAConfiguracionId { get; set; } = 1;

        public int MaximoAnterior { get; set; }

        public bool IlimitadasAnterior { get; set; }

        public int MaximoNuevo { get; set; }

        public bool IlimitadasNuevo { get; set; }

        public int UsuarioId { get; set; }

        public DateTime FechaUtc { get; set; }
    }

}
