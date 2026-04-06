# Runtime replay inspection guide

This page indexes the runtime replay cases used in the benchmark and links directly to the exact code snapshots under test. The goal is simple: make it easy to click straight into the method under review without rebuilding the whole context from commits and diffs.

## Branch and ref legend

| Label | Repo / ref | Role |
| --- | --- | --- |
| `bench/baseline` | [`richlander/runtime`](https://github.com/richlander/runtime/tree/bench/baseline) | Synthetic replay baseline branch |
| `bench/proposed-nosafe` | [`richlander/runtime`](https://github.com/richlander/runtime/tree/bench/proposed-nosafe) | Same replay with inner `unsafe` blocks but no `safe` marker |
| `bench/proposed-safe` | [`richlander/runtime`](https://github.com/richlander/runtime/tree/bench/proposed-safe) | Same replay with explicit `safe` markers |
| `main` | [`dotnet/runtime`](https://github.com/dotnet/runtime/tree/main) | Current fixed control |
| `<fix>^` | `dotnet/runtime` parent of the fix commit | Historical vulnerable snapshot used as the positive control for a replayed CVE |

## Synthetic replay (non-CVE): `CollectionsMarshal.AsSpan<T>`

This is the branch-trio replay currently used to compare the `safe` and `nosafe` variants.

| Condition | Direct link | Note |
| --- | --- | --- |
| `bench/baseline` | [`CollectionsMarshal.AsSpan<T>`](https://github.com/richlander/runtime/blob/bench/baseline/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/CollectionsMarshal.cs#L23-L37) | Runtime guard removed; only `Debug.Assert` remains |
| `bench/proposed-nosafe` | [`CollectionsMarshal.AsSpan<T>`](https://github.com/richlander/runtime/blob/bench/proposed-nosafe/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/CollectionsMarshal.cs#L23-L43) | Same replay plus annotation/scoping changes, no `safe` keyword |
| `bench/proposed-safe` | [`CollectionsMarshal.AsSpan<T>`](https://github.com/richlander/runtime/blob/bench/proposed-safe/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/CollectionsMarshal.cs#L23-L43) | Same replay plus explicit `safe` markers elsewhere in the experiment |
| `main` control | [`CollectionsMarshal.AsSpan<T>`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/CollectionsMarshal.cs#L23-L39) | Release-mode guard is still present |

## Replayed CVEs

### CVE-2025-21171 — `Convert.TryToHexString`

- **Category:** safe-boundary
- **Primary target:** `System.Convert.TryToHexString(ReadOnlySpan<byte>, Span<char>, out int)`
- **Pattern:** wrong-direction bounds check

| Snapshot | Direct link |
| --- | --- |
| Vulnerable positive ref (`9da8c6a4...^`) | [`TryToHexString` with `destination.Length > source.Length * 2`](https://github.com/dotnet/runtime/blob/f6615d27fdbef2e7216d9762aaea9a6aa49e1be5/src/libraries/System.Private.CoreLib/src/System/Convert.cs#L3095-L3110) |
| Fixed commit | [`9da8c6a4`](https://github.com/dotnet/runtime/commit/9da8c6a4a6ea03054e776275d3fd5c752897842e) |
| Current `main` control | [`TryToHexString` on `main`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Convert.cs#L2813-L2824) |
| Companion method | [`TryToHexStringLower` on `main`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Convert.cs#L2908-L2919) |

### CVE-2026-26127 — `Base64DecoderHelper.DecodeFrom<TBase64Decoder, T>`

- **Category:** unsafe-core
- **Primary target:** `System.Buffers.Text.Base64Helper.Base64DecoderHelper.DecodeFrom<TBase64Decoder, T>(...)`
- **Pattern:** missing validation before `Unsafe.Add`

| Snapshot | Direct link |
| --- | --- |
| Vulnerable positive ref (`19c07820...^`) | [`DecodeFrom` before the `i0 < 0` guard was added](https://github.com/dotnet/runtime/blob/1f0cdd4ecdafad440995c014f8aa44ed43c11d6e/src/libraries/System.Private.CoreLib/src/System/Buffers/Text/Base64Helper/Base64DecoderHelper.cs#L166-L183) |
| Fixed commit | [`19c07820`](https://github.com/dotnet/runtime/commit/19c07820cb72aafc554c3bc8fe3c54010f5123f0) |
| Current `main` control | [`DecodeFrom` on `main`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/Text/Base64Helper/Base64DecoderHelper.cs#L166-L193) |
| Current `main` char-path guard | [`DecodeRemaining(ushort*)` on `main`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/Text/Base64Helper/Base64DecoderHelper.cs#L1772-L1811) |

### CVE-2024-30045 — `Number.BigInteger`

- **Category:** unsafe-core
- **Primary target:** `System.Number.BigInteger.ShiftLeft(int)`  
  Historical code also surfaces the same pattern in `Add`, `Multiply`, `Multiply10`, and `Pow2`.
- **Pattern:** missing runtime bounds checks around fixed-buffer writes

| Snapshot | Direct link |
| --- | --- |
| Vulnerable positive ref (`415ab3a3...^`) | [`ShiftLeft(uint)` with only `Debug.Assert(writeIndex < MaxBlockCount)`](https://github.com/dotnet/runtime/blob/2b0c1debd40d45d2bda81c719d0719439596c248/src/libraries/System.Private.CoreLib/src/System/Number.BigInteger.cs#L1136-L1180) |
| Fixed commit | [`415ab3a3`](https://github.com/dotnet/runtime/commit/415ab3a31982b2bd544c188824eeba34954ae3f6) |
| Current `main` control | [`ShiftLeft(int)` on `main`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Number.BigInteger.cs#L1231-L1295) |
| Current `main` sizing fix | [`MaxBlockCount` on `main`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Number.BigInteger.cs#L32-L36) |

## True clean baseline

This is the null case used to measure spontaneous false positives on a proposal-touched method with no known replay or CVE attached to it.

| Condition | Direct link | Note |
| --- | --- | --- |
| Current `main` | [`BitArray.CopyTo(Array, int)`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Collections/BitArray.cs#L664-L831) | Clean baseline used for the `NOT PRESENT` null run |
| `bench/baseline` | [`BitArray.CopyTo(Array, int)`](https://github.com/richlander/runtime/blob/bench/baseline/src/libraries/System.Private.CoreLib/src/System/Collections/BitArray.cs#L664-L831) | Proposal-touched branch, no known vulnerability replay in this method |
| `bench/proposed-nosafe` | [`BitArray.CopyTo(Array, int)`](https://github.com/richlander/runtime/blob/bench/proposed-nosafe/src/libraries/System.Private.CoreLib/src/System/Collections/BitArray.cs#L664-L831) | No `safe` keyword |
| `bench/proposed-safe` | [`BitArray.CopyTo(Array, int)`](https://github.com/richlander/runtime/blob/bench/proposed-safe/src/libraries/System.Private.CoreLib/src/System/Collections/BitArray.cs#L664-L831) | Explicit `safe` keyword |
