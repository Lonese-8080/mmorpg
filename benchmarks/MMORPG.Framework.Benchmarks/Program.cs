using BenchmarkDotNet.Running;

namespace MMORPG.Framework.Benchmarks;

internal class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
