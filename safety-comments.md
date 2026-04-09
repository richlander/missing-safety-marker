# Standardized Comments as Safety Evolution

Developers write comments as some combination of just-in-time spec, reminder to future me, or to give others some hints as to what the code does or doesn't do. In the typical case, methods expose a functionality contract that performs a well understood operation, like `List<T>.Sort`. In other cases, the method also imposes an obligation on safe use where the fault for crashes or security vulnerability falls (in theory) with the caller. In such a case, the caller deserves a bit of help.

> Generally, a comment is an annotation intended to make the code easier for a programmer to understand – often explaining an aspect that is not readily apparent in the program (non-comment) code.

Source: https://en.wikipedia.org/wiki/Comment_(computer_programming)

The Rust community has a long history of [Safety comments](https://std-dev-guide.rust-lang.org/policy/safety-comments.html). There is also a more recent effort to improve this style of ducumentation with [rust-lang/rust-project-goals #511](https://github.com/rust-lang/rust-project-goals/pull/511). We should incorporate this tradition in our Unsafe Evolution work.

## Domain of comments

Safety comments seems to range from:

- This is what the method does, which is unsafe by implication.
- This is what the caller has to do to discharge obligations.
- This is what the method did to discharge obligations.

Those descriptions contain a lot of the same words but are about as similar as hot and neutral wires and with the same shock potential. If you are only comfortable with the ground wire, stick to safe code.

This example from the Rust safety comments page demonstrates safety documentation in practice.

```rust
/// Converts a mutable string slice to a mutable byte slice.
///
/// # Safety
///
/// The caller must ensure that the content of the slice is valid UTF-8
/// before the borrow ends and the underlying `str` is used.
///
/// Use of a `str` whose contents are not valid UTF-8 is undefined behavior.
///
/// ...
pub unsafe fn as_bytes_mut(&mut self) -> &mut [u8] {
    // SAFETY: the cast from `&str` to `&[u8]` is safe since `str`
    // has the same layout as `&[u8]` (only libstd can make this guarantee).
    // The pointer dereference is safe since it comes from a mutable reference which
    // is guaranteed to be valid for writes.
    unsafe { &mut *(self as *mut str as *mut [u8]) }
}
```

The [`as_bytes_mut` example](https://github.com/rust-lang/rust/blob/a08f25a7ef2800af5525762e981c24d96c14febe/library/core/src/str/mod.rs#L278) includes two safety blocks. The first block explains how to discharge obligations and the interior block discusses a combination of how safety has been maintained and/or discharged. The note about "only libstd" is basically saying "don't do this in your code; it isn't a general pattern".

The first block is formal. It uses triple slash comments and is opt-in linter-enfored. The `unsafe` keyword propogates and (in effect) includes the formal caller contract with it. The interior safety block is informal, more of a worksheet to code writers/reviewers on what was done to maintain soundness.

In general, `unsafe` methods should have both of these blocks while safe-callable boundary methods should only have the interior block since they supress the unsafety contract.

The double and triple slash comment scheme will appear immediately familiar to any C# developer.

The following [`transmute_copy` example](https://github.com/rust-lang/rust/blob/d0442e2800d356ae282ddcdbe0eff8798fe648b6/library/core/src/mem/mod.rs#L1072) appears to use an older safety comment style.

```rust
pub const unsafe fn transmute_copy<Src, Dst>(src: &Src) -> Dst {
    assert!(
        size_of::<Src>() >= size_of::<Dst>(),
        "cannot transmute_copy if Dst is larger than Src"
    );

    // If Dst has a higher alignment requirement, src might not be suitably aligned.
    if align_of::<Dst>() > align_of::<Src>() {
        // SAFETY: `src` is a reference which is guaranteed to be valid for reads.
        // The caller must guarantee that the actual transmutation is safe.
        unsafe { ptr::read_unaligned(src as *const Src as *const Dst) }
    } else {
        // SAFETY: `src` is a reference which is guaranteed to be valid for reads.
        // We just checked that `src as *const Dst` was properly aligned.
        // The caller must guarantee that the actual transmutation is safe.
        unsafe { ptr::read(src as *const Src as *const Dst) }
    }
}
```

It is missing the formal block, relying solely on interior comments. It mixes assumptions, safety checks, and caller responsibilities. The comments are largely repeated because they are documenting two branches of an if statement. That's likely not  desirable for most readers, who would benefit from a single formal statement of effective responsibiles for callers. [rust-lang/rust #154665](https://github.com/rust-lang/rust/pull/154665) aims to transition these comments to the newer style.

The Rust standard library is a large body of code. It takes a long time to transition code to newer styles, much like our own work with nullable, and that we'll need to take on with our Unsafe project. It is impressive see the Rust community taking on the charter of safety improvement.

## Applying safety comments to C\#

C# already has a `///` commenting scheme. It would be straightforward to add a `<safety>` section and also enable markdown, which is already present in some C# files. This approach would enable teams that write both C# and Rust to have an almost identical scheme between both languages.

There is some discussion in Rust forums about how safety comments can be used by auditors as an input. That's a compelling byproduct of an engineering quality system. The open question is whether we can deliver a more well-defined end-to-end experience if we expand out the edges of what Rust has defined.

### A typed Safety attribute

Comments cover the source-visible part of the safety story. The next step is to give that story a *fixed vocabulary* — a closed taxonomy of obligation kinds — that prose descriptions, interior `// SAFETY:` comments, and source-level analysis all reason about in the same terms. The `Safety` attribute is how that vocabulary gets delivered. Each `unsafe` method declares one or more `[Safety(SafetyKind.X, "description")]` attributes, one per residual obligation. `safe` methods declare none, because they have nothing to declare.

The value is the taxonomy itself, not the metadata the attribute happens to put in binaries. Reflection-queryable obligations are a small side benefit — fine for producing inventories when only reference assemblies are available — but the real wins are source-level: compile-time discharge checking, call-graph audits by obligation kind, and historical CVE retrofit. All of those require source and a shared vocabulary. None of them require reflection.

### The taxonomy

A first cut, twelve kinds plus an escape hatch, organized by the axis of failure they correspond to. Each kind captures a *distinct local proof obligation*: a different kind of thing the caller has to verify, with a different kind of failure mode if they get it wrong.

| Axis | Kind | Meaning |
|---|---|---|
| Spatial | `BufferLength` | Buffer or span has at least the required size in elements or bytes. |
| | `PointerNonNull` | Pointer is not null where dereference is intended. |
| | `Alignment` | Pointer or reference is aligned for the target type. |
| Temporal | `Lifetime` | Returned reference or span must not outlive backing storage. |
| | `Pinning` | Caller has pinned movable storage for the duration of the access. |
| | `Aliasing` | No conflicting read or write to the same memory during the access. |
| Representation | `Initialization` | Memory is initialized to a meaningful value before read. |
| | `ValidBitPattern` | Bytes form a legal value of the target type (no invalid enums, no invalid references, no invalid UTF-8). |
| | `TypeShape` | Type parameter satisfies an unenforced structural constraint (`unmanaged`, no references, etc.). |
| Concurrency | `ThreadSafety` | Caller serializes concurrent access externally. |
| Foreign | `NativeContract` | External library's documented preconditions are met. |
| Escape hatch | `Other` | Free-form obligation not covered above; description required. |

Twelve plus `Other` is closed enough to mean something — a tool can enumerate every method in the BCL with a given safety kind and produce a report — and open enough to absorb edge cases without forcing a category mismatch. The granularity rule is: a kind exists if there is a *distinct local proof* for it. Splitting `PointerValidity` into `PointerNonNull` + `Lifetime` + `Initialization` is deliberate, because each one corresponds to a different failure mode and a different caller proof.

### Applied to the running examples

`MemoryMarshal.CreateSpan<T>` has two unenforced caller obligations. It gets two attributes:

```csharp
/// <summary>
/// Creates a new span over a portion of a regular managed object.
/// </summary>
/// <safety>
/// `length` is **not validated**. The caller must ensure that at least `length`
/// contiguous elements of `T` are reachable from `reference`.
///
/// The returned span's lifetime is **not validated** by the compiler. The
/// caller must ensure the span does not outlive the storage that
/// `reference` points to.
/// </safety>
[Safety(SafetyKind.BufferLength,
    "`length` elements of T must be reachable from `reference`.")]
[Safety(SafetyKind.Lifetime,
    "Returned span must not outlive the storage `reference` points to.")]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static unsafe Span<T> CreateSpan<T>(scoped ref T reference, int length)
{
    unsafe
    {
        // SAFETY: BufferLength and Lifetime obligations are caller responsibilities
        // per [Safety] attributes. There is no runtime check.
        return new Span<T>(ref Unsafe.AsRef(in reference), length);
    }
}
```

`MemoryMarshal.AsRef<T>(Span<byte>)` has one residual obligation, after two runtime checks discharge the type-shape and length conditions. It gets one attribute:

```csharp
/// <summary>
/// Re-interprets a span of bytes as a reference to a structure of type <typeparamref name="T"/>.
/// </summary>
/// <safety>
/// On platforms that disallow misaligned memory access, <paramref name="span"/>
/// must be aligned for `T` by some external guarantee. **This is not checked.**
/// </safety>
[Safety(SafetyKind.Alignment,
    "On strict-alignment platforms, span must be aligned for T.")]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static unsafe ref T AsRef<T>(Span<byte> span)
    where T : struct
{
    if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        ThrowHelper.ThrowArgument_TypeContainsReferences(typeof(T));
    if (span.Length < sizeof(T))
        ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.length);

    unsafe
    {
        // SAFETY: TypeShape and BufferLength are discharged by the runtime checks
        // above. Alignment remains the caller's responsibility per [Safety].
        return ref Unsafe.As<byte, T>(ref GetReference(span));
    }
}
```

`MemoryMarshal.AsBytes<T>` gets **zero** attributes. It is `safe`, the runtime check discharges the only obligation, and there is nothing left for the caller to verify. The absence of `[Safety]` is itself the contract.

The number of `[Safety]` attributes equals the number of `<safety>` paragraphs equals the number of unfulfilled obligations. The three are redundant on purpose: the attribute is the canonical machine-readable form, the doc is the human-readable form, and the interior `// SAFETY:` comment is the local proof worksheet. A Roslyn analyzer can require they correspond.

### End to end

A note on what the attribute model covers and what it deliberately does not. In theory, attributes could signal both obligations *and* discharge — `[Safety(SafetyKind.BufferLength, ...)]` on an unsafe method, `[SafetyDischarges(SafetyKind.BufferLength)]` on a guarded caller — so a tool could prove the full chain of responsibility. That sounds appealing and would suffer from drift. The two kinds of drift are not symmetric. Obligation drift is tolerable: a stale `[Safety]` attribute that no longer matches the code is a bug in the direction of over-reporting, and a reviewer who notices it still reads the code correctly. Discharge drift is much worse: a stale `[SafetyDischarges]` attribute would quietly claim a soundness guarantee the code no longer provides, and the reviewer who trusts it would stop reading the code at exactly the moment it matters most. False positives on obligations cost time. False negatives on discharge cost correctness. The proposal therefore models obligations only. Discharge stays in the code, where the reviewer can read it and the analyzer cannot fake it.

Under that model, each edge in the call graph presents an obligation to the caller. The reviewer walks the edge and asks one of two questions: does this method *discharge* the obligation locally (by reading the code around the call site and checking the guards), or does it *re-present* the obligation by carrying a matching `[Safety]` attribute on its own signature? If neither, the edge is a bug — the obligation has disappeared from the attribute graph without being accounted for. Two scenarios show how this plays out in practice. Both are source-level.

**Adding a new unsafe method.** A developer adds an unsafe method to `System.Private.CoreLib` with `[Safety(SafetyKind.Alignment, "...")]`. A source-level tool enumerates every `safe` method that transitively calls it, and every intermediate unsafe method on each path. Each path becomes a review task. At each edge along the path, the reviewer applies the discharge-or-re-present test. The developer's job is to arrange for every path to terminate honestly: by adding a guard that discharges at a specific edge, by propagating a matching `[Safety]` attribute upward so the next method re-presents the obligation, or by changing a public signature from `safe` to `unsafe` so the obligation surfaces in the BCL's public contract and stops there.

**Auditing an existing library.** A reviewer asks: *show me every `Lifetime` obligation in `System.Private.CoreLib` and every path from a `safe` root that reaches it.* A source-level tool produces the list of methods tagged `[Safety(SafetyKind.Lifetime, ...)]` and walks the call graph upward from each one. Each path is annotated with the edges where the obligation is re-presented (hops that carry the same attribute on their own signature) and the edges where it disappears (the transition from `unsafe` back to `safe`). Those transition edges are the review points. The reviewer reads the code around each one and either confirms discharge or flags the boundary for deeper analysis. Repeat for each `SafetyKind` and the result is a per-kind coverage map for the entire library, with every review decision anchored to a specific line of code.

The taxonomy is what makes both scenarios workable. It gives human review a targeted, finite, kind-indexed worklist instead of an unbounded prose-reading exercise. The attribute carries just enough information to locate the worklist. Everything downstream of the worklist is code reading, and code reading is what the attribute model is deliberately *not* trying to replace.

### What the analyzer enforces

Three rules cover most of the value. Each one is a few hundred lines of analyzer, no more.

1. **Every `unsafe` method must declare at least one `[Safety]` attribute.** A method with none either should be `safe` (the obligation has been discharged), or is missing documentation. Either case is a bug.
2. **Every `[Safety]` kind must have a corresponding paragraph in the `<safety>` doc element**, and every `<safety>` paragraph must correspond to a `[Safety]` kind. The doc and the attribute are kept in sync mechanically.
3. **`safe` methods may not declare any `[Safety]` attribute.** This is the principle that safe code cannot pass safety obligations to its caller, enforced as a compile-time rule rather than a convention. If you write `[Safety(...)]` on a `safe` method, the analyzer rejects it. You either remove the attribute (because the obligation is actually discharged) or change the method to `unsafe` (because it isn't).

That third rule is the load-bearing one. It is what gives `safe` its meaning, and it is what makes the whole scheme honest. You cannot quietly pretend an obligation is gone when it isn't — the analyzer will not let you.

These rules enforce *presentation*, not verification. The analyzer does not try to prove that a specific guard actually discharges `SafetyKind.BufferLength`; that is a question about the code at a particular call site, and the proposal leaves it to human review for the reasons given in the previous section.

### What this gives the project

Beyond the two end-to-end scenarios above, a closed taxonomy turns several existing pain points into one-liner queries or compile-time checks.

- **Typed audit grep.** `grep -rn '\[Safety(SafetyKind\.Lifetime' --include="*.cs" .` finds every method in the BCL with a lifetime obligation, in seconds, with no false positives. Same discoverability story as the README's grep-ability section, with semantic precision instead of keyword matching.
- **CVE retrofit.** The CVE analysis already classifies historical .NET bugs by structural pattern. Adding a `SafetyKind` column to each entry would let us say things like *"of the 40 CVEs analyzed, 12 are `BufferLength`, 8 are `Aliasing`, 5 are `Initialization`."* This is a human mapping exercise, not a tool-driven one — the taxonomy just gives the mapping a controlled vocabulary. The result is the kind of evidence that lands with safety assessors.
- **Structured API documentation.** API reference tooling can render a Safety panel for every `unsafe` method, grouped by kind, with links to a taxonomy reference page. The free-form `<safety>` text fills in per-method specifics; the taxonomy gives every page the same shape.
- **Binary-level inventory, as a side benefit.** When only reference assemblies are available — evaluating a third-party library, auditing a distributed component without source — reflection over `[Safety]` attributes still produces an obligation inventory. This is useful but not the main story, and it is strictly weaker than the source-level scenarios above: an inventory tells you *what obligations exist*, not *whether the safe callers discharge them*.

### Open questions

These are calibration questions, not blockers. They get answered once the proposal is applied to enough real code.

- **Granularity.** Twelve feels right; ten might be cleaner; twenty starts to fragment. The right answer is "as coarse as possible while distinct proofs stay distinct." This needs review against the actual surface area of the BCL before being frozen.
- **`Other` discipline.** The escape hatch is necessary. If more than ~5% of unsafe methods need `Other`, the taxonomy is wrong and a new kind should be promoted. A periodic audit of `Other` usage is the feedback loop that keeps the taxonomy honest.
- **Conditional obligations.** `Alignment` is required only on certain platforms. `ThreadSafety` is required only if the method is called concurrently. Does the taxonomy capture this in the description string, or does each kind get an optional `Condition` field?
- **Versioning.** Adding a new `SafetyKind` is a binary-compatible enum extension. Tightening an analyzer rule is not. The taxonomy needs a clear "introduced in" lineage so analyzers can be opt-in by language version.

C# and Rust are more closely aligned in design philosophy than most cross-language pairings — much more so than C# and Go, for example — and a shared safety vocabulary would make mixed C# + Rust codebases materially more appealing for organizations that want what each stack does best without paying a steep tradeoff at the boundary.
