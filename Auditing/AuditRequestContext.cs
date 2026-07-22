using System.Collections.Concurrent;

namespace CONATRADEC_API.Auditing
{
    /// <summary>
    /// Mantiene los cambios detectados durante una única solicitud HTTP.
    /// Los cambios de una transacción explícita permanecen pendientes hasta
    /// que Entity Framework confirma el Commit.
    /// </summary>
    public sealed class AuditRequestContext
    {
        private readonly ConcurrentQueue<AuditEntityChange>
            cambiosConfirmados = new();

        private readonly ConcurrentDictionary<
            Guid,
            ConcurrentQueue<AuditEntityChange>>
            cambiosPendientesPorTransaccion = new();

        public bool Activo { get; private set; }

        public Guid BitacoraId { get; private set; }

        /// <summary>
        /// Solo expone cambios definitivamente confirmados.
        /// Los cambios de transacciones abiertas o revertidas no aparecen.
        /// </summary>
        public IReadOnlyCollection<AuditEntityChange> Cambios =>
            cambiosConfirmados.ToArray();

        public void Iniciar(Guid bitacoraId)
        {
            Limpiar();

            BitacoraId = bitacoraId;
            Activo = true;
        }

        public void AgregarCambio(
            AuditEntityChange cambio,
            Guid? transaccionId = null)
        {
            if (!Activo || cambio == null)
                return;

            if (transaccionId.HasValue &&
                transaccionId.Value != Guid.Empty)
            {
                ConcurrentQueue<AuditEntityChange> pendientes =
                    cambiosPendientesPorTransaccion.GetOrAdd(
                        transaccionId.Value,
                        _ => new ConcurrentQueue<AuditEntityChange>());

                pendientes.Enqueue(cambio);
                return;
            }

            cambiosConfirmados.Enqueue(cambio);
        }

        /// <summary>
        /// Mueve a la colección final todos los cambios de una transacción
        /// que fue confirmada satisfactoriamente.
        /// </summary>
        public void ConfirmarTransaccion(Guid transaccionId)
        {
            if (!Activo || transaccionId == Guid.Empty)
                return;

            if (!cambiosPendientesPorTransaccion.TryRemove(
                    transaccionId,
                    out ConcurrentQueue<AuditEntityChange>? pendientes))
            {
                return;
            }

            while (pendientes.TryDequeue(out AuditEntityChange? cambio))
            {
                cambiosConfirmados.Enqueue(cambio);
            }
        }

        /// <summary>
        /// Descarta los cambios de una transacción que fue revertida o falló.
        /// </summary>
        public void RevertirTransaccion(Guid transaccionId)
        {
            if (transaccionId == Guid.Empty)
                return;

            cambiosPendientesPorTransaccion.TryRemove(
                transaccionId,
                out _);
        }

        public void Limpiar()
        {
            while (cambiosConfirmados.TryDequeue(out _))
            {
            }

            cambiosPendientesPorTransaccion.Clear();

            BitacoraId = Guid.Empty;
            Activo = false;
        }
    }

    public sealed class AuditEntityChange
    {
        public DateTime FechaHoraUtc { get; init; }

        public string Entidad { get; init; } = string.Empty;

        public string EntidadId { get; init; } = string.Empty;

        public string Operacion { get; init; } = string.Empty;

        public string ValoresAnteriores { get; init; } = string.Empty;

        public string ValoresNuevos { get; init; } = string.Empty;

        public string PropiedadesModificadas { get; init; } =
            string.Empty;
    }
}
