# Safety model comparison

> The C# memory safety proposal becomes much easier to evaluate when placed next to Rust. The shared shape is already strong; the remaining differences are specific, understandable, and mostly about making obligations more explicit.

This note extracts the safety-model material from the interop paper so that the interop paper can stay focused on boundary mechanics. The point here is simpler: define the model precisely, compare it to Rust, and make the boundary between `safe` and `unsafe` mechanically legible.

## Rust and C# memory safety v2

| Dimension | Rust | C# memory safety v2 |
|---|---|---|
| Safe semantics | Safe-by-default. Ordinary functions are safe to call. The contextual `safe` keyword exists today only for items inside `unsafe extern` blocks ([RFC 3484](https://rust-lang.github.io/rfcs/3484-unsafe-extern-blocks.html)). | Safe-by-default for ordinary code. Explicit `safe` marks a safety boundary method or a `safe extern` declaration whose signature is intended to be the complete contract. |
| Unsafe semantics | `unsafe fn` defines caller obligations. `unsafe {}` discharges obligations locally. `unsafe extern` marks foreign declarations as a human-checked responsibility. [RFC 2585](https://rust-lang.github.io/rfcs/2585-unsafe-block-in-unsafe-fn.html) reinforces the split between defining and discharging obligations. | `unsafe` on a signature means the caller still bears obligations. `unsafe {}` marks the local sharp edge. `unsafe extern` marks body-less declarations whose correctness cannot be compiler-verified. |
| Pointer creation | Creating raw pointers is generally permitted, but raw pointers carry no safety guarantee by themselves; turning them into references or dereferencing them is where obligations bite. | Raw pointer types and byref-forging APIs live under `unsafe`. Safe code stays on managed references, `Span<T>`, and marshalled values. |
| Pointer arithmetic (`add`, `offset`) | Unchecked pointer arithmetic such as `ptr.add` / `ptr.offset` is `unsafe` because it relies on in-bounds provenance. | `Unsafe.Add`, `Unsafe.AddByteOffset`, and similar unchecked offsetting APIs should be `unsafe`, because they manufacture references or pointers that depend on unverified bounds. |
| Pointer cast (type reinterpretation) | Pointer casts are easy to write, but using the result safely depends on provenance, alignment, and layout. `transmute`-style reinterpretation is `unsafe`. | Unchecked reinterpretation APIs (`Unsafe.As`, raw `void*` casts, `Unsafe.BitCast` where validity is not intrinsic) are `unsafe`. Safe wrappers stay safe only when they fully discharge alignment, shape, and validity obligations. |
| Pointer dereference | Raw pointer dereference is `unsafe`. | Pointer dereference and unchecked reference creation are `unsafe`. |
| Bounded access (`&[T]` vs `Span<T>`) | Once a slice exists, ordinary indexing and iteration are safe. The unsafe part is constructing the slice from raw parts. | Once a `Span<T>` / `ReadOnlySpan<T>` exists, ordinary use is safe. The unsafe part is constructing it from unchecked state. |
| `ref` / reference returns from unchecked sources | Safe Rust cannot conjure `&T` / `&mut T` from raw pointers or unchecked offsets. | This is the critical C# gap. `ref` values are directly usable in safe code, so APIs that produce them from unchecked origins must be marked `unsafe` or they become invisible obligation carriers. |
| FFI export declaration | `pub extern "C" fn` if safe for any well-typed input; `pub unsafe extern "C" fn` if the foreign caller must uphold unchecked obligations. | `safe` exports for `[UnmanagedCallersOnly]` methods whose contract is complete in the signature; `unsafe` exports when the foreign caller must provide validity, pinning, lifetime, or aliasing guarantees not carried by the types. |
| FFI import declaration | `unsafe extern { pub safe fn ...; pub unsafe fn ...; }` under RFC 3484. The block author audits the declaration; individual items say whether callers need `unsafe`. | `[LibraryImport] safe` for imports whose contract is complete in the signature; `[LibraryImport] unsafe` when raw pointers or byrefs carry extra obligations. |
| Compiler verification scope | Rust verifies the safe subset, borrow rules, and lifetimes; it does not verify the semantic correctness of unsafe blocks or foreign implementations. | C# verifies the ordinary safe subset and can enforce explicit containment around unsafe operations and declarations; it still cannot inspect foreign bodies or prove arbitrary semantic guards. |
| Safety documentation requirements | `unsafe fn` needs `# Safety`; unsafe blocks increasingly require `// SAFETY:` comments by culture and linting. | `unsafe` methods require `<safety>` docs and (optionally) `[Safety]` attributes. `safe` methods ordinarily need no safety docs because the signature is the whole contract. |
| Body-less method obligations (intrinsics, externs) | The declaration site bears the responsibility because the compiler cannot inspect a body. Safe extern items are allowed only when the signature is enough. | Same rule. This is the one legitimate case where a `safe` declaration may still need safety documentation: not for the caller, but to justify the human claim at the declaration site. |
| Provenance model | Rust has an explicit and actively refined provenance story for raw pointers and references. | C# does not expose a Rust-style provenance model, but it still has real origin constraints: GC movement, object layout, byref scoping, and the difference between managed references and raw pointers. |
| GC pinning obligations | No moving GC. Pinning is not part of the core language story. | A moving GC makes pinning a first-class obligation whenever raw pointers into managed memory cross an unsafe boundary. Safe interop must hide pinning in marshalling or `fixed`-style infrastructure. |

The alignment is already substantial. Both models agree that unchecked arithmetic and dereference are the sharp edge, while bounded views (`&[T]`, `Span<T>`) are the safe surface. The main differences are also clear: Rust has borrow checking and an explicit provenance discourse; C# has GC pinning and the peculiarity that `ref` values can be used directly in safe code once created.

## Definitions

The proposed C# memory safety v2 model becomes easier to evaluate when stated precisely.

### `safe` (no interior unsafe)

Compiler-verified safe code. No safety documentation is needed because the method contains no operations that can violate memory safety. This is just ordinary C#.

### `safe` (with interior unsafe)

Compiler-verified containment. The method contains local `unsafe` blocks, but the method does not export residual obligations to its caller. The unsafe operations are contained and discharged internally. The signature is the complete contract.

This is the missing safety marker proposal: the method is safe to call, but that fact is explicit instead of being inferred from the absence of `unsafe`.

### `safe extern` (body-less)

Human-asserted, not compiler-inspected. This is the one legitimate exception to the rule that "`safe` means no safety docs needed." A P/Invoke or intrinsic has no body the compiler can inspect, so the declaration site must justify why the signature is genuinely safe for all inputs constructable in safe code.

The docs here are **not** caller obligations. They are declaration-site evidence.

### `unsafe`

Caller bears obligations. The signature alone is not enough. The caller must uphold preconditions the compiler cannot check, and those preconditions must be documented.

That leads to the load-bearing principle:

> **If you need a `# Safety` section to explain why a caller can use the API correctly, then it is not `safe`; it is `unsafe` with documentation.**

The one exception is a body-less `safe extern`, where the compiler has no jurisdiction over the implementation.

## Why this matters

This model is not merely philosophical. It tells reviewers where proofs live, tells tooling what to grep for, and makes interop rules much easier to state. The companion paper [`interop-identity-transform.md`](./interop-identity-transform.md) picks up from here and asks a harder question: what happens when these safety claims cross the C#-Rust boundary?
