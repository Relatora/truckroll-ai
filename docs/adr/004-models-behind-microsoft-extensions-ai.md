# ADR-004: Put every model behind Microsoft.Extensions.AI, with Ollama by default

- **Status:** Accepted
- **Date:** 2026-11-26

## Context

Four v0.1 features need a language model: the cited assistant, dispatch explanations, call
summaries and the MCP tools that call back into them. Retrieval also needs an embedding model.

Three things pull in different directions:

- **The demo has to run for free, offline, on one machine.** A reviewer cloning the repo shouldn't
  need an API key or a credit card. Ollama with `qwen2.5:7b` and `nomic-embed-text` runs on a
  consumer GPU.
- **A company running this for real would want a stronger model** for the answers customers and
  staff read. Claude writes clearer explanations than a 7B model, and the switch shouldn't need code.
- **Customer records are personal data.** Phone numbers, addresses and service history shouldn't
  leave the building just to be turned into vectors.

And the tests must never depend on a model at all: a model is slow, non-deterministic and, for
Claude, costs money per run.

Microsoft.Extensions.AI is .NET's standard abstraction for this. `IChatClient` and
`IEmbeddingGenerator` have adapters for Ollama (OllamaSharp) and Claude (Anthropic's official SDK),
plus middleware for OpenTelemetry, logging, caching and function calling.

## Decision

- Features depend only on `IChatClient` and `IEmbeddingGenerator<string, Embedding<float>>`.
- The provider is one setting, `AI:Provider`: `Ollama` (default) or `Claude`. Claude's key lives in
  user secrets, and the API refuses to start if Claude is chosen without one.
- **Embeddings always come from Ollama**, whichever provider answers chat.
- Each service is a pipeline over a keyed provider client: OpenTelemetry, then logging, then the
  provider. Spans record the model, provider and token counts but never prompt or answer text.
- Tests replace only the keyed provider client, with a scripted chat fake and a deterministic
  hashing embedder, and point Ollama's address at a closed port so a missed call fails loudly.

## Consequences

- Swapping or adding a provider (Azure OpenAI, a newer local model) is a new branch in one switch
  statement. No feature changes.
- The Redis vector index depends on the embedding model's width (768 for `nomic-embed-text`).
  Changing the embedding model means re-indexing; `GET /api/dev/ai-check` reports a mismatch.
- With Claude selected, the text of a question and the passages retrieved for it do leave the
  machine. Masking phone numbers and emails before anything reaches a model (issue #11) applies to
  both providers.
- Small local models follow structured-output instructions less reliably than Claude. Features that
  need structured output (call summaries) must validate what comes back and say so when it fails,
  rather than trust it.
- A cold local model can take tens of seconds to load. The Ollama `HttpClient` and the Next.js
  rewrite proxy both allow five minutes.

## Alternatives considered

- **Semantic Kernel.** Builds on the same abstractions but adds planners and plugins v0.1 doesn't
  need. Microsoft.Extensions.AI is the layer underneath, and the smaller surface is easier to read.
- **Calling each provider's SDK directly.** No shared telemetry or test seam, and every feature
  would need to know which provider it's talking to.
- **Claude by default.** Better answers, but the demo would stop being free and offline, and every
  test run that slipped past a fake would cost money.
- **Embeddings from the chat provider too.** Anthropic has no embedding API, and moving vectors
  between providers would mean re-indexing whenever chat changed.
