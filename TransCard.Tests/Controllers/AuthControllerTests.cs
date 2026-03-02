using AutoFixture;
using AutoFixture.AutoMoq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using TransCard.Controllers;
using TransCard.Models.Requests;
using TransCard.Models.Responses;
using TransCard.Services;

namespace TransCard.Tests.Controllers;

[TestFixture]
public class AuthControllerTests
{
    private IFixture _fixture = null!;
    private Mock<ITokenService> _tokenServiceMock = null!;
    private AuthController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _tokenServiceMock = _fixture.Freeze<Mock<ITokenService>>();
        _controller = new AuthController(_tokenServiceMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Test]
    public void When_ValidCredentialsProvided_Then_Returns200OkWithToken()
    {
        var request = _fixture.Create<TokenRequest>();
        var tokenResponse = _fixture.Create<TokenResponse>();

        _tokenServiceMock
            .Setup(s => s.GenerateToken(request.ClientId, request.ClientSecret))
            .Returns(tokenResponse);

        var result = _controller.GetToken(request);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));
        Assert.That(okResult.Value, Is.EqualTo(tokenResponse));
    }

    [Test]
    public void When_InvalidCredentialsProvided_Then_Returns401WithAuthError()
    {
        var request = _fixture.Create<TokenRequest>();

        _tokenServiceMock
            .Setup(s => s.GenerateToken(request.ClientId, request.ClientSecret))
            .Returns((TokenResponse?)null);

        var result = _controller.GetToken(request);

        var unauthorizedResult = result as UnauthorizedObjectResult;
        Assert.That(unauthorizedResult, Is.Not.Null);
        Assert.That(unauthorizedResult!.StatusCode, Is.EqualTo(401));

        var error = unauthorizedResult.Value as ApiErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Type, Is.EqualTo("AuthenticationError"));
        Assert.That(error.Title, Is.EqualTo("Invalid credentials."));
        Assert.That(error.Status, Is.EqualTo(401));
        Assert.That(error.Detail, Is.EqualTo("The provided client ID or secret is invalid."));
    }

    [Test]
    public void When_AuthenticationFails_Then_TraceIdIsIncludedInError()
    {
        var traceId = _fixture.Create<string>();
        _controller.HttpContext.TraceIdentifier = traceId;
        var request = _fixture.Create<TokenRequest>();

        _tokenServiceMock
            .Setup(s => s.GenerateToken(request.ClientId, request.ClientSecret))
            .Returns((TokenResponse?)null);

        var result = _controller.GetToken(request);

        var unauthorizedResult = result as UnauthorizedObjectResult;
        var error = unauthorizedResult!.Value as ApiErrorResponse;
        Assert.That(error!.TraceId, Is.EqualTo(traceId));
    }

    [Test]
    public void When_TokenRequested_Then_ServiceReceivesExactCredentials()
    {
        var request = _fixture.Create<TokenRequest>();
        var tokenResponse = _fixture.Create<TokenResponse>();

        _tokenServiceMock
            .Setup(s => s.GenerateToken(request.ClientId, request.ClientSecret))
            .Returns(tokenResponse);

        _controller.GetToken(request);

        _tokenServiceMock.Verify(
            s => s.GenerateToken(request.ClientId, request.ClientSecret), Times.Once);
    }

    [Test]
    public void When_ValidCredentialsProvided_Then_ResponseContainsTokenAndExpiry()
    {
        var request = _fixture.Create<TokenRequest>();
        var tokenResponse = _fixture.Create<TokenResponse>();

        _tokenServiceMock
            .Setup(s => s.GenerateToken(request.ClientId, request.ClientSecret))
            .Returns(tokenResponse);

        var result = _controller.GetToken(request);

        var okResult = result as OkObjectResult;
        var token = okResult!.Value as TokenResponse;
        Assert.That(token, Is.Not.Null);
        Assert.That(token!.Token, Is.EqualTo(tokenResponse.Token));
        Assert.That(token.ExpiresAt, Is.EqualTo(tokenResponse.ExpiresAt));
    }
}
