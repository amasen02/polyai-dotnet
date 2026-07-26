using FluentAssertions;
using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Providers.Anthropic;
using PolyAI.Providers.OpenAI;
using PolyAI.Tests.QaProbes.Fakes;

namespace PolyAI.Tests.QaProbes;

/// <summary>
/// GRO-123 QA probes — structured output and malformed/partial response handling.
/// The shipped suite has one happy-path StructuredAsync test using a single-word property.
/// </summary>
public sealed class P3_StructuredOutputProbes
{
    /// <summary>Two-word property names — the ordinary case for a real DTO.</summary>
    public sealed class WeatherReport
    {
        public string? City { get; set; }
        public string? CityName { get; set; }
        public int TemperatureCelsius { get; set; }
    }

    private static OpenAIProvider ProviderReturning(string assistantContent)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize(assistantContent);
        var json = $"{{\"choices\":[{{\"message\":{{\"role\":\"assistant\",\"content\":{escaped}}}," +
                   "\"finish_reason\":\"stop\"}]}";
        return new OpenAIProvider(new HttpClient(CapturingHandler.Json(json)), new OpenAIOptions { ApiKey = "k" });
    }

    // ---------------------------------------------------------------- P3.1
    // ProviderBase.JsonOptions sets PropertyNamingPolicy = SnakeCaseLower. That policy is
    // correct for provider wire bodies, but the SAME options object deserializes the CALLER'S
    // type — so STJ expects "city_name" for CityName. Nothing in the prompt asks the model for
    // snake_case, so every multi-word property silently binds to its default value.
    [Fact(Skip = "Documented defect: StructuredAsync uses snake_case naming policy on caller types. Tracked in GRO 18c408e8.")]
    public async Task P3_1_StructuredAsync_binds_camelCase_multi_word_properties()
    {
        var provider = ProviderReturning("""{"city":"Colombo","cityName":"Colombo","temperatureCelsius":31}""");

        var result = await provider.StructuredAsync<WeatherReport>([ChatMessage.User("weather?")]);

        result.City.Should().Be("Colombo", "single-word properties bind either way");
        result.CityName.Should().Be("Colombo",
            "the model is asked for JSON 'matching this C# type', so it emits camelCase or " +
            "PascalCase; a snake_case naming policy silently drops every multi-word property");
        result.TemperatureCelsius.Should().Be(31);
    }

    // ---------------------------------------------------------------- P3.2
    [Fact(Skip = "Documented defect: StructuredAsync uses snake_case naming policy on caller types. Tracked in GRO 18c408e8.")]
    public async Task P3_2_StructuredAsync_binds_PascalCase_multi_word_properties()
    {
        var provider = ProviderReturning("""{"City":"Colombo","CityName":"Colombo","TemperatureCelsius":31}""");

        var result = await provider.StructuredAsync<WeatherReport>([ChatMessage.User("weather?")]);

        result.CityName.Should().Be("Colombo");
        result.TemperatureCelsius.Should().Be(31);
    }

    // ---------------------------------------------------------------- P3.3
    // Partial / truncated JSON in structured output — QA scope. Expected to PASS:
    // the JsonException is wrapped in PolyAIException. Recorded as verified-good.
    [Fact]
    public async Task P3_3_Truncated_structured_output_raises_PolyAIException_with_the_raw_payload()
    {
        var provider = ProviderReturning("""{"city":"Colombo","temperatureCel""");

        var act = async () => await provider.StructuredAsync<WeatherReport>([ChatMessage.User("weather?")]);

        (await act.Should().ThrowAsync<PolyAIException>())
            .WithMessage("*invalid JSON*").WithMessage("*Colombo*");
    }

    // ---------------------------------------------------------------- P3.4
    // Prose around the JSON is the single most common structured-output failure. ExtractJson
    // only strips fenced blocks, so a leading sentence produces a raw parse failure.
    [Fact(Skip = "Documented defect: ExtractJson does not strip leading prose or handle single-line fences. Tracked in GRO-STRUCTUREDREMAINING.")]
    public async Task P3_4_StructuredAsync_extracts_the_JSON_object_when_the_model_adds_prose()
    {
        var provider = ProviderReturning("Sure! Here is the JSON you asked for:\n{\"city\":\"Colombo\"}");

        var result = await provider.StructuredAsync<WeatherReport>([ChatMessage.User("weather?")]);

        result.City.Should().Be("Colombo");
    }

    // ---------------------------------------------------------------- P3.5
    // A single-line fenced block: firstNewline is -1, so the opening fence is never stripped
    // while the closing fence IS — producing "```json {...}" and a guaranteed parse failure.
    [Fact(Skip = "Documented defect: ExtractJson does not strip leading prose or handle single-line fences. Tracked in GRO-STRUCTUREDREMAINING.")]
    public async Task P3_5_StructuredAsync_handles_a_single_line_fenced_JSON_block()
    {
        var provider = ProviderReturning("""```json {"city":"Colombo"} ```""");

        var result = await provider.StructuredAsync<WeatherReport>([ChatMessage.User("weather?")]);

        result.City.Should().Be("Colombo");
    }

    // ---------------------------------------------------------------- P3.6
    // Malformed top-level response body. JsonNode.Parse throws a raw System.Text.Json
    // JsonException that escapes the PolyAIException hierarchy, so `catch (PolyAIException)`
    // — the documented error-handling contract — does not catch it.
    [Fact(Skip = "Documented defect: raw System.Text.Json exceptions escape PolyAIException hierarchy. Tracked in GRO d3710e2c.")]
    public async Task P3_6_A_malformed_response_body_raises_PolyAIException_not_a_raw_JsonException()
    {
        var provider = new OpenAIProvider(
            new HttpClient(CapturingHandler.Json("{\"choices\": [ truncated")),
            new OpenAIOptions { ApiKey = "k" });

        var act = async () => await provider.ChatAsync([ChatMessage.User("Hi")]);

        await act.Should().ThrowAsync<PolyAIException>(
            "every documented failure mode of this SDK is a PolyAIException subclass");
    }

    // ---------------------------------------------------------------- P3.7
    // Same class of defect on Anthropic, via a different mechanism: AsArray() on a
    // non-array node throws InvalidOperationException.
    [Fact(Skip = "Documented defect: raw System.Text.Json exceptions escape PolyAIException hierarchy. Tracked in GRO d3710e2c.")]
    public async Task P3_7_An_unexpected_Anthropic_content_shape_raises_PolyAIException()
    {
        var provider = new AnthropicProvider(
            new HttpClient(CapturingHandler.Json("""{"content":{"type":"text","text":"oops"}}""")),
            new AnthropicOptions { ApiKey = "k" });

        var act = async () => await provider.ChatAsync([ChatMessage.User("Hi")]);

        await act.Should().ThrowAsync<PolyAIException>();
    }

    // ---------------------------------------------------------------- P3.8
    // A 200 response with an empty body is what a truncated/proxied connection produces.
    [Fact(Skip = "Documented defect: raw System.Text.Json exceptions escape PolyAIException hierarchy. Tracked in GRO d3710e2c.")]
    public async Task P3_8_An_empty_response_body_raises_PolyAIException()
    {
        var provider = new OpenAIProvider(
            new HttpClient(CapturingHandler.Json(string.Empty)), new OpenAIOptions { ApiKey = "k" });

        var act = async () => await provider.ChatAsync([ChatMessage.User("Hi")]);

        await act.Should().ThrowAsync<PolyAIException>();
    }

    // ---------------------------------------------------------------- P3.9
    // Retry-After may legitimately be an HTTP-date rather than a delta-seconds value.
    // Only .Delta is read, so the date form is silently discarded and callers lose their
    // backoff hint exactly when they need it.
    [Fact(Skip = "Documented defect. Tracked in GRO-STRUCTUREDREMAINING.")]
    public async Task P3_9_A_429_with_an_HTTP_date_Retry_After_still_reports_RetryAfter()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"error\":\"slow down\"}")
        };
        response.Headers.RetryAfter =
            new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(90));

        var provider = new OpenAIProvider(
            new HttpClient(new CapturingHandler(_ => response)), new OpenAIOptions { ApiKey = "k" });

        var act = async () => await provider.ChatAsync([ChatMessage.User("Hi")]);

        var ex = await act.Should().ThrowAsync<ProviderRateLimitException>();
        ex.Which.RetryAfter.Should().NotBeNull(
            "RFC 9110 allows Retry-After as an HTTP-date; only Delta is read, so the hint is lost");
    }

    // ---------------------------------------------------------------- P3.10
    // Azure failures must be attributable to Azure. AzureOpenAIProvider delegates to an inner
    // OpenAIProvider, and it is the inner provider's name that reaches the exception.
    [Fact(Skip = "Documented defect. Tracked in GRO-STRUCTUREDREMAINING.")]
    public async Task P3_10_An_Azure_failure_is_reported_against_the_azure_openai_provider()
    {
        var provider = new PolyAI.Providers.Azure.AzureOpenAIProvider(
            new HttpClient(CapturingHandler.Json("{\"error\":\"nope\"}", System.Net.HttpStatusCode.Unauthorized)),
            new PolyAI.Providers.Azure.AzureOpenAIOptions
            {
                ApiKey = "k",
                Endpoint = "https://unit-test.openai.azure.com",
                DeploymentName = "gpt-4o",
            });

        var act = async () => await provider.ChatAsync([ChatMessage.User("Hi")]);

        var ex = await act.Should().ThrowAsync<ProviderAuthException>();
        ex.Which.Provider.Should().Be("azure-openai",
            "an operator reading this exception must be able to tell which credential failed");
    }
}
