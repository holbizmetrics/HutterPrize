using System.Text;
using HutterLab.Core;
using HutterLab.Core.Methods;
using HutterLab.Core.Methods.Backend;
using HutterLab.Core.Methods.Preprocessing;
using HutterLab.Core.Models;
using HutterLab.Core.Pipeline;
using Xunit;

namespace HutterLab.Tests;

public class CompressionMethodTests
{
    private static readonly byte[] SimpleText = Encoding.UTF8.GetBytes("Hello, world! This is a test of compression.");
    
    private static readonly byte[] RepetitiveText = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 100)));
    
    private static readonly byte[] WikipediaSnippet = Encoding.UTF8.GetBytes("""
        <page>
            <title>Test Article</title>
            <id>12345</id>
            <revision>
              <id>67890</id>
              <timestamp>2024-01-01T00:00:00Z</timestamp>
              <contributor>
                <username>TestUser</username>
                <id>11111</id>
              </contributor>
              <text xml:space="preserve">This is the article content.</text>
            </revision>
          </page>
        """);
    
    [Fact]
    public void RawMethod_RoundTrip_Success()
    {
        var method = new RawMethod();
        var compressed = method.Compress(SimpleText);
        var decompressed = method.Decompress(compressed.CompressedData);
        
        Assert.Equal(SimpleText, decompressed.DecompressedData);
        Assert.Equal(1.0, compressed.Ratio, 2);
    }
    
    [Fact]
    public void BrotliBackend_RoundTrip_Success()
    {
        var method = new BrotliBackend();
        var compressed = method.Compress(RepetitiveText);
        var decompressed = method.Decompress(compressed.CompressedData);
        
        Assert.Equal(RepetitiveText, decompressed.DecompressedData);
        Assert.True(compressed.Ratio > 1, "Brotli should compress repetitive text");
    }
    
    [Fact]
    public void GZipBackend_RoundTrip_Success()
    {
        var method = new GZipBackend();
        var compressed = method.Compress(RepetitiveText);
        var decompressed = method.Decompress(compressed.CompressedData);
        
        Assert.Equal(RepetitiveText, decompressed.DecompressedData);
        Assert.True(compressed.Ratio > 1, "GZip should compress repetitive text");
    }
    
    [Fact]
    public void ZLibBackend_RoundTrip_Success()
    {
        var method = new ZLibBackend();
        var compressed = method.Compress(RepetitiveText);
        var decompressed = method.Decompress(compressed.CompressedData);
        
        Assert.Equal(RepetitiveText, decompressed.DecompressedData);
    }
    
    [Fact]
    public void DeflateBackend_RoundTrip_Success()
    {
        var method = new DeflateBackend();
        var compressed = method.Compress(RepetitiveText);
        var decompressed = method.Decompress(compressed.CompressedData);
        
        Assert.Equal(RepetitiveText, decompressed.DecompressedData);
    }
    
    [Fact]
    public void XmlPatternSubstitution_RoundTrip_Success()
    {
        var method = new XmlPatternSubstitution();
        var compressed = method.Compress(WikipediaSnippet);
        var decompressed = method.Decompress(compressed.CompressedData);
        
        Assert.Equal(WikipediaSnippet, decompressed.DecompressedData);
    }
    
    [Fact]
    public void WordDictionary_RoundTrip_Success()
    {
        var method = new WordDictionary();
        var compressed = method.Compress(RepetitiveText);
        var decompressed = method.Decompress(compressed.CompressedData);
        
        Assert.Equal(RepetitiveText, decompressed.DecompressedData);
    }
    
    [Fact]
    public void MethodRegistry_ListsAllMethods()
    {
        var methods = MethodRegistry.List();
        
        Assert.NotEmpty(methods);
        Assert.Contains(methods, m => m.Name == "raw");
        Assert.Contains(methods, m => m.Name == "brotli");
    }
    
    [Fact]
    public void MethodRegistry_GetByName_Works()
    {
        var method = MethodRegistry.Get("brotli");
        
        Assert.NotNull(method);
        Assert.Equal("brotli", method.Name);
    }
    
    [Fact]
    public void Pipeline_SingleMethod_RoundTrip()
    {
        var pipeline = new CompressionPipeline().Add("brotli");
        
        var compressed = pipeline.Compress(RepetitiveText);
        var decompressed = pipeline.Decompress(compressed.CompressedData);
        
        Assert.Equal(RepetitiveText, decompressed.DecompressedData);
    }
    
    [Fact]
    public void Pipeline_MultipleStages_RoundTrip()
    {
        var pipeline = CompressionPipeline.From("word_dict", "brotli");
        
        var compressed = pipeline.Compress(RepetitiveText);
        var decompressed = pipeline.Decompress(compressed.CompressedData);
        
        Assert.Equal(RepetitiveText, decompressed.DecompressedData);
    }
    
    [Fact]
    public void CompressionResult_CalculatesMetricsCorrectly()
    {
        var result = new CompressionResult
        {
            Method = "test",
            OriginalSize = 1000,
            CompressedSize = 400,
            CompressedData = new byte[400],
            AuxiliarySize = 100,
            Duration = TimeSpan.FromSeconds(1)
        };
        
        Assert.Equal(500, result.TotalSize);
        Assert.Equal(2.0, result.Ratio);
        Assert.Equal(50.0, result.Percentage);
        Assert.Equal(500, result.BytesSaved);
    }
}
