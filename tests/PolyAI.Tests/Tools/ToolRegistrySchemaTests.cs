using FluentAssertions;
using PolyAI.Errors;
using PolyAI.Tools;

namespace PolyAI.Tests.Tools;

/// <summary>
/// Covers the two silent-schema defects in <see cref="ToolRegistry"/>: discovery driven by the
/// static type rather than the runtime type, and parameter types that fell through to "string"
/// instead of being described or rejected.
/// </summary>
public sealed class ToolRegistrySchemaTests
{
    private abstract class ToolsBase;

    private sealed class WeatherTools : ToolsBase
    {
        [PolyAITool("Gets weather for a city", name: "get_weather")]
        public string GetWeather([PolyAIParam("City name")] string city) => $"{city}: sunny";
    }

    private sealed class StaticTools
    {
        [PolyAITool("A tool that does not need an instance", name: "static_tool")]
        public static string Ping() => "pong";
    }

    private enum Unit { Celsius, Fahrenheit }

    private sealed class Dto
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class SchemaTools
    {
        [PolyAITool("Books a slot")]
        public string Book(
            [PolyAIParam("When")] DateTime when,
            [PolyAIParam("Tags")] string[] tags,
            [PolyAIParam("How many")] int count) => string.Empty;

        [PolyAITool("Lists things")]
        public string Listing([PolyAIParam("Names")] List<string> names) => string.Empty;

        [PolyAITool("Sets a unit")]
        public string SetUnit([PolyAIParam("Unit")] Unit unit) => string.Empty;
    }

    private sealed class UnrepresentableTools
    {
        [PolyAITool("Takes a DTO")]
        public string Save([PolyAIParam("Payload")] Dto payload) => string.Empty;
    }

    // ---------- defect 1: FromInstance discarded the runtime type ----------

    [Fact]
    public void FromInstance_uses_the_runtime_type_not_the_static_type()
    {
        ToolsBase instance = new WeatherTools();

        var tools = ToolRegistry.FromInstance(instance);

        tools.Should().ContainSingle(
            "the tool is declared on the runtime type WeatherTools, and DI hands callers the base type")
            .Which.Name.Should().Be("get_weather");
    }

    [Fact]
    public void FromInstance_agrees_with_FromType_on_the_runtime_type()
    {
        ToolsBase instance = new WeatherTools();

        ToolRegistry.FromInstance(instance).Select(t => t.Name)
            .Should().BeEquivalentTo(ToolRegistry.FromType(instance.GetType()).Select(t => t.Name));
    }

    [Fact]
    public void FromInstance_still_reports_static_tools_declared_on_the_type()
    {
        var tools = ToolRegistry.FromInstance(new StaticTools());

        tools.Should().ContainSingle(
            "PolyAI never invokes a tool itself - the caller dispatches ToolCall.ArgumentsJson, so a "
            + "static tool is as callable as an instance one and must stay advertised")
            .Which.Name.Should().Be("static_tool");
    }

    // ---------- defect 2: unmapped parameter types silently became "string" ----------

    [Fact]
    public void Array_parameters_are_declared_as_array()
    {
        var tool = ToolRegistry.FromType(typeof(SchemaTools)).Single(t => t.Name == "book");

        tool.Parameters.Single(p => p.Name == "tags").JsonSchemaType.Should().Be(
            "array", "a string[] told to the model as \"string\" makes it emit \"a, b\" instead of [\"a\",\"b\"]");
    }

    [Fact]
    public void Generic_collection_parameters_are_declared_as_array()
    {
        var tool = ToolRegistry.FromType(typeof(SchemaTools)).Single(t => t.Name == "listing");

        tool.Parameters.Single(p => p.Name == "names").JsonSchemaType.Should().Be("array");
    }

    [Fact]
    public void Enum_parameters_are_declared_as_string()
    {
        var tool = ToolRegistry.FromType(typeof(SchemaTools)).Single(t => t.Name == "set_unit");

        tool.Parameters.Single(p => p.Name == "unit").JsonSchemaType.Should().Be("string");
    }

    [Fact]
    public void Primitive_parameters_are_still_mapped_as_before()
    {
        var tool = ToolRegistry.FromType(typeof(SchemaTools)).Single(t => t.Name == "book");

        tool.Parameters.Single(p => p.Name == "count").JsonSchemaType.Should().Be("integer");
        tool.Parameters.Single(p => p.Name == "when").JsonSchemaType.Should().Be("string");
    }

    [Fact]
    public void An_unrepresentable_parameter_type_is_rejected_rather_than_called_a_string()
    {
        var act = () => ToolRegistry.FromType(typeof(UnrepresentableTools));

        act.Should().Throw<PolyAIException>(
            "a tool schema that silently lies to the model is worse than a startup error")
            .WithMessage("*payload*");
    }

    // ---------- the schema detail the model actually needs ----------

    [Fact]
    public void Enum_parameters_declare_their_permitted_values()
    {
        var tool = ToolRegistry.FromType(typeof(SchemaTools)).Single(t => t.Name == "set_unit");

        tool.Parameters.Single(p => p.Name == "unit").Schema.EnumValues
            .Should().BeEquivalentTo("Celsius", "Fahrenheit");
    }

    [Fact]
    public void Array_parameters_describe_their_element_type()
    {
        var tool = ToolRegistry.FromType(typeof(SchemaTools)).Single(t => t.Name == "book");

        tool.Parameters.Single(p => p.Name == "tags").Schema.Items!.Type.Should().Be("string");
    }

    [Fact]
    public void Generic_collection_parameters_describe_their_element_type()
    {
        var tool = ToolRegistry.FromType(typeof(SchemaTools)).Single(t => t.Name == "listing");

        tool.Parameters.Single(p => p.Name == "names").Schema.Items!.Type.Should().Be("string");
    }

    [Fact]
    public void DateTime_is_annotated_with_the_date_time_format()
    {
        var tool = ToolRegistry.FromType(typeof(SchemaTools)).Single(t => t.Name == "book");

        tool.Parameters.Single(p => p.Name == "when").Schema.Format.Should().Be("date-time");
    }

    private sealed class FormatEdgeTools
    {
        [PolyAITool("Edge formats")]
        public string Edges(
            [PolyAIParam("Id")] Guid id,
            [PolyAIParam("Day")] DateOnly day,
            [PolyAIParam("Time")] TimeOnly time) => string.Empty;
    }

    [Fact]
    public void Guid_and_date_only_and_time_only_carry_no_format_keyword()
    {
        var tool = ToolRegistry.FromType(typeof(FormatEdgeTools)).Single();

        // Gemini rejects the request outright for any other string format:
        // "only 'enum' and 'date-time' are supported for STRING type". Emitting "uuid"/"date"/"time"
        // would turn a valid tool into a 400 on one of the five supported providers.
        tool.Parameters.Should().OnlyContain(p => p.Schema.Type == "string" && p.Schema.Format == null);
    }

    private sealed class NullableTools
    {
        [PolyAITool("Nullables")]
        public string Take(
            [PolyAIParam("Count")] int? count = null,
            [PolyAIParam("Unit")] Unit? unit = null) => string.Empty;
    }

    [Fact]
    public void Nullable_value_types_map_to_the_schema_of_their_underlying_type()
    {
        var tool = ToolRegistry.FromType(typeof(NullableTools)).Single();

        tool.Parameters.Single(p => p.Name == "count").JsonSchemaType.Should().Be("integer");

        var unit = tool.Parameters.Single(p => p.Name == "unit");
        unit.JsonSchemaType.Should().Be("string");
        unit.Schema.EnumValues.Should().BeEquivalentTo("Celsius", "Fahrenheit");
    }

    private sealed class NestedCollectionTools
    {
        [PolyAITool("Nested")]
        public string Take([PolyAIParam("Groups")] List<Unit[]> groups) => string.Empty;
    }

    [Fact]
    public void Nested_collections_describe_every_level()
    {
        var groups = ToolRegistry.FromType(typeof(NestedCollectionTools)).Single().Parameters.Single();

        groups.Schema.Type.Should().Be("array");
        groups.Schema.Items!.Type.Should().Be("array");
        groups.Schema.Items.Items!.EnumValues.Should().BeEquivalentTo("Celsius", "Fahrenheit");
    }

    private sealed class CancellableTools
    {
        [PolyAITool("Async tool")]
        public Task<string> Fetch(
            [PolyAIParam("City")] string city,
            CancellationToken cancellationToken = default) => Task.FromResult(city);
    }

    [Fact]
    public void A_cancellation_token_is_not_advertised_to_the_model()
    {
        var tool = ToolRegistry.FromType(typeof(CancellableTools)).Single();

        tool.Parameters.Select(p => p.Name).Should().Equal(
            ["city"],
            "a CancellationToken is supplied by the dispatching caller, never produced by the model");
    }

    private sealed class MultiDimensionalTools
    {
        [PolyAITool("Grid")]
        public string Take([PolyAIParam("Grid")] int[,] grid) => string.Empty;
    }

    [Fact]
    public void A_multi_dimensional_array_is_rejected_rather_than_flattened()
    {
        var act = () => ToolRegistry.FromType(typeof(MultiDimensionalTools));

        act.Should().Throw<PolyAIException>("int[,] is not a JSON array of int").WithMessage("*grid*");
    }

    private sealed class Tree : IEnumerable<Tree>
    {
        public IEnumerator<Tree> GetEnumerator() => Enumerable.Empty<Tree>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SelfReferentialTools
    {
        [PolyAITool("Tree")]
        public string Take([PolyAIParam("Tree")] Tree tree) => string.Empty;
    }

    [Fact]
    public void A_self_referential_collection_is_rejected_instead_of_overflowing_the_stack()
    {
        var act = () => ToolRegistry.FromType(typeof(SelfReferentialTools));

        act.Should().Throw<PolyAIException>(
            "an unbounded descent would die on an uncatchable StackOverflowException")
            .WithMessage("*tree*");
    }

    private sealed class WideNumericTools
    {
        [PolyAITool("Numbers")]
        public string Take(
            [PolyAIParam("A")] long a,
            [PolyAIParam("B")] short b,
            [PolyAIParam("C")] double c,
            [PolyAIParam("D")] decimal d) => string.Empty;
    }

    [Fact]
    public void Numeric_types_beyond_int_are_mapped_rather_than_called_strings()
    {
        var tool = ToolRegistry.FromType(typeof(WideNumericTools)).Single();

        tool.Parameters.Single(p => p.Name == "a").JsonSchemaType.Should().Be("integer");
        tool.Parameters.Single(p => p.Name == "b").JsonSchemaType.Should().Be("integer");
        tool.Parameters.Single(p => p.Name == "c").JsonSchemaType.Should().Be("number");
        tool.Parameters.Single(p => p.Name == "d").JsonSchemaType.Should().Be("number");
    }
}
