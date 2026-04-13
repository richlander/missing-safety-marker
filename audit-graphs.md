# Shallow Structure is a Security Property

> Small, focused methods with a short path from safe validation to unsafe effect are reviewable. Large methods that repeatedly interleave safe logic and unsafe effects are not merely harder to read. They produce cyclic audit graphs that force the reviewer to re-enter earlier assumptions and are, in practice, not auditable at scale.

This paper is a companion to [notable-patterns.md](notable-patterns.md) and [cve-analysis.md](cve-analysis.md). It deliberately reuses a **closed set of examples** from those documents so the argument stays compact and easy to follow.

## The claim

The core claim is stronger than "small methods are nicer."

Security review is a proof exercise. The reviewer needs to answer a narrow question:

> *Why is this unsafe operation sound here?*

That question becomes tractable when the proof has a shallow, one-way shape:

```text
inputs -> safe guards -> unsafe operation -> result
```

That is a **DAG-shaped audit graph**. The reviewer can move forward once, confirm each condition, and stop.

The opposite shape is much worse:

```text
type checks -> helper calls -> unsafe op A -> mutation -> later guard -> unsafe op B
      ^                                                            |
      |____________________________________________________________|
```

Here the reasoning has back-edges. To justify one unsafe operation, the reviewer must understand a helper; to trust the helper, they must inspect a later mutation; to trust the mutation, they must return to an earlier assumption. The graph becomes **cyclic** or **re-entrant** from the auditor's point of view.

The key refinement is that **not every safe dependency creates meaningful cycle risk**. A call to `ArgumentNullException.ThrowIfNull` or a tiny pure predicate is almost proof notation: non-stateful, banal, and easy to trust. It may improve readability more than it increases audit burden. The real risk begins when unsafe code depends on **stateful or semantically rich safe code** — for example, a container method, a parser helper, a mutating routine, or any helper whose correctness itself depends on hidden invariants. That is where the auditor has to leave the local proof and start tracing nonlocal state.

Another way to say this is **call graph versus object graph**. With a safe static helper, the reviewer usually only needs to reason about the call graph: this function called that function, and the callee is simple enough to trust locally. With a stateful instance method, the reviewer must also reason about the object graph: receiver state, aliasing, ownership, representation invariants, and who else may observe or mutate the same object. That is where the audit stops being local and starts becoming architectural.

That is not just a readability problem. It is a reviewability limit.

## A closed example set

This paper uses eight examples, all already introduced elsewhere in the repo:

| Group | Example | Source doc |
|---|---|---|
| Best C# | `Span<T>.Slice`, `String.CopyTo` | [notable-patterns.md](notable-patterns.md) |
| Best Rust | `[T]::swap`, `split_at_checked` | [notable-patterns.md](notable-patterns.md) |
| Productive security analysis | `Convert.TryToHexString` | [cve-analysis.md](cve-analysis.md#cve-2025-21171--heap-buffer-overflow-from-wrong-comparison-operator) |
| Challenging C# | `Base64DecoderHelper.DecodeFrom`, `ArraySinglePrimitiveRecord.DecodePrimitiveTypes`, `Number.BigInteger` | [cve-analysis.md](cve-analysis.md) |

The thesis of the paper is that the first five are biased toward DAGs, while the latter three are not.

## What a reviewable proof looks like

### C# — `Span<T>.Slice`

[`Span<T>.Slice`](notable-patterns.md#c-spancs--spantslice) is nearly the canonical shape:

```csharp
public Span<T> Slice(int start, int length)
{
    if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
        ThrowHelper.ThrowArgumentOutOfRangeException();

    return new Span<T>(ref Unsafe.Add(ref _reference, (nint)(uint)start), length);
}
```

The proof graph is tiny:

1. validate `start`
2. validate `length`
3. call `Unsafe.Add`
4. construct the result

There is no backtracking. The unsafe operation is adjacent to the guards that justify it.

### C# — `String.CopyTo`

[`String.CopyTo`](notable-patterns.md#c-stringcs--stringcopyto) is slightly larger but still shallow. Five guard lines protect one `Buffer.Memmove` call. The reviewer can hold the entire argument in working memory: source bounds, destination bounds, count, then copy.

This is the shape we want from a safe boundary method. It is safe to call because the proof is local.

### Rust — `[T]::swap`

[`[T]::swap`](notable-patterns.md#rust-slicemodrs--tswap) has the same shape with Rust syntax:

```rust
pub const fn swap(&mut self, a: usize, b: usize) {
    let pa = &raw mut self[a];
    let pb = &raw mut self[b];
    unsafe {
        ptr::swap(pa, pb);
    }
}
```

The safe indexing operations are the proof. The `unsafe` block is tiny. The `// SAFETY:` comment explains the exact handoff. This is ideal review structure: the contract is local, the dangerous region is small, and the proof flows in one direction.

### Rust — `split_at_checked`

[`split_at_checked`](notable-patterns.md#rust-slicemodrs--tsplit_at_checked) is even cleaner:

```rust
pub const fn split_at_checked(&self, mid: usize) -> Option<(&[T], &[T])> {
    if mid <= self.len() {
        Some(unsafe { self.split_at_unchecked(mid) })
    } else {
        None
    }
}
```

One predicate guards one unsafe call. There is almost no graph to speak of. That is the point.

## Why shallow structure enables security analysis

The best reason to care about this shape is not elegance. It is **debug speed and review throughput**.

### Example: `Convert.TryToHexString`

[CVE-2025-21171](cve-analysis.md#cve-2025-21171--heap-buffer-overflow-from-wrong-comparison-operator) is a perfect demonstration. The bug was one character:

```csharp
else if (source.Length > int.MaxValue / 2 || destination.Length > source.Length * 2) // BUG
```

The fix changed `>` to `<`.

This is exactly the kind of bug that shallow structure makes tractable. The method is small. The guard is adjacent to the sink (`HexConverter.EncodeToUtf16`). The reviewer does not need a whole-program model. The proof fits on screen.

That is not an accident. **DAG-shaped proof code compresses security analysis into a local task.**

### Rust shows the same productivity pattern

The Rust examples in [notable-patterns.md](notable-patterns.md) are not CVE write-ups, which is part of the argument. Their structure helps the review happen in the PR rather than forcing a later postmortem. When `split_at_checked` or `[T]::swap` is reviewed, the auditor sees the guard and the `unsafe` use in one glance.

This is what strong review culture wants: not just sound code, but code whose soundness claim is cheap to inspect.

## What makes a graph cyclic

The warning signs are structural:

1. **Large method bodies** with several distinct proof obligations.
2. **Method-level or type-level `unsafe`** spanning pages of ordinary control flow.
3. **Multiple safe/unsafe alternations** inside one routine.
4. **Guards far away from the operations they justify.**
5. **Helper methods whose correctness must be trusted before the local proof can be completed.**

Any one of these raises review cost. In combination, they create back-edges in the audit graph.

It is also useful to distinguish **trivial safe helpers** from **stateful safe dependencies**:

- **Trivial helpers** — `ThrowIfNull`, `ThrowIfNegative`, small pure predicates, single-purpose range checks. These usually *improve* auditability. They centralize boilerplate and rarely force the reviewer into another proof.
- **Stateful dependencies** — container methods like `List<T>` operations, parser helpers, shape-computing routines, mutating helpers, alias-sensitive helpers. These are where cycles begin, because the unsafe operation is now justified by code that has its own hidden state transitions and invariants.

That distinction matters. The problem is not that unsafe code calls safe code. The problem is that the safety proof for the unsafe step is outsourced to nontrivial safe code whose behavior itself has to be audited.

This also explains why the C# diagnostic that suggests marking methods `static` when they do not use instance state is more than a style preference. It imposes useful discipline and advertises a stronger contract to auditors: **this helper does not depend on object state**. For safety-boundary code, that matters. A static guard helper usually expands only the call graph. An instance helper may drag the object graph in with it, which is exactly where audit complexity spikes.

## The challenging C# cases

### `Base64DecoderHelper.DecodeFrom`

[CVE-2026-26127](cve-analysis.md#cve-2026-26127--out-of-bounds-read-in-unsafe-base64-decoder) is the clearest "large method" case in the set.

The method is **313 lines long**. It carries `unsafe` on the signature but has **no inner `unsafe` blocks**, so the dangerous operations are not visually isolated. The actual vulnerability was a missing check on the return from `DecodeRemaining()` before two `Unsafe.Add` calls indexed the 256-byte decoding map.

This is not a straight-line proof. The reviewer has to shuttle among:

- decode status handling
- padding logic
- ASCII validity assumptions
- map indexing
- control-flow exits

That is a cyclic audit graph. You do not prove it once and move on. You keep re-entering it. The issue is not the presence of simple guards; it is that the method mixes validation, stateful decode logic, and unsafe table access in one place.

### `ArraySinglePrimitiveRecord.DecodePrimitiveTypes`

[CVE-2024-43498](cve-analysis.md#cve-2024-43498--type-confusion-in-nrbf-parser-via-unsafe-apis) is worse in a different way.

The method is safe-callable, contains **zero `unsafe` keywords**, and still relies on `Unsafe.SizeOf<T>()` and `MemoryMarshal.AsBytes<T>()`. The graph is hard to even *find*, much less audit. Type validation, size computation, chunking, and reinterpretation are interleaved in one routine.

This is the exact case where the absence of inner `unsafe` blocks and the absence of a `safe` marker combine to hide the proof structure entirely. The unsafe reinterpretation depends on nontrivial safe-side logic about type shape and stream state, so the review cannot stay local.

### `Number.BigInteger`

[CVE-2024-30045](cve-analysis.md#cve-2024-30045--heap-buffer-overflow-in-unsafe-ref-struct-biginteger) shows the most extreme shape in the set.

At the time of the bug:

- the file was **1,371 lines**
- the type was declared `unsafe ref struct`
- `_blocks[]` accesses were spread across roughly **20 methods**
- the file contained **64** direct `_blocks[]` uses

This is not one audit graph. It is an entire subgraph with implicit unsafe reachability everywhere. The reviewer cannot ask "where is the proof for this unsafe operation?" because the unsafe region is the type itself.

That is what "re-entrant" means in practice. The proof is no longer local to a method or block. It is smeared across the file.

## A practical test

The proposed standard is intentionally simple:

> **Could a reviewer explain the safety proof in one pass, from top to bottom, without revisiting earlier assumptions?**

If yes, the graph is probably shallow enough.

If not, the code is carrying too much proof state in one place. It should be split, localized, or wrapped in smaller inner `unsafe` blocks.

## What this implies for C#

The `safe` / `unsafe` / inner-`unsafe` model is attractive not only because it is grep-friendly. It also exerts pressure toward a better **shape**:

- `safe` marks the root of the proof
- `unsafe` signatures mark propagated obligations
- `unsafe {}` blocks mark the leaves where the sharp operations occur

That naturally encourages shallow DAGs and discourages file-wide or method-wide unsafe haze.

It also aligns with existing good C# hygiene. Pushing non-stateful helpers toward `static` reduces accidental dependence on receiver state and makes them safer building blocks for guards around unsafe code. In effect, the language and the analyzer are nudging code toward the same audit-friendly shape.

This is the deeper design claim: **a good safety model does not just classify code; it nudges code toward forms that humans can actually audit.**

## Conclusion

The best examples in C# and Rust share a simple shape: safe checks first, small unsafe step second, done. The difficult C# examples share the opposite shape: long routines, diffuse proof obligations, and repeated re-entry into earlier assumptions.

That is the paper's thesis in one line:

> **Shallow structure is a security property because shallow structure is what makes safety claims auditable.**

The next step is to extend this paper with a few PR-centered mini case studies using the same closed example set, so the argument stays cumulative rather than sprawling.
