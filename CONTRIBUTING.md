# Contributing to PolyAI.DotNet

Thank you for your interest in contributing.

## Ground rules

- Follow the [Code of Conduct](CODE_OF_CONDUCT.md).
- Open an issue before starting large new features so the design can be discussed.
- Every change must keep `dotnet build -warnaserror` and `dotnet test` fully green.

## Setup

```bash
git clone https://github.com/amasen02/polyai-dotnet.git
cd polyai-dotnet
dotnet restore
dotnet build
dotnet test
```

## Adding a new provider

1. Create a folder under `src/PolyAI/Providers/<ProviderName>/`.
2. Add `<ProviderName>Options.cs` with provider-specific configuration.
3. Implement `ProviderBase` in `<ProviderName>Provider.cs`.
4. Register it in `PolyAIBuilder` as a `Use<ProviderName>(...)` method.
5. Add tests in `tests/PolyAI.Tests/Providers/<ProviderName>ProviderTests.cs` using `FakeHttpMessageHandler`. Cover at minimum: successful chat, streaming, auth error (401), and constructor validation.

## Extending the API surface

The core interface is `IPolyAIClient`. Any new capability should slot into `ChatOptions` (for request-time configuration) or be added to the interface with a default implementation on `ProviderBase` where possible.

## Pull request process

1. Branch from `main`.
2. Follow the conventional commit format (`fix:`, `feat:`, `docs:`, `chore:`).
3. Fill in the PR template — especially the checklist.
4. A passing CI run is required before merge.

## Questions

Open a [Discussion](https://github.com/amasen02/polyai-dotnet/discussions) rather than an issue for general questions.
