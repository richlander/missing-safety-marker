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

## Applying safety comments

C# already have a `///` commenting scheme. We could add a `<safety>` section and also enable markdown, which is already present in some C# files. This approach would enable teams that write both C# and Rust to have an almost identical scheme between both languages.

