# Rust Safety Culture: A four-day argument over three docstrings

A pull request landed on April 1, 2026 in `rust-lang/rust` that adds `# Safety` sections to three functions in `core::mem`: `mem::uninitialized`, `mem::zeroed`, and `mem::transmute_copy`. No code changes. Three docstrings.

That PR ([#154665](https://github.com/rust-lang/rust/pull/154665)) is still open six days later, marked `CHANGES_REQUESTED`. The reason it stalled is not in the GitHub thread. It is in a parallel 79-message debate on the [t-opsem Zulip channel](https://rust-lang.zulipchat.com/#narrow/channel/136281-t-opsem/topic/Potential.20UB.20via.20.60mem.3A.3Atransmute_copy.60.20not.20detected.20by.20Miri) that ran for four days, pulled in more than a dozen contributors — t-opsem reviewers, libs maintainers, Rust-for-Linux folks, and an academic safety research group from [Fudan University](https://github.com/safer-rust) — and ended with the participants agreeing they need to write a brand-new central document about how unsafe code in Rust actually works.

This is the story of that debate. It is what `// SAFETY:` actually costs to maintain — and what kind of collaboration Rust has built up around it.

## The provocation

While preparing the PR, the author Hui Xu — a Fudan University researcher who runs the [`safer-rust`](https://github.com/safer-rust) group and the [Asterinas](https://github.com/asterinas/asterinas) Rust kernel project — had been examining `mem::transmute_copy` and built a four-line example that he believed should be flagged as unsound:

```rust
fn main() {
    let mut a = Box::new(42u32);
    // Miri can detect UB if `b` is a mutable reference.
    let b: &Box<u32> = unsafe { std::mem::transmute_copy(&&a) };
    println!("{}", b);
    *a = 2;
    println!("{}", b);
}
```

`b` is a shared reference to the box. `*a = 2` mutates through the unique owner. Both pointers exist at the same program point. This should be a borrow-checker problem, except `transmute_copy` launders the lifetime so the borrow checker never sees it. Miri runs the program clean.

Hui Xu posted the example to Zulip the same morning. His framing was that this is a Rust aliasing rule violation that Miri is failing to detect. Within two hours, Ralf Jung replied:

> whether or not this is a violation of Rust's aliasing rules is an open question
>
> Stacked Borrows and Tree Borrows both do not consider this a violation, as you have found out via Miri. Specifically, on the *language* level, those models say that immutability is "shallow". Put differently, for optimization purposes, the compiler is only allowed to assume "shallow" immutability.
>
> Violations of "deep" immutability can still be *library* UB, but they cannot by themselves cause miscompilations and are hence not flagged by Miri.

This is the move the rest of the thread is about. There are two kinds of UB in Rust, and they live in different layers:

- **Language UB** is what the compiler is allowed to assume never happens. Stacked Borrows and Tree Borrows formalize what counts. Miri is the executable model. Violating it can cause miscompilation.
- **Library UB** is when a specific safe API has documented invariants its callers rely on, and unsafe code reaches into those invariants without restoring them before handing the value back. The compiler does not need to break, because the compiler's optimization model never required the library invariant in the first place.

Hui Xu's example is library UB, not language UB. The compiler does not currently exploit deep immutability, so Miri has nothing to flag. The program is still unsound — just at a different layer of the system.

Ralf Jung's [*Two Kinds of Invariants*](https://www.ralfj.de/blog/2018/08/22/two-kinds-of-invariants.html) (2018) is where this distinction was first written down. Eight years later, it is still load-bearing in real review work. Not load-bearing in the sense of being cited in passing. Load-bearing in the sense that the standard library cannot land a three-paragraph documentation change until the contributor is fluent in it.

## The easy fix that was rejected

The thread immediately produces an obvious patch: add a "safety" bullet to `ptr::read`, `ptr::copy`, and friends warning that even when `T: Copy`, you can still construct values that violate library invariants. Johannes Hostert puts it bluntly:

> Nonetheless I feel like the `ptr::copy` docs should say something about the resulting value having to satisfy its validity requirements. @Hui Xu is not the first person I know to forget this. The list [in `ptr::read`'s safety section] not being exhaustive is a footgun.

Kevin Reid (kpreid) suggests folding the existing "Ownership of the Returned Value" section into the Safety section and strengthening the language.

Ralf rejects this. Twice, in the space of a few minutes:

> it shouldnt be in the "safety" documentation since that is about the preconditions for `ptr::read`, and its preconditions are not violated in these examples. IOW, that list *is* exhaustive.
>
> the examples just use `ptr::*` to break preconditions of other operations

And then:

> anyone trying to understand this by syntactically applying rules for unsafe methods will end up in a pointless game of whack-a-mole. it requires properly understanding the role of library invariants vs language invariants (safety invariants vs validity invariants)
>
> I dont disagree that the docs should be improved here, but I dont think that adding more trees helps. we need docs that actually talk about the forest.

This is the pivotal moment. Ralf is refusing to add a one-line warning to the docs of every operation that could be involved in this kind of unsoundness. Not because the warning would be wrong, but because writing it would conflate two concepts the project has spent years trying to keep separate. The wording is treated as part of the implementation of soundness reasoning. Get it wrong in a docstring, and the conceptual surface gets harder to reason about for everyone who reads that docstring next.

This is the standard. Not "fewer footguns." *More legible footguns,* explained in vocabulary that scales beyond the current example.

## The thread becomes a seminar

A documentation review turns into a multi-day technical seminar.

Julien Cretin (ia0) introduces a Hoare-logic frame: the safety condition only needs to imply the weakest precondition of the trivial postcondition, written `wp { _. True }`. The documented precondition has to be enough to guarantee that *during* the call, no UB happens. It does not have to guarantee that no UB happens *after* the call returns. That gap is exactly where library invariants live.

He then writes out a four-statement formalization of how the pieces relate:

> 1. Violating a validity invariant when materializing a value is language UB.
> 2. Violating a safety condition when calling an unsafe function is library UB.
> 3. A safety condition must be sufficient for an unsafe function call to not have language UB (during the operation of the call). When it happens to also be a necessary condition, then violating it is morally (but not technically) language UB.
> 4. A safety condition may be sufficient for an unsafe function call to not break typing.

He then flags one of the underlying concepts as freshly invented:

> I came up with it for this thread. I'm not even sure it's a widely known concept (before even wondering whether people agree on how to name it).

Inside a thread that is supposed to be reviewing a docstring, a participant is inventing terminology in real time, in public, on the record, because the existing vocabulary is not precise enough to describe what the docstring is supposed to say. That is the tax on getting this right.

Alice Ryhl, a Rust-for-Linux maintainer, then registers the practitioner's complaint:

> That's quite unsatisfying. I would like it to be the case that if you satisfy the safety requirements of each unsafe operation you perform, then that implies your program does not trigger UB. If we don't have that guarantee, then what are we supposed to do to avoid UB?

This is the right question. It is exactly the question a kernel author has to ask. Julien's answer is short:

> Make sure you restore any safety invariant before reaching public safe APIs.

Nadrieril sharpens it:

> you have to satisfy the safety requirements of *all* the operations you perform, including calling safe functions. For these, that means the arguments must satisfy their safety invariants.

That principle — *the safety contract is everywhere a value crosses an API boundary, including safe ones* — is what the thread is converging on. It is also exactly the principle that makes documentation hard, because most safe APIs do not currently say which library invariants they assume.

## A novice in the room

Some way into the discussion, LemonJ posts what may be the most honest message in the thread:

> I had zero awareness of the "*language* vs. *library* invariants" distinction before this. And to be frank, even after this discussion, the practical line between them isn't completely clear to me yet.

This is not treated as a problem. Vague immediately points at the [Unsafe Code Guidelines glossary](https://rust-lang.github.io/unsafe-code-guidelines/glossary.html#validity-and-safety-invariant), which has had formal definitions of validity and safety invariants for some time, and at the existing GitHub issue ([#539](https://github.com/rust-lang/unsafe-code-guidelines/issues/539)) where the naming was originally argued out. The thread keeps going.

This detail is small but it is the most culturally significant moment in the whole exchange. A senior expert refused a wording fix because it would have papered over a real conceptual gap. A novice, several days into the resulting debate, said "I still don't fully get this." Both messages are treated as legitimate participation in the same conversation. The novice is not told to come back later. The expert is not told to lighten up. They are working on the same problem.

## Two forums, one problem

In parallel with the Zulip thread, the original PR was getting normal review. bjorn3 caught duplicated text. CI caught trailing whitespace. Hui Xu reworked the section so that all prose before "Examples" was genuinely safety-relevant. None of this was the hard part.

The hard part surfaced when Hui Xu pinged Ralf directly on the PR with a question: you said it seems incorrect to state that "users must ensure that creating the returned value does not violate Rust's aliasing rules" — what should it say instead? Ralf's reply on GitHub does not waste any words:

> You seem to have misunderstood what I said. "Rust's aliasing rules" refers to two different things, depending on context:
> - The language-level rules assumed by the compiler (Stacked/Tree Borrows) — at least in t-opsem, this is what we mean most of the time. These must always be followed everywhere, or else we have UB.
> - The type-system-level rules — this is what you seem to mean. These can be temporarily violated within a module, but cannot be violated across modules as that would be unsound and could indirectly lead to UB.
>
> This is the same dichotomy as in my old blog post. It is a fundamental aspect of working with unsafe Rust that your model and PR do not seem to properly capture.

This is the same point Ralf made on Zulip, addressed at the docstring author rather than the model. The two forums are doing different work on the same problem. The Zulip thread is the open seminar. The PR thread is the binding decision about what words go into the standard library.

## Convergence

By the end of the four days, the participants are no longer arguing about the original PR. They are arguing about whether Rust should have a single "How to write unsafe code" page that the standard library can link to from every function with a non-trivial safety contract. Julien:

> Yes, I would love to see a single place where all things unsafe-related are documented.

asquared31415, two messages earlier:

> I think the biggest issue is that a developer might not even know that there exists a concept of "language vs library invariants", and so might not understand that they need to find some other page that details how to be careful with library code. an "unknown unknowns" situation.

Nadrieril floats — and then half-rejects — the idea of a `MaybeUnsafe<T>` wrapper type to surface "values that satisfy validity but not safety invariants" in the type system itself. Julien explains why that does not generalize. The thread ends not with a resolution but with an open question: how many functions in the standard library have safety conditions that can leave the program in a state where typing is broken until the caller restores it? They want a study and a consensus.

Meanwhile, on the PR, Hui Xu reworks the wording, distinguishes which level his preconditions live on, and re-marks the PR `@rustbot ready`. The three docstrings still have not landed.

## What should be unsafe?

The transmute_copy seminar is a debate about how to *describe* an unsafe contract. A different kind of debate plays out elsewhere, about which code should be marked unsafe in the first place. It is the same culture, applied to a different question, by a different set of people.

In February 2026, tczajka opened a thread on Internals titled [*Private unsafe fields are a poorly motivated feature*](https://internals.rust-lang.org/t/private-unsafe-fields-are-a-poorly-motivated-feature/23976). The opening post argues that the unsafe-fields RFC is conceptually incoherent for private fields. Some logic invariant in any module *might* end up being depended on for soundness somewhere downstream, so either every invariant should be marked unsafe (absurd) or none of them should (defeats the purpose). tczajka concludes:

> I think this feature tries to do something impossible. It tries to use unsafe to indicate places in code where bugs might cause UB. But by design, that's basically impossible. unsafe code often relies on correctness of surrounding safe code.

The replies are immediate. Ralf Jung, within minutes:

> A field should be unsafe if it is important for soundness of the module itself. Whether other modules rely on functional behavior of your module is irrelevant here and not your concern.

Scott McMurray (scottmcm) reframes the motivation:

> The point of the RFC, as I see it, is to solve the Vec::set_len problem.
>
> There's lots of people, even academic papers, who make the mistake of "well there was no unsafe in the body so the function didn't need to be unsafe", which is just wrong.
>
> But by making the len field in Vec be unsafe, you eliminate that confusion.

This is the canonical case the original `// SAFETY:` doc on the standard library has been pointing at for years. `Vec::set_len` is a safe-looking function that is actually soundness-critical because it changes the length without checking. Its body contains no `unsafe` expression. Generations of Rust programmers — including, scottmcm notes, "even academic papers" — have looked at it and concluded the function did not need to be marked unsafe. The unsafe-fields proposal exists because the project decided that pattern was wrong, and that the safety boundary should be visible at the field where the invariant lives.

Julien Cretin (ia0) takes it from a third angle:

> Exactly, and there's another related argument: unsafe reviews. Those are manual human reviews, and as most humans they want to do as little as possible. So being able to audit foreign code for soundness without having to reverse the safety invariants is an advantage.

The thread does not resolve the disagreement. tczajka does not end up convinced. But the thread is *re-litigating where the safety boundary lives* — across modules, across privacy, across fields — in public, with named participants from across the project, on a feature whose purpose is to make a known confusion (the `Vec::set_len` problem) less likely. That re-litigation is the work. The community does not assume this question has a settled answer.

A parallel thread on Internals from the same week, [*Conditions for unsafe code to rely on correctness*](https://internals.rust-lang.org/t/conditions-for-unsafe-code-to-rely-on-correctness/23995), takes a different cut at the same problem: when an upstream crate is wrong, which crate should get the soundness advisory? Different starter, different participants, same underlying question about where responsibility for soundness lives in a multi-author ecosystem.

## The kernel parallel

The standard library is not the only place where this work is happening. The Linux kernel — the most safety-conscious deployment of Rust today — has been arguing the same questions for years, in its own forums, with its own protagonists.

In 2024, Benno Lossin posted a patch series to the Rust-for-Linux mailing list titled [*Introduce the Rust Safety Standard*](https://lore.kernel.org/rust-for-linux/20240717221133.459589-1-benno.lossin@proton.me/). Its premise is blunt: unsafe Rust code in the kernel is required to have safety documentation, and there is currently no agreed-upon standard for how to write it. The kernel does not consider current `// SAFETY:` practice sufficient, and is willing to invest in a more rigorous standard.

Miguel Ojeda, the Rust-for-Linux maintainer, summarized the kernel's experience on a separate Rust project goal thread:

> Over the years, we have had many discussions on how it would be best to write `# Safety` sections, `// SAFETY` comments, and so on (and whether be more formal or not, whether using a special notation, whether we should use lists of bullet points, etc.); and generally how to handle unsafety in the kernel...
>
> In particular, @BennoLossin ended up proposing a Safety Standard back in 2024.

Lossin himself replied to say where he wanted the kernel's standard to go:

> Oh yeah I wanted to do something like a structured language that could eventually be checked by an external tool. I then ended up doing other things and it has mostly stalled on the kernel side.
>
> At RustWeek 2025 I talked with @obi1kenobi, who shared the idea of having a simple approach where we tag each safety requirement and then have a tool which checks that all tags are present on both sides.

Two senior contributors, two communities, one shared frustration: prose is not enough. They want safety contracts written in something a tool can check. The kernel is not waiting for upstream Rust to provide that. They are building toward it from both sides — and they explicitly want the kernel and the standard library to converge on the same vocabulary.

## Academic collaboration

One of the things visible across the threads above is something easy to miss in the line-by-line technical detail: Rust's safety culture has attracted serious academic engagement, and the project takes it seriously in return.

Hui Xu — the contributor at the center of the primary vignette — is at Fudan University in Shanghai. He runs a Rust safety research group whose visible artifacts include the [`safer-rust`](https://github.com/safer-rust) GitHub organization ("Closing the last mile towards safe software written in Rust"), the [Asterinas](https://github.com/asterinas/asterinas) Rust kernel project (a production-aimed Linux alternative, ~4,400 stars), the [`safety-tags`](https://github.com/safer-rust/safety-tags) prototype tooling for structured safety annotations, the [pre-RFC for a Rust Safety Standard](https://internals.rust-lang.org/t/pre-rfc-rust-safety-standard/23963), and [project goal #511](https://github.com/rust-lang/rust-project-goals/pull/511) — *Improving Unsafe Code Documentation in the Rust Standard Library* — accepted by `@rust-lang/libs` and `@rust-lang/libs-api` for 2026. His PhD student DiuDiu777 is the named author on most of the standard library safety doc PRs that have landed over the past year: [#134496](https://github.com/rust-lang/rust/pull/134496), [#134953](https://github.com/rust-lang/rust/pull/134953), [#135009](https://github.com/rust-lang/rust/pull/135009), [#135334](https://github.com/rust-lang/rust/pull/135334), [#135805](https://github.com/rust-lang/rust/pull/135805), [#137714](https://github.com/rust-lang/rust/pull/137714), [#138309](https://github.com/rust-lang/rust/pull/138309), [#140359](https://github.com/rust-lang/rust/pull/140359), [#146870](https://github.com/rust-lang/rust/pull/146870), [#146925](https://github.com/rust-lang/rust/pull/146925).

What is striking is not that an academic group cares about Rust safety. It is that the libs team, the opsem team, and the Rust-for-Linux maintainers treat them as collaborators rather than as outside observers. In [project goal #511](https://github.com/rust-lang/rust-project-goals/pull/511), Mark-Simulacrum was explicit about the cost of reviewing this work — and the libs team accepted the goal anyway, with Josh Triplett confirming on behalf of `@rust-lang/libs-api` and `@rust-lang/libs`:

> Confirming that @rust-lang/libs-api and @rust-lang/libs are fine with accepting this goal, with the understanding that the "Small" support level is intended as normative: we're willing to invest that amount of time reviewing potential patches that arise as a result of this.

Ralf Jung shows up in PRs and Zulip topics opened by Fudan contributors and engages on the substance, not the source. Benno Lossin (Rust-for-Linux) sees the alignment between Fudan's safety auditing and the kernel's own Safety Standard effort and reaches across in `#511`. Niko Matsakis pushes on scope and ultimately approves. Paul LeVasseur, writing from the safety-critical industry side, frames the application pressure that makes the work matter to assessors:

> Having a standard like this would be of great benefit as Rust continues to see adoption into safety-critical industries like automotive, medical devices, industrial, and so on. The ability to write down these things can help organizations and teams to show traceability to having followed such a standard when they take their software to safety assessors.

That is the larger community story. The community in question is not just Rust contributors. It is Rust contributors plus an embedded academic research group plus kernel maintainers plus safety-critical practitioners, all working on the same problem from different sides. Rust's safety culture has been credible enough, and durable enough, that an academic group has chosen to make the standard library their primary research artifact. The standard library, in return, treats their contributions as work to review on the merits.

This is rare. Most language standard libraries do not have a university research group systematically auditing their unsafe documentation. Most academic safety research does not result in PRs against `core::mem`. The two-way engagement is itself part of what `// SAFETY:` is now worth.

## Where this work happens

The three vignettes above — a four-day Zulip seminar, a public Internals debate about what should be marked unsafe, and a kernel patch series with cross-project ambition — are not the whole picture. They are visible cross-sections of a much larger sustained workstream that runs through many forums, many teams, and years of accumulated decisions. A partial map:

**Norms and conventions**
- [Rust standard library developer guide: safety comments policy](https://std-dev-guide.rust-lang.org/policy/safety-comments.html) — codified expectation that every `unsafe` block carries a `SAFETY:` comment, with a precise distinction between safe and unsafe contexts
- [Rust 2024 edition: warn on unsafe ops in unsafe fn by default](https://doc.rust-lang.org/edition-guide/rust-2024/unsafe-op-in-unsafe-fn.html) — the language now insists on visible, local proof sites even inside an `unsafe fn`

**Conceptual roots**
- Ralf Jung, [*Two Kinds of Invariants: Safety and Validity*](https://www.ralfj.de/blog/2018/08/22/two-kinds-of-invariants.html), 2018 — the canonical reference for the validity vs safety invariant distinction, still load-bearing in 2026 review threads
- [Unsafe Code Guidelines glossary](https://rust-lang.github.io/unsafe-code-guidelines/glossary.html#validity-and-safety-invariant) — the project's reference vocabulary
- [Unsafe Code Guidelines issue #539](https://github.com/rust-lang/unsafe-code-guidelines/issues/539) — long-running nomenclature debate
- [RFC 2585: `unsafe-block-in-unsafe-fn`](https://rust-lang.github.io/rfcs/2585-unsafe-block-in-unsafe-fn.html) — the RFC that decoupled "imposing an obligation" from "discharging one." Its core line: *"`unsafe {}` blocks are about discharging obligations, but `unsafe fn` are about defining obligations."*

**Live community debates**
- [*Pre-RFC: Rust Safety Standard*](https://internals.rust-lang.org/t/pre-rfc-rust-safety-standard/23963) — long-form proposal for a structured safety language
- [*Conditions for unsafe code to rely on correctness*](https://internals.rust-lang.org/t/conditions-for-unsafe-code-to-rely-on-correctness/23995) — when an upstream crate is wrong, which crate gets the soundness advisory
- [*Private unsafe fields are a poorly motivated feature*](https://internals.rust-lang.org/t/private-unsafe-fields-are-a-poorly-motivated-feature/23976) — the secondary vignette above
- [t-opsem Zulip channel](https://rust-lang.zulipchat.com/#narrow/channel/136281-t-opsem) — where most live unsafe-code debate happens, including the four-day `transmute_copy` thread

**Project commitments**
- [Project goal: Improving Unsafe Code Documentation in the Rust Standard Library](https://rust-lang.github.io/rust-project-goals/2026/improve-std-unsafe.html) ([discussion](https://github.com/rust-lang/rust-project-goals/pull/511)) — accepted with the explicit understanding that libs and libs-api will spend reviewer time on the language of safety contracts, not just on code
- [Project goal: Borrow checking in a-mir-formality](https://github.com/rust-lang/rust-project-goals/pull/486) — applies the zerocopy model to systems workloads, starting with Eclipse iceoryx2 (~3,300 unsafe usages)

**Cross-project work**
- [Benno Lossin, *Introduce the Rust Safety Standard*](https://lore.kernel.org/rust-for-linux/20240717221133.459589-1-benno.lossin@proton.me/) (Rust-for-Linux, 2024)
- [Rust-for-Linux mailing list](https://lore.kernel.org/rust-for-linux/) — where the kernel's safety conventions are debated in the open

**Academic collaboration**
- [`safer-rust`](https://github.com/safer-rust) — Fudan University's Rust safety research group
- [Asterinas](https://github.com/asterinas/asterinas) — production-aimed Rust kernel maintained by the same group
- [`safety-tags`](https://github.com/safer-rust/safety-tags) — prototype tooling for structured safety annotations
- The full list of standard library safety doc PRs landed by the group is enumerated in the *Academic collaboration* section above

## Why this is the standard

It would be easy to read these threads as bureaucratic. A four-day Zulip seminar over three docstrings. An Internals debate where the project re-litigates whether private fields can be marked unsafe at all. A 2024 kernel patch series whose ambition is a tool-checkable safety language. A multi-year audit of the standard library carried out by a Fudan University research group and reviewed line by line by the libs and opsem teams. Multiple deep concepts. Senior contributors invoking eight-year-old blog posts. Junior contributors admitting they still do not follow. All so the word "Safety" can carry its weight above a function signature in `core::mem`, in a kernel module, in the standard library's audit graph.

That reading misses the work. The threads are doing several things at once:

- protecting the meaning of the word "Safety" so future readers can rely on it,
- refusing fixes that would have made real conceptual gaps harder to see,
- carrying an eight-year-old distinction (language vs library invariants) into corners of the API that were not previously documented at this precision,
- giving a Rust-for-Linux maintainer a usable answer to "what am I actually supposed to do",
- exposing — without papering over — the fact that the project does not yet have a central place to explain this to a new contributor,
- and committing scarce reviewer time, as a normative project commitment, to wording rather than only to code.

This is what it costs to keep `// SAFETY:` from degrading into a habit. Each instance of the comment is supposed to be a small local proof. For the proofs to mean anything, the words inside them have to keep their referents. That means the project has to be willing, periodically, to spend four days and a dozen senior people on three docstrings — and to commit project goals, kernel patch series, and team-level review bandwidth to nothing more dramatic than wording.

Most projects do not do this. Most projects, faced with a Miri non-detection report and a contributor proposing to add a safety bullet point, would have merged the bullet point. Rust did not, because Rust has decided that getting the wording right *is* part of the implementation of soundness, not a downstream artifact of it.

That is the standard to aspire to. It is not a clever trick or a transferable convention. It is years of patient, public, often tedious argument, kept up by people willing to slow down a doc-only PR until everyone in the room understands the same thing by the same word. If we want our own languages to support the kind of safety reasoning Rust supports, this is the work we should aim to match. Not the keyword. Not the comment style. The willingness to treat a docstring as a soundness artifact, and to argue accordingly.
