using BoardVerse.API.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace BoardVerse.Tests.Filters;

/// <summary>
/// Unit tests cho <see cref="DeprecationHeadersAttribute"/> — Phase 4 / RFC 8594.
/// Verify response headers theo spec: Deprecation, Sunset, Link.
/// </summary>
public class DeprecationHeadersAttributeTests
{
    [Fact]
    public void OnResultExecuting_SetsAllRfc8594Headers()
    {
        var filter = new DeprecationHeadersAttribute
        {
            Sunset = "Wed, 31 Dec 2026 23:59:59 GMT",
            DocsLink = "/docs/api/booking#deprecation"
        };

        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        var resultExecutedContext = new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new OkResult(),
            controller: new object());

        filter.OnResultExecuting(resultExecutedContext);

        Assert.Equal("true", httpContext.Response.Headers["Deprecation"]);
        Assert.Equal("Wed, 31 Dec 2026 23:59:59 GMT", httpContext.Response.Headers["Sunset"]);
        Assert.Equal("</docs/api/booking#deprecation>; rel=\"deprecation\"",
            httpContext.Response.Headers["Link"]);
    }

    [Fact]
    public void OnResultExecuting_DefaultsTo2026Sunset()
    {
        var filter = new DeprecationHeadersAttribute();
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        filter.OnResultExecuting(new ResultExecutingContext(
            actionContext, new List<IFilterMetadata>(), new OkResult(), new object()));

        Assert.Equal("true", httpContext.Response.Headers["Deprecation"]);
        Assert.Equal("Wed, 31 Dec 2026 23:59:59 GMT", httpContext.Response.Headers["Sunset"]);
    }

    [Fact]
    public void AttributeUsage_AllowsBothClassAndMethod()
    {
        var usage = typeof(DeprecationHeadersAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Method));
        Assert.False(usage.AllowMultiple);
        Assert.True(usage.Inherited);
    }
}