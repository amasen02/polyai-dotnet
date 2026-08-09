# Changelog

All notable changes to `PolyAI.DotNet` are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed

- **A misconfigured `AddPolyAI` now fails at startup instead of on the first user request.**
  `WithDefaultProvider(name)` stored the name verbatim and `Build()` never compared it against the
  registered factories, so `.UseOpenAI(key).WithDefaultProvider("opemai")` returned normally,
  `BuildServiceProvider()` succeeded, health checks passed and the pod went Ready — then every
  request threw `PolyAIException: No provider registered under 'opemai'`. A zero-provider
  registration behaved the same way: `PolyAIRouter`'s constructor did guard it, but that constructor
  runs on first *resolution*, not at registration. `Build()` now rejects both, before it touches the
  `IServiceCollection`, and the unknown-default message lists the providers that *were* registered.
  The default may still be named before the provider it refers to — the check is deliberately in
  `Build()`, not in `WithDefaultProvider`, so `.WithDefaultProvider("openai").UseOpenAI(key)` keeps
  working.
- **A default provider bound from an absent configuration key is no longer guessed.**
  `WithDefaultProvider` accepted null, empty and whitespace, so the common
  `.WithDefaultProvider(config["Ai:DefaultProvider"]!)` silently fell back to whichever provider
  happened to be registered first — the application ran, and answered every request with a provider
  nobody had chosen. It now throws `ArgumentException` at the call site. Naming no default at all is
  still valid and still selects the first registered provider; that is a stated default, not a
  missing one.
- **Cancelling a stream no longer wedges the caller, on any of the five providers.**
  `ProviderBase.ReadSseChunksAsync` and the NDJSON loop in `OllamaProvider.StreamAsync` drove their
  reads from `while (!reader.EndOfStream)`. `StreamReader.EndOfStream` refills its buffer with a
  *synchronous*, non-cancellable read, and responses are requested with
  `HttpCompletionOption.ResponseHeadersRead`, so on an open-but-idle connection — the normal state
  between tokens — the loop parked in its **condition** and never reached the
  `ThrowIfCancellationRequested()` in its body. The token was therefore never observed: an ASP.NET
  Core client disconnect leaked the request for the lifetime of the connection, and every buffer
  refill blocked a thread-pool thread on network I/O, capping streaming concurrency under load.
  Both loops now drive on `await reader.ReadLineAsync(cancellationToken)` and stop when it returns
  `null`. OpenAI, Anthropic, Gemini and Azure OpenAI share the SSE path; Ollama has its own.
- **A truncated Ollama stream now ends the enumeration instead of spinning.** In the NDJSON loop the
  end-of-stream check deliberately precedes the blank-line skip, because
  `string.IsNullOrWhiteSpace(null)` is `true` — folding the two together would swallow the
  end-of-stream signal and spin on a stream that ends without a `done: true` terminator.
- **A malformed provider response no longer escapes the `PolyAIException` contract.** Every provider
  parsed the response body with an unguarded `JsonNode.Parse`, so a truncated body, an empty `200`
  from a proxy, or an unexpected field shape surfaced as a raw `System.Text.Json.JsonReaderException`
  (or, on Anthropic, an `InvalidOperationException` from `AsArray()`). Callers following the
  documented `catch (PolyAIException)` contract missed it entirely and the process took an unhandled
  exception. Response parsing now runs behind a single boundary,
  `ProviderBase.ReadChatResponseAsync`, which raises a `ProviderException` carrying the provider,
  status code, and raw `ResponseBody`, with the original parse failure as `InnerException`.
  `StructuredAsync` already did this for its own deserialization step; the top-level response parse
  never got the same treatment.
- **An unexpected `content`/`parts` shape is reported instead of read as empty.** Anthropic
  `content` and Gemini `candidates[0].content.parts` are now shape-checked: a value that is present
  but is not a JSON array raises a `ProviderException` naming the field and the expected shape.
  Legitimate absence is unchanged — an Anthropic response with no `content` block, and a Gemini
  candidate stopped by a safety filter, both still read as an empty message.
- **Gemini `ChatAsync` and `StructuredAsync` could not reach the API.** `BuildEndpoint` appended the
  API key with `&key=`, but the non-streaming URL never opened a query string, so
  `generateContent&key=...` became part of the URL *path* and every request resolved to HTTP 404.
  `StreamAsync` was unaffected — `streamGenerateContent?alt=sse` already opened the query string,
  which is why the streaming tests passed and hid the defect.

### Security

- **The Gemini API key is no longer placed in the request URL.** It is sent in the `x-goog-api-key`
  header, so it can no longer leak into `Microsoft.Extensions.Http` request logs or into any proxy
  along the path.
- **The Gemini model name is escaped before it enters the URL path.** A caller-supplied model name
  such as `evil?alt=json&key=leaked` was previously interpolated raw and could inject or override
  query parameters.

### Added

- `CapturingHandler` test fake plus Gemini wire-format regression tests that assert on the outgoing
  request URI, headers, and body. The existing `FakeHttpMessageHandler` accepts any URI silently,
  which is why no shipped test caught the malformed URL.
- An **Error handling** section in the README documenting the `PolyAIException` contract, and
  `MalformedResponseTests` covering every provider against a truncated body, an empty `200`, a
  `null` body, a missing `choices` entry, and a wrong-shaped `content`/`parts` list.

## [1.0.0] - 2026-07-18

### Added

- `IPolyAIClient` — unified interface for chat, streaming, and structured output.
- `IPolyAIRouter` — routes requests to a named provider or the registered default.
- **OpenAI** provider: chat completions, SSE streaming, tool calling (function calls).
- **Anthropic** provider: messages API, SSE streaming, tool use (system-message extraction, tool_use content blocks).
- **Google Gemini** provider: generateContent, streaming, function declarations.
- **Ollama** provider: local chat completions, NDJSON streaming.
- **Azure OpenAI** provider: wraps OpenAI provider with Azure auth header and deployment endpoint.
- `services.AddPolyAI(o => o.UseAnthropic(...).UseOpenAI(...))` — ASP.NET Core DI integration.
- `PolyAIBuilder.WithDefaultProvider(name)` — explicit default override.
- `IPolyAIClient.StructuredAsync<T>()` — instructs the model to return JSON and deserializes it.
- `[PolyAITool]` / `[PolyAIParam]` attributes + `ToolRegistry.FromInstance<T>()` — reflection-based tool discovery.
- `ProviderException`, `ProviderAuthException`, `ProviderRateLimitException` — typed per-provider error hierarchy.
- `ChatOptions` — temperature, top_p, max_tokens, stop sequences, tools.
- `TokenUsage` — prompt + completion token counts.
- `ToolCall` — tool invocation parsed from model responses.
- xUnit test suite (30 tests) with `FakeHttpMessageHandler`.
- GitHub Actions CI: build, test, security scan, NuGet pack, optional publish.
- Docker support via `Dockerfile` and `docker-compose.yml` for the sample app.
