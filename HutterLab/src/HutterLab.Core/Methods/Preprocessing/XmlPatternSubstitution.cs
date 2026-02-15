using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using HutterLab.Core.Interfaces;
using HutterLab.Core.Models;

namespace HutterLab.Core.Methods.Preprocessing;

/// <summary>
/// Extracts and substitutes repetitive XML patterns and usernames from Wikipedia dumps.
/// Based on analysis showing 85MB+ savings from structural patterns alone.
/// </summary>
public sealed partial class XmlPatternSubstitution : CompressionMethodBase
{
	public override string Name => "xml_patterns";
	public override string Description => "Wikipedia XML structure substitution";
	public override string Category => "Preprocessing";
	public override bool IsChainable => true;

	// Magic bytes to identify our format
	private static readonly byte[] Magic = "XMLP"u8.ToArray();
	private const byte Version = 1;

	// Escape byte for substitutions (must be rare in input)
	private const byte EscapeByte = 0x1F; // Unit Separator - rare in text

	// Pre-defined patterns (order matters - longer patterns first)
	// NOTE: Username tags are handled separately in Phase 1, so we don't include them here
	// Codes use printable ASCII (0x21-0x7E) to avoid UTF-8 multi-byte encoding issues
	private static readonly (string Pattern, byte Code)[] StructuralPatterns =
	[
        // Contributor blocks (most savings) - WITHOUT username tags
        ("</timestamp>\n      <contributor>\n        ", 0x21), // '!'
        ("\n        <id>", 0x22), // '"'
        ("</id>\n      </contributor>\n      <minor/>", 0x23), // '#'
        ("</id>\n      </contributor>\n      <comment>", 0x24), // '$'
        ("</id>\n      </contributor>\n      <text xml:space=\"preserve\">", 0x25), // '%'
        ("</comment>\n      <text xml:space=\"preserve\">", 0x26), // '&'
        ("</text>\n    </revision>\n  </page>\n", 0x27), // '''
        
        // Page structure
        ("<page>\n    <title>", 0x28), // '('
        ("</title>\n    <id>", 0x29), // ')'
        ("</id>\n    <revision>\n      <id>", 0x2A), // '*'
        ("</id>\n      <timestamp>", 0x2B), // '+'
        
        // Common elements
        ("#REDIRECT [[", 0x2C), // ','
        ("[[Category:", 0x2D), // '-'
        ("[[Image:", 0x2E), // '.'
        ("|thumb|", 0x2F), // '/'
        
        // HTML entities
        ("&quot;", 0x30), // '0'
        ("&amp;", 0x31), // '1'
        ("&lt;", 0x32), // '2'
        ("&gt;", 0x33), // '3'
        
        // URLs
        ("http://", 0x34), // '4'
        ("https://", 0x35), // '5'
        
        // Common suffixes
        (".org", 0x36), // '6'
        (".com", 0x37), // '7'
        ("wikipedia", 0x38), // '8'
        ("Wikipedia", 0x39), // '9'
    ];

	public override CompressionResult Compress(ReadOnlySpan<byte> data, CompressionOptions? options = null)
	{
		var opts = GetOptions(options);
		var sw = Stopwatch.StartNew();

		var text = Encoding.UTF8.GetString(data);
		var originalSize = data.Length;

		Log(opts, $"Input: {originalSize:N0} bytes");

		// Build username dictionary
		var usernames = ExtractUsernames(text);
		Log(opts, $"Found {usernames.Count:N0} unique usernames");

		// Create output buffer
		using var output = new MemoryStream((int)(data.Length * 1.1));
		using var writer = new BinaryWriter(output);

		// Write header
		writer.Write(Magic);
		writer.Write(Version);

		// Write username dictionary
		writer.Write((ushort)usernames.Count);
		foreach (var (username, code) in usernames)
		{
			writer.Write((ushort)code);
			var usernameBytes = Encoding.UTF8.GetBytes(username);
			writer.Write((ushort)usernameBytes.Length);
			writer.Write(usernameBytes);
		}

		var dictEnd = output.Position;
		Log(opts, $"Username dictionary: {dictEnd:N0} bytes ({usernames.Count} entries)");

		// PHASE 1: Replace username tags FIRST (before structural patterns consume the tags)
		Log(opts, $"Phase 1: Replacing username tags...");

		var usernameStats = new Dictionary<string, int>();
		var result = new StringBuilder(text.Length);
		int pos = 0;
		const string openTag = "<username>";
		const string closeTag = "</username>";
		long usernameReplacements = 0;

		while (pos < text.Length)
		{
			// Look for next <username> tag
			int tagStart = text.IndexOf(openTag, pos, StringComparison.Ordinal);

			if (tagStart < 0)
			{
				// No more username tags - copy rest and done
				result.Append(text, pos, text.Length - pos);
				break;
			}

			// Copy everything before the tag
			result.Append(text, pos, tagStart - pos);

			// Find closing tag
			int nameStart = tagStart + openTag.Length;
			int nameEnd = text.IndexOf(closeTag, nameStart, StringComparison.Ordinal);

			if (nameEnd < 0)
			{
				// Malformed - no closing tag, copy as-is and continue
				result.Append(openTag);
				pos = nameStart;
				continue;
			}

			// Extract username
			var username = text[nameStart..nameEnd];

			// Check if it's in our dictionary
			if (usernames.TryGetValue(username, out int code))
			{
				// Replace with escape sequence using 3 ASCII-safe bytes (each < 128)
				// This avoids UTF-8 multi-byte encoding issues
				result.Append((char)EscapeByte);
				result.Append('U');
				result.Append((char)((code / 16384) % 128 + 0x20)); // High (space to DEL-1)
				result.Append((char)((code / 128) % 128 + 0x20));   // Mid  
				result.Append((char)(code % 128 + 0x20));            // Low
				usernameReplacements++;
				usernameStats[username] = usernameStats.GetValueOrDefault(username) + 1;
			}
			else
			{
				// Not in dictionary - keep as-is
				result.Append(openTag);
				result.Append(username);
				result.Append(closeTag);
			}

			pos = nameEnd + closeTag.Length;
		}

		Log(opts, $"Phase 1 complete. Username replacements: {usernameReplacements:N0}");

		// PHASE 2: Replace structural patterns (now that usernames are handled)
		Log(opts, $"Phase 2: Replacing {StructuralPatterns.Length} structural patterns...");

		text = result.ToString();

		var structureReplacements = new Dictionary<string, string>();
		foreach (var (pattern, code) in StructuralPatterns)
		{
			structureReplacements[pattern] = $"{(char)EscapeByte}P{(char)code}";
		}

		var structureRegex = new Regex(
			string.Join("|", StructuralPatterns.Select(p => Regex.Escape(p.Pattern))),
			RegexOptions.Compiled);

		var structureStats = new Dictionary<byte, int>();
		text = structureRegex.Replace(text, match =>
		{
			var code = StructuralPatterns.First(p => p.Pattern == match.Value).Code;
			structureStats[code] = structureStats.GetValueOrDefault(code) + 1;
			return structureReplacements[match.Value];
		});

		Log(opts, $"Phase 2 complete. Structural replacements: {structureStats.Values.Sum():N0}");

		// PHASE 3: Escape any literal escape bytes
		// NOTE: Use 'text' which has Phase 2 results, not 'result' which only has Phase 1
		var escaped = new StringBuilder(text.Length);
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (c == (char)EscapeByte)
			{
				// Check if this is one of our sequences
				if (i + 1 < text.Length)
				{
					char next = text[i + 1];
					if (next == 'U' || next == 'P')
					{
						// This is our sequence, keep as-is
						escaped.Append(c);
						continue;
					}
				}
				// Raw escape byte - double it
				escaped.Append(c);
				escaped.Append(c);
			}
			else
			{
				escaped.Append(c);
			}
		}

		// Write processed data
		var processedBytes = Encoding.UTF8.GetBytes(escaped.ToString());
		writer.Write(processedBytes);

		sw.Stop();

		var compressedData = output.ToArray();

		if (opts.Verbose)
		{
			Console.WriteLine($"\n  Top structural patterns:");
			foreach (var (code, count) in structureStats.OrderByDescending(kv => kv.Value).Take(5))
			{
				var pattern = StructuralPatterns.First(p => p.Code == code).Pattern;
				var display = pattern.Length > 30 ? pattern[..27] + "..." : pattern;
				display = display.Replace("\n", "\\n");
				Console.WriteLine($"    {display}: {count:N0}×");
			}

			Console.WriteLine($"\n  Top usernames:");
			foreach (var (uname, count) in usernameStats.OrderByDescending(kv => kv.Value).Take(5))
			{
				Console.WriteLine($"    {uname}: {count:N0}×");
			}
		}

		Log(opts, $"Output: {compressedData.Length:N0} bytes ({(double)originalSize / compressedData.Length:F2}×)");

		return new CompressionResult
		{
			Method = Name,
			OriginalSize = originalSize,
			CompressedSize = compressedData.Length - dictEnd,
			AuxiliarySize = dictEnd,
			CompressedData = compressedData,
			Duration = sw.Elapsed,
			IsLossless = true,
			Metadata = new Dictionary<string, object>
			{
				["username_count"] = usernames.Count,
				["username_replacements"] = usernameReplacements,
				["structure_replacements"] = structureStats.Values.Sum()
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
			throw new InvalidDataException("Invalid XMLP magic bytes");

		var version = reader.ReadByte();
		if (version != Version)
			throw new InvalidDataException($"Unsupported XMLP version: {version}");

		// Read username dictionary
		var usernameCount = reader.ReadUInt16();
		var usernames = new Dictionary<int, string>();
		for (int i = 0; i < usernameCount; i++)
		{
			var code = reader.ReadUInt16();
			var len = reader.ReadUInt16();
			var bytes = reader.ReadBytes(len);
			usernames[code] = Encoding.UTF8.GetString(bytes);
		}

		// Read processed data
		var remaining = compressedData.Length - input.Position;
		var processedBytes = reader.ReadBytes((int)remaining);
		var processed = Encoding.UTF8.GetString(processedBytes);

		// Reverse substitutions
		var result = new StringBuilder(processed.Length * 2);
		int pos = 0;

		while (pos < processed.Length)
		{
			if (processed[pos] == (char)EscapeByte)
			{
				if (pos + 1 >= processed.Length)
					throw new InvalidDataException("Truncated escape sequence");

				var next = processed[pos + 1];

				if (next == (char)EscapeByte)
				{
					// Escaped escape byte
					result.Append((char)EscapeByte);
					pos += 2;
				}
				else if (next == 'U')
				{
					// Username substitution (3 ASCII-safe bytes)
					if (pos + 4 >= processed.Length)
						throw new InvalidDataException("Truncated username code");

					var high = processed[pos + 2] - 0x20;
					var mid = processed[pos + 3] - 0x20;
					var low = processed[pos + 4] - 0x20;
					var code = high * 16384 + mid * 128 + low;

					if (!usernames.TryGetValue(code, out var username))
						throw new InvalidDataException($"Unknown username code: {code}");

					result.Append($"<username>{username}</username>");
					pos += 5;
				}
				else if (next == 'P')
				{
					// Pattern substitution
					if (pos + 2 >= processed.Length)
						throw new InvalidDataException("Truncated pattern code");

					var code = (byte)processed[pos + 2];
					var pattern = StructuralPatterns.FirstOrDefault(p => p.Code == code);
					if (pattern.Pattern == null)
						throw new InvalidDataException($"Unknown pattern code: {code:X2}");

					result.Append(pattern.Pattern);
					pos += 3;
				}
				else
				{
					throw new InvalidDataException($"Unknown escape type: {(int)next}");
				}
			}
			else
			{
				result.Append(processed[pos]);
				pos++;
			}
		}

		sw.Stop();

		var decompressedData = Encoding.UTF8.GetBytes(result.ToString());

		return new DecompressionResult
		{
			Method = Name,
			CompressedSize = compressedData.Length,
			DecompressedSize = decompressedData.Length,
			DecompressedData = decompressedData,
			Duration = sw.Elapsed
		};
	}

	/// <summary>
	/// Extract all usernames from Wikipedia XML and assign codes.
	/// </summary>
	private static Dictionary<string, int> ExtractUsernames(string text)
	{
		var regex = UsernameRegex();
		var counts = new Dictionary<string, int>();

		foreach (Match match in regex.Matches(text))
		{
			var username = match.Groups[1].Value;
			counts[username] = counts.GetValueOrDefault(username) + 1;
		}

		// Assign codes to usernames sorted by frequency (most frequent = lowest codes)
		// Using 3-byte base-128 encoding, we can encode up to 2M usernames
		int code = 0;
		return counts
			.OrderByDescending(kv => kv.Value * kv.Key.Length) // Prioritize by bytes saved
			.Take(2000000) // Max usernames with 3-byte encoding
			.ToDictionary(kv => kv.Key, _ => code++);
	}

	private static int CountOccurrences(string text, string pattern)
	{
		int count = 0;
		int pos = 0;
		while ((pos = text.IndexOf(pattern, pos, StringComparison.Ordinal)) >= 0)
		{
			count++;
			pos += pattern.Length;
		}
		return count;
	}

	[GeneratedRegex(@"<username>([^<]+)</username>", RegexOptions.Compiled)]
	private static partial Regex UsernameRegex();
}