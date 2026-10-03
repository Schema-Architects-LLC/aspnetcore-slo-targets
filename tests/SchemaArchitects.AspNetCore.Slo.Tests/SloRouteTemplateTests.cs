namespace SchemaArchitects.AspNetCore.Slo.Tests;

public class SloRouteTemplateTests
{
    [Theory]
    [InlineData("/api/orders/{id}", "/api/orders/{id}")]          // already canonical
    [InlineData("api/Orders/{id:int}", "/api/Orders/{id}")]       // controller route: no leading slash + constraint
    [InlineData("/api/orders/{id:int:min(1)}", "/api/orders/{id}")] // multiple constraints
    [InlineData("/api/orders/{id?}", "/api/orders/{id}")]         // optional parameter
    [InlineData("/api/reports/{page=1}", "/api/reports/{page}")]  // default value
    [InlineData("/files/{**path}", "/files/{path}")]              // catch-all
    [InlineData("  /api/orders/  ", "/api/orders")]               // whitespace + trailing slash
    [InlineData("/", "/")]
    public void Normalize_ProducesCanonicalRoute(string input, string expected)
    {
        Assert.Equal(expected, SloRouteTemplate.Normalize(input));
    }
}
