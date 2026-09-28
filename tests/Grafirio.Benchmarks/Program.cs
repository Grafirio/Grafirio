using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

// Ortak ayar: bellek olcumu ve BenchmarkDotNet'in "full" JSON raporu. Dashboard
// bu JSON'u dogrudan iceri aliyor (grafirio-measure publish-bdn); Grafirio
// tarafinda dashboard'a ait hicbir kod yok.
var config = DefaultConfig.Instance
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(JsonExporter.Full);

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
