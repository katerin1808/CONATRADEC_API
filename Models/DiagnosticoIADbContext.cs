using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CONATRADEC_API.Models
{
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

        public DbSet<DiagnosticoIAValidacion> Validaciones =>
            Set<DiagnosticoIAValidacion>();

        public DbSet<DiagnosticoIARevision> Revisiones =>
            Set<DiagnosticoIARevision>();

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

            modelBuilder.Entity<DiagnosticoIAValidacion>()
                .HasIndex(item => new
                {
                    item.DiagnosticoIAId,
                    item.FechaValidacionUtc
                });

            modelBuilder.Entity<DiagnosticoIARevision>()
                .HasIndex(item => new
                {
                    item.DiagnosticoIAId,
                    item.FechaSolicitudRevisionUtc
                });

            modelBuilder.Entity<DiagnosticoIAImagen>()
                .HasOne(item => item.Diagnostico)
                .WithMany(item => item.Imagenes)
                .HasForeignKey(item => item.DiagnosticoIAId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIAValidacion>()
                .HasOne(item => item.Diagnostico)
                .WithMany(item => item.Validaciones)
                .HasForeignKey(item => item.DiagnosticoIAId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DiagnosticoIARevision>()
                .HasOne(item => item.Diagnostico)
                .WithMany(item => item.Revisiones)
                .HasForeignKey(item => item.DiagnosticoIAId)
                .OnDelete(DeleteBehavior.Cascade);
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
            "ANALIZANDO_IA";

        [Required, MaxLength(80)]
        public string ModeloGemini { get; set; } =
            string.Empty;

        [MaxLength(1000)]
        public string ObservacionUsuario { get; set; } =
            string.Empty;

        public bool ImagenValida { get; set; }

        public bool ParecePlantaCafe { get; set; }

        public bool ResultadoConcluyente { get; set; }

        public bool PosibleDanoNoBiotico { get; set; }

        [MaxLength(300)]
        public string DiagnosticoSugerido { get; set; } =
            string.Empty;

        [MaxLength(30)]
        public string NivelCoincidencia { get; set; } =
            "NO_DETERMINADO";

        [MaxLength(2000)]
        public string Resumen { get; set; } =
            string.Empty;

        [MaxLength(500)]
        public string PosibleCausaNoBiotica { get; set; } =
            string.Empty;

        public string SintomasVisiblesJson { get; set; } =
            "[]";

        public string DiagnosticosAlternativosJson { get; set; } =
            "[]";

        public string RecomendacionesCapturaJson { get; set; } =
            "[]";

        public string AdvertenciasJson { get; set; } =
            "[]";

        public string RespuestaOriginalJson { get; set; } =
            string.Empty;

        [MaxLength(2000)]
        public string ErrorAnalisis { get; set; } =
            string.Empty;

        public bool RequiereValidacionHumana { get; set; } =
            true;

        public bool Activo { get; set; } = true;

        public ICollection<DiagnosticoIAImagen> Imagenes { get; set; }
            = new List<DiagnosticoIAImagen>();

        public ICollection<DiagnosticoIAValidacion> Validaciones { get; set; }
            = new List<DiagnosticoIAValidacion>();

        public ICollection<DiagnosticoIARevision> Revisiones { get; set; }
            = new List<DiagnosticoIARevision>();
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
    }

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

    /// <summary>
    /// Conserva cada segunda opinión emitida por Gemini después de recibir
    /// observaciones de la persona clasificadora. Nunca sustituye el primer
    /// veredicto ni la validación humana final.
    /// </summary>
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
            "ANALIZANDO_IA";

        public bool ImagenValida { get; set; }

        public bool ResultadoConcluyente { get; set; }

        public bool MantieneVeredictoOriginal { get; set; }

        [MaxLength(30)]
        public string RelacionConCriterioTecnico { get; set; } =
            "NO_EVALUABLE";

        [MaxLength(300)]
        public string DiagnosticoRevisado { get; set; } =
            string.Empty;

        [MaxLength(30)]
        public string NivelCoincidencia { get; set; } =
            "NO_DETERMINADO";

        [MaxLength(2000)]
        public string ResumenRevision { get; set; } =
            string.Empty;

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
}
