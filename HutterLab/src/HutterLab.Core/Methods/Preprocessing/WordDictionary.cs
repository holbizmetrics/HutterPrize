using System.Diagnostics;
using System.Text;
using HutterLab.Core.Interfaces;
using HutterLab.Core.Models;

namespace HutterLab.Core.Methods.Preprocessing;

/// <summary>
/// Replaces frequent words with single-byte codes.
/// Exploits Zipf's law - top words dominate frequency.
/// </summary>
public sealed class WordDictionary : CompressionMethodBase
{
    public override string Name => "word_dict";
    public override string Description => "Frequent word substitution (Zipf)";
    public override string Category => "Preprocessing";
    public override bool IsChainable => true;
    
    // Magic bytes
    private static readonly byte[] Magic = "WDCT"u8.ToArray();
    private const byte Version = 1;
    
    // We use bytes 0x80-0xFF for single-byte word codes (128 words)
    // And 0x01-0x7F as prefix for 2-byte codes (128 * 256 = 32K more words)
    private const byte SingleByteStart = 0x80;
    private const byte TwoBytePrefix = 0x01;
    private const byte EscapeByte = 0x00; // Escape for literal bytes in reserved range
    
    // Word boundary characters
    private static readonly HashSet<char> Boundaries = [' ', '\n', '\r', '\t', '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '<', '>', '/', '\\', '|', '{', '}', '=', '+', '-', '*', '&', '^', '%', '$', '#', '@', '~', '`'];
    
    public override CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
    {
        var opts = GetOptions(options);
        var sw = Stopwatch.StartNew();
        
        var text = Encoding.UTF8.GetString(data);
        var originalSize = data.Length;
        
        Log(opts, $"Input: {originalSize:N0} bytes");
        
        // Extract and count words
        var wordCounts = CountWords(text);
        Log(opts, $"Found {wordCounts.Count:N0} unique words");
        
        // Select words for dictionary based on savings potential
        // savings = (original_bytes - code_bytes) * frequency
        // For single-byte: savings = (len - 1) * freq (must be len >= 2)
        // For two-byte: savings = (len - 2) * freq (must be len >= 3)
        
        var ranked = wordCounts
            .Select(kv => new { Word = kv.Key, Count = kv.Value, Savings = (kv.Key.Length - 1) * kv.Value })
            .Where(w => w.Savings > 0 && w.Word.Length >= 2)
            .OrderByDescending(w => w.Savings)
            .ToList();
        
        // Take top 128 for single-byte codes
        var singleByteWords = ranked.Take(128).ToList();
        
        // Take next ~32K for two-byte codes (if beneficial)
        var twoByteWords = ranked.Skip(128)
            .Where(w => (w.Word.Length - 2) * w.Count > 0 && w.Word.Length >= 3)
            .Take(32000)
            .ToList();
        
        Log(opts, $"Single-byte words: {singleByteWords.Count}");
        Log(opts, $"Two-byte words: {twoByteWords.Count}");
        
        // Build encoding dictionary
        var wordToCode = new Dictionary<string, (bool SingleByte, byte Code1, byte Code2)>();
        
        byte code = SingleByteStart;
        foreach (var w in singleByteWords)
        {
            wordToCode[w.Word] = (true, code++, 0);
        }
        
        byte prefix = TwoBytePrefix;
        byte suffix = 0;
        foreach (var w in twoByteWords)
        {
            wordToCode[w.Word] = (false, prefix, suffix++);
            if (suffix == 0) // Wrapped
            {
                prefix++;
                if (prefix >= SingleByteStart) break; // Out of codes
            }
        }
        
        // Build output
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        
        // Write header
        writer.Write(Magic);
        writer.Write(Version);
        
        // Write dictionary
        writer.Write((ushort)singleByteWords.Count);
        writer.Write((ushort)twoByteWords.Count);
        
        foreach (var w in singleByteWords)
        {
            var bytes = Encoding.UTF8.GetBytes(w.Word);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }
        
        foreach (var w in twoByteWords)
        {
            var bytes = Encoding.UTF8.GetBytes(w.Word);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }
        
        var dictSize = output.Position;
        Log(opts, $"Dictionary size: {dictSize:N0} bytes");
        
        // Encode text
        var textBytes = Encoding.UTF8.GetBytes(text);
        int pos = 0;
        long substitutions = 0;
        
        while (pos < text.Length)
        {
            // Try to match a word at current position
            var wordEnd = FindWordEnd(text, pos);
            
            if (wordEnd > pos)
            {
                var word = text[pos..wordEnd];
                
                if (wordToCode.TryGetValue(word, out var encoding))
                {
                    if (encoding.SingleByte)
                    {
                        output.WriteByte(encoding.Code1);
                    }
                    else
                    {
                        output.WriteByte(encoding.Code1);
                        output.WriteByte(encoding.Code2);
                    }
                    substitutions++;
                    pos = wordEnd;
                    continue;
                }
            }
            
            // No match - write literal byte (with escaping if needed)
            var b = textBytes[pos];
            if (b < SingleByteStart && b >= TwoBytePrefix && b != 0)
            {
                // Could be confused with two-byte prefix, escape it
                output.WriteByte(EscapeByte);
            }
            else if (b >= SingleByteStart)
            {
                // Could be confused with single-byte word code, escape it
                output.WriteByte(EscapeByte);
            }
            output.WriteByte(b);
            pos++;
        }
        
        sw.Stop();
        
        var compressedData = output.ToArray();
        
        Log(opts, $"Substitutions: {substitutions:N0}");
        Log(opts, $"Output: {compressedData.Length:N0} bytes ({(double)originalSize / compressedData.Length:F2}×)");
        
        if (opts.Verbose)
        {
            Console.WriteLine($"\n  Top 10 words by savings:");
            foreach (var w in singleByteWords.Take(10))
            {
                Console.WriteLine($"    \"{w.Word}\": {w.Count:N0}× × {w.Word.Length} bytes = {w.Savings:N0} saved");
            }
        }
        
        return new CompressionResult
        {
            Method = Name,
            OriginalSize = originalSize,
            CompressedSize = compressedData.Length - dictSize,
            AuxiliarySize = dictSize,
            CompressedData = compressedData,
            Duration = sw.Elapsed,
            IsLossless = true,
            Metadata = new Dictionary<string, object>
            {
                ["single_byte_words"] = singleByteWords.Count,
                ["two_byte_words"] = twoByteWords.Count,
                ["substitutions"] = substitutions
            }
        };
    }
    
    public override DecompressionResult Decompress(ReadOnlySpan<byte> compressedData, CompressionOptions? options = null)
    {
        var opts = GetOptions(options);
        var sw = Stopwatch.StartNew();
        
        using var input = new MemoryStream(compressedData.ToArray());
        using var reader = new BinaryReader(input);
        
        // Verify magic
        var magic = reader.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("Invalid WDCT magic bytes");
        
        var version = reader.ReadByte();
        if (version != Version)
            throw new InvalidDataException($"Unsupported WDCT version: {version}");
        
        // Read dictionary
        var singleByteCount = reader.ReadUInt16();
        var twoByteCount = reader.ReadUInt16();
        
        var singleByteWords = new string[128];
        for (int i = 0; i < singleByteCount; i++)
        {
            var len = reader.ReadByte();
            var bytes = reader.ReadBytes(len);
            singleByteWords[i] = Encoding.UTF8.GetString(bytes);
        }
        
        var twoByteWords = new string[twoByteCount];
        for (int i = 0; i < twoByteCount; i++)
        {
            var len = reader.ReadUInt16();
            var bytes = reader.ReadBytes(len);
            twoByteWords[i] = Encoding.UTF8.GetString(bytes);
        }
        
        // Decode
        using var output = new MemoryStream();
        
        while (input.Position < input.Length)
        {
            var b = reader.ReadByte();
            
            if (b == EscapeByte)
            {
                // Escaped literal
                output.WriteByte(reader.ReadByte());
            }
            else if (b >= SingleByteStart)
            {
                // Single-byte word code
                var index = b - SingleByteStart;
                if (index >= singleByteCount)
                    throw new InvalidDataException($"Invalid single-byte word index: {index}");
                var wordBytes = Encoding.UTF8.GetBytes(singleByteWords[index]);
                output.Write(wordBytes);
            }
            else if (b >= TwoBytePrefix && b < SingleByteStart)
            {
                // Two-byte word code
                var suffix = reader.ReadByte();
                var index = ((b - TwoBytePrefix) << 8) | suffix;
                if (index >= twoByteCount)
                    throw new InvalidDataException($"Invalid two-byte word index: {index}");
                var wordBytes = Encoding.UTF8.GetBytes(twoByteWords[index]);
                output.Write(wordBytes);
            }
            else
            {
                // Literal byte
                output.WriteByte(b);
            }
        }
        
        sw.Stop();
        
        var decompressedData = output.ToArray();
        
        return new DecompressionResult
        {
            Method = Name,
            CompressedSize = compressedData.Length,
            DecompressedSize = decompressedData.Length,
            DecompressedData = decompressedData,
            Duration = sw.Elapsed
        };
    }
    
    private static Dictionary<string, int> CountWords(string text)
    {
        var counts = new Dictionary<string, int>();
        int pos = 0;
        
        while (pos < text.Length)
        {
            // Skip boundaries
            while (pos < text.Length && Boundaries.Contains(text[pos]))
                pos++;
            
            if (pos >= text.Length) break;
            
            // Extract word
            var end = FindWordEnd(text, pos);
            if (end > pos)
            {
                var word = text[pos..end];
                counts[word] = counts.GetValueOrDefault(word) + 1;
            }
            pos = end;
        }
        
        return counts;
    }
    
    private static int FindWordEnd(string text, int start)
    {
        int pos = start;
        while (pos < text.Length && !Boundaries.Contains(text[pos]))
            pos++;
        return pos;
    }
}
