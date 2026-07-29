using Microsoft.EntityFrameworkCore;

namespace CONATRADEC_API.Models
{
    public sealed class ActualizacionesDbContext : DbContext
    {
        public ActualizacionesDbContext(
            DbContextOptions<ActualizacionesDbContext> options)
            : base(options)
        {
        }

        public DbSet<ActualizacionAplicacion> ActualizacionesAplicacion =>
            Set<ActualizacionAplicacion>();

        public DbSet<ActualizacionLlaveDescarga> LlavesDescarga =>
            Set<ActualizacionLlaveDescarga>();

        public DbSet<ActualizacionDescargaAuditoria> AuditoriaDescargas =>
            Set<ActualizacionDescargaAuditoria>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            ConfigurarActualizaciones(modelBuilder);
            ConfigurarLlaves(modelBuilder);
            ConfigurarAuditoria(modelBuilder);
        }

        private static void ConfigurarActualizaciones(
            ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ActualizacionAplicacion>(entity =>
            {
                entity.ToTable("actualizacionAplicacion", "dbo");

                entity.HasKey(x => x.ActualizacionAplicacionId);

                entity.Property(x => x.Plataforma)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.Canal)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.VersionNombre)
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.NotasVersion)
                    .HasMaxLength(4000)
                    .IsRequired();

                entity.Property(x => x.Estado)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.NombreArchivo)
                    .HasMaxLength(260)
                    .IsRequired();

                entity.Property(x => x.NombreArchivoAlmacenado)
                    .HasMaxLength(260)
                    .IsRequired();

                entity.Property(x => x.RutaArchivo)
                    .HasMaxLength(700)
                    .IsRequired();

                entity.Property(x => x.TipoContenido)
                    .HasMaxLength(150)
                    .IsRequired();

                entity.Property(x => x.HashSha256)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(x => x.FechaCreacionUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.FechaUltimaModificacionUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.FechaPublicacionUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.Activo)
                    .HasDefaultValue(true)
                    .IsRequired();

                entity.HasIndex(x => new
                {
                    x.Plataforma,
                    x.Canal,
                    x.VersionCodigo
                }).IsUnique();

                entity.HasIndex(x => new
                {
                    x.Plataforma,
                    x.Canal,
                    x.Estado,
                    x.Activo
                });
            });
        }

        private static void ConfigurarLlaves(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ActualizacionLlaveDescarga>(entity =>
            {
                entity.ToTable("actualizacionLlaveDescarga", "dbo");

                entity.HasKey(x => x.ActualizacionLlaveDescargaId);

                entity.Property(x => x.HashLlave)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(x => x.UltimosCaracteres)
                    .HasMaxLength(4)
                    .IsRequired();

                entity.Property(x => x.Plataforma)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.Canal)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.Estado)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.Destinatario)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(x => x.Observacion)
                    .HasMaxLength(500)
                    .IsRequired();

                entity.Property(x => x.FechaCreacionUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.FechaExpiracionUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.FechaUltimoUsoUtc)
                    .HasColumnType("datetime2(0)");

                entity.Property(x => x.FechaRevocacionUtc)
                    .HasColumnType("datetime2(0)");

                entity.HasIndex(x => x.HashLlave)
                    .IsUnique();

                entity.HasIndex(x => new
                {
                    x.Plataforma,
                    x.Canal,
                    x.Estado,
                    x.Activo
                });

                entity.HasIndex(x => x.FechaExpiracionUtc);
            });
        }

        private static void ConfigurarAuditoria(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ActualizacionDescargaAuditoria>(entity =>
            {
                entity.ToTable("actualizacionDescargaAuditoria", "dbo");

                entity.HasKey(x =>
                    x.ActualizacionDescargaAuditoriaId);

                entity.Property(x => x.OperacionId)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(x => x.Resultado)
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.Detalle)
                    .HasMaxLength(500)
                    .IsRequired();

                entity.Property(x => x.Plataforma)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.Canal)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.VersionNombre)
                    .HasMaxLength(30)
                    .IsRequired();

                entity.Property(x => x.NombreArchivo)
                    .HasMaxLength(260)
                    .IsRequired();

                entity.Property(x => x.IpCliente)
                    .HasMaxLength(80)
                    .IsRequired();

                entity.Property(x => x.EncabezadoForwardedFor)
                    .HasMaxLength(500)
                    .IsRequired();

                entity.Property(x => x.AgenteUsuario)
                    .HasMaxLength(1000)
                    .IsRequired();

                entity.Property(x => x.Navegador)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(x => x.SistemaOperativo)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(x => x.TipoDispositivo)
                    .HasMaxLength(80)
                    .IsRequired();

                entity.Property(x => x.IdentificadorDispositivoWeb)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(x => x.Destinatario)
                    .HasMaxLength(200)
                    .IsRequired();

                entity.Property(x => x.FechaUtc)
                    .HasColumnType("datetime2(0)");

                entity.HasIndex(x => x.FechaUtc);

                entity.HasIndex(x => new
                {
                    x.IpCliente,
                    x.Resultado,
                    x.FechaUtc
                });

                entity.HasIndex(x => new
                {
                    x.OperacionId,
                    x.Resultado
                });
            });
        }
    }
}
