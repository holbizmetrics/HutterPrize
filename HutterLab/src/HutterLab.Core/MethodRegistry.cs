using HutterLab.Core.Interfaces;

namespace HutterLab.Core;

/// <summary>
/// Registry for all available compression methods.
/// Methods are auto-discovered or manually registered.
/// </summary>
public static class MethodRegistry
{
    private static readonly Dictionary<string, ICompressionMethod> _methods = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;
    
    /// <summary>
    /// Get a method by name.
    /// </summary>
    public static ICompressionMethod Get(string name)
    {
        EnsureInitialized();
        if (_methods.TryGetValue(name, out var method))
            return method;
        throw new ArgumentException($"Unknown compression method: '{name}'. Use List() to see available methods.");
    }
    
    /// <summary>
    /// Try to get a method by name.
    /// </summary>
    public static bool TryGet(string name, out ICompressionMethod? method)
    {
        EnsureInitialized();
        return _methods.TryGetValue(name, out method);
    }
    
    /// <summary>
    /// Check if a method exists.
    /// </summary>
    public static bool Exists(string name)
    {
        EnsureInitialized();
        return _methods.ContainsKey(name);
    }
    
    /// <summary>
    /// List all available methods.
    /// </summary>
    public static IReadOnlyList<ICompressionMethod> List()
    {
        EnsureInitialized();
        return _methods.Values.ToList();
    }
    
    /// <summary>
    /// List methods by category.
    /// </summary>
    public static IReadOnlyList<ICompressionMethod> ListByCategory(string category)
    {
        EnsureInitialized();
        return _methods.Values
            .Where(m => m.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
    
    /// <summary>
    /// Get all unique categories.
    /// </summary>
    public static IReadOnlyList<string> Categories()
    {
        EnsureInitialized();
        return _methods.Values
            .Select(m => m.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }
    
    /// <summary>
    /// Manually register a method.
    /// </summary>
    public static void Register(ICompressionMethod method)
    {
        _methods[method.Name] = method;
    }
    
    /// <summary>
    /// Initialize with auto-discovery of built-in methods.
    /// </summary>
    private static void EnsureInitialized()
    {
        if (_initialized) return;
        
        lock (_methods)
        {
            if (_initialized) return;
            
            // Auto-discover all ICompressionMethod implementations in this assembly
            // Exclude CompressionPipeline - it's a container, not a method
            var methodTypes = typeof(MethodRegistry).Assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => t.Name != "CompressionPipeline")
                .Where(t => typeof(ICompressionMethod).IsAssignableFrom(t));
            
            foreach (var type in methodTypes)
            {
                try
                {
                    if (Activator.CreateInstance(type) is ICompressionMethod method)
                    {
                        _methods[method.Name] = method;
                    }
                }
                catch
                {
                    // Skip types that can't be instantiated with default constructor
                }
            }
            
            _initialized = true;
        }
    }
    
    /// <summary>
    /// Print a formatted list of all methods.
    /// </summary>
    public static void PrintList(TextWriter? output = null)
    {
        output ??= Console.Out;
        EnsureInitialized();
        
        output.WriteLine();
        output.WriteLine("Available compression methods:");
        output.WriteLine(new string('─', 70));
        
        foreach (var category in Categories())
        {
            output.WriteLine();
            output.WriteLine($"  {category}:");
            
            foreach (var method in ListByCategory(category))
            {
                var flags = new List<string>();
                if (method.IsLossless) flags.Add("✓");
                else flags.Add("~");
                if (method.IsChainable) flags.Add("chain");
                
                var flagStr = string.Join(" ", flags);
                output.WriteLine($"    {method.Name,-25} [{flagStr,-8}] {method.Description}");
            }
        }
        
        output.WriteLine();
        output.WriteLine("  ✓ = Lossless, ~ = Lossy, chain = Can be chained");
        output.WriteLine();
    }
}
