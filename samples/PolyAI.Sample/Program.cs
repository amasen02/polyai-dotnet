using Microsoft.Extensions.DependencyInjection;
using PolyAI.Abstractions;
using PolyAI.Extensions;
using PolyAI.Tools;

// ── 1. Register PolyAI with ASP.NET Core DI ──────────────────────────────────
var services = new ServiceCollection();

services.AddPolyAI(o => o
    // Add every provider you have API keys for; the first one becomes the default
    .UseAnthropic(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "demo-key")
    .UseOpenAI(Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "demo-key")
    .UseOllama()                                                   // no key needed
    .UseGemini(Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "demo-key")
    .WithDefaultProvider("anthropic"));

var sp = services.BuildServiceProvider();

// ── 2. Resolve the default provider ──────────────────────────────────────────
var client = sp.GetRequiredService<IPolyAIClient>();
Console.WriteLine($"Default provider: {client.ProviderName}");

// ── 3. Basic chat ─────────────────────────────────────────────────────────────
if (Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is { Length: > 0 } anthropicKey
    && anthropicKey != "demo-key")
{
    Console.WriteLine("\n── Chat ──");
    var response = await client.ChatAsync([
        ChatMessage.System("You are a concise assistant."),
        ChatMessage.User("What is the capital of France?"),
    ]);
    Console.WriteLine($"Answer: {response.Content}");
    Console.WriteLine($"Tokens used: {response.Usage?.TotalTokens}");

    // ── 4. Streaming ─────────────────────────────────────────────────────────
    Console.Write("\n── Stream ── ");
    await foreach (var chunk in client.StreamAsync([
        ChatMessage.User("Count from 1 to 5, one number per line."),
    ]))
    {
        Console.Write(chunk);
    }
    Console.WriteLine();

    // ── 5. Structured output ─────────────────────────────────────────────────
    Console.WriteLine("\n── Structured output ──");
    var planet = await client.StructuredAsync<Planet>([
        ChatMessage.User("Give me facts about Mars."),
    ]);
    Console.WriteLine($"Planet: {planet.Name}, diameter: {planet.DiameterKm} km, moons: {planet.Moons}");
}
else
{
    Console.WriteLine("(No real API key set — set ANTHROPIC_API_KEY to run live examples)");
}

// ── 6. Tool/function calling via attributes ───────────────────────────────────
Console.WriteLine("\n── Tool definitions discovered via reflection ──");
var tools = ToolRegistry.FromInstance(new WeatherTools());
foreach (var tool in tools)
{
    Console.WriteLine($"  {tool.Name}: {tool.Description}");
    foreach (var param in tool.Parameters)
        Console.WriteLine($"    - {param.Name} ({param.JsonSchemaType}, required={param.Required}): {param.Description}");
}

// ── 7. Route to a specific provider ──────────────────────────────────────────
var router = sp.GetRequiredService<IPolyAIRouter>();
Console.WriteLine($"\n── Registered providers: {string.Join(", ", router.RegisteredProviders)} ──");
var ollama = router.GetProvider("ollama");
Console.WriteLine($"Ollama provider ready: {ollama.ProviderName}");

record Planet(string Name, int DiameterKm, int Moons);

sealed class WeatherTools
{
    [PolyAITool("Get current weather for a location")]
    public string GetWeather(
        [PolyAIParam("City name or coordinates")] string location,
        [PolyAIParam("Temperature unit (celsius or fahrenheit)")] string unit = "celsius")
        => $"Weather in {location}: 22°{(unit == "fahrenheit" ? "F" : "C")}, sunny";

    [PolyAITool("Get a 5-day forecast for a location")]
    public string GetForecast(
        [PolyAIParam("City name")] string city,
        [PolyAIParam("Number of days")] int days = 5)
        => $"5-day forecast for {city}: mostly sunny";
}
