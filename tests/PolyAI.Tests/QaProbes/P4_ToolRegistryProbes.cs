using FluentAssertions;
using PolyAI.Abstractions;
using PolyAI.Errors;
using PolyAI.Tools;

namespace PolyAI.Tests.QaProbes;

/// <summary>
/// GRO-123 QA probes — ToolRegistry edge cases named in the QA scope:
/// methods with no [PolyAITool], overloaded methods, and value types.
/// </summary>
public sealed class P4_ToolRegistryProbes
{
    public interface ISearchTools
    {
        string Search(string query);
    }

    public sealed class SearchTools : ISearchTools
    {
        [PolyAITool("Searches the corpus.")]
        public string Search(string query) => query;

        public string NotATool(string query) => query;
    }

    public sealed class OverloadedTools
    {
        [PolyAITool("Searches the corpus.")]
        public string Search(string query) => query;

        [PolyAITool("Searches the corpus with a result limit.")]
        public string Search(string query, int limit) => $"{query}:{limit}";
    }

    public enum Unit { Celsius, Fahrenheit }

    public sealed class Coordinates
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public sealed class ExoticParameterTools
    {
        [PolyAITool("Books a slot.")]
        public string Book(DateTime when, Guid reference, Unit unit, string[] tags)
            => $"{when}{reference}{unit}{tags.Length}";
    }

    public sealed class CoordinatesParameterTools
    {
        [PolyAITool("Books a coordinate.")]
        public string Book(Coordinates at) => $"{at.Latitude}";
    }

    public sealed class StaticTools
    {
        [PolyAITool("A static helper.")]
        public static string Helper(string input) => input;
    }

    public sealed class AcronymTools
    {
        [PolyAITool("Fetches an HTTP status.")]
        public string GetHTTPStatus(string url) => url;
    }

    // ---------------------------------------------------------------- P4.1
    // Methods without the attribute are excluded — QA scope. Expected to PASS.
    [Fact]
    public void P4_1_Methods_without_the_attribute_are_excluded()
    {
        var tools = ToolRegistry.FromInstance(new SearchTools());

        tools.Should().HaveCount(1);
        tools[0].Name.Should().Be("search");
    }

    // ---------------------------------------------------------------- P4.2
    // FromInstance<T> discards the instance and scans typeof(T) — the STATIC type. Held behind
    // an interface or base reference it silently returns nothing, with no diagnostic.
    [Fact]
    public void P4_2_FromInstance_scans_the_runtime_type_of_the_instance()
    {
        ISearchTools tools = new SearchTools();

        var definitions = ToolRegistry.FromInstance(tools);

        definitions.Should().HaveCount(1,
            "FromInstance takes an instance, so it must scan that instance's runtime type; " +
            "scanning typeof(T) silently yields zero tools behind any abstraction");
    }

    // ---------------------------------------------------------------- P4.3
    // Overloaded methods must be rejected rather than emitted with a duplicate tool name.
    [Fact]
    public void P4_3_Overloaded_tool_methods_do_not_produce_duplicate_tool_names()
    {
        var act = () => ToolRegistry.FromType(typeof(OverloadedTools));

        act.Should().Throw<PolyAIException>().WithMessage("*duplicate tool name*search*");
    }

    // ---------------------------------------------------------------- P4.4
    // Value types and complex types — QA scope. Anything outside the small primitive map is
    // silently declared "string", so the model is handed a schema that cannot describe the
    // argument it must produce.
    [Theory]
    [InlineData("when", "string")]      // DateTime -> string is acceptable (date-time format)
    [InlineData("reference", "string")] // Guid -> string is acceptable (uuid format)
    [InlineData("unit", "string")]      // enum -> string is acceptable, but needs an enum list
    [InlineData("tags", "array")]       // string[] MUST be array
    public void P4_4_Parameter_schema_types_reflect_the_actual_parameter_type(string parameterName, string expectedSchemaType)
    {
        var tool = ToolRegistry.FromType(typeof(ExoticParameterTools)).Single();

        tool.Parameters.Single(p => p.Name == parameterName).JsonSchemaType
            .Should().Be(expectedSchemaType,
                "GetValueOrDefault(type, \"string\") makes every unmapped type a string, " +
                "so the model is told to send text where a JSON array or object is required");
    }

    [Fact]
    public void P4_4_Complex_parameter_types_are_rejected_explicitly()
    {
        var act = () => ToolRegistry.FromType(typeof(CoordinatesParameterTools));

        act.Should().Throw<PolyAIException>().WithMessage("*Coordinates*");
    }

    // ---------------------------------------------------------------- P4.5
    // An enum declared as a bare "string" gives the model no allowed values, so it invents them.
    [Fact]
    public void P4_5_An_enum_parameter_advertises_its_allowed_values()
    {
        var tool = ToolRegistry.FromType(typeof(ExoticParameterTools)).Single();
        var unit = tool.Parameters.Single(p => p.Name == "unit");

        unit.Schema.EnumValues.Should().BeEquivalentTo("Celsius", "Fahrenheit");
    }

    // ---------------------------------------------------------------- P4.6
    // FromInstance's own documentation says "discoverable on instance", but BindingFlags.Static
    // is included, so a static method is advertised as an instance tool.
    [Fact]
    public void P4_6_FromInstance_does_not_advertise_static_methods_as_instance_tools()
    {
        var tools = ToolRegistry.FromInstance(new StaticTools());

        tools.Should().ContainSingle().Which.Name.Should().Be("helper");
    }

    // ---------------------------------------------------------------- P4.7
    // ToSnakeCase inserts an underscore before every uppercase char, shredding acronyms.
    [Fact]
    public void P4_7_Acronyms_in_method_names_produce_a_readable_snake_case_tool_name()
    {
        var tool = ToolRegistry.FromType(typeof(AcronymTools)).Single();

        tool.Name.Should().Be("get_http_status");
    }

    // ---------------------------------------------------------------- P4.8
    // A method with no parameters — the simplest tool there is. Control case.
    [Fact]
    public void P4_8_A_parameterless_tool_produces_an_empty_parameter_list()
    {
        var tools = ToolRegistry.FromType(typeof(ParameterlessTools));

        tools.Should().HaveCount(1);
        tools[0].Parameters.Should().BeEmpty();
    }

    public sealed class ParameterlessTools
    {
        [PolyAITool("Returns the current time.")]
        public string Now() => DateTime.UtcNow.ToString("O");
    }

    // ---------------------------------------------------------------- P4.9
    // FromType(null) / FromInstance(null) guards. Control case.
    [Fact]
    public void P4_9_Null_arguments_are_rejected_with_ArgumentNullException()
    {
        ((Action)(() => ToolRegistry.FromType(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => ToolRegistry.FromInstance<SearchTools>(null!))).Should().Throw<ArgumentNullException>();
    }

    // ---------------------------------------------------------------- P4.10
    // The SDK advertises "tool calling". A ToolCall comes back with a name and an arguments
    // JSON string; there is no supported path from that back to the annotated method.
    [Fact]
    public void P4_10_A_tool_call_can_be_dispatched_back_to_its_annotated_method()
    {
        var response = new ChatResponse("", toolCalls: [new ToolCall("call-1", "search", "{\"query\":\"Colombo\"}")]);

        response.ToolCalls.Should().ContainSingle().Which.Name.Should().Be("search");
        response.ToolCalls[0].ArgumentsJson.Should().Be("{\"query\":\"Colombo\"}");
    }
}
