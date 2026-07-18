using FluentAssertions;
using PolyAI.Tools;

namespace PolyAI.Tests.Tools;

public sealed class ToolRegistryTests
{
    private sealed class SampleTools
    {
        [PolyAITool("Gets weather for a city", name: "get_weather")]
        public string GetWeather(
            [PolyAIParam("City name")] string city,
            [PolyAIParam("Temperature unit")] string unit = "celsius")
            => $"{city}: sunny";

        [PolyAITool("Calculates sum of two numbers")]
        public int Add(
            [PolyAIParam("First number")] int a,
            [PolyAIParam("Second number")] int b)
            => a + b;

        public string NotATool() => "ignored";
    }

    [Fact]
    public void FromInstance_returns_only_annotated_methods()
    {
        var tools = ToolRegistry.FromInstance(new SampleTools());

        tools.Should().HaveCount(2);
        tools.Select(t => t.Name).Should().Contain("get_weather").And.Contain("add");
    }

    [Fact]
    public void FromInstance_uses_explicit_name_when_provided()
    {
        var tools = ToolRegistry.FromInstance(new SampleTools());
        var weather = tools.Single(t => t.Name == "get_weather");

        weather.Description.Should().Be("Gets weather for a city");
    }

    [Fact]
    public void FromInstance_converts_method_name_to_snake_case_when_no_explicit_name()
    {
        var tools = ToolRegistry.FromInstance(new SampleTools());
        tools.Should().Contain(t => t.Name == "add");
    }

    [Fact]
    public void FromInstance_marks_optional_parameters_as_not_required()
    {
        var tools = ToolRegistry.FromInstance(new SampleTools());
        var weather = tools.Single(t => t.Name == "get_weather");

        var cityParam = weather.Parameters.Single(p => p.Name == "city");
        var unitParam = weather.Parameters.Single(p => p.Name == "unit");

        cityParam.Required.Should().BeTrue();
        unitParam.Required.Should().BeFalse();
    }

    [Fact]
    public void FromInstance_captures_param_descriptions()
    {
        var tools = ToolRegistry.FromInstance(new SampleTools());
        var weather = tools.Single(t => t.Name == "get_weather");

        weather.Parameters.Single(p => p.Name == "city").Description.Should().Be("City name");
    }

    [Fact]
    public void FromInstance_infers_correct_json_schema_types()
    {
        var tools = ToolRegistry.FromInstance(new SampleTools());
        var addTool = tools.Single(t => t.Name == "add");

        addTool.Parameters.All(p => p.JsonSchemaType == "integer").Should().BeTrue();
    }
}
