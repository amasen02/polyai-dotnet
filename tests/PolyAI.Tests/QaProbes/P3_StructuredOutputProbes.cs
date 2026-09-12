using FluentAssertions;
using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Providers;
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
    public sealed class ExplicitNameReport
    {
        [System.Text.Json.Serialization.JsonPropertyName("city_label")]
        public string? CityName { get; set; }
    }
    /// <summary>Two-word property names — the ordinary case for a real DTO.</summary>
    public sealed class WeatherReport
    {
        public string? City { get; set; }
        public string? CityName { get; set; }
        public int TemperatureCelsius { get; set; }
    }

    [Fact]
    public async Task StructuredAsync_accepts_a_complete_raw_JSON_array()
    {
        var provider = ProviderReturning("[{\"city\":\"Colombo\"},{\"city\":\"Kandy\"}]");

        var result = await provider.StructuredAsync<WeatherReport[]>([ChatMessage.User("weather?")]);

        result.Should().HaveCount(2);
        result.Select(report => report.City).Should().Equal("Colombo", "Kandy");
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
    [Fact]
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
    [Fact]
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
    // Unfenced prose plus JSON is deliberately rejected; extraction never brace-scans prose.
    [Fact]
    public async Task P3_4_StructuredAsync_rejects_unfenced_prose_plus_JSON()
    {
        var provider = ProviderReturning("Sure! Here is the JSON you asked for:\n{\"city\":\"Colombo\"}");

        var act = async () => await provider.StructuredAsync<WeatherReport>([ChatMessage.User("weather?")]);

        await act.Should().ThrowAsync<PolyAIException>();
    }

    // ---------------------------------------------------------------- P3.5
    // A conventional single fenced JSON payload may have surrounding prose.
    [Fact]
    public async Task P3_5_StructuredAsync_handles_a_fenced_JSON_block_with_surrounding_prose()
    {
        var provider = ProviderReturning("Sure:\n```json\n{\"city\":\"Colombo\"}\n```\nThanks.");

        var result = await provider.StructuredAsync<WeatherReport>([ChatMessage.User("weather?")]);

        result.City.Should().Be("Colombo");
    }

    [Fact]
    public async Task StructuredAsync_handles_a_CRLF_fenced_JSON_block_with_surrounding_prose()
    {
        var provider = ProviderReturning("Before:\r\n```json\r\n{\"city\":\"Colombo\"}\r\n```\r\nAfter.");

        var result = await provider.StructuredAsync<WeatherReport>([ChatMessage.User("weather?")]);

        result.City.Should().Be("Colombo");
    }

    [Fact]
    public async Task StructuredAsync_accepts_fenced_JSON_with_fence_text_inside_a_string()
    {
        var provider = ProviderReturning("```json\n{\"city\":\"```json text\"}\n```\n");

        var result = await provider.StructuredAsync<WeatherReport>([ChatMessage.User("weather?")]);

        result.City.Should().Be("```json text");
    }

    [Fact]
    public async Task StructuredAsync_prefers_complete_raw_JSON_with_fence_text_inside_a_string()
    {
        var provider = ProviderReturning("""{"city":"```json not a fence"}""");

        var result = await provider.StructuredAsync<WeatherReport>([ChatMessage.User("weather?")]);

        result.City.Should().Be("```json not a fence");
    }

    [Fact]
    public async Task StructuredAsync_rejects_multiple_fenced_payloads()
    {
        var provider = ProviderReturning("```json\n{\"city\":\"A\"}\n```\n```json\n{\"city\":\"B\"}\n```");

        var act = async () => await provider.StructuredAsync<WeatherReport>([ChatMessage.User("weather?")]);

        await act.Should().ThrowAsync<PolyAIException>();
    }

    [Fact]
    public async Task StructuredAsync_rejects_the_JSON_literal_null()
    {
        var provider = ProviderReturning("null");

        var act = async () => await provider.StructuredAsync<WeatherReport>([ChatMessage.User("weather?")]);

        await act.Should().ThrowAsync<PolyAIException>();
    }

    [Fact]
    public async Task StructuredAsync_preserves_explicit_json_property_names()
    {
        var provider = ProviderReturning("""{"city_label":"Colombo"}""");

        var result = await provider.StructuredAsync<ExplicitNameReport>([ChatMessage.User("weather?")]);

        result.CityName.Should().Be("Colombo");
    }

    // ---------------------------------------------------------------- P3.6
    // Malformed top-level response body. JsonNode.Parse throws a raw System.Text.Json
    // JsonException that escapes the PolyAIException hierarchy, so `catch (PolyAIException)`
    // — the documented error-handling contract — does not catch it.
    [Fact]
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
    [Fact]
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
    [Fact]
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
    [Fact]
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

    [Fact]
    public void ParseRetryAfter_uses_the_exact_clock_for_a_future_HTTP_date()
    {
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var future = now.AddMinutes(7);
        var header = new System.Net.Http.Headers.RetryConditionHeaderValue(future);

        ProviderBase.ParseRetryAfter(header, now).Should().Be(TimeSpan.FromMinutes(7));
    }

    [Fact]
    public void ParseRetryAfter_clamps_equal_and_past_HTTP_dates_to_zero()
    {
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

        ProviderBase.ParseRetryAfter(
            new System.Net.Http.Headers.RetryConditionHeaderValue(now), now).Should().Be(TimeSpan.Zero);
        ProviderBase.ParseRetryAfter(
            new System.Net.Http.Headers.RetryConditionHeaderValue(now.AddSeconds(-1)), now)
            .Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ParseRetryAfter_preserves_delta_seconds_without_a_clock_rounding()
    {
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var delta = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(123));

        ProviderBase.ParseRetryAfter(delta, now).Should().Be(TimeSpan.FromSeconds(123));
    }

    [Fact]
    public void ParseRetryAfter_returns_null_for_absent_or_malformed_headers()
    {
        var now = DateTimeOffset.UtcNow;
        System.Net.Http.Headers.RetryConditionHeaderValue.TryParse("not-a-retry-after", out var malformed)
            .Should().BeFalse();

        ProviderBase.ParseRetryAfter(null, now).Should().BeNull();
        ProviderBase.ParseRetryAfter(malformed, now).Should().BeNull();
    }

    [Fact]
    public void ParseRetryAfter_preserves_a_large_valid_future_date()
    {
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var future = now.AddYears(10);

        ProviderBase.ParseRetryAfter(
            new System.Net.Http.Headers.RetryConditionHeaderValue(future), now)
            .Should().Be(TimeSpan.FromDays(3652));
    }

    // ---------------------------------------------------------------- P3.10
    // Azure failures must be attributable to Azure. AzureOpenAIProvider delegates to an inner
    // OpenAIProvider, and it is the inner provider's name that reaches the exception.
    [Fact]
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

    [Fact]
    public async Task Azure_status_failure_preserves_provider_attribution()
    {
        var provider = new PolyAI.Providers.Azure.AzureOpenAIProvider(
            new HttpClient(CapturingHandler.Json("{\"error\":\"nope\"}", System.Net.HttpStatusCode.BadGateway)),
            new PolyAI.Providers.Azure.AzureOpenAIOptions
            {
                ApiKey = "k", Endpoint = "https://unit-test.openai.azure.com", DeploymentName = "gpt-4o"
            });

        var act = async () => await provider.ChatAsync([ChatMessage.User("Hi")]);

        var ex = await act.Should().ThrowAsync<ProviderException>();
        ex.Which.Provider.Should().Be("azure-openai");
        ex.Which.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task Azure_malformed_response_preserves_provider_attribution()
    {
        var provider = new PolyAI.Providers.Azure.AzureOpenAIProvider(
            new HttpClient(CapturingHandler.Json("{\"choices\":[truncated")),
            new PolyAI.Providers.Azure.AzureOpenAIOptions
            {
                ApiKey = "k", Endpoint = "https://unit-test.openai.azure.com", DeploymentName = "gpt-4o"
            });

        var act = async () => await provider.ChatAsync([ChatMessage.User("Hi")]);

        var ex = await act.Should().ThrowAsync<ProviderException>();
        ex.Which.Provider.Should().Be("azure-openai");
    }

    [Fact]
    public async Task Azure_stream_failure_preserves_provider_attribution()
    {
        var provider = new PolyAI.Providers.Azure.AzureOpenAIProvider(
            new HttpClient(new CapturingHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StreamContent(new ThrowingReadStream())
            })),
            new PolyAI.Providers.Azure.AzureOpenAIOptions
            {
                ApiKey = "k", Endpoint = "https://unit-test.openai.azure.com", DeploymentName = "gpt-4o"
            });

        var act = async () =>
        {
            await foreach (var _ in provider.StreamAsync([ChatMessage.User("Hi")])) { }
        };

        var ex = await act.Should().ThrowAsync<ProviderException>();
        ex.Which.Provider.Should().Be("azure-openai");
    }

    private sealed class ThrowingReadStream : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("stream broke");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(new IOException("stream broke"));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
