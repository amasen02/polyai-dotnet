# Changelog

All notable changes to `PolyAI.DotNet` are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed

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
