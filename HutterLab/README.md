# HUTTER LAB

**C# .NET 10 compression research laboratory for the Hutter Prize.**

> Status: Research / Active Development  
> Goal: Beat the Hutter Prize benchmark (~114 MB for enwik9)

---

## The Mission

Compress 1 GB of Wikipedia (enwik9) as small as possible while maintaining byte-exact reconstruction. Every byte saved advances our understanding of data compression and language modeling.

Current Hutter Prize leader: ~114 MB (11.4%)

---

## Architecture

HutterLab uses a modular, pluggable architecture inspired by [FrontierCompression](https://github.com/holbizmetrics/FrontierCompression-CSharp):

```
HutterLab/
├── HutterLab.sln
├── src/
│   ├── HutterLab.Core/           # Core library
│   │   ├── Models/               # Result types
│   │   ├── Interfaces/           # ICompressionMethod
│   │   ├── Pipeline/             # Method chaining
│   │   └── Methods/
│   │       ├── Preprocessing/    # xml_patterns, word_dict, bpe
│   │       ├── Statistical/      # Arithmetic, ANS, PPM (planned)
│   │       └── Backend/          # brotli, gzip, zstd, paq
│   │
│   └── HutterLab.Cli/            # Command-line interface
│
└── tests/                        # Unit tests
```

**Key Design Principle:** Each compression method implements `ICompressionMethod`, enabling:
- Individual testing
- Pipeline chaining (Preprocessing → Backend)
- Combinatorial sweeps to find optimal combinations

---

## Quick Start

```bash
# Build
dotnet build -c Release

# Run all methods on a file
hutterlab enwik9

# Run a specific method
hutterlab enwik9 -m brotli -v

# Run a pipeline
hutterlab enwik9 -p "xml_patterns+word_dict+brotli" -v

# Find best combinations
hutterlab enwik9 --sweep --sweep-top 10

# Verify round-trip
hutterlab enwik9 -m xml_patterns --verify
```

---

## Available Methods

### Preprocessing (Chainable)

| Method | Description | Estimated Savings |
|--------|-------------|-------------------|
| `xml_patterns` | Wikipedia XML structure substitution | ~85 MB |
| `word_dict` | Zipf's law word replacement | ~40-80 MB |
| `bpe` | BPE tokenization (planned) | TBD |

### Backend (Final Stage)

| Method | Description | Typical Ratio |
|--------|-------------|---------------|
| `brotli` | Google's Brotli (quality 11) | 4-5× |
| `gzip` | Standard GZip | 3-4× |
| `zlib` | ZLib compression | 3-4× |
| `deflate` | Raw DEFLATE | 3-4× |
| `zstd` | Facebook's Zstandard (planned) | 4-5× |
| `paq` | PAQ8 family (planned) | 6-8× |

### Baseline

| Method | Description |
|--------|-------------|
| `raw` | No compression (baseline reference) |

---

## Pipelines

Chain methods using `+` syntax:

```bash
# XML patterns → Brotli
hutterlab enwik9 -p "xml_patterns+brotli"

# XML patterns → Word dictionary → Brotli
hutterlab enwik9 -p "xml_patterns+word_dict+brotli"
```

Or programmatically:

```csharp
var pipeline = new CompressionPipeline()
    .Add("xml_patterns")
    .Add("word_dict")
    .Add("brotli");

var result = pipeline.Compress(data);
Console.WriteLine($"Ratio: {result.Ratio:F2}×");
```

---

## The Strategy

### Phase 1: Preprocessing (Current)
Extract domain-specific redundancy that statistical compressors miss:

1. **XML Structure** (85 MB) - Wikipedia's repetitive XML skeleton
2. **Username Dictionary** (1.2 MB) - 19K usernames appearing millions of times
3. **Word Dictionary** (40-80 MB) - Zipf's law exploitation
4. **BPE Tokens** (TBD) - Leverage LLM tokenizer research

### Phase 2: Statistical Backend
Feed preprocessed data to state-of-the-art compressors:

- Brotli (fast, good)
- Zstd (fast, great)
- PAQ8/cmix (slow, best)

### Phase 3: Custom Models
Build Wikipedia-specific context models:

- Article structure prediction
- Temporal patterns in revision history
- Cross-reference modeling

---

## Research Notes

### XML Pattern Analysis (enwik9)

From initial analysis:

| Pattern Type | Occurrences | Bytes Saved |
|--------------|-------------|-------------|
| Username tags | 205K | 4.5 MB |
| Contributor blocks | 205K | 10 MB |
| Page structure | 243K | 15 MB |
| `&quot;` entities | 1.87M | 7.5 MB |
| **Total structural** | - | **~85 MB** |

### Word Frequency (Zipf)

| Word | Est. Frequency | Savings |
|------|----------------|---------|
| the | ~10M | ~20 MB |
| of | ~5M | ~5 MB |
| and | ~4M | ~8 MB |
| in | ~4M | ~4 MB |
| **Top 200** | - | **~40-80 MB** |

### The Compounding Question

Key research question: Does preprocessing *help* or *hurt* the statistical backend?

- Pro: Removes redundancy the backend would waste bits on
- Con: May destroy patterns the backend could exploit better

The sweep functionality tests all combinations to find the answer empirically.

---

## Building

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Single-file executable
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Run tests
dotnet test
```

### Requirements

- .NET 10 SDK
- x64 processor (AVX2 recommended for future SIMD methods)

---

## CLI Reference

```
Usage: hutterlab <file> [options]

Options:
  -m, --method <name>     Run a specific compression method
  -p, --pipeline <spec>   Run a pipeline (e.g., 'xml_patterns+brotli')
  -v, --verbose           Show detailed output
  --verify                Verify decompression matches original
  --sweep                 Test all method combinations
  --sweep-top <n>         Show top N results (default: 20)
  --list                  List all available methods
  --help                  Show this help
  --version               Show version
```

---

## Roadmap

- [x] Core architecture (ICompressionMethod, Pipeline, Registry)
- [x] Built-in backends (Brotli, GZip, ZLib, Deflate)
- [x] XML pattern substitution
- [x] Word dictionary
- [ ] BPE tokenization (tiktoken integration)
- [ ] Zstd backend (native wrapper)
- [ ] PAQ8 backend (native wrapper)
- [ ] Arithmetic coding implementation
- [ ] ANS implementation
- [ ] PPM context modeling
- [ ] SIMD-accelerated operations
- [ ] Parallel processing for large files
- [ ] Full enwik9 benchmarking suite

---

## License

Research code. Use at your own risk.

---

## Authors

Holger Morlok & Eve | 2026

💛🜂
