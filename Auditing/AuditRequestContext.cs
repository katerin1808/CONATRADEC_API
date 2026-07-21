using System.Collections.Concurrent;

namespace CONATRADEC_API.Auditing
{
    public sealed class AuditRequestContext
    {
        private readonly ConcurrentQueue<AuditEntityChange> cambios = new();

        public bool Activo { get; private set; }
        public Guid BitacoraId { get; private set; }

        public IReadOnlyCollection<AuditEntityChange> Cambios =>
            cambios.ToArray();

        public void Iniciar(Guid bitacoraId)
        {
            Limpiar();
            BitacoraId = bitacoraId;
            Activo = true;
        }

        public void AgregarCambio(AuditEntityChange cambio)
        {
            if (Activo)
                cambios.Enqueue(cambio);
        }

        public void Limpiar()
        {
            while (cambios.TryDequeue(out _))
            {
            }

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
        public string PropiedadesModificadas { get; init; } = string.Empty;
    }
}
