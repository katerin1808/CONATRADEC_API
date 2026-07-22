using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace CONATRADEC_API.Auditing
{
    /// <summary>
    /// Detecta cambios efectuados mediante el DBContext principal.
    /// Si existe una transacción explícita, los cambios no se confirman en la
    /// bitácora hasta que AuditTransactionInterceptor recibe el Commit.
    /// </summary>
    public sealed class AuditSaveChangesInterceptor :
        SaveChangesInterceptor
    {
        private readonly AuditRequestContext auditContext;
        private readonly object sincronizacion = new();

        private List<CambioPendiente> pendientes = new();

        public AuditSaveChangesInterceptor(
            AuditRequestContext auditContext)
        {
            this.auditContext = auditContext;
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            PrepararCambios(eventData.Context);

            return base.SavingChanges(
                eventData,
                result);
        }

        public override ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            PrepararCambios(eventData.Context);

            return base.SavingChangesAsync(
                eventData,
                result,
                cancellationToken);
        }

        public override int SavedChanges(
            SaveChangesCompletedEventData eventData,
            int result)
        {
            CompletarCambios(eventData.Context);

            return base.SavedChanges(
                eventData,
                result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            CompletarCambios(eventData.Context);

            return base.SavedChangesAsync(
                eventData,
                result,
                cancellationToken);
        }

        public override void SaveChangesFailed(
            DbContextErrorEventData eventData)
        {
            DescartarPendientes();

            base.SaveChangesFailed(eventData);
        }

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            DescartarPendientes();

            return base.SaveChangesFailedAsync(
                eventData,
                cancellationToken);
        }

        public override void SaveChangesCanceled(
            DbContextEventData eventData)
        {
            DescartarPendientes();

            base.SaveChangesCanceled(eventData);
        }

        public override Task SaveChangesCanceledAsync(
            DbContextEventData eventData,
            CancellationToken cancellationToken = default)
        {
            DescartarPendientes();

            return base.SaveChangesCanceledAsync(
                eventData,
                cancellationToken);
        }

        private void PrepararCambios(
            DbContext? dbContext)
        {
            lock (sincronizacion)
            {
                pendientes = new List<CambioPendiente>();

                if (!auditContext.Activo ||
                    dbContext == null)
                {
                    return;
                }

                dbContext.ChangeTracker.DetectChanges();

                IEnumerable<EntityEntry> entradas =
                    dbContext.ChangeTracker
                        .Entries()
                        .Where(x =>
                            x.State == EntityState.Added ||
                            x.State == EntityState.Modified ||
                            x.State == EntityState.Deleted)
                        .Where(x =>
                            x.Metadata.ClrType.Name != "Bitacora" &&
                            x.Metadata.ClrType.Name !=
                                "BitacoraDetalle");

                foreach (EntityEntry entrada in entradas)
                {
                    string operacion = entrada.State switch
                    {
                        EntityState.Added => "CREAR",
                        EntityState.Modified => "ACTUALIZAR",
                        EntityState.Deleted => "ELIMINAR",
                        _ => "CAMBIAR"
                    };

                    List<PropertyEntry> propiedades =
                        entrada.State == EntityState.Modified
                            ? entrada.Properties
                                .Where(x => x.IsModified)
                                .ToList()
                            : entrada.Properties.ToList();

                    if (propiedades.Count == 0)
                        continue;

                    var anteriores =
                        new Dictionary<string, object?>();

                    var nuevos =
                        new Dictionary<string, object?>();

                    var modificadas = new List<string>();

                    foreach (PropertyEntry propiedad in propiedades)
                    {
                        string nombre =
                            propiedad.Metadata.Name;

                        modificadas.Add(nombre);

                        if (AuditSanitizer.EsSensible(nombre))
                        {
                            if (entrada.State !=
                                EntityState.Added)
                            {
                                anteriores[nombre] =
                                    "***PROTEGIDO***";
                            }

                            if (entrada.State !=
                                EntityState.Deleted)
                            {
                                nuevos[nombre] =
                                    "***PROTEGIDO***";
                            }

                            continue;
                        }

                        if (entrada.State ==
                                EntityState.Modified ||
                            entrada.State ==
                                EntityState.Deleted)
                        {
                            anteriores[nombre] =
                                NormalizarValor(
                                    propiedad.OriginalValue);
                        }

                        if (entrada.State ==
                                EntityState.Modified ||
                            entrada.State ==
                                EntityState.Added)
                        {
                            nuevos[nombre] =
                                NormalizarValor(
                                    propiedad.CurrentValue);
                        }
                    }

                    pendientes.Add(new CambioPendiente
                    {
                        Entrada = entrada,
                        Entidad =
                            entrada.Metadata.ClrType.Name,
                        Operacion = operacion,
                        ValoresAnteriores =
                            Serializar(anteriores),
                        ValoresNuevos =
                            Serializar(nuevos),
                        PropiedadesModificadas =
                            Serializar(modificadas)
                    });
                }
            }
        }

        private void CompletarCambios(
            DbContext? dbContext)
        {
            List<CambioPendiente> cambiosProcesados;

            lock (sincronizacion)
            {
                if (!auditContext.Activo ||
                    pendientes.Count == 0)
                {
                    pendientes.Clear();
                    return;
                }

                cambiosProcesados = pendientes.ToList();
                pendientes.Clear();
            }

            Guid? transaccionId =
                dbContext?
                    .Database
                    .CurrentTransaction?
                    .TransactionId;

            foreach (CambioPendiente pendiente
                     in cambiosProcesados)
            {
                auditContext.AgregarCambio(
                    new AuditEntityChange
                    {
                        FechaHoraUtc = DateTime.UtcNow,
                        Entidad = pendiente.Entidad,
                        EntidadId =
                            ObtenerLlave(pendiente.Entrada),
                        Operacion = pendiente.Operacion,
                        ValoresAnteriores =
                            pendiente.ValoresAnteriores,
                        ValoresNuevos =
                            pendiente.ValoresNuevos,
                        PropiedadesModificadas =
                            pendiente.PropiedadesModificadas
                    },
                    transaccionId);
            }
        }

        private void DescartarPendientes()
        {
            lock (sincronizacion)
            {
                pendientes.Clear();
            }
        }

        private static string ObtenerLlave(
            EntityEntry entrada)
        {
            var llave =
                entrada.Metadata.FindPrimaryKey();

            if (llave == null)
                return string.Empty;

            return string.Join(
                ", ",
                llave.Properties.Select(propiedad =>
                {
                    object? valor = entrada
                        .Property(propiedad.Name)
                        .CurrentValue;

                    return $"{propiedad.Name}={valor}";
                }));
        }

        private static object? NormalizarValor(
            object? valor)
        {
            return valor switch
            {
                null => null,
                byte[] bytes =>
                    $"[BINARIO: {bytes.Length} bytes]",
                DateTime fecha => fecha.ToString("O"),
                DateTimeOffset fecha => fecha.ToString("O"),
                DateOnly fecha =>
                    fecha.ToString("yyyy-MM-dd"),
                TimeOnly hora =>
                    hora.ToString("HH:mm:ss.fffffff"),
                string texto =>
                    AuditSanitizer.Truncar(
                        texto,
                        3000),
                _ => valor
            };
        }

        private static string Serializar(
            object valor)
        {
            return JsonSerializer.Serialize(
                valor,
                new JsonSerializerOptions
                {
                    WriteIndented = false
                });
        }

        private sealed class CambioPendiente
        {
            public EntityEntry Entrada { get; init; } =
                null!;

            public string Entidad { get; init; } =
                string.Empty;

            public string Operacion { get; init; } =
                string.Empty;

            public string ValoresAnteriores { get; init; } =
                string.Empty;

            public string ValoresNuevos { get; init; } =
                string.Empty;

            public string PropiedadesModificadas { get; init; } =
                string.Empty;
        }
    }
}
