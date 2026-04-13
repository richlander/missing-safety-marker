# Standardized comments are safety evolution

> Generally, a comment is an annotation intended to make the code easier for a programmer to understand – often explaining an aspect that is not readily apparent in the program (non-comment) code.

Source: https://en.wikipedia.org/wiki/Comment_(computer_programming)

Comments are used for a variety of purposes in large projects. Many members benefit from comments that describe edge conditions, like how [`Array.MaxLength`](https://github.com/dotnet/runtime/blob/6b6a8b5f46ba62baa83bf5391c3ce093d0b30c4c/src/libraries/System.Private.CoreLib/src/System/Array.cs#L2632-L2640) defines its implementation and edge-case constraints. Unsafe members, like [`MemoryMarshal.CreateSpan()`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/MemoryMarshal.cs#L214-L229), are a special case that impose a obligation on safe use on the caller. The open question is how to scale the production of [safe code at the boundary](./safe-boundary-marker.md) when there are so many sharp edges. The state of the art is safety documentation.

The Rust community has a long history of safety comments, including ongoing efforts to improve documentation with [rust-lang/rust-project-goals #511](https://github.com/rust-lang/rust-project-goals/pull/511). We should incorporate this tradition in our Unsafe Evolution work.

The entire point is that the compiler cannot make any guarantees about unsafe code, requiring some semi-formal system above the language to fill that gap. To a large degree, it's a best effort system. Rust's safety document is state of the art. However, there is nother special about the format. In the end, its the calories that are applied to writing and reviewing the comments, with at least the same rigor as the code.

## Domain of comments

Safety comments range from:

1. This is what the method does, which is unsafe by implication.
2. This is what the caller has to do to discharge obligations.
3. This is what the method did to discharge obligations.

There is a gap between case 1 and cases 2 and 3, which represents a maturity gradient. Case 1 is where at least some code starts before any standardized system exists. Case 2 is the formal contract that the Rust `# Safety` convention introduced. Case 3 is the implementation worksheet that accompanies it.

There is an electrical analogy here. If you misunderstand potential, then your code may be subject to a shocking discharge. If that is too much, go with the green wire.

Three examples have been chosen from the Rust standard library and are presented in order of maturity — from the absence of a system, to partial adoption, to the fully onboarded style.

Note: The Rust standard library is a large body of code. It takes a long time to transition code to newer styles, with nullable reference types and the upcoming `unsafe` syntax changes being analogous examples for C#. It is inspiring to see the Rust community taking on the charter of safety improvement.

### Case 1: description only

The [`atomic_store` intrinsic](https://github.com/rust-lang/rust/blob/cd14b73b4a41542d921f59e362a5b5005fa4f2ef/library/core/src/intrinsics/mod.rs#L134-L141) demonstrates what safety documentation looks like before any formal system has been adopted.

```rust
/// Stores the value at the specified memory location.
/// `T` must be an integer or pointer type.
#[rustc_intrinsic]
#[rustc_nounwind]
pub unsafe fn atomic_store<T: Copy, const ORD: AtomicOrdering>(dst: *mut T, val: T);
```

Two terse sentences, leaving the implied responsibility of storing through a raw pointer to an _expert_ reader. This is not broken code. It is code that was written before the community fully established a convention for stating obligations explicitly. The `unsafe fn` keyword signals that obligations exist; the doc just doesn't fully spell out what they are.

A correct formal safety section for this method would need three bullets:

- `dst` must be valid for writes.
- `dst` must be properly aligned for `T` (natural alignment is required for atomic access).
- `dst` must not be concurrently accessed by non-atomic operations on any thread.

All three are implicit in the one-liner. Case 1 is the condition that the Rust safety documentation initiative is designed to fix.

Note: The `///` comments look more formal than interior comments. For an intrinsic, the `///` comment style is the only choice (no body).

### Case 2: partial adoption

The [`transmute_copy` example](https://github.com/rust-lang/rust/blob/d0442e2800d356ae282ddcdbe0eff8798fe648b6/library/core/src/mem/mod.rs#L1072) shows the next step: interior `// SAFETY:` comments have been adopted, but the formal `# Safety` section is still absent.

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

This is an improvement over case 1 — a reviewer is given more hints at each unsafe operation. The interior comments mix case 2 and and case 3 content. The comments are repeated across two branches of an if statement. There is no single formal statement of effective caller responsibilities. [rust-lang/rust #154665](https://github.com/rust-lang/rust/pull/154665) aims to transition these comments to the newer style.

### Case 3: fully onboarded

The [`as_bytes_mut` example](https://github.com/rust-lang/rust/blob/a08f25a7ef2800af5525762e981c24d96c14febe/library/core/src/str/mod.rs#L278) comes from the Rust [Safety comments](https://std-dev-guide.rust-lang.org/policy/safety-comments.html) page, so is presumably best-in-class. It demonstrates what safety documentation looks like when both blocks are used correctly.

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

The formal `# Safety` section (case 2) states the caller obligation clearly: maintain UTF-8 validity before the borrow ends. The `unsafe` keyword propagates upwards and (in effect) includes the formal caller contract with it. The triple slash comments indicate formal documentation and has [linter support](https://rust-lang.github.io/rust-clippy/master/index.html#missing_safety_doc).

The interior `// SAFETY:` comment (case 3) is a separate concern: a worksheet for code writers and reviewers on how soundness is maintained at this specific site. It names the structural reasoning ("str has the same layout as &[u8]") and the access guarantee ("comes from a mutable reference"). The note about "only libstd" is saying "don't do this in your code; it isn't a general pattern."

The two blocks serve different audiences and should not be confused. The formal block faces the caller. The interior block faces the reviewer.

### Guidelines

In general, `unsafe` methods should have both blocks while safe-callable boundary methods should only have the interior block since they suppress the unsafety contract.

The Rust standard library follows this convention: interior comments name the local reasoning ("we just checked that `mid` is on a char boundary"), with much less emphasis or responsbility on the caller obligation. The formal block handles the latter ("the caller must ensure valid UTF-8").

## Safety comments as auditor-facing evidence

Safety comments are a developer practice, but they have a second audience: auditors and safety assessors in regulated industries.

Paul LeVasseur made this point directly in the [goal #511 discussion](https://github.com/rust-lang/rust-project-goals/pull/511):

> "Having a standard like this would be of great benefit as Rust continues to see adoption into safety-critical industries like automotive, medical devices, industrial, and so on. The ability to write down these things can help organizations and teams to show traceability to having followed such a standard when they take their software to safety assessors."

The Rust-for-Linux effort applies the same pressure from a different direction. The kernel is not a regulated product, but it has its own review culture that demands explicit justification for unsafe operations. The [kernel coding guidelines](https://docs.kernel.org/rust/coding-guidelines.html) require that every `unsafe` block be preceded by a `// SAFETY:` comment. Benno Lossin's [patch series introducing a Rust Safety Standard for the kernel](https://lore.kernel.org/rust-for-linux/20240717221133.459589-1-benno.lossin@proton.me/) opens with two concrete statements: "`unsafe` Rust code in the kernel is required to have safety documentation" and "at this point in time there does not exist a standard way of writing safety documentation ... it's the wild west." Miguel Ojeda, the Rust-for-Linux maintainer, reinforced this in [goal #511](https://github.com/rust-lang/rust-project-goals/pull/511): "Over the years, we have had many discussions on how it would be best to write `# Safety` sections, `// SAFETY` comments, and so on ... and generally how to handle unsafety in the kernel." The kernel's needs and the automotive/industrial needs converge on the same requirement: safety reasoning must be written down, standardized, and reviewable by someone other than the original author.

This reframes what safety comments are for. A `// SAFETY:` comment is not just a note to the next developer. It is a local proof term — adjacent to the sharp operation, preserved in `git blame`, visible in review — that an auditor can point to when asked "how do you know this is sound?" The [2026 project goal on improving unsafe code documentation](https://rust-lang.github.io/rust-project-goals/2026/improve-std-unsafe.html) makes the quality bar explicit: the documentation must match the rigor of the code itself.

## Applying safety comments to C\#

The safety rigor being applied to Rust is equally relevant to C#. The risk profile of pacemakers and webservers are not the same, however, the baseline ingredients that build confidence are identical.

C# already has a `///` commenting scheme. It would be straightforward to add a `<safety>` section and also enable markdown. It's well within reach to define the same system. It would enable teams that write both C# and Rust to reason about safety with a similar lens across both languages.

We also have the opportunity to evolve the Rust system with a queryable taxonomy system, using attributes. It's a natural extension for our metadata-rich system and tradition.

The problem is solve is the reality that obligations flow to safety. Correct discharge enables boundary methods to provide a fascade that is level with compiler-validated safe code, much like how the Columbia meets the ocean level with Pacific waters. We want to avoid the rare and spectactular case where a river meets the ocean with falls over a cliff.

### A typed Safety attribute

Comments with free-formed text are the primary way to describe safety obligations. The next frontier is a closed taxonomy of obligation kinds. Each `unsafe` method declares one or more `[Safety(SafetyKind.X, "description")]` attributes, one per residual obligation. `safe` methods declare none, because they have nothing to declare.

A defined taxonomy has multiple benefits:

- Free-form text will tend to adopt the same terms
- Grepable-source, for the attributes
- Reflection-queryable over binaries
- Straightforward to determine the set of obligations that should be discharged between (indirect) callees and the root boundary method.

One guesses that the auditors currently reviewing Rust code would appreciate this level of formalism and tool friendliness. C# is likely not as heavily audited as Rust, however, the requirement of auditing is a future we should be equally preparing for.

### The taxonomy

A first cut defines twelve kinds plus an escape hatch, organized by the axis of failure they correspond to. Each kind captures a distinct local proof obligation: a different kind of thing the caller has to verify, with an associated failure mode.

| Axis | Kind | Meaning | Example |
|---|---|---|---|
| Spatial | `BufferLength` | Buffer or span has at least the required size in elements or bytes. | [`MemoryMarshal.CreateSpan`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/MemoryMarshal.cs#L228) |
| | `PointerNonNull` | Pointer is not null where dereference is intended. | [`Marshal.ReadByte`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.cs#L287) |
| | `Alignment` | Pointer or reference is aligned for the target type. | [`MemoryMarshal.AsRef`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/MemoryMarshal.cs#L554) |
| Temporal | `Lifetime` | Returned reference or span must not outlive backing storage. | [`CollectionsMarshal.AsSpan`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/CollectionsMarshal.cs#L23) |
| | `Pinning` | Caller has pinned movable storage for the duration of the access. | [`MemoryMarshal.CreateFromPinnedArray`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/MemoryMarshal.cs#L606) |
| | `Aliasing` | No conflicting read or write to the same memory during the access. | [`Buffer.MemoryCopy`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Buffer.cs#L105) |
| Representation | `Initialization` | Memory is initialized to a meaningful value before read. | [`Unsafe.SkipInit`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/Unsafe.cs#L824) |
| | `ValidBitPattern` | Bytes form a legal value of the target type (no invalid enums, no invalid references, no invalid UTF-8). | [`Unsafe.BitCast`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/Unsafe.cs#L265) |
| | `TypeShape` | Type parameter satisfies an unenforced structural constraint (`unmanaged`, no references, etc.). | [`Marshal.SizeOf<T>()`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.cs#L122) |
| Concurrency | `ThreadSafety` | Caller serializes concurrent access externally. | [`NativeMemory.Free`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/NativeMemory.Unix.cs#L182) |
| Foreign | `NativeContract` | External library's documented preconditions are met. | [`Marshal.PtrToStructure<T>()`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/Marshal.cs#L600) |
| Escape hatch | `Other` | Free-form obligation not covered above; description required. | — |

The primary win of a closed taxonomy is consistency and tool-based access/inventory/querying. One can imagine a tool that enumerate methods with a given safety kind or inventory all obligations starting from safe boundary method. It's also possible to correlate stack traces from production crashes to proof obligations in the search of patterns or culprits.

Every kind in the table is motivated by an existing method in `System.Private.CoreLib` that already carries that obligation today — just not in a structured, queryable form.

### Examples

The Rust examples are all from the Rust Standard Library. The following examples are a sort of C# mirror of the same thing in the .NET Runtime Libraries. The examples are taken from [`MemoryMarshal`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/MemoryMarshal.cs), each at a different position on the spectrum from fully-discharged to fully-on-the-caller.

The `<safety>` doc element carries the complete prose — the authoritative human-readable contract, consistent with Rust's `# Safety` convention. The `[Safety]` attribute carries only the taxonomy kind, making the obligation machine-readable without duplicating the prose. A description string on the attribute is reserved for cases where the kind name alone is insufficient (e.g., `SafetyKind.Other`).

[`MemoryMarshal.CreateSpan<T>`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/MemoryMarshal.cs#L214-L229):

Before:

```csharp
/// <summary>
/// Creates a new span over a portion of a regular managed object. This can be useful
/// if part of a managed object represents a "fixed array." This is dangerous because the
/// <paramref name="length"/> is not checked.
/// </summary>
/// <param name="reference">A reference to data.</param>
/// <param name="length">The number of <typeparamref name="T"/> elements the memory contains.</param>
/// <returns>A span representing the specified reference and length.</returns>
/// <remarks>
/// This method should be used with caution. It is dangerous because the length argument is not checked.
/// Even though the ref is annotated as scoped, it will be stored into the returned span, and the lifetime
/// of the returned span will not be validated for safety, even by span-aware languages.
/// </remarks>
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static Span<T> CreateSpan<T>(scoped ref T reference, int length) =>
    new Span<T>(ref Unsafe.AsRef(in reference), length);
```

- Two caller obligations.
- No soundness maintenance.
- Currently not marked `unsafe`.

After:

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
[Safety(SafetyKind.BufferLength)]
[Safety(SafetyKind.Lifetime)]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static unsafe Span<T> CreateSpan<T>(scoped ref T reference, int length)
{
    unsafe
    {
        return new Span<T>(ref Unsafe.AsRef(in reference), length);
    }
}
```

- Adds `unsafe` to signature (formally passes obligations to callers)
- Adds two `[Safety]` attributes
- Adds `<safety>` doc
- Adds interior unsafe block
- No inner safety comment since there is nothing to say (pure passthrough).

[`MemoryMarshal.AsRef<T>(Span<byte>)`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/MemoryMarshal.cs#L545-L566):

Before:

```csharp
/// <summary>
/// Re-interprets a span of bytes as a reference to structure of type T.
/// The type may not contain pointers or references. This is checked at runtime in order to preserve type safety.
/// </summary>
/// <remarks>
/// Supported only for platforms that support misaligned memory access or when the memory block is aligned by other means.
/// </remarks>
[MethodImpl(MethodImplOptions.AggressiveInlining)]
[OverloadResolutionPriority(1)]
public static unsafe ref T AsRef<T>(Span<byte> span)
    where T : struct
{
    if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
    {
        ThrowHelper.ThrowArgument_TypeContainsReferences(typeof(T));
    }
    if (span.Length < sizeof(T))
    {
        ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.length);
    }
    return ref Unsafe.As<byte, T>(ref GetReference(span));
}
```

- One caller obligation (alignment).
- Two runtime checks discharge the type-shape and length conditions.

After:

```csharp
/// <summary>
/// Re-interprets a span of bytes as a reference to a structure of type <typeparamref name="T"/>.
/// </summary>
/// <safety>
/// On platforms that disallow misaligned memory access, <paramref name="span"/>
/// must be aligned for `T` by some external guarantee. **This is not checked.**
/// </safety>
[Safety(SafetyKind.Alignment)]
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

- Adds `[Safety]` attribute for the residual alignment obligation
- Adds `<safety>` doc detailing same
- Adds interior `// SAFETY:` comment naming what was discharged and what remains

[`MemoryMarshal.AsBytes<T>(Span<T>)`](https://github.com/dotnet/runtime/blob/ddba7854ea97d1899650af2779a98fcfdc3fc83c/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/MemoryMarshal.cs#L18-L40):

Before:

```csharp
/// <summary>
/// Casts a Span of one primitive type <typeparamref name="T"/> to Span of bytes.
/// That type may not contain pointers or references. This is checked at runtime in order to preserve type safety.
/// </summary>
/// <param name="span">The source slice, of type <typeparamref name="T"/>.</param>
/// <exception cref="ArgumentException">
/// Thrown when <typeparamref name="T"/> contains pointers.
/// </exception>
/// <exception cref="OverflowException">
/// Thrown if the Length property of the new Span would exceed int.MaxValue.
/// </exception>
[MethodImpl(MethodImplOptions.AggressiveInlining)]
[OverloadResolutionPriority(1)]
public static unsafe Span<byte> AsBytes<T>(Span<T> span)
    where T : struct
{
    if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        ThrowHelper.ThrowArgument_TypeContainsReferences(typeof(T));

    return new Span<byte>(
        ref Unsafe.As<T, byte>(ref GetReference(span)),
        checked(span.Length * sizeof(T)));
}
```

- Obligations are fully discharged by the runtime check.

After:

```csharp
/// <summary>
/// Casts a Span of one primitive type <typeparamref name="T"/> to Span of bytes.
/// </summary>
/// <exception cref="ArgumentException">
/// Thrown when <typeparamref name="T"/> contains pointers or references.
/// </exception>
/// <exception cref="OverflowException">
/// Thrown if the Length property of the new Span would exceed int.MaxValue.
/// </exception>
[MethodImpl(MethodImplOptions.AggressiveInlining)]
[OverloadResolutionPriority(1)]
public static safe Span<byte> AsBytes<T>(Span<T> span)
    where T : struct
{
    if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        ThrowHelper.ThrowArgument_TypeContainsReferences(typeof(T));

    unsafe
    {
        // SAFETY: the runtime check above guarantees T contains no managed
        // references, and `where T : struct` guarantees T is a value type.
        // Reinterpreting any such T's storage as `byte` is layout-safe.
        return new Span<byte>(
            ref Unsafe.As<T, byte>(ref GetReference(span)),
            checked(span.Length * sizeof(T)));
    }
}
```

- Switches `unsafe` with `safe` given that all obligations have been discharged.
- Adds inner `unsafe { }` block.
- No `[Safety]` attribute because there is nothing left for the caller to verify.
- The absence of `[Safety]` is descriptive of the contract.

### Guidance

The number of `[Safety]` attributes equals the number of `<safety>` paragraphs equals the number of unfulfilled obligations. The three are redundant on purpose: the attribute is the canonical machine-readable form, the doc is the human-readable form, and the interior `// SAFETY:` comment is the local proof worksheet. A combination of Roslyn analyzer and LLM analysis can require/determine they correspond.

Tools can present reviewers and writers of unsafe code with transitive obligations. It is then up to them to walk the graph to validate that obligations have been fully discharged by the time the safe/unsafe boundary has been crossed.

The document system is only as good as the effort put into it. This is, in part, why the attribute model only describes obligations not discharge. There is no `[SafetyDischarges(SafetyKind.BufferLength)]`. A sort of "checked" model would be attractive and enable tools to "prove" the safety of code. The proposed model (C# or Rust) is already subject to under-reporting of safety obligations. That's a serious problem to consider. Over-reporting discharge is a much more serious issue that we shouldn't invite into common practice. Over-reporting can happen as a result of incorrect marking or as a result of drift with the code.

### What the analyzer should enforce

Three rules cover most of the value.

1. **Every `unsafe` method must declare at least one `[Safety]` attribute.** A method with none either should be `safe` (the obligation has been discharged), or is missing documentation. Either case is a diagnostic to resolve.
2. **Every `[Safety]` kind must have a corresponding paragraph in the `<safety>` doc element**, and every `<safety>` paragraph must correspond to a `[Safety]` kind. The `<safety>` element is the authoritative prose; the attribute carries only the taxonomy kind. A description string on the attribute is optional and should be used only when the kind name alone is insufficient (e.g., `SafetyKind.Other`).
3. **`safe` methods may not declare any `[Safety]` attribute.** This is the principle that safe code cannot pass safety obligations to its caller, enforced as a compile-time rule rather than a convention. If you write `[Safety(...)]` on a `safe` method, the analyzer rejects it. You either remove the attribute (because the obligation is actually discharged) or change the method to `unsafe` (because it isn't).

That third rule is the load-bearing one. It is what gives `safe` its meaning, and it is what makes the whole scheme honest. You cannot quietly pretend an obligation is gone when it isn't — the analyzer will not let you.

These rules enforce presentation, not verification. The analyzer does not try to prove that a specific guard actually discharges `SafetyKind.BufferLength`; that is a question about the code at a particular call site, and the proposal leaves it to human and/or agent review for the reasons given in the previous section.

### The benefits

A closed taxonomy turns several existing pain points into one-liner queries or compile-time checks.

- **Typed audit grep.** `grep -rn '\[Safety(SafetyKind\.Lifetime' --include="*.cs" .` finds every method in the runtime libraries (or any codebase) with a lifetime obligation, in seconds, with no false positives.
- **CVE retrofit.** The CVE analysis already classifies historical .NET bugs by structural pattern. Adding a `SafetyKind` column to each entry lets us say things like *"in this dataset, `BufferLength` dominates, with smaller `TypeShape`, `PointerNonNull` / `NativeContract`, and `Lifetime` / `ThreadSafety` buckets."* This is a human mapping exercise, not a tool-driven one — the taxonomy just gives the mapping a controlled vocabulary. The result is the kind of evidence that lands with safety assessors.
- **Structured API documentation.** API reference tooling can render a Safety panel for every `unsafe` method, grouped by kind, with links to a taxonomy reference page. The free-form `<safety>` text fills in per-method specifics; the taxonomy gives every page the same shape.
- **Binary-level inventory, as a side benefit.** When only reference assemblies are available — evaluating a third-party library, auditing a distributed component without source — reflection over `[Safety]` attributes still produces an obligation inventory. It can provide a sense of the gap that unsafe code generates and needs to be remediated before exposing a safe fascade.

### Open questions

These are calibration questions, not blockers. They get answered once the proposal is applied to enough real code.

- **Granularity.** Twelve feels right; ten might be cleaner; twenty starts to fragment. The right answer is "as coarse as possible while distinct proofs stay distinct." This needs review against the actual surface area of the BCL before being frozen.
- **`Other` discipline.** The escape hatch is necessary. If more than ~5% of unsafe methods need `Other`, the taxonomy is wrong and a new kind should be promoted. A periodic audit of `Other` usage is the feedback loop that keeps the taxonomy honest.
- **Conditional obligations.** `Alignment` is required only on certain platforms. `ThreadSafety` is required only if the method is called concurrently. Does the taxonomy capture this in the description string, or does each kind get an optional `Condition` field?
- **Versioning.** Adding a new `SafetyKind` is a binary-compatible enum extension. Tightening an analyzer rule is not. The taxonomy needs a clear "introduced in" lineage so analyzers can be opt-in by language version.

C# and Rust are more closely aligned in design philosophy than most cross-language pairings — much more so than C# and Go, for example — and a shared safety vocabulary would make mixed C# + Rust codebases materially more appealing for organizations that want what each stack does best without paying a steep tradeoff at the boundary.
