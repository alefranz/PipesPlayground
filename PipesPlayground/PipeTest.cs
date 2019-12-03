using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PipesPlaygroun
{
    public class PipeTest
    {
        private readonly int _timeout = Debugger.IsAttached ? -1 : 500;

        [Fact]
        public async Task Copy_MultipleSegments()
        {
            var segments = new List<byte[]>
            {
                new byte[] { 1 },
                new byte[] { 2, 3 }
            };
            var receivedSegments = new List<byte[]>();

            var pipe = new Pipe();

            using var cts = new CancellationTokenSource(_timeout);

            var readerTask = ReceiveDataAsync(pipe.Reader, receivedSegments, cts.Token);
            var writerTask = CopyDataAsync(segments, pipe.Writer, cts.Token);

            await Task.WhenAll(readerTask, writerTask);

            Assert.Equal(new byte[] { 1, 2, 3 }, receivedSegments.SelectMany(x => x).ToArray());
        }

        private async Task CopyDataAsync(List<byte[]> segments, PipeWriter writer, CancellationToken cancellationToken)
        {
            foreach (var segment in segments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Copy(segment, writer);

                await writer.FlushAsync();
            }
            await writer.CompleteAsync();
        }

        private static void Copy(byte[] segment, PipeWriter destination)
        {
            var span = destination.GetSpan(segment.Length);

            segment.CopyTo(span);
            destination.Advance(segment.Length);
        }

        private async Task ReceiveDataAsync(PipeReader reader, List<byte[]> receivedSegments, CancellationToken cancellationToken)
        {
            while (true)
            {
                var result = await reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                foreach (var memory in buffer)
                {
                    receivedSegments.Add(memory.ToArray());
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
            await reader.CompleteAsync();
        }
    }
}
