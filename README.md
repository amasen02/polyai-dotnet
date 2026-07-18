# PolyAI.DotNet

[![CI](https://github.com/amasen02/polyai-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/amasen02/polyai-dotnet/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/PolyAI.DotNet.svg)](https://www.nuget.org/packages/PolyAI.DotNet)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](CONTRIBUTING.md)
[![Contributor Covenant](https://img.shields.io/badge/Contributor%20Covenant-2.1-4baaaa.svg)](CODE_OF_CONDUCT.md)

> **Lightweight multi-provider AI SDK for ASP.NET Core.** One interface. Five providers. Zero framework lock-in.

Semantic Kernel is a 28 000-line orchestration framework. `Microsoft.Extensions.AI` is just interfaces with no implementation. PolyAI.DotNet is the missing middle: a single 20 kB NuGet package that wires five AI providers into ASP.NET Core's DI container in two lines of code, with first-class streaming, typed structured output, and attribute-based tool/function calling — no Semantic Kernel dependency.

## Supported providers

| Provider | Chat | Streaming | Structured output | Tool calling |
|---|:---:|:---:|:---:|:---:|
| OpenAI (GPT-4o, GPT-4o mini, …) | ✅ | ✅ | ✅ | ✅ |
| Anthropic Claude (3.5 Sonnet, Haiku, …) | ✅ | ✅ | ✅ | ✅ |
| Google Gemini (1.5 Flash, 1.5 Pro, …) | ✅ | ✅ | ✅ | ✅ |
| Ollama (local — Llama 3.2, Mistral, …) | ✅ | ✅ | ✅ | — |
| Azure OpenAI | ✅ | ✅ | ✅ | ✅ |

## Quickstart

### Install

```bash
dotnet add package PolyAI.DotNet
```

### Register

```csharp
// Program.cs (ASP.NET Core / Generic Host)
builder.Services.AddPolyAI(o => o
    .UseAnthropic(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")!)
    .UseOpenAI(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!)
    .UseOllama()                    // no key required for local Ollama
    .UseGemini(Environment.GetEnvironmentVariable("GEMINI_API_KEY")!)
    .WithDefaultProvider("anthropic"));
```

### Chat

```csharp
public class MyService(IPolyAIClient client)
{
    public async Task<string> AskAsync(string question)
    {
        var response = await client.ChatAsync([
            ChatMessage.System("You are a concise assistant."),
            ChatMessage.User(question),
        ]);

        Console.WriteLine($"Tokens used: {response.Usage?.TotalTokens}");
        return response.Content;
    }
}
```

### Streaming

```csharp
await foreach (var chunk in client.StreamAsync([ChatMessage.User("Tell me a joke.")]))
    Console.Write(chunk);    // each chunk is a string, stream to the browser as SSE
```

### Structured output

```csharp
record MovieReview(string Title, int Score, string Summary);

var review = await client.StructuredAsync<MovieReview>([
    ChatMessage.User("Review Inception in JSON."),
]);

Console.WriteLine($"{review.Title}: {review.Score}/10 — {review.Summary}");
```

### Tool / function calling

```csharp
using PolyAI.Tools;

public class WeatherTools
{
    [PolyAITool("Get current weather for a location")]
    public string GetWeather(
        [PolyAIParam("City name")] string city,
        [PolyAIParam("Unit: celsius or fahrenheit")] string unit = "celsius")
        => $"22°{(unit == "fahrenheit" ? "F" : "C")} in {city}, sunny";
}

// Discover tools via reflection
var tools = ToolRegistry.FromInstance(new WeatherTools());

var response = await client.ChatAsync(
    [ChatMessage.User("What's the weather in Tokyo?")],
    new ChatOptions { Tools = tools });

foreach (var call in response.ToolCalls)
    Console.WriteLine($"Model called: {call.Name}({call.ArgumentsJson})");
```

### Route to a specific provider

```csharp
var router = serviceProvider.GetRequiredService<IPolyAIRouter>();

// Use OpenAI for this request regardless of the default
var openaiClient = router.GetProvider("openai");
var response = await openaiClient.ChatAsync([ChatMessage.User("Hi")],
    new ChatOptions { Model = "gpt-4o" });
```

## Architecture

```
PolyAI.DotNet
├── Abstractions/
│   ├── IPolyAIClient       ← the core interface (chat / stream / structured)
│   ├── IPolyAIRouter       ← routes requests to a named provider
│   ├── ChatMessage         ← System / User / Assistant / Tool roles
│   ├── ChatOptions         ← temperature, top_p, max_tokens, stop, tools
│   ├── ChatResponse        ← content, tool calls, token usage, finish reason
│   └── TokenUsage
├── Providers/
│   ├── OpenAI/             ← OpenAIProvider (OpenAIOptions)
│   ├── Anthropic/          ← AnthropicProvider (AnthropicOptions)
│   ├── Gemini/             ← GeminiProvider (GeminiOptions)
│   ├── Ollama/             ← OllamaProvider (OllamaOptions)
│   └── Azure/              ← AzureOpenAIProvider (AzureOpenAIOptions)
├── Tools/
│   ├── [PolyAITool]        ← mark a method as a callable tool
│   ├── [PolyAIParam]       ← describe a parameter
│   └── ToolRegistry        ← reflection-based discovery
├── Errors/
│   ├── PolyAIException
│   ├── ProviderException   ← status code, raw body
│   ├── ProviderAuthException     ← 401/403
│   └── ProviderRateLimitException ← 429, RetryAfter
└── Extensions/
    ├── ServiceCollectionExtensions  ← .AddPolyAI(...)
    └── PolyAIBuilder                ← .UseOpenAI / .UseAnthropic / ...
```

**Extension points:**
- Add a new provider: implement `ProviderBase`, register via `PolyAIBuilder`.
- Custom router: implement `IPolyAIRouter` and register in DI before calling `Build()`.
- Custom `ChatOptions`: `ChatOptions` is a `sealed class` with nullable properties — extend by subclassing if you need provider-specific extras.

## Configuration

All options classes follow the same pattern: pass the API key to `Use<Provider>()`, then optionally pass a configure action for advanced settings.

```csharp
services.AddPolyAI(o => o
    .UseOpenAI("key", opts =>
    {
        opts.DefaultModel = "gpt-4o";
        opts.BaseUrl = "https://my-custom-proxy/v1";  // OpenAI-compatible endpoint
    })
    .UseAnthropic("sk-ant-...", opts => opts.DefaultModel = "claude-3-5-sonnet-20241022")
    .UseOllama(opts => opts.BaseUrl = "http://my-ollama-host:11434"));
```

## Running locally

```bash
git clone https://github.com/amasen02/polyai-dotnet.git
cd polyai-dotnet

# Build and test
dotnet build
dotnet test

# Run the sample (set at least one API key)
export ANTHROPIC_API_KEY=sk-ant-...
dotnet run --project samples/PolyAI.Sample
```

### Docker

```bash
# Build
docker build -f samples/PolyAI.Sample/Dockerfile -t polyai-sample .

# Run
docker run -e ANTHROPIC_API_KEY=sk-ant-... polyai-sample

# Or with docker compose
ANTHROPIC_API_KEY=sk-ant-... docker compose up
```

## Tests

34 xUnit tests covering all providers, the DI wiring, error types, streaming, tool discovery, and structured output deserialization. All tests use `FakeHttpMessageHandler` — no live API calls, no environment variables required.

```bash
dotnet test
```

## Open source commitments

- **License:** MIT, permanently. No relicensing.
- **No CLA:** all contributors retain ownership of their contributions.
- **Honest history:** no backdated commits, no fabricated activity.
- **Security:** vulnerabilities acknowledged within 48 hours. See [SECURITY.md](SECURITY.md).
- **Code of Conduct:** [Contributor Covenant 2.1](CODE_OF_CONDUCT.md).
- **Reproducible CI:** green build required before any merge.

## Comparison

| | PolyAI.DotNet | Semantic Kernel | Microsoft.Extensions.AI |
|---|---|---|---|
| Package size | ~20 kB | ~2.5 MB | ~30 kB (interfaces only) |
| DI integration | ✅ Built-in | ✅ | ✅ |
| Streaming | ✅ `IAsyncEnumerable<string>` | ✅ | Partial |
| Structured output | ✅ `StructuredAsync<T>()` | Via plugins | ❌ |
| Tool calling | ✅ `[PolyAITool]` attributes | Via `KernelFunction` | ❌ |
| Providers in one package | 5 | Many (separate packages) | 0 (you wire them) |
| Learning curve | Low | High | Medium |

## Author

**Ama Senevirathne** — [github.com/amasen02](https://github.com/amasen02)

Also see: [freshcart-backend](https://github.com/amasen02/freshcart-backend) — a full .NET 10 Aspire microservices e-commerce platform.
