using System.Diagnostics;
using HutterLab.Core;
using HutterLab.Core.Models;
using HutterLab.Core.Pipeline;

namespace HutterLab.Cli;

class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }
        
        if (args[0] == "--version")
        {
            PrintVersion();
            return 0;
        }
        
        if (args[0] == "--list")
        {
            MethodRegistry.PrintList();
            return 0;
        }
        
        // Parse arguments
        string? inputFile = null;
        string? method = null;
        string? pipeline = null;
        bool verbose = false;
        bool verify = false;
        bool sweep = false;
        int sweepTop = 20;
        
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-m" or "--method":
                    method = args[++i];
                    break;
                case "-p" or "--pipeline":
                    pipeline = args[++i];
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                case "--verify":
                    verify = true;
                    break;
                case "--sweep":
                    sweep = true;
                    break;
                case "--sweep-top":
                    sweepTop = int.Parse(args[++i]);
                    break;
                default:
                    if (!args[i].StartsWith('-'))
                        inputFile = args[i];
                    break;
            }
        }
        
        if (inputFile == null)
        {
            Console.Error.WriteLine("Error: No input file specified");
            return 1;
        }
        
        if (!File.Exists(inputFile))
        {
            Console.Error.WriteLine($"Error: File not found: {inputFile}");
            return 1;
        }
        
        // Load input
        PrintBanner();
        Console.WriteLine($"Loading {inputFile}...");
        var data = File.ReadAllBytes(inputFile);
        Console.WriteLine($"Size: {data.Length:N0} bytes\n");
        
        var options = new CompressionOptions { Verbose = verbose };
        
        // Run sweep
        if (sweep)
        {
            PipelineSweep.PrintSweep(data, sweepTop, options);
            return 0;
        }
        
        // Run pipeline
        if (pipeline != null)
        {
            var stages = pipeline.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var pipe = CompressionPipeline.From(stages);
            
            var result = pipe.Compress(data, options);
            PrintResult(result);
            
            if (verify)
                VerifyResult(pipe, data, result, options);
            
            return 0;
        }
        
        // Run single method
        if (method != null)
        {
            var m = MethodRegistry.Get(method);
            var result = m.Compress(data, options);
            PrintResult(result);
            
            if (verify)
                VerifyResult(m, data, result, options);
            
            return 0;
        }
        
        // Run all methods
        Console.WriteLine("Running all methods...\n");
        Console.WriteLine($"{"Method",-25} {"Ratio",10} {"Size",15} {"Saved",15} {"Time",10}");
        Console.WriteLine(new string('─', 75));
        
        var results = new List<CompressionResult>();
        
        foreach (var m in MethodRegistry.List().OrderBy(m => m.Category).ThenBy(m => m.Name))
        {
            try
            {
                var result = m.Compress(data, options);
                results.Add(result);
                
                var flag = result.IsLossless ? "✓" : "~";
                Console.WriteLine($"{flag} {m.Name,-23} {result.Ratio,10:F2}× {result.TotalSize,15:N0} {result.BytesSaved,15:N0} {result.Duration.TotalSeconds,10:F2}s");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ {m.Name,-23} {"ERROR",-10} {ex.Message}");
            }
        }
        
        Console.WriteLine(new string('─', 75));
        
        var best = results.OrderByDescending(r => r.Ratio).First();
        Console.WriteLine($"\nBest: {best.Method} at {best.Ratio:F2}× ({best.TotalSize:N0} bytes)");
        
        return 0;
    }
    
    static void PrintBanner()
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                      HUTTER LAB                              ║");
        Console.WriteLine("║         Compression Research & Experimentation               ║");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║         Holger Morlok & Eve | 2026                           ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }
    
    static void PrintVersion()
    {
        Console.WriteLine("HutterLab v0.1.0");
    }
    
    static void PrintHelp()
    {
        PrintBanner();
        Console.WriteLine("Usage: hutterlab <file> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -m, --method <name>     Run a specific compression method");
        Console.WriteLine("  -p, --pipeline <spec>   Run a pipeline (e.g., 'xml_patterns+brotli')");
        Console.WriteLine("  -v, --verbose           Show detailed output");
        Console.WriteLine("  --verify                Verify decompression matches original");
        Console.WriteLine("  --sweep                 Test all method combinations");
        Console.WriteLine("  --sweep-top <n>         Show top N results (default: 20)");
        Console.WriteLine("  --list                  List all available methods");
        Console.WriteLine("  --help                  Show this help");
        Console.WriteLine("  --version               Show version");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  hutterlab enwik9                          # Run all methods");
        Console.WriteLine("  hutterlab enwik9 -m brotli -v             # Single method, verbose");
        Console.WriteLine("  hutterlab enwik9 -p xml_patterns+brotli   # Pipeline");
        Console.WriteLine("  hutterlab enwik9 --sweep --sweep-top 10   # Find best combinations");
        Console.WriteLine();
    }
    
    static void PrintResult(CompressionResult result)
    {
        Console.WriteLine();
        Console.WriteLine(new string('═', 60));
        Console.WriteLine($"  Method:     {result.Method}");
        Console.WriteLine($"  Original:   {result.OriginalSize:N0} bytes");
        Console.WriteLine($"  Compressed: {result.CompressedSize:N0} bytes");
        if (result.AuxiliarySize > 0)
            Console.WriteLine($"  Auxiliary:  {result.AuxiliarySize:N0} bytes (dictionary/metadata)");
        Console.WriteLine($"  Total:      {result.TotalSize:N0} bytes");
        Console.WriteLine($"  Ratio:      {result.Ratio:F2}× ({result.Percentage:F2}%)");
        Console.WriteLine($"  Saved:      {result.BytesSaved:N0} bytes");
        Console.WriteLine($"  Speed:      {result.SpeedMBps:F1} MB/s");
        Console.WriteLine($"  Time:       {result.Duration.TotalSeconds:F2}s");
        Console.WriteLine($"  Lossless:   {(result.IsLossless ? "Yes" : "No")}");
        Console.WriteLine(new string('═', 60));
        
        // Hutter Prize context
        if (result.OriginalSize == 1_000_000_000)
        {
            Console.WriteLine();
            Console.WriteLine("  Hutter Prize context:");
            Console.WriteLine($"    Current leader: ~114 MB (11.4%)");
            Console.WriteLine($"    Your result:    {result.TotalSize / 1_000_000.0:F1} MB ({result.Percentage:F2}%)");
            
            if (result.TotalSize < 114_000_000)
                Console.WriteLine($"    🎉 BEATS CURRENT LEADER!");
            else
                Console.WriteLine($"    Gap to leader: {(result.TotalSize - 114_000_000) / 1_000_000.0:F1} MB");
        }
    }
    
    static void VerifyResult(Core.Interfaces.ICompressionMethod method, byte[] original, CompressionResult compressed, CompressionOptions options)
    {
        Console.WriteLine("\nVerifying decompression...");
        var sw = Stopwatch.StartNew();
        
        try
        {
            var decompressed = method.Decompress(compressed.CompressedData, options);
            sw.Stop();
            
            if (original.AsSpan().SequenceEqual(decompressed.DecompressedData))
            {
                Console.WriteLine($"  ✓ VERIFIED: Decompression matches original ({sw.Elapsed.TotalSeconds:F2}s)");
            }
            else
            {
                Console.WriteLine($"  ✗ FAILED: Decompression does not match original!");
                Console.WriteLine($"    Original size:     {original.Length:N0}");
                Console.WriteLine($"    Decompressed size: {decompressed.DecompressedSize:N0}");
                
                // Find first difference
                for (int i = 0; i < Math.Min(original.Length, decompressed.DecompressedData.Length); i++)
                {
                    if (original[i] != decompressed.DecompressedData[i])
                    {
                        Console.WriteLine($"    First difference at byte {i}: expected 0x{original[i]:X2}, got 0x{decompressed.DecompressedData[i]:X2}");
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ ERROR during decompression: {ex.Message}");
        }
    }
}
