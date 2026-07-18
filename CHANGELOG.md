# Changelog

All notable changes to `PolyAI.DotNet` are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/).

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
