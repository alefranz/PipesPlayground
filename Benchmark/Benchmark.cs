using BenchmarkDotNet.Attributes;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Benchmark
{
    public class Benchmark
    {
        private readonly byte[] _data = new byte[1024 * 1024];
        private readonly byte[] _destination = new byte[1024 * 1024];
        private readonly byte[][] _buffers = new byte[1024 / 4][];
        private const int _chunkSize = 4 * 1024;

        public Benchmark()
        {
            for(int i = 0; i < _buffers.Length; i++)
            {
                _buffers[i] = new byte[_chunkSize];
            }
        }

        [Benchmark]
        public void CopyMemory()
        {
            var sourceMemory = new ReadOnlyMemory<byte>(_data);
            var destinationMemory = _destination.AsMemory();

            sourceMemory.CopyTo(destinationMemory);
        }

        [Benchmark]
        public void CopyMemoryChunks()
        {
            var sourceMemory = new ReadOnlyMemory<byte>(_data);
            var destinationMemory = _destination.AsMemory();

            for (int position = 0; position < _data.Length; position += _chunkSize) {
                var source = sourceMemory.Slice(position, _chunkSize);
                var destination = destinationMemory.Slice(position, _chunkSize);

                source.CopyTo(destination);
            }
        }

        [Benchmark]
        public void CopyMemoryChunksWithBufferAsReadOnlySequence()
        {
            // Trying to be vaguely similar to pipe behaviour for happy path

            var sourceMemory = new ReadOnlyMemory<byte>(_data);

            for (int i=0, sorucePosition = 0; sorucePosition < _data.Length; sorucePosition += _chunkSize, i++)
            {
                var source = sourceMemory.Slice(sorucePosition, _chunkSize);
                var destination = _buffers[i].AsMemory();

                source.CopyTo(destination);
            }

            var segments = ArraySegment.Create(_buffers);
            var destinationMemory = _destination.AsMemory();

            var destinationPosition = 0;
            foreach (var segment in segments)
            {
                var destination = destinationMemory.Slice(destinationPosition, _chunkSize);
                segment.CopyTo(destination);
                destinationPosition += _chunkSize;
            }
        }

        [Benchmark]
        public async Task CopyPipeAsync()
        {
            var pipe = new Pipe();
            var consumer = ReceiveDataAsync(pipe.Reader, _destination, CancellationToken.None);
            var producer = WriteDataAsync(pipe.Writer, new ReadOnlyMemory<byte>(_data));
            await Task.WhenAll(consumer, producer);
        }

        [Benchmark]
        public async Task CopyPipeAsStreamAsync()
        {
            var pipe = new Pipe();
            var consumer = ReceiveDataAsync(pipe.Reader, _destination.AsMemory(), CancellationToken.None);
            using (var stream = pipe.Writer.AsStream())
            {
                stream.Write(_data, 0, _data.Length);
            }
            await consumer;
        }

        private async Task WriteDataAsync(PipeWriter writer, ReadOnlyMemory<byte> data)
        {
            await writer.WriteAsync(data);
            await writer.FlushAsync();
            await writer.CompleteAsync();
        }

        private async Task ReceiveDataAsync(PipeReader reader, Memory<byte> destinationMemory, CancellationToken cancellationToken)
        {
            var position = 0;

            while (true)
            {
                var result = await reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                foreach (var memory in buffer)
                {
                    memory.CopyTo(destinationMemory.Slice(position, memory.Length));
                    position += memory.Length;
                }
                
                reader.AdvanceTo(buffer.End, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
            await reader.CompleteAsync();
        }

        private class ArraySegment : ReadOnlySequenceSegment<byte>
        {
            public static ReadOnlySequence<byte> Create(byte[][] buffer)
            {
                ArraySegment segment = null;
                ArraySegment endSegment = null;
                
                int index = buffer.Length * _chunkSize;

                for (int i = buffer.Length - 1; i >= 0; i--) {
                    index -= _chunkSize;

                    segment = new ArraySegment
                    {
                        Memory = buffer[i].AsMemory(),
                        Next = segment,
                        RunningIndex = index
                    };

                    if (i == buffer.Length - 1) endSegment = segment;
                }

                return new ReadOnlySequence<byte>(segment, 0, endSegment, buffer.Length - 1);
            }
        }
    }
}
