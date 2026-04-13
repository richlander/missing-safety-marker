# Safety Interop identity transform as Model Validation

> A safety model is most convincing when it survives across a critical boundary. If a value can cross the C#-Rust FFI boundary and come back without picking up undisclosed or unspeakable obligations, the model is coherent. If it cannot, it identifies opportunities to alter the model.

One of the goals of the project is that teams can use C# and Rust together, with easy interoperation. Certainly, teams can already pass values and references across the divide. It's just C-ABI FFI after all. The project will be much more meaningful if we can reason about how the safety layer "interops".

An even simpler view is that we can use Rust as a "TechEmpower Safety Benchark".

A strict interop model should be unwilling to accept wild pointers, unpinned references, or unbounded data sequences at a `safe` boundary. If the value is not self-describing enough to cross the border safely, the signature should say so with `unsafe`.

In the short-term, we can validate the fidelity of safety with identify transform mechanics. Identity transforms are cheap validation tools, using to test and understand JSON serializers and XML transforms. We can use the same general methodology to test how both safety and obligations cross the FFI boundary between Rust and C# (memory safety v2), two languages that claim to implement both rigorous and similar models.

In the long-term, there is an opportunity to build tooling that generates fully-coordinated safe externs on both sides of the C#/Rust boundary. `LibraryImport` is an excellent precedent on the C# side: the developer authors a rich contract and tooling generates the ABI-lowering code. Rust has movement in the same direction. [RFC 3722](https://rust-lang.github.io/rfcs/3722-explicit-extern-abis.html) makes ABI choice explicit and leaves room for a future `"stable-rust-abi"`-style direction, while the Rust compiler team has separately approved experimentation on [`extern "crabi"` / `repr(crabi)`](https://github.com/rust-lang/compiler-team/issues/631), a higher-level ABI that lowers rich types such as counted UTF-8 strings and counted slices through the C ABI. It is not stable today, but makes a two-way `LibraryImport`-like interop experience plausible.

An interesting thought experiment is whether a source-only "safety ABI" shared among C#, Rust, and Swift is a lighter lift than the Rust ABI. A direct analog is that HTTPS uses the same underlying transport as HTTP. No new internet was required. The opportunity is a tool-based interop system uses the C ABI as transport, providing safety via restrictions and canonical lowering, while continuing to fully support everyting `unsafe`. Obviously, "source-only ABI" is a misnomer. The opportunity is a versioned safe-interop profile over C ABI that can absorb breaking changes (including Rust editions) across toolchains.

The broader Rust-vs-C# model comparison now lives in [`safety-model.md`](./safety-model.md). This paper assumes that framing and focuses on the narrower interop question: which values and obligations may cross the boundary as `safe`, and which ones must remain `unsafe`.

## 1. Interop rule: only self-describing types may cross a `safe` boundary

The practical interop rule is:

> **A `safe` P/Invoke or `safe` export may only use types where the safety proof is intrinsic to the value itself, not carried by a side-channel promise.**

That safe set includes:

- primitives (`int`, `long`, `double`, `bool`, enums with defined representation)
- blittable value types with no hidden pointer obligations
- bounded views like `Span<T>` and `ReadOnlySpan<T>` (where the infrastructure lowers them to pointer + length + lifetime)
- managed types with well-defined marshalling support
- `out` parameters for single-value writeback

That safe set does **not** include:

- raw pointers (`void*`, `int*`, `byte*`)
- `ref T` values whose origin comes from unchecked sources like `Unsafe.Add`
- null-terminated string protocols where bounds are not intrinsic

Why this matters is straightforward. A `safe extern` taking `int*` immediately creates invisible obligations:

1. the pointer must be valid for the number of elements the callee will access
2. if the pointer refers to managed memory, that storage must be pinned for the duration of the call

Neither obligation is carried by the type `int*`. They live in the caller's head. That is exactly what `unsafe` is for.

By contrast, a bounded type like `ReadOnlySpan<int>` carries the important proof ingredients directly: base address, length, and an API shape that does not promise ownership transfer. The marshalling layer may still need `unsafe`, but the application contract does not.

This is also why [`String.CopyTo`](notable-patterns.md#c-stringcs--stringcopyto) is such a useful real-world example. The method validates source and destination ranges in safe code and only then reaches for `Unsafe.Add` and `Buffer.Memmove`. A good interop story should let that same structure survive the FFI boundary rather than forcing developers back to raw pointers.

## 2. Interop examples as stress tests

These examples are not just API design sketches. They are **model validation tests**. If the wrong version can be written as `safe`, the model is leaking obligations.

### Example 1: value round-trip

This is the trivial path, and it should be trivial.

**C# import**

```csharp
[LibraryImport("mylib", EntryPoint = "rs_abs")]
public static partial int Abs(int value);

int result = Abs(-42);
```

**Rust export**

```rust
#[no_mangle]
pub extern "C" fn rs_abs(value: i32) -> i32 {
    value.abs()
}
```

No pointers, no lifetimes, no pinning, no aliasing story. If the model cannot make this path obviously safe, something is badly wrong.

### Example 2: buffer processing via `ReadOnlySpan<T>`

This is the important positive case. The application code on both sides should be safe; the only `unsafe` should live in generated or audited boundary shims.

**C# safe surface**

```csharp
// North-star shape: the signature is safe and self-describing.
[LibraryImport("mylib", EntryPoint = "rs_sum_i32")]
public static partial int Sum(ReadOnlySpan<int> values);

int total = Sum(stackalloc[] { 1, 2, 3, 4 });
```

**Conceptual generated marshalling shim**

```csharp
// Sketch of the lowering, not user code.
private static unsafe int SumMarshaller(ReadOnlySpan<int> values)
{
    fixed (int* pValues = values)
    {
        return __PInvoke_rs_sum_i32(pValues, (nuint)values.Length);
    }
}
```

**Rust side**

```rust
fn sum(values: &[i32]) -> i32 {
    values.iter().copied().sum()
}

#[no_mangle]
pub unsafe extern "C" fn rs_sum_i32(ptr: *const i32, len: usize) -> i32 {
    // `from_raw_parts` is Rust's standard "pointer + length -> borrowed slice" API.
    // It is unsafe because the caller must promise the region is valid for `len` items.
    let values: &[i32] = unsafe { std::slice::from_raw_parts(ptr, len) };
    sum(values)
}
```

This is the desired shape:

- C# application code uses `ReadOnlySpan<int>`
- Rust application code uses `&[i32]`
- only the boundary shim is unsafe

That is exactly the same manual shape seen in [`String.CopyTo`](notable-patterns.md#c-stringcs--stringcopyto): safe bounds reasoning on the outside, unchecked pointer mechanics at the narrow boundary.

### Example 3: wild pointer injection

This is the failure case. If the model lets this stay visually safe, the model is wrong.

The sample below still uses `unsafe` because today's C# pointer syntax requires it. The point is about the proposed classification: if unchecked offsetting APIs and pointer-taking imports were left in the safe set, the dangerous part of this chain would disappear from the audit surface.

**Wrong shape**

```csharp
// WRONG under memory safety v2: raw pointers should not appear in a safe import.
[LibraryImport("mylib", EntryPoint = "rs_sum_raw")]
public static partial int SumRaw(int* ptr, nuint len);

static unsafe int Hole(Span<int> values)
{
    int* p = (int*)Unsafe.Add(
        (void*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(values)),
        values.Length + 1); // one past the end

    return SumRaw(p, 4);
}
```

**Rust side**

```rust
#[no_mangle]
pub unsafe extern "C" fn rs_sum_raw(ptr: *const i32, len: usize) -> i32 {
    let values = unsafe { std::slice::from_raw_parts(ptr, len) };
    values.iter().copied().sum()
}
```

The Rust side is behaving exactly as designed: if you hand it a valid pointer + length pair, it can reconstruct a slice. The bug is that C# manufactured a wild pointer first.

This is why `Unsafe.Add` must be `unsafe`, and why a pointer-taking import must also be `unsafe`. We want **two visible markers**, not zero:

1. the construction of the unchecked pointer/reference
2. the crossing of the FFI boundary with that unchecked value

If either marker is missing, the entire chain becomes harder to audit. If both are missing, the chain becomes invisible.

### Example 4: reverse direction — Rust calling a C# export

The same story should hold in reverse.

**Safe value export**

```csharp
[UnmanagedCallersOnly(EntryPoint = "cs_abs")]
public static int AbsExport(int value) => Math.Abs(value);
```

**Rust import**

```rust
unsafe extern "C" {
    pub safe fn cs_abs(value: i32) -> i32;
}

fn demo() -> i32 {
    cs_abs(-42)
}
```

That is the clean round-trip: safe caller, safe callee, no extra narrative needed.

**Unsafe pointer export**

```csharp
[UnmanagedCallersOnly(EntryPoint = "cs_sum_raw")]
public static unsafe int SumRawExport(int* ptr, nuint len)
{
    if (ptr is null || len > int.MaxValue)
        return 0;

    var values = new ReadOnlySpan<int>(ptr, checked((int)len));
    return SumManaged(values);
}

private static int SumManaged(ReadOnlySpan<int> values)
    => values.ToArray().Sum();
```

**Rust import**

```rust
unsafe extern "C" {
    pub unsafe fn cs_sum_raw(ptr: *const i32, len: usize) -> i32;
}
```

Again the pattern is symmetric. Value types can stay on the safe path. Raw pointer protocols cannot.

### Example 5: `out` parameters

Single-value writeback is a good case for safe-by-construction interop because the raw pointer never needs to appear in application code.

**C# call site**

```csharp
[LibraryImport("mylib", EntryPoint = "rs_double")]
[return: MarshalAs(UnmanagedType.Bool)]
public static partial bool DoubleValue(int input, out int output);

if (DoubleValue(21, out int doubled))
{
    Console.WriteLine(doubled); // 42
}
```

**Rust export**

```rust
#[no_mangle]
pub unsafe extern "C" fn rs_double(input: i32, output: *mut i32) -> bool {
    if output.is_null() {
        return false;
    }

    unsafe { *output = input * 2; }
    true
}
```

The unsafe pointer still exists at the ABI level, but it is infrastructure, not user-facing contract. For many common APIs, `out` is the correct safe shape while `T*` is not.

### Example 6: strings

Strings are the most important real-world FFI type, and they expose the difference between **bounded** and **search-until-you-hit-zero** protocols.

**Unsafe C style**

```rust
unsafe extern "C" {
    pub unsafe fn strlen(p: *const core::ffi::c_char) -> usize;
}
```

`strlen` is inherently unsafe because the pointer carries no bound. The callee discovers the end by walking memory until it finds a zero byte. That is exactly the kind of protocol that a strict `safe extern` should reject.

**Preferred bounded shape**

```csharp
[LibraryImport("mylib", EntryPoint = "rs_count_utf8")]
public static partial nuint CountUtf8(ReadOnlySpan<byte> utf8);
```

```rust
#[no_mangle]
pub unsafe extern "C" fn rs_count_utf8(ptr: *const u8, len: usize) -> usize {
    let bytes = unsafe { std::slice::from_raw_parts(ptr, len) };
    let text = match std::str::from_utf8(bytes) {
        Ok(text) => text,
        Err(_) => return 0,
    };

    text.chars().count()
}
```

Length-prefixed strings are compatible with a safe boundary because the bound is intrinsic. Null-terminated strings are not.

The same pattern extends naturally to `string` or `ReadOnlySpan<char>` once the marshalling layer takes responsibility for UTF-8 encoding and passes an explicit byte length. The safe contract is the bounded view, not the sentinel walk.

## 3. North star

The ideal end state is ambitious but clear.

### The P/Invoke signature as IDL

The C# `safe` P/Invoke signature becomes the canonical interface definition. It uses self-describing types:

- primitives
- bounded spans
- marshalable value types
- strings and buffers with explicit length semantics

A binding generator on the Rust side can consume that contract and generate the FFI shim automatically. Neither application developer writes `unsafe` for routine interop. The unsafe bridge becomes generated infrastructure, audited once and reused many times.

This is similar to WinRT in spirit: metadata acts as interface definition and multiple languages project over it. The improvement here is that types like `Span<T>` and explicit lengths carry more of the safety proof directly than older convention-heavy interop surfaces such as `IBuffer`.

This direction also matches active Rust thinking. The experimental `crabi` proposal is explicitly about defining a higher-level ABI for safe data types and lowering it through the C ABI so languages can share one canonical transport shape instead of hand-writing pairwise adapters. That is very close to the interop experience we want here: author the contract once, generate the raw extern layer, and centralize the audit burden in shared tooling.

### Full obligation chain

The ideal interop surface has no hidden caller obligations:

- no uninitialized access
- no ambiguous ownership transfer
- no lifetime mismatch
- no raw-pointer arithmetic in application code
- no silent GC pinning obligations at a `safe` boundary

Values are copied when they should be copied. Bounded borrows stay bounded for the duration of the call. Pointers do not surface in application code unless the signature is explicitly `unsafe`.

### Harmonized regulations

In the good end state, C# and Rust agree on the same basic border law:

- unchecked pointer arithmetic is unsafe
- bounded views are safe
- dereference is gated
- foreign declarations are audited at the declaration site
- safe interop requires self-describing types

That makes the two languages materially more attractive together. It also makes the interop surface much easier to explain to security assessors: the safety policy is the same on both sides of the border.

### Mechanical auditability

The rules should be greppable, not mystical:

```bash
rg -w "safe"   --type cs
rg -w "unsafe" --type cs
rg -w "safe"   --type rust
rg -w "unsafe" --type rust
```

If a pointer appears in a `safe` import or export signature, the compiler should reject it. If `Unsafe.Add` appears without an `unsafe` region, the compiler should reject it. If a body-less `safe extern` has no declaration-site safety explanation, the tooling should warn. This is the same philosophy as the broader proposal: we want the audit surface to be visible in source, not reconstructed later by folklore.

### What may not be day one

Not every part of this vision has to ship in the first step:

- automatic `Span<T>` <-> Rust slice binding generation
- compiler or analyzer enforcement for documentation on body-less `safe extern`
- a standard IDL projection story for Rust

Those are tooling investments. The language foundation comes first:

1. explicit `safe` / `unsafe` roles
2. correct marking of unchecked APIs like `Unsafe.Add`
3. interop rules that reject raw pointers in `safe` declarations

Once that foundation exists, the rest becomes a straightforward engineering problem instead of a philosophical one.

## Conclusion

C#-Rust interop is not just a deployment story. It is a validation harness for the memory safety model itself.

If a safe value can cross the boundary and return with no extra narrative, the model is working. If the round-trip requires hidden promises about pinning, bounds, provenance, or lifetimes, the signature is not actually safe and should say so with `unsafe`.

That is why the identity-transform lens is valuable. It forces the model to show its seams.

## Related reading

- [`safe-boundary-marker.md`](safe-boundary-marker.md)
- [`safety-comments.md`](safety-comments.md)
- [`safety-model.md`](safety-model.md)
- [`cve-analysis.md`](cve-analysis.md)
- [`audit-graphs.md`](audit-graphs.md)
- [RFC 3484 - unsafe extern blocks](https://rust-lang.github.io/rfcs/3484-unsafe-extern-blocks.html)
- [RFC 2585 - unsafe_op_in_unsafe_fn](https://rust-lang.github.io/rfcs/2585-unsafe-block-in-unsafe-fn.html)
- [RFC 3722 - explicit extern ABIs](https://rust-lang.github.io/rfcs/3722-explicit-extern-abis.html)
- [Rust compiler-team issue #631 - `extern "crabi"` / `repr(crabi)` experiment](https://github.com/rust-lang/compiler-team/issues/631)
- [Swift SE-0458 - Strict Memory Safety](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0458-strict-memory-safety.md)
