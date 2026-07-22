using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace CONATRADEC_API.Auditing
{
    /// <summary>
    /// Confirma o descarta los cambios de auditoría asociados a transacciones
    /// explícitas. Evita que un Rollback aparezca como cambio definitivo.
    /// </summary>
    public sealed class AuditTransactionInterceptor :
        DbTransactionInterceptor
    {
        private readonly AuditRequestContext auditContext;

        public AuditTransactionInterceptor(
            AuditRequestContext auditContext)
        {
            this.auditContext = auditContext;
        }

        public override void TransactionCommitted(
            DbTransaction transaction,
            TransactionEndEventData eventData)
        {
            auditContext.ConfirmarTransaccion(
                eventData.TransactionId);

            base.TransactionCommitted(
                transaction,
                eventData);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            auditContext.ConfirmarTransaccion(
                eventData.TransactionId);

            return base.TransactionCommittedAsync(
                transaction,
                eventData,
                cancellationToken);
        }

        public override void TransactionRolledBack(
            DbTransaction transaction,
            TransactionEndEventData eventData)
        {
            auditContext.RevertirTransaccion(
                eventData.TransactionId);

            base.TransactionRolledBack(
                transaction,
                eventData);
        }

        public override Task TransactionRolledBackAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            auditContext.RevertirTransaccion(
                eventData.TransactionId);

            return base.TransactionRolledBackAsync(
                transaction,
                eventData,
                cancellationToken);
        }

        public override void TransactionFailed(
            DbTransaction transaction,
            TransactionErrorEventData eventData)
        {
            auditContext.RevertirTransaccion(
                eventData.TransactionId);

            base.TransactionFailed(
                transaction,
                eventData);
        }

        public override Task TransactionFailedAsync(
            DbTransaction transaction,
            TransactionErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            auditContext.RevertirTransaccion(
                eventData.TransactionId);

            return base.TransactionFailedAsync(
                transaction,
                eventData,
                cancellationToken);
        }
    }
}
