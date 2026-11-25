namespace PocketBase.SDK.Tests;

// Ported from the official js-sdk tests/Client.spec.ts "filter()"
public class FilterTests
{
    private const string Raw = "a > {:test1} && b = {:test2} || c = {:test2}";

    [Fact]
    public void WithoutParams_ReturnsRaw()
    {
        Assert.Equal(Raw, PocketBaseFilter.Build(Raw, (IReadOnlyDictionary<string, object?>?)null));
        Assert.Equal(Raw, PocketBaseFilter.Build(Raw, new Dictionary<string, object?>()));
    }

    [Fact]
    public void PartialParams_AndSpecialRegexChars()
    {
        var result = PocketBaseFilter.Build(Raw, new Dictionary<string, object?>
        {
            ["test0"] = "abc", // no such placeholder
            ["test2"] = "test$$",
        });

        Assert.Equal("a > {:test1} && b = \"test$$\" || c = \"test$$\"", result);
    }

    [Fact]
    public void ParamValuesMatchingPlaceholders_AreNotChained()
    {
        var result = PocketBaseFilter.Build("a > {:test1} && b = {:test2} || c = {:test3}", new Dictionary<string, object?>
        {
            ["test1"] = "{:test2}",
            ["test2"] = "{:test1}",
            ["test3"] = "{:test3}",
        });

        Assert.Equal("a > \"{:test2}\" && b = \"{:test1}\" || c = \"{:test3}\"", result);
    }

    [Fact]
    public void AllPlaceholderTypes()
    {
        var p = new Dictionary<string, object?>
        {
            ["test1"] = "a'b'c'\"\n\\",
            ["test2"] = null,
            ["test3"] = true,
            ["test4"] = false,
            ["test5"] = 123,
            ["test6"] = -123.45,
            ["test7"] = 123.45m,
            ["test8"] = new DateTimeOffset(2023, 10, 18, 10, 11, 12, TimeSpan.FromHours(3)),
            ["test9"] = new object[] { 1, 2, 3, "test'12\"3" },
            ["test10"] = new Dictionary<string, object> { ["a"] = "test'123" },
        };

        var raw = string.Join(" || ", p.Keys.Select(k => $"{k}={{:{k}}}"));

        Assert.Equal(
            "test1=\"a'b'c'\\\"\\n\\\\\" || test2=null || test3=true || test4=false || test5=123 || test6=-123.45 || test7=123.45 || test8=\"2023-10-18 07:11:12.000Z\" || test9=\"[1,2,3,\\\"test'12\\\\\\\"3\\\"]\" || test10=\"{\\\"a\\\":\\\"test'123\\\"}\"",
            PocketBaseFilter.Build(raw, p));
    }

    [Fact]
    public void AnonymousObjectParams()
    {
        Assert.Equal("title ~ \"te'st\" && n = 1.5", PocketBaseFilter.Build("title ~ {:title} && n = {:n}", new { title = "te'st", n = 1.5 }));
    }

    [Fact]
    public void Numbers_UseInvariantCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("es-ES");
            Assert.Equal("n = 1.5", PocketBaseFilter.Build("n = {:n}", new { n = 1.5 }));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
