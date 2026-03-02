using System.Text.Json;
using AutoFixture;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using TransCard.Middleware;
using TransCard.Models.Responses;

namespace TransCard.Tests.Middleware;

[TestFixture]
public class GlobalExceptionMiddlewareTests
{
    private IFixture _fixture = null!;
    private Mock<ILogger<GlobalExceptionMiddleware>> _loggerMock = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture();
        _loggerMock = new Mock<ILogger<GlobalExceptionMiddleware>>();
    }

    private static HttpContext CreateHttpContext(bool isDevelopment)
    {
        var hostEnvironmentMock = new Mock<IHostEnvironment>();
        hostEnvironmentMock
            .Setup(e => e.EnvironmentName)
            .Returns(isDevelopment ? Environments.Development : Environments.Production);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(hostEnvironmentMock.Object);
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<ApiErrorResponse?> ReadResponseBody(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<ApiErrorResponse>(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    [Test]
    public async Task When_NoExceptionOccurs_Then_RequestPassesThroughWithStatus200()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new GlobalExceptionMiddleware(next, _loggerMock.Object);
        var context = CreateHttpContext(isDevelopment: true);

        await middleware.InvokeAsync(context);

        Assert.That(nextCalled, Is.True);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public async Task When_ExceptionThrown_Then_Returns500StatusCode()
    {
        var errorMessage = _fixture.Create<string>();
        RequestDelegate next = _ => throw new InvalidOperationException(errorMessage);

        var middleware = new GlobalExceptionMiddleware(next, _loggerMock.Object);
        var context = CreateHttpContext(isDevelopment: true);

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task When_ExceptionThrown_Then_ContentTypeIsJson()
    {
        RequestDelegate next = _ => throw new Exception(_fixture.Create<string>());

        var middleware = new GlobalExceptionMiddleware(next, _loggerMock.Object);
        var context = CreateHttpContext(isDevelopment: true);

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.ContentType, Is.EqualTo("application/json"));
    }

    [Test]
    public async Task When_ExceptionThrownInDevelopment_Then_ExceptionMessageIsExposed()
    {
        var errorMessage = _fixture.Create<string>();
        RequestDelegate next = _ => throw new InvalidOperationException(errorMessage);

        var middleware = new GlobalExceptionMiddleware(next, _loggerMock.Object);
        var context = CreateHttpContext(isDevelopment: true);

        await middleware.InvokeAsync(context);

        var error = await ReadResponseBody(context);
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Detail, Is.EqualTo(errorMessage));
    }

    [Test]
    public async Task When_ExceptionThrownOutsideDevelopment_Then_ExceptionMessageIsHidden()
    {
        var errorMessage = _fixture.Create<string>();
        RequestDelegate next = _ => throw new InvalidOperationException(errorMessage);

        var middleware = new GlobalExceptionMiddleware(next, _loggerMock.Object);
        var context = CreateHttpContext(isDevelopment: false);

        await middleware.InvokeAsync(context);

        var error = await ReadResponseBody(context);
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Detail, Does.Not.Contain(errorMessage));
        Assert.That(error.Detail, Is.EqualTo("Please contact support if this persists."));
    }

    [Test]
    public async Task When_ExceptionThrown_Then_ResponseContainsCorrectErrorStructure()
    {
        RequestDelegate next = _ => throw new Exception(_fixture.Create<string>());

        var middleware = new GlobalExceptionMiddleware(next, _loggerMock.Object);
        var context = CreateHttpContext(isDevelopment: true);

        await middleware.InvokeAsync(context);

        var error = await ReadResponseBody(context);
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Type, Is.EqualTo("InternalServerError"));
        Assert.That(error.Title, Is.EqualTo("An unexpected error occurred."));
        Assert.That(error.Status, Is.EqualTo(500));
    }

    [Test]
    public async Task When_ExceptionThrown_Then_TraceIdIsIncludedInResponse()
    {
        var traceId = _fixture.Create<string>();
        RequestDelegate next = _ => throw new Exception(_fixture.Create<string>());

        var middleware = new GlobalExceptionMiddleware(next, _loggerMock.Object);
        var context = CreateHttpContext(isDevelopment: true);
        context.TraceIdentifier = traceId;

        await middleware.InvokeAsync(context);

        var error = await ReadResponseBody(context);
        Assert.That(error!.TraceId, Is.EqualTo(traceId));
    }

    [Test]
    public async Task When_ExceptionThrown_Then_ErrorIsLoggedAtErrorLevel()
    {
        RequestDelegate next = _ => throw new InvalidOperationException(_fixture.Create<string>());

        var middleware = new GlobalExceptionMiddleware(next, _loggerMock.Object);
        var context = CreateHttpContext(isDevelopment: true);

        await middleware.InvokeAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Test]
    public async Task When_DifferentExceptionTypesThrown_Then_AllAreCaughtAndReturn500()
    {
        var exceptions = new Exception[]
        {
            new ArgumentNullException(_fixture.Create<string>()),
            new NullReferenceException(_fixture.Create<string>()),
            new TimeoutException(_fixture.Create<string>()),
        };

        foreach (var ex in exceptions)
        {
            var thrownEx = ex;
            RequestDelegate next = _ => throw thrownEx;

            var middleware = new GlobalExceptionMiddleware(next, _loggerMock.Object);
            var context = CreateHttpContext(isDevelopment: true);

            await middleware.InvokeAsync(context);

            Assert.That(context.Response.StatusCode, Is.EqualTo(500),
                $"Failed for exception type {thrownEx.GetType().Name}");
        }
    }

    [Test]
    public async Task When_ExceptionThrown_Then_ResponseBodyIsValidJson()
    {
        RequestDelegate next = _ => throw new Exception(_fixture.Create<string>());

        var middleware = new GlobalExceptionMiddleware(next, _loggerMock.Object);
        var context = CreateHttpContext(isDevelopment: true);

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        Assert.DoesNotThrow(() => JsonDocument.Parse(body));
    }

    [Test]
    public async Task When_ExceptionThrown_Then_JsonPropertiesUseCamelCase()
    {
        RequestDelegate next = _ => throw new Exception(_fixture.Create<string>());

        var middleware = new GlobalExceptionMiddleware(next, _loggerMock.Object);
        var context = CreateHttpContext(isDevelopment: true);

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        Assert.That(body, Does.Contain("\"type\""));
        Assert.That(body, Does.Contain("\"title\""));
        Assert.That(body, Does.Contain("\"status\""));
        Assert.That(body, Does.Contain("\"traceId\""));
        Assert.That(body, Does.Not.Contain("\"Type\""));
        Assert.That(body, Does.Not.Contain("\"TraceId\""));
    }
}
