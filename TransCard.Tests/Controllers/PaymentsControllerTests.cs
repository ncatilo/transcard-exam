using AutoFixture;
using AutoFixture.AutoMoq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using TransCard.Controllers;
using TransCard.Handlers;
using TransCard.Handlers.Commands;
using TransCard.Handlers.Queries;
using TransCard.Models.Requests;
using TransCard.Models.Responses;

namespace TransCard.Tests.Controllers;

[TestFixture]
public class PaymentsControllerTests
{
    private IFixture _fixture = null!;
    private Mock<ICommandHandler<ProcessPaymentCommand, ProcessPaymentResult>> _commandHandlerMock = null!;
    private Mock<IQueryHandler<GetPaymentByIdQuery, PaymentResponse?>> _queryHandlerMock = null!;
    private Mock<IQueryHandler<GetPaymentEventsQuery, IReadOnlyList<PaymentEventResponse>>> _eventsHandlerMock = null!;
    private Mock<ILogger<PaymentsController>> _loggerMock = null!;
    private PaymentsController _controller = null!;

    // Valid currencies matching the controller's supported set
    private static readonly string[] ValidCurrencies =
        ["USD", "EUR", "GBP", "JPY", "CAD", "AUD", "CHF", "CNY", "SEK", "NZD"];

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _commandHandlerMock = _fixture.Freeze<Mock<ICommandHandler<ProcessPaymentCommand, ProcessPaymentResult>>>();
        _queryHandlerMock = _fixture.Freeze<Mock<IQueryHandler<GetPaymentByIdQuery, PaymentResponse?>>>();
        _eventsHandlerMock = _fixture.Freeze<Mock<IQueryHandler<GetPaymentEventsQuery, IReadOnlyList<PaymentEventResponse>>>>();
        _loggerMock = _fixture.Freeze<Mock<ILogger<PaymentsController>>>();
        _controller = new PaymentsController(
            _commandHandlerMock.Object,
            _queryHandlerMock.Object,
            _eventsHandlerMock.Object,
            _loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private ProcessPaymentRequest CreateValidRequest()
    {
        return new ProcessPaymentRequest
        {
            Amount = _fixture.Create<decimal>(),
            Currency = ValidCurrencies[Random.Shared.Next(ValidCurrencies.Length)],
            ReferenceId = _fixture.Create<string>()
        };
    }

    private PaymentResponse CreatePaymentResponse(ProcessPaymentRequest? fromRequest = null) =>
        _fixture.Build<PaymentResponse>()
            .With(p => p.Currency, fromRequest?.Currency ?? ValidCurrencies[Random.Shared.Next(ValidCurrencies.Length)])
            .With(p => p.ReferenceId, fromRequest?.ReferenceId ?? _fixture.Create<string>())
            .With(p => p.Amount, fromRequest?.Amount ?? _fixture.Create<decimal>())
            .Create();

    #region ProcessPayment

    [Test]
    public async Task When_ValidPaymentSubmitted_Then_Returns201Created()
    {
        var request = CreateValidRequest();
        var paymentResponse = CreatePaymentResponse(request);

        _commandHandlerMock
            .Setup(h => h.HandleAsync(It.Is<ProcessPaymentCommand>(c =>
                c.Amount == request.Amount &&
                c.Currency == request.Currency &&
                c.ReferenceId == request.ReferenceId)))
            .ReturnsAsync(new ProcessPaymentResult(paymentResponse, false, []));

        var result = await _controller.ProcessPayment(request);

        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult, Is.Not.Null);
        Assert.That(createdResult!.StatusCode, Is.EqualTo(201));
        Assert.That(createdResult.Value, Is.EqualTo(paymentResponse));
        Assert.That(createdResult.ActionName, Is.EqualTo(nameof(PaymentsController.GetPayment)));
    }

    [Test]
    public async Task When_DuplicateReferenceIdSubmitted_Then_Returns200OkWithOriginalPayment()
    {
        var request = CreateValidRequest();
        var paymentResponse = CreatePaymentResponse(request);

        _commandHandlerMock
            .Setup(h => h.HandleAsync(It.Is<ProcessPaymentCommand>(c =>
                c.ReferenceId == request.ReferenceId)))
            .ReturnsAsync(new ProcessPaymentResult(paymentResponse, true, []));

        var result = await _controller.ProcessPayment(request);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));
        Assert.That(okResult.Value, Is.EqualTo(paymentResponse));
    }

    [Test]
    public async Task When_UnsupportedCurrencyProvided_Then_Returns400WithValidationError()
    {
        var unsupportedCurrency = _fixture.Create<string>()[..3].ToUpperInvariant();
        // Ensure it's not accidentally valid
        while (ValidCurrencies.Contains(unsupportedCurrency))
            unsupportedCurrency = _fixture.Create<string>()[..3].ToUpperInvariant();

        var request = new ProcessPaymentRequest
        {
            Amount = _fixture.Create<decimal>(),
            Currency = unsupportedCurrency,
            ReferenceId = _fixture.Create<string>()
        };

        var result = await _controller.ProcessPayment(request);

        var badResult = result as BadRequestObjectResult;
        Assert.That(badResult, Is.Not.Null);
        Assert.That(badResult!.StatusCode, Is.EqualTo(400));

        var error = badResult.Value as ApiErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Type, Is.EqualTo("ValidationError"));
        Assert.That(error.Title, Is.EqualTo("Invalid currency code."));
        Assert.That(error.Status, Is.EqualTo(400));
        Assert.That(error.Detail, Does.Contain(unsupportedCurrency));
        Assert.That(error.Detail, Does.Contain("not a supported currency"));
    }

    [Test]
    public async Task When_UnsupportedCurrencyProvided_Then_HandlerIsNotCalled()
    {
        var request = new ProcessPaymentRequest
        {
            Amount = _fixture.Create<decimal>(),
            Currency = _fixture.Create<string>(),
            ReferenceId = _fixture.Create<string>()
        };

        await _controller.ProcessPayment(request);

        _commandHandlerMock.Verify(
            h => h.HandleAsync(It.IsAny<ProcessPaymentCommand>()), Times.Never);
    }

    [TestCase("usd")]
    [TestCase("Usd")]
    [TestCase("USD")]
    public async Task When_CurrencyProvidedInDifferentCase_Then_IsAccepted(string currency)
    {
        var request = new ProcessPaymentRequest
        {
            Amount = _fixture.Create<decimal>(),
            Currency = currency,
            ReferenceId = _fixture.Create<string>()
        };

        _commandHandlerMock
            .Setup(h => h.HandleAsync(It.IsAny<ProcessPaymentCommand>()))
            .ReturnsAsync(new ProcessPaymentResult(CreatePaymentResponse(request), false, []));

        var result = await _controller.ProcessPayment(request);

        Assert.That(result, Is.InstanceOf<CreatedAtActionResult>());
    }

    [TestCase("EUR")]
    [TestCase("GBP")]
    [TestCase("JPY")]
    [TestCase("CAD")]
    [TestCase("AUD")]
    [TestCase("CHF")]
    [TestCase("CNY")]
    [TestCase("SEK")]
    [TestCase("NZD")]
    public async Task When_SupportedCurrencyProvided_Then_PaymentIsAccepted(string currency)
    {
        var request = new ProcessPaymentRequest
        {
            Amount = _fixture.Create<decimal>(),
            Currency = currency,
            ReferenceId = _fixture.Create<string>()
        };

        _commandHandlerMock
            .Setup(h => h.HandleAsync(It.IsAny<ProcessPaymentCommand>()))
            .ReturnsAsync(new ProcessPaymentResult(new PaymentResponse { Currency = currency }, false, []));

        var result = await _controller.ProcessPayment(request);

        Assert.That(result, Is.InstanceOf<CreatedAtActionResult>());
    }

    [Test]
    public async Task When_UnsupportedCurrencyProvided_Then_ErrorListsAllSupportedCurrencies()
    {
        var request = new ProcessPaymentRequest
        {
            Amount = _fixture.Create<decimal>(),
            Currency = _fixture.Create<string>(),
            ReferenceId = _fixture.Create<string>()
        };

        var result = await _controller.ProcessPayment(request);

        var badResult = result as BadRequestObjectResult;
        var error = badResult!.Value as ApiErrorResponse;
        foreach (var currency in ValidCurrencies)
        {
            Assert.That(error!.Detail, Does.Contain(currency));
        }
    }

    [Test]
    public async Task When_ValidationFails_Then_TraceIdIsIncludedInError()
    {
        var traceId = _fixture.Create<string>();
        _controller.HttpContext.TraceIdentifier = traceId;
        var request = new ProcessPaymentRequest
        {
            Amount = _fixture.Create<decimal>(),
            Currency = _fixture.Create<string>(),
            ReferenceId = _fixture.Create<string>()
        };

        var result = await _controller.ProcessPayment(request);

        var badResult = result as BadRequestObjectResult;
        var error = badResult!.Value as ApiErrorResponse;
        Assert.That(error!.TraceId, Is.EqualTo(traceId));
    }

    [Test]
    public async Task When_NewPaymentCreated_Then_LocationHeaderContainsPaymentId()
    {
        var expectedId = _fixture.Create<Guid>();
        var request = CreateValidRequest();

        _commandHandlerMock
            .Setup(h => h.HandleAsync(It.IsAny<ProcessPaymentCommand>()))
            .ReturnsAsync(new ProcessPaymentResult(new PaymentResponse { Id = expectedId }, false, []));

        var result = await _controller.ProcessPayment(request);

        var createdResult = result as CreatedAtActionResult;
        Assert.That(createdResult!.RouteValues!["id"], Is.EqualTo(expectedId));
    }

    #endregion

    #region GetPayment

    [Test]
    public async Task When_PaymentExists_Then_Returns200OkWithPayment()
    {
        var paymentId = _fixture.Create<Guid>();
        var paymentResponse = _fixture.Build<PaymentResponse>()
            .With(p => p.Id, paymentId)
            .Create();

        _queryHandlerMock
            .Setup(h => h.HandleAsync(It.Is<GetPaymentByIdQuery>(q => q.Id == paymentId)))
            .ReturnsAsync(paymentResponse);

        var result = await _controller.GetPayment(paymentId);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));
        Assert.That(okResult.Value, Is.EqualTo(paymentResponse));
    }

    [Test]
    public async Task When_PaymentDoesNotExist_Then_Returns404WithErrorDetail()
    {
        var paymentId = _fixture.Create<Guid>();

        _queryHandlerMock
            .Setup(h => h.HandleAsync(It.Is<GetPaymentByIdQuery>(q => q.Id == paymentId)))
            .ReturnsAsync((PaymentResponse?)null);

        var result = await _controller.GetPayment(paymentId);

        var notFoundResult = result as NotFoundObjectResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult!.StatusCode, Is.EqualTo(404));

        var error = notFoundResult.Value as ApiErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Type, Is.EqualTo("NotFound"));
        Assert.That(error.Title, Is.EqualTo("Payment not found."));
        Assert.That(error.Status, Is.EqualTo(404));
        Assert.That(error.Detail, Does.Contain(paymentId.ToString()));
    }

    [Test]
    public async Task When_PaymentNotFound_Then_TraceIdIsIncludedInError()
    {
        var traceId = _fixture.Create<string>();
        _controller.HttpContext.TraceIdentifier = traceId;
        var paymentId = _fixture.Create<Guid>();

        _queryHandlerMock
            .Setup(h => h.HandleAsync(It.Is<GetPaymentByIdQuery>(q => q.Id == paymentId)))
            .ReturnsAsync((PaymentResponse?)null);

        var result = await _controller.GetPayment(paymentId);

        var notFoundResult = result as NotFoundObjectResult;
        var error = notFoundResult!.Value as ApiErrorResponse;
        Assert.That(error!.TraceId, Is.EqualTo(traceId));
    }

    [Test]
    public async Task When_GetPaymentCalled_Then_HandlerReceivesCorrectId()
    {
        var paymentId = _fixture.Create<Guid>();

        _queryHandlerMock
            .Setup(h => h.HandleAsync(It.Is<GetPaymentByIdQuery>(q => q.Id == paymentId)))
            .ReturnsAsync(_fixture.Build<PaymentResponse>().With(p => p.Id, paymentId).Create());

        await _controller.GetPayment(paymentId);

        _queryHandlerMock.Verify(
            h => h.HandleAsync(It.Is<GetPaymentByIdQuery>(q => q.Id == paymentId)), Times.Once);
    }

    #endregion

    #region GetPaymentEvents

    [Test]
    public async Task When_PaymentExistsAndHasEvents_Then_Returns200OkWithEvents()
    {
        var paymentId = _fixture.Create<Guid>();
        var paymentResponse = _fixture.Build<PaymentResponse>()
            .With(p => p.Id, paymentId)
            .Create();
        var events = new List<PaymentEventResponse>
        {
            new() { Id = _fixture.Create<Guid>(), PaymentId = paymentId, EventType = "PaymentCreated", Payload = "{}", OccurredAt = DateTime.UtcNow },
            new() { Id = _fixture.Create<Guid>(), PaymentId = paymentId, EventType = "PaymentCompleted", Payload = "{}", OccurredAt = DateTime.UtcNow }
        };

        _queryHandlerMock
            .Setup(h => h.HandleAsync(It.Is<GetPaymentByIdQuery>(q => q.Id == paymentId)))
            .ReturnsAsync(paymentResponse);
        _eventsHandlerMock
            .Setup(h => h.HandleAsync(It.Is<GetPaymentEventsQuery>(q => q.PaymentId == paymentId)))
            .ReturnsAsync(events);

        var result = await _controller.GetPaymentEvents(paymentId);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult!.StatusCode, Is.EqualTo(200));
        Assert.That(okResult.Value, Is.EqualTo(events));
    }

    [Test]
    public async Task When_PaymentDoesNotExistForEvents_Then_Returns404WithErrorDetail()
    {
        var paymentId = _fixture.Create<Guid>();

        _queryHandlerMock
            .Setup(h => h.HandleAsync(It.Is<GetPaymentByIdQuery>(q => q.Id == paymentId)))
            .ReturnsAsync((PaymentResponse?)null);

        var result = await _controller.GetPaymentEvents(paymentId);

        var notFoundResult = result as NotFoundObjectResult;
        Assert.That(notFoundResult, Is.Not.Null);
        Assert.That(notFoundResult!.StatusCode, Is.EqualTo(404));

        var error = notFoundResult.Value as ApiErrorResponse;
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Type, Is.EqualTo("NotFound"));
        Assert.That(error.Detail, Does.Contain(paymentId.ToString()));
    }

    [Test]
    public async Task When_PaymentNotFoundForEvents_Then_TraceIdIsIncludedInError()
    {
        var traceId = _fixture.Create<string>();
        _controller.HttpContext.TraceIdentifier = traceId;
        var paymentId = _fixture.Create<Guid>();

        _queryHandlerMock
            .Setup(h => h.HandleAsync(It.Is<GetPaymentByIdQuery>(q => q.Id == paymentId)))
            .ReturnsAsync((PaymentResponse?)null);

        var result = await _controller.GetPaymentEvents(paymentId);

        var notFoundResult = result as NotFoundObjectResult;
        var error = notFoundResult!.Value as ApiErrorResponse;
        Assert.That(error!.TraceId, Is.EqualTo(traceId));
    }

    [Test]
    public async Task When_PaymentNotFoundForEvents_Then_EventsHandlerIsNotCalled()
    {
        var paymentId = _fixture.Create<Guid>();

        _queryHandlerMock
            .Setup(h => h.HandleAsync(It.Is<GetPaymentByIdQuery>(q => q.Id == paymentId)))
            .ReturnsAsync((PaymentResponse?)null);

        await _controller.GetPaymentEvents(paymentId);

        _eventsHandlerMock.Verify(
            h => h.HandleAsync(It.IsAny<GetPaymentEventsQuery>()), Times.Never);
    }

    #endregion
}
