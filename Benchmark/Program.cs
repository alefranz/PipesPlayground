using BenchmarkDotNet.Running;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Benchmark
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (Debugger.IsAttached)
            {
                var benchmark = new Benchmark();
                benchmark.CopyMemoryChunksWithBuffers();
                await benchmark.CopyPipeAsync();
            }
            else
            {
                var summary = BenchmarkRunner.Run<Benchmark>();
            }
        }
    }
}
