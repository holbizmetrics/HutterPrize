using System.Diagnostics;
using HutterLab.Core.Interfaces;
using HutterLab.Core.Models;

namespace HutterLab.Core.Pipeline;

/// <summary>
/// A pipeline that chains multiple compression methods together.
/// Preprocessing → Preprocessing → ... → Backend
/// </summary>
public sealed class CompressionPipeline : ICompressionMethod
{
    private readonly List<ICompressionMethod> _stages = [];
    private string? _customName;
    
    public string Name => _customName ?? string.Join(" → ", _stages.Select(s => s.Name));
    public string Description => $"Pipeline with {_stages.Count} stages";
    public string Category => "Pipeline";
    public bool IsLossless => _stages.All(s => s.IsLossless);
    public bool IsChainable => false;
    
    /// <summary>
    /// Add a method to the pipeline by name.
    /// </summary>
    public CompressionPipeline Add(string methodName)
    {
        var method = MethodRegistry.Get(methodName);
        return Add(method);
    }
    
    /// <summary>
    /// Add a method instance to the pipeline.
    /// </summary>
    public CompressionPipeline Add(ICompressionMethod method)
    {
        _stages.Add(method);
        return this;
    }
    
    /// <summary>
    /// Set a custom name for this pipeline.
    /// </summary>
    public CompressionPipeline WithName(string name)
    {
        _customName = name;
        return this;
    }
    
    /// <summary>
    /// Compress through all stages in order.
    /// </summary>
    public CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
    {
        options ??= CompressionOptions.Default;
        
        if (_stages.Count == 0)
            throw new InvalidOperationException("Pipeline has no stages");
        
        var sw = Stopwatch.StartNew();
        var originalSize = data.Length;
        var currentData = data.ToArray();
        long totalAuxSize = 0;
        var stageResults = new List<CompressionResult>();
        
        if (options.Verbose)
        {
            Console.WriteLine($"\n  Pipeline: {Name}");
            Console.WriteLine($"  Input: {originalSize:N0} bytes");
            Console.WriteLine(new string('─', 50));
        }
        
        foreach (var stage in _stages)
        {
            var stageResult = stage.Compress(currentData, options);
            stageResults.Add(stageResult);
            
            totalAuxSize += stageResult.AuxiliarySize;
            currentData = stageResult.CompressedData;
            
            if (options.Verbose)
            {
                Console.WriteLine($"  {stage.Name,-20} → {currentData.Length:N0} bytes ({stageResult.Ratio:F2}×)");
            }
        }
        
        sw.Stop();
        
        if (options.Verbose)
        {
            Console.WriteLine(new string('─', 50));
            Console.WriteLine($"  Total: {originalSize:N0} → {currentData.Length + totalAuxSize:N0} bytes ({(double)originalSize / (currentData.Length + totalAuxSize):F2}×)");
        }
        
        return new CompressionResult
        {
            Method = Name,
            OriginalSize = originalSize,
            CompressedSize = currentData.Length,
            CompressedData = currentData,
            AuxiliarySize = totalAuxSize,
            Duration = sw.Elapsed,
            IsLossless = IsLossless,
            Metadata = new Dictionary<string, object>
            {
                ["stages"] = _stages.Select(s => s.Name).ToArray(),
                ["stage_results"] = stageResults
            }
        };
    }
    
    /// <summary>
    /// Decompress through all stages in reverse order.
    /// </summary>
    public DecompressionResult Decompress(ReadOnlySpan<byte> compressedData, CompressionOptions? options = null)
    {
        options ??= CompressionOptions.Default;
        
        if (_stages.Count == 0)
            throw new InvalidOperationException("Pipeline has no stages");
        
        var sw = Stopwatch.StartNew();
        var currentData = compressedData.ToArray();
        
        // Decompress in reverse order
        foreach (var stage in _stages.AsEnumerable().Reverse())
        {
            var result = stage.Decompress(currentData, options);
            currentData = result.DecompressedData;
        }
        
        sw.Stop();
        
        return new DecompressionResult
        {
            Method = Name,
            CompressedSize = compressedData.Length,
            DecompressedSize = currentData.Length,
            DecompressedData = currentData,
            Duration = sw.Elapsed
        };
    }
    
    /// <summary>
    /// Create a pipeline from a list of method names.
    /// </summary>
    public static CompressionPipeline From(params string[] methodNames)
    {
        var pipeline = new CompressionPipeline();
        foreach (var name in methodNames)
            pipeline.Add(name);
        return pipeline;
    }
}

/// <summary>
/// Utility for testing all combinations of methods.
/// </summary>
public static class PipelineSweep
{
    /// <summary>
    /// Test all valid combinations of preprocessing + backend methods.
    /// </summary>
    public static IEnumerable<(CompressionPipeline Pipeline, CompressionResult Result)> Sweep(
        byte[] data,
        CompressionOptions? options = null)
    {
        var preprocessing = MethodRegistry.ListByCategory("Preprocessing");
        var backends = MethodRegistry.ListByCategory("Backend");
        
        // Test backends alone
        foreach (var backend in backends)
        {
            var pipeline = new CompressionPipeline().Add(backend);
            var result = pipeline.Compress(data, options);
            yield return (pipeline, result);
        }
        
        // Test single preprocessing + backend
        foreach (var pre in preprocessing)
        {
            foreach (var backend in backends)
            {
                var pipeline = new CompressionPipeline().Add(pre).Add(backend);
                var result = pipeline.Compress(data, options);
                yield return (pipeline, result);
            }
        }
        
        // Test pairs of preprocessing + backend
        for (int i = 0; i < preprocessing.Count; i++)
        {
            for (int j = i + 1; j < preprocessing.Count; j++)
            {
                foreach (var backend in backends)
                {
                    var pipeline = new CompressionPipeline()
                        .Add(preprocessing[i])
                        .Add(preprocessing[j])
                        .Add(backend);
                    var result = pipeline.Compress(data, options);
                    yield return (pipeline, result);
                }
            }
        }
    }
    
    /// <summary>
    /// Run sweep and print results sorted by ratio.
    /// </summary>
    public static void PrintSweep(byte[] data, int topN = 20, CompressionOptions? options = null)
    {
        Console.WriteLine($"\nRunning pipeline sweep on {data.Length:N0} bytes...\n");
        
        var results = Sweep(data, options)
            .OrderByDescending(r => r.Result.Ratio)
            .Take(topN)
            .ToList();
        
        Console.WriteLine($"Top {topN} pipelines by compression ratio:");
        Console.WriteLine(new string('═', 80));
        Console.WriteLine($"{"Rank",-5} {"Pipeline",-40} {"Ratio",10} {"Size",15} {"Time",10}");
        Console.WriteLine(new string('─', 80));
        
        int rank = 1;
        foreach (var (pipeline, result) in results)
        {
            Console.WriteLine($"{rank,-5} {pipeline.Name,-40} {result.Ratio,10:F2}× {result.TotalSize,15:N0} {result.Duration.TotalSeconds,10:F2}s");
            rank++;
        }
        
        Console.WriteLine(new string('═', 80));
    }
}