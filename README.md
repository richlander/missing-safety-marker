# Missing Safety Marker Proposal

[Memory safety v2](https://github.com/dotnet/designs/tree/main/accepted/2025/memory-safety) is one of the highest-stakes features we have taken on. It bears directly on the most foundational value propositions of the language and on how C# is compared to and interacts with other industry languages.

Memory safety v2 is a transformational change to the C# safety model. The new model closely matches Rust, making it a better target for this critique. In Rust, unsafe functions are bimodal. There are two sets of methods with interior `unsafe` blocks. One is marked `unsafe` and the other presents no safety-related signature marking at all. The two modes can be thought of as: "unsafe unsafe" and "safe unsafe". The former freely generates safety obligations for callers while the latter form is responsible to collapse them. The "unsafe" family name is common because they both harbor unsafety.

"unsafe unsafe" methods are not required to generate safety and are in fact in the business of deferring it. They need to be sound, but are typically conditionally safe on specific operations by callers (per safety documentation). "safe unsafe" methods have no such flexibility. They must be unconditionally safe, and are required to close the safety gaps created by their dependencies. That's the critical difference that motivates the proposal.

This proposal defines multiple "safety markers" to add to C#:

- Mark unsafe methods with [`safe` to suppress unsafe propagation](./safe-boundary-marker.md), resulting in a much brighter light where safety obligations matter most. This is instead of the safety claim and unsafe suppression being indicated via absence of a marker.
- Add/enforce [Rust-style safety comments](./safety-comments.md)
- Add [`Safety` attributes](./safety-comments.md) to unsafe methods to enable querying code for obligations, in part to sum the obligations that must be discharged at a given safe/unsafe boundary.

## unsafe methods

The business of unsafe methods:

- Unsafe methods are by definition not safe to call for unconditional inputs or naive callers.
- They take liberties with safety in service of their business.
- The liberties are the subject of the safety documentation.
- Safety can only be achieved in a caller by complying with the safety documentation.
- Safety = sound unsafe implementation + good safety documentation + caller compliance.

Safety can be thought of as an eventual consistency property. The point of consistency is the unsafe boundary.

## `safe keyword`

This proposal advocates for an explicit `safe` marking for unsafe boundary methods. `safe` is solely a statement of the caller-contract and is therefore the opposite of `unsafe`. It is obviously not a statement of the implementation, which will by its very nature contain `unsafe` blocks. Explicit markings are intended to make method differences starkly apparent in C#, visible in source control diffs (left and right side always have a term to compare), and to make safety roots trivial to discover (simple grep queries).

The [`CopyTo` method](https://github.com/dotnet/runtime/blob/a8836bb928cbb045bb19a1a2a3353f4aa23302f4/src/libraries/System.Private.CoreLib/src/System/String.cs#L427) is a concrete example of a method that would benefit from the `safe` keyword.

```csharp
safe void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
{
    ArgumentNullException.ThrowIfNull(destination);
    ArgumentOutOfRangeException.ThrowIfNegative(count);
    ArgumentOutOfRangeException.ThrowIfNegative(sourceIndex);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Length - sourceIndex, nameof(sourceIndex));
    ArgumentOutOfRangeException.ThrowIfGreaterThan(destinationIndex, destination.Length - count);
    ArgumentOutOfRangeException.ThrowIfNegative(destinationIndex);

    unsafe
    {
        Buffer.Memmove(
            destination: ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(destination), destinationIndex),
            source: ref Unsafe.Add(ref _firstChar, sourceIndex),
            elementCount: (uint)count);
    }
}
```

This example makes it clear that `CopyTo` has been attested to offer a safe contract. It structurally separates unsafe operations from the safe guards. The safe guards ensure that unsafe methods are called in a sound way. This pattern directly relates to the soundness of unsafe code being conditional on the way they are called.

The `safe` keyword proposal is a bit like driving on the Coquihalla, a notoriously dangerous highway in British Columbia. Mountain roads often have stiff guardrails on the "unsafe edge" to increase safety and make the boundary more evident. Best-in-class implementers add reflective contrasting color stripes to guardrails around the corners, enabling visibility in variety of light conditions, including in the dark.

Tap the guardrail and you'll lose a strip of paint. It is certain to be an exceptional experience and it is OK to panic in the process. Safe and sound.

Read [`safe` marks the unsafe boundary](./safe-boundary-marker.md) for a deeper analysis.

## Safety documentation

Safety related comments are common in standard libraries. They describe how methods can be called safely or the assumptions that were made to consider an algorithm safe. The Rust community has established a [safety comments](https://std-dev-guide.rust-lang.org/policy/safety-comments.html) standard. It is based on the observation that safety comments are special and only fully dispatch their intent if they are elevated above the fray of implementation concerns.

```csharp
/// <summary>
/// Returns a reference to the element at `elementOffset` from `source`.
/// </summary>
/// <safety>
/// `elementOffset` is not validated. The caller must ensure that the returned
/// reference stays within the same allocated object as `source`.
///
/// The lifetime of the returned reference is not validated. The caller must
/// ensure the underlying storage remains valid for any subsequent use.
/// </safety>
public static unsafe ref T Add<T>(ref T source, int elementOffset)
```

This update of `Unsafe.Add` `///` comments integrates and elevates safety comments to a more critical concern. They are now part of the "safety manual".

We can directly connect these obligations to the safe guards we saw in `String.CopyTo` above. As a reminder, `String.CopyTo` calls `Unsafe.Add`.

- `ThrowIfNegative(sourceIndex)` and `ThrowIfGreaterThan(count, Length - sourceIndex, nameof(sourceIndex))` discharge the `BufferLength` obligation for the source reference.
- `ThrowIfNegative(destinationIndex)` and `ThrowIfGreaterThan(destinationIndex, destination.Length - count)` discharge the `BufferLength` obligation for the destination reference.
- `ThrowIfNull(destination)` establishes that the destination storage exists before any reference arithmetic occurs.
- Immediate use of the resulting references by `Buffer.Memmove` contains the `Lifetime` obligation; the references do not escape the method.

Guards correlate with obligations; cause safety.

Read [Standardized comments are safety evolution](./safety-comments.md) for a deeper analyis.

## Safety attributes

Safety attributes summarize safety comments into a queryable typed marker. Attributes are the natural next step to describe obligations with a closed taxonomy.

Each `unsafe` method declares one or more `[Safety(SafetyKind.X, "description")]` attributes, one per residual obligation. `safe` methods declare none, because they have nothing to declare.

A defined taxonomy has multiple benefits:

- Grepable over source
- Reflection-queryable over binaries
- Straightforward to determine the set of obligations that should be discharged between (indirect) callees and the root boundary method.
- Free-form safety comment text will tend to adopt the same terms

We can update `Unsafe.Add` one more time, now with safety attributes:

```csharp
/// <summary>
/// Returns a reference to the element at `elementOffset` from `source`.
/// </summary>
/// <safety>
/// `elementOffset` is not validated. The caller must ensure that the returned
/// reference stays within the same allocated object as `source`.
///
/// The lifetime of the returned reference is not validated. The caller must
/// ensure the underlying storage remains valid for any subsequent use.
/// </safety>
[Safety(SafetyKind.BufferLength)]
[Safety(SafetyKind.Lifetime)]
public static unsafe ref T Add<T>(ref T source, int elementOffset)
```

Again, `String.CopyTo` supplies the matching proof:

- `sourceIndex` guards -> `BufferLength` for the source
- `destinationIndex` guards -> `BufferLength` for the destination
- `ThrowIfNull(destination)` -> destination storage exists
- immediate `Buffer.Memmove` use -> `Lifetime` is contained

There is no `SafetyObligation` and `SafetyDischarge` pair. This choice is based on the observation that the risk of fidelity due to code drift is assymetric, with `SafetyObligation` being annoying and `SafetyDischarage` being devestating. The single `Safety` attribute is the `SafetyObligation` side of that pair.

Read [Standardized comments are safety evolution](./safety-comments.md) for a deeper analysis. The introduction of safety attributes comes after the half-way point.

## Stress test

We can perform a thought exercise about a hyper-successful Rust. What if all the C++ code in .NET apps was replaced with Rust? This isn't even that hard to imagine. One can imagine establish a safer profile of C ABI across the boundary. We're actually nearly there with `LibraryImport`. We can better prepare for that future by stress testing safety as a currency that "interops" across the boundary. Concepts that are unspeakable or that don't naturally compose are opportunities to update the model.

## Relation to AI

We don't know where the industry is headed next given the quick rise of agents. AI research tells us well-defined grammars perform better than weaker ones. It also tells us AIs are much weaker at absense or negation than positive terms. We also know that confidence and alignment are unsolved problems. There is no frequently cited research that advocates for simultaneously weakening grammars while increasing critical characteristics such as safety as a profitable direction.

## Complete analysis

The following documents make this case, listed in recommend order of reading:

- [`safe` marks the unsafe boundary](./safe-boundary-marker.md)
- [Standardized comments as safety evolution](./safety-comments.md)
- [Safety model comparison](./safety-model.md) — the Rust vs C# memory-safety model, stated directly and side-by-side
- [Notable patterns](./notable-patterns.md) — real-world examples from .NET, Rust, and Swift standard libraries
- [CVE analysis](./cve-analysis.md) — 40 .NET CVEs analyzed for safety boundary relevance
- [Audit graphs](./audit-graphs.md) — why shallow, DAG-like proof structure is reviewable and cyclic proof graphs are not
- [Interop identity transform](./interop-identity-transform.md) — use C#-Rust FFI round-trips as a validation harness for the safety model
- [Language comparison](./language-comparison.md) — grep-based discoverability across D, Rust, Swift, and C#, in ranking order
- [Scoring methodology](./scoring-methodology.md) — the grep test framework and detailed scoring
- [Runtime replay inspection guide](runtime-replay-cases.md) — direct links to the vulnerable snapshots, controls, and proposal branches used in the runtime benchmark
- [Research support](./research-support.md) — how explicit safety markers improve LLM accuracy and efficiency
- [Appendices](./appendices.md) — optional background on lossless attestations, xz, binary distribution, agent workflows, and keyword lineage

## Scratch

Deep analysis of Rust and C# demonstrates the degree to which they are a good pairing, particularly after memory safety v2 is delivered. We should be motivated to ensure this pairing is attractive as possible, both because of the increasing popularity of Rust, but because no other native toolchain offers the same attractive similarlity (async, strict safety, FFI friendly, `///` comments), and disimilarity (GC vs no GC). A key way to make this pairing attractive is aligning the safety models (as much as possible). The opportunity can be visualized as safe p/invokes to safe Rust with no break in safety analysis through the call chain (and only one GC).
