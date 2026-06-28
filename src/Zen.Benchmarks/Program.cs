using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;

namespace Zen.Benchmarks;

internal static class Program
{
    private static void Main(string[] args)
    {
        // `dotnet run -c Release -- --mem` => standalone memory report.
        // otherwise => BenchmarkDotNet throughput suite.
        if (args.Contains("--mem"))
        {
            MemoryReport.Run();
            return;
        }

        var config = new ManualConfig()
            .AddLogger(ConsoleLogger.Default)
            .AddExporter(MarkdownExporter.GitHub)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddJob(Job.Default
                .WithRuntime(CoreRuntime.Core80)
                .WithWarmupCount(3)
                .WithIterationCount(5)
                .WithMinIterationCount(5)
                .WithMaxIterationCount(10));

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
