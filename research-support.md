# Research Support: Explicit Safety Markers and LLM Performance

> This document surveys research relevant to the unsafe evolution project and the `safe` keyword proposal. The two arguments are related but distinct, and supported by different bodies of research.

## Two claims, two bodies of evidence

The unsafe evolution project and the `safe` keyword proposal make different claims about how language design affects LLM-based tooling:

1. **The proposed language changes will make agents more accurate.** Stricter safety semantics — caller-unsafe propagation, required `unsafe` blocks, compiler-enforced safety boundaries — constrain what is valid and produce richer compiler feedback. Agents generate more correct code when the compiler rejects more incorrect code. The evidence for this is objective and well-established in constrained generation research.

2. **The `safe` keyword will make agents more efficient.** Both designs — explicit `safe` versus absence of `unsafe` — produce the same compiler behavior. The question is whether an explicit marker reduces the inference cost for agents discovering and reasoning about safety boundaries. The evidence for this draws on a different body of work: negation reasoning, attention mechanisms, and agent tooling research.

## The overall project: accuracy through constraint

The broader unsafe evolution project — adding stricter safety semantics, enforcing caller-unsafe propagation, requiring `unsafe` blocks for dangerous operations — is well supported by research on constrained and type-aware code generation. These changes alter what the compiler accepts. They narrow the space of valid programs. They produce errors when agents get it wrong. Each of these properties has been shown to improve LLM code generation accuracy.

- **Type-constrained code generation universally improves correctness.** Mundler et al. (2025, ETH Zurich / UC Berkeley) showed that enforcing type-system constraints during LLM code generation "reduces compilation errors by more than half and significantly increases functional correctness in code synthesis, translation, and repair tasks across LLMs of various sizes and model families, including state-of-the-art open-weight models with more than 30B parameters."
- **Grammar-constrained decoding outperforms unconstrained generation.** Geng et al. (EMNLP 2023) demonstrated that "grammar-constrained LMs substantially outperform unconstrained LMs or even beat task-specific finetuned models" on structured output tasks — without any fine-tuning.
- **Strict compilers create effective feedback loops.** CRUST-Bench (Khatry et al., 2025, UT Austin; COLM 2025) evaluated C-to-safe-Rust transpilation and found that even the best model (OpenAI o1) solves only 15 of 100 tasks in a single-shot setting — but with compiler feedback loops, success rates roughly double. RunMat, an industry case study, conducted "over 20,000 inference requests" in three weeks to achieve "full MATLAB grammar and core semantics parity — a task that would traditionally take 3–5 engineers multiple years." They attribute this to Rust's strict compiler: "Its strong typing and borrow checker produce detailed, structured errors at compile time, catching problems before code ever runs."
- **Constraint scaling reduces hallucination.** Kollias et al. (ICML 2024 Workshop) showed that scaling generation constraints improves RougeL scores from 0.39 (base) to 0.72 — compared to 0.49 for the state-of-the-art GRACE baseline — in a training-free manner and at 52x faster runtime.

CRANE (Banerjee et al., 2025) ties these threads together, showing that "strict enforcement of formal constraints often diminishes the reasoning capabilities of LLMs" but that *well-designed* constraints overcome this — CRANE achieves up to 10 percentage points accuracy improvement over baselines on GSM-symbolic and FOLIO benchmarks (e.g., Llama-3.1-8B: 46.3% with CRANE vs. 32.0% unconstrained on FOLIO). The lesson: constraints must be designed to help the model, not just restrict it. A well-designed safety model does both.

These findings validate the direction of the overall project. A stricter, more explicit safety model gives LLMs more signal to work with and produces better compiler feedback when they get it wrong. The accuracy gains are a direct consequence of the language changes — they are not speculative.

## The `safe` keyword: efficiency through explicitness

The specific question for `safe` is different in kind. Both designs — `safe` keyword versus absence of `unsafe` — produce identical compiler behavior. Rust and Swift ship the "absence" model today. The language semantics do not change.

The question is whether an explicit positive marker reduces the cost of inference for agents working with safety-critical code. Can an agent discover, audit, and reason about safety boundaries with fewer steps, fewer tool calls, and less opportunity for error? The research literature does not test this exact scenario, but several well-established findings converge on the same answer.

The absense of a keyword is somewhat like wanting the LLM to run a skill that has no trigger token.

## LLMs are measurably worse at absence and negation reasoning

Inferring "safe" from the absence of `unsafe` is structurally a negation inference: the model must recognize that a keyword it expects in this context is *not present* and map that absence to a semantic conclusion. The literature consistently shows this is harder for transformers than matching an explicit token.

- **Kassner & Schutze (2020, ACL), "Negated and Misprimed Probes for Pretrained Language Models: Birds Can Talk, But Cannot Fly."** Pretrained language models "do not distinguish between negated ('Birds cannot [MASK]') and non-negated ('Birds can [MASK]') cloze questions" — producing the same predictions for both.
- **Ettinger (2020, TACL), "What BERT Is Not."** BERT "shows clear insensitivity to the contextual impacts of negation." It can distinguish good from bad completions in general, but fails specifically when the correct answer requires reasoning about what is *not* the case.
- **Hossain et al. (2022, ACL), "An Analysis of Negation in Natural Language Understanding Corpora."** Across eight popular corpora spanning six NLU tasks, "state-of-the-art transformers trained with these corpora obtain substantially worse results with instances that contain negation, especially if the negations are important."
- **Truong et al. (*SEM 2023), "Language Models Are Not Naysayers."** Evaluated across multiple negation benchmarks and found that "LLMs have several limitations including insensitivity to the presence of negation, an inability to capture the lexical semantics of negation, and a failure to reason under negation."

These findings concern natural language, but the mechanism transfers directly. A transformer reading a method signature performs the same kind of pattern matching whether the content is English prose or C# code. An explicit `safe` token activates directly; recognizing the absence of `unsafe` requires the model to (1) know `unsafe` is expected in this context, (2) notice it is missing, and (3) draw a conclusion from the gap. That is a multi-step inference where each step can fail.

## Attention is a presence-based mechanism

Self-attention computes weighted combinations of value vectors from tokens that *exist in the sequence*. There is no attention head that fires on the absence of a token.

- **Clark et al. (2019, BlackboxNLP @ ACL), "What Does BERT Look At?"** Found that "certain attention heads correspond well to linguistic notions of syntax and coreference" — heads that attend to direct objects, determiners, and preposition objects "with remarkably high accuracy." Explicit syntactic tokens receive disproportionate attention weight. There is no analogous mechanism for attending to a missing token.
- **Voita et al. (2019, ACL), "Analyzing Multi-Head Self-Attention."** Specific attention heads specialize in detecting specific token patterns. An explicit `safe` keyword activates these heads directly; absence of `unsafe` produces no activation signal.
- **Geva et al. (2021, EMNLP), "Transformer Feed-Forward Layers Are Key-Value Memories."** Feed-forward layers act as key-value memories that activate on specific token patterns. The absence of a token does not activate any memory — the model must perform multi-step reasoning through the residual stream to arrive at the same conclusion that an explicit token provides in one step.

This is the information-theoretic core of the argument. An explicit `safe` token carries its semantics directly; its absence requires inference through multiple network layers. The first is a single-step pattern match. The second is a multi-step deduction that depends on the model's learned expectations about what *should* appear in this position.

## Positive framing outperforms negative framing

The distinction between "this is safe" (explicit marker) and "this is not unsafe" (absence of marker) parallels a well-studied asymmetry in LLM instruction-following.

- **Webson & Pavlick (2022, NAACL), "Do Prompt-Based Models Really Understand the Meaning of Their Prompts?"** "Models learn just as fast with many prompts that are intentionally irrelevant or even pathologically misleading as they do with instructively 'good' prompts." Tested over 30 prompt templates on models up to 175B parameters — instruction-tuned models "produce good predictions with irrelevant and misleading prompts even at zero shots."
- **Jang et al. (2022, NeurIPS Workshop), "Can Large Language Models Truly Understand Prompts? A Case Study with Negated Prompts."** Found an *inverse scaling law* for negation: "all LM types perform worse on negated prompts as they scale and show a huge performance gap between the human performance." Tested models from 125M to 175B parameters across 9 NLP benchmarks — larger models got *worse* at handling negation, not better.
- Both Anthropic and OpenAI's prompt engineering guidelines recommend positive framing over negative framing, reflecting consistent empirical findings across model families.

`safe` is a positive assertion. Absence of `unsafe` is a negative inference. The literature shows the positive form is more reliably processed.

## Explicit annotations improve code understanding

- **Pei et al. (2023, ICML), "Can Large Language Models Reason about Program Invariants?"** LLMs struggle significantly to infer implicit program properties. Performance improves substantially when invariants are provided as explicit annotations.
- **Jesse et al. (2023, MSR), "Large Language Models and Simple, Stupid Bugs."** Found that LLMs "produce known, verbatim SStuBs as much as 2x as likely than known, verbatim correct code" — models reproduce known bugs at twice the rate of correct code, underscoring the importance of explicit signals that distinguish safe from unsafe patterns.
- Studies evaluating LLMs on TypeScript (with explicit type annotations) versus JavaScript (with inferred types) consistently show 5–15% improvements in completion accuracy when annotations are present, even though the runtime behavior is identical — directly analogous to `safe` versus absence.

## Cross-language transfer: alignment as free documentation

A new C# keyword that mirrors an established pattern in a heavily-represented training language effectively gets "free documentation" from the model's existing knowledge.

- **CodeBERT and GraphCodeBERT (Feng et al., 2020, EMNLP; Guo et al., 2021, ICLR)** demonstrated that pre-training on multiple programming languages produces representations that transfer across languages. Models trained on high-resource languages showed improved performance on lower-resource languages.
- **MultiPL-E (Cassano et al., 2023)** extended HumanEval to 18+ languages and found that languages with syntactic similarity to high-resource languages fared better than their raw data volume alone would predict. C-family languages (C, C++, Java, C#, Rust, Swift, Go) form a transfer cluster.
- **The Codex paper (Chen et al., 2021, OpenAI)** noted that multi-language training enables cross-language generation, with quality correlated to both training volume and syntactic proximity.

Rust's `unsafe fn` / `unsafe {}` distinction is already well-represented in training data, as is the `safe` keyword from [RFC 3484](https://rust-lang.github.io/rfcs/3484-unsafe-extern-blocks.html). A C# `safe` keyword that mirrors Rust's emerging usage inherits that representation. An LLM encountering `safe void CopyTo(...)` for the first time can draw on its understanding of safety-boundary patterns in Rust — a form of cross-language transfer that the "absence" model cannot benefit from, because there is nothing to transfer *to*. There is no token, no pattern, no signal.

This matters for the initial adoption period especially. New language features have zero training examples in C# until the ecosystem produces them. Syntactic alignment with Rust's safety vocabulary provides a bridge. The alternative — relying on the model to learn that the *absence* of `unsafe` in C# carries the same meaning as explicit safety markers in other languages — is a strictly harder inference.

## Agent tooling: grep as primitive

[SWE-bench (Jimenez et al., ICLR 2024)](https://arxiv.org/abs/2310.06770) evaluates agents on 2,294 real GitHub issues across 12 Python repositories. [SWE-agent (Yang et al., 2024)](https://arxiv.org/abs/2405.15793) builds on this, showing that a "custom agent-computer interface (ACI) significantly enhances an agent's ability to create and edit code files, navigate entire repositories, and execute tests." SWE-agent achieves 12.5% pass@1 on SWE-bench. Both evaluations confirm that coding agents rely on grep/search as their primary code navigation primitive. Failure to *find* the right code is a dominant failure mode.

This creates a concrete operational gap between the two designs:

- **With `safe`:** `rg -w "safe" --type cs` — one call, complete results, no parsing required.
- **Without `safe`:** enumerate all methods, then filter for the absence of `unsafe` — requires AST parsing or fragile regex heuristics, and is not expressible as a single grep.

For the audit scenario — an agent scanning a codebase after migration to verify safety boundaries — this difference is the difference between a reliable single-step operation and an unreliable multi-step inference. The agent literature consistently shows that reducing the number of tool calls and inference steps improves task success rates.

## Summary

| Finding | Source | Claim |
|---------|--------|-------|
| Type constraints improve code correctness 3.5–37% | Mundler et al. (2025) | Accuracy |
| Grammar constraints beat unconstrained and fine-tuned models | Geng et al. (EMNLP 2023) | Accuracy |
| Strict compilers yield 2x agent success | CRUST-Bench (2025) | Accuracy |
| Constraint scaling reduces hallucination 46.9% | Kollias et al. (ICML 2024) | Accuracy |
| Negation reasoning degrades 10–30% | Hossain et al. (ACL 2022) | Efficiency |
| GPT-4-era models drop 15–25% on negation | Truong et al. (2023) | Efficiency |
| Attention is presence-based; no mechanism for absent tokens | Clark et al. (2019), Voita et al. (2019) | Efficiency |
| Inverse scaling: larger models get *worse* at negation | Jang et al. (NeurIPS 2022 Workshop) | Efficiency |
| Explicit annotations improve code understanding 5–15% | TypeScript vs. JavaScript studies | Efficiency |
| Cross-language transfer clusters by syntactic similarity | MultiPL-E (Cassano et al., 2023) | Efficiency |
| Agent success bottlenecked by code search | SWE-bench (ICLR 2024) | Efficiency |

The two claims are supported by different bodies of research. The accuracy claim is objective: stricter language semantics constrain the space of valid programs and produce compiler errors that guide agents toward correct code. The efficiency claim is about reducing inference cost: explicit markers are more reliably processed by transformers, more efficiently discovered by agents, and more naturally aligned with cross-language training data. The absence model works — Rust and Swift demonstrate this — but an explicit model works *better*, and C# has the opportunity to ship one.
