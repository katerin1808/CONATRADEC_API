namespace CONATRADEC_API.Middleware
{
    /// <summary>
    /// Envía la respuesta normalmente al cliente y conserva únicamente los
    /// primeros bytes necesarios para auditar mensajes de error. Nunca carga
    /// un PDF, una imagen o una respuesta grande completa en memoria.
    /// </summary>
    internal sealed class LimitedResponseCaptureStream :
        Stream
    {
        private readonly Stream destino;
        private readonly MemoryStream captura;
        private readonly int maximoBytes;

        public LimitedResponseCaptureStream(
            Stream destino,
            int maximoBytes)
        {
            this.destino = destino ??
                throw new ArgumentNullException(
                    nameof(destino));

            this.maximoBytes =
                Math.Max(0, maximoBytes);

            captura = new MemoryStream(
                Math.Min(
                    this.maximoBytes,
                    16 * 1024));
        }

        public byte[] ObtenerBytes()
        {
            return captura.ToArray();
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite =>
            destino.CanWrite;

        public override long Length =>
            throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            destino.Flush();
        }

        public override Task FlushAsync(
            CancellationToken cancellationToken)
        {
            return destino.FlushAsync(
                cancellationToken);
        }

        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
            Capturar(
                buffer.AsSpan(offset, count));

            destino.Write(
                buffer,
                offset,
                count);
        }

        public override async Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            Capturar(
                buffer.AsSpan(offset, count));

            await destino.WriteAsync(
                buffer.AsMemory(offset, count),
                cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Capturar(buffer.Span);

            return destino.WriteAsync(
                buffer,
                cancellationToken);
        }

        public override void WriteByte(
            byte value)
        {
            if (captura.Length < maximoBytes)
                captura.WriteByte(value);

            destino.WriteByte(value);
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(
            long offset,
            SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(
            long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(
            bool disposing)
        {
            if (disposing)
                captura.Dispose();

            // No se elimina el stream de respuesta original.
            base.Dispose(disposing);
        }

        private void Capturar(
            ReadOnlySpan<byte> datos)
        {
            int disponibles =
                maximoBytes - (int)captura.Length;

            if (disponibles <= 0 ||
                datos.Length == 0)
            {
                return;
            }

            int cantidad =
                Math.Min(
                    disponibles,
                    datos.Length);

            captura.Write(
                datos[..cantidad]);
        }
    }
}
