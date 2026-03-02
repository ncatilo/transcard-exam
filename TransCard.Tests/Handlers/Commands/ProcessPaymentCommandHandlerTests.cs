using System.Text.Json;
using AutoFixture;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TransCard.Data;
using TransCard.Handlers.Commands;
using TransCard.Services;

namespace TransCard.Tests.Handlers.Commands;

[TestFixture]
public class ProcessPaymentCommandHandlerTests
{
    private IFixture _fixture = null!;
    private SqliteConnection _connection = null!;
    private PaymentsDbContext _db = null!;
    private Mock<PaymentProcessor> _processorMock = null!;
    private Mock<ILogger<ProcessPaymentCommandHandler>> _loggerMock = null!;
    private ProcessPaymentCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture();

        // In-memory SQLite supports transactions (EF Core InMemory provider does not)
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new PaymentsDbContext(options);
        _db.Database.EnsureCreated();

        _processorMock = new Mock<PaymentProcessor>();
        _processorMock
            .Setup(p => p.Process(It.IsAny<decimal>(), It.IsAny<string>()))
            .Returns("Completed");

        _loggerMock = new Mock<ILogger<ProcessPaymentCommandHandler>>();

        _handler = new ProcessPaymentCommandHandler(_db, _processorMock.Object, _loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private ProcessPaymentCommand CreateCommand() => new(
        Amount: _fixture.Create<decimal>(),
        Currency: "USD",
        ReferenceId: _fixture.Create<string>()
    );

    #region New payment

    [Test]
    public async Task When_NewPaymentSubmitted_Then_IsExistingPaymentIsFalse()
    {
        var command = CreateCommand();

        var result = await _handler.HandleAsync(command);

        Assert.That(result.IsExistingPayment, Is.False);
    }

    [Test]
    public async Task When_NewPaymentSubmitted_Then_PaymentIsPersistedToDatabase()
    {
        var command = CreateCommand();

        await _handler.HandleAsync(command);

        var persisted = await _db.Payments.FirstOrDefaultAsync(p => p.ReferenceId == command.ReferenceId);
        Assert.That(persisted, Is.Not.Null);
    }

    [Test]
    public async Task When_NewPaymentSubmitted_Then_ResponseMapsAllFields()
    {
        var command = CreateCommand();

        var result = await _handler.HandleAsync(command);

        Assert.That(result.Response.Amount, Is.EqualTo(command.Amount));
        Assert.That(result.Response.Currency, Is.EqualTo(command.Currency.ToUpperInvariant()));
        Assert.That(result.Response.ReferenceId, Is.EqualTo(command.ReferenceId));
        Assert.That(result.Response.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(result.Response.CreatedAt, Is.Not.EqualTo(default(DateTime)));
        Assert.That(result.Response.ProcessedAt, Is.Not.Null);
    }

    [Test]
    public async Task When_NewPaymentSubmitted_Then_CurrencyIsStoredUppercase()
    {
        var command = new ProcessPaymentCommand(
            _fixture.Create<decimal>(), "eur", _fixture.Create<string>());

        var result = await _handler.HandleAsync(command);

        Assert.That(result.Response.Currency, Is.EqualTo("EUR"));
    }

    [Test]
    public async Task When_ProcessorReturnsCompleted_Then_StatusIsCompleted()
    {
        _processorMock
            .Setup(p => p.Process(It.IsAny<decimal>(), It.IsAny<string>()))
            .Returns("Completed");

        var result = await _handler.HandleAsync(CreateCommand());

        Assert.That(result.Response.Status, Is.EqualTo("Completed"));
    }

    [Test]
    public async Task When_ProcessorReturnsFailed_Then_StatusIsFailed()
    {
        _processorMock
            .Setup(p => p.Process(It.IsAny<decimal>(), It.IsAny<string>()))
            .Returns("Failed");

        var result = await _handler.HandleAsync(CreateCommand());

        Assert.That(result.Response.Status, Is.EqualTo("Failed"));
    }

    [Test]
    public async Task When_ProcessorReturnsPending_Then_StatusIsPending()
    {
        _processorMock
            .Setup(p => p.Process(It.IsAny<decimal>(), It.IsAny<string>()))
            .Returns("Pending");

        var result = await _handler.HandleAsync(CreateCommand());

        Assert.That(result.Response.Status, Is.EqualTo("Pending"));
    }

    [Test]
    public async Task When_NewPaymentSubmitted_Then_ProcessorReceivesAmountAndCurrency()
    {
        var command = CreateCommand();

        await _handler.HandleAsync(command);

        _processorMock.Verify(
            p => p.Process(command.Amount, command.Currency.ToUpperInvariant()), Times.Once);
    }

    [Test]
    public async Task When_NewPaymentSubmitted_Then_ProcessedAtIsSet()
    {
        var before = DateTime.UtcNow;

        var result = await _handler.HandleAsync(CreateCommand());

        Assert.That(result.Response.ProcessedAt, Is.Not.Null);
        Assert.That(result.Response.ProcessedAt, Is.GreaterThanOrEqualTo(before));
    }

    [Test]
    public async Task When_NewPaymentSubmitted_Then_CreatedAtIsSet()
    {
        var before = DateTime.UtcNow;

        var result = await _handler.HandleAsync(CreateCommand());

        Assert.That(result.Response.CreatedAt, Is.GreaterThanOrEqualTo(before));
    }

    #endregion

    #region Idempotency

    [Test]
    public async Task When_DuplicateReferenceIdSubmitted_Then_IsExistingPaymentIsTrue()
    {
        var command = CreateCommand();
        await _handler.HandleAsync(command);

        var result = await _handler.HandleAsync(command);

        Assert.That(result.IsExistingPayment, Is.True);
    }

    [Test]
    public async Task When_DuplicateReferenceIdSubmitted_Then_OriginalPaymentIsReturned()
    {
        var command = CreateCommand();
        var original = await _handler.HandleAsync(command);

        var retry = await _handler.HandleAsync(command);

        Assert.That(retry.Response.Id, Is.EqualTo(original.Response.Id));
        Assert.That(retry.Response.Amount, Is.EqualTo(original.Response.Amount));
        Assert.That(retry.Response.Status, Is.EqualTo(original.Response.Status));
    }

    [Test]
    public async Task When_DuplicateReferenceIdSubmitted_Then_ProcessorIsNotCalledAgain()
    {
        var command = CreateCommand();
        await _handler.HandleAsync(command);

        _processorMock.Invocations.Clear();
        await _handler.HandleAsync(command);

        _processorMock.Verify(
            p => p.Process(It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task When_DuplicateReferenceIdSubmitted_Then_NoDuplicateRecordCreated()
    {
        var command = CreateCommand();
        await _handler.HandleAsync(command);
        await _handler.HandleAsync(command);

        var count = await _db.Payments.CountAsync(p => p.ReferenceId == command.ReferenceId);
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task When_DifferentReferenceIds_Then_BothPaymentsAreCreated()
    {
        var command1 = CreateCommand();
        var command2 = CreateCommand();

        await _handler.HandleAsync(command1);
        await _handler.HandleAsync(command2);

        var count = await _db.Payments.CountAsync();
        Assert.That(count, Is.EqualTo(2));
    }

    #endregion

    #region Transaction and atomicity

    [Test]
    public async Task When_PaymentProcessed_Then_EntityStatusMatchesProcessorResult()
    {
        _processorMock
            .Setup(p => p.Process(It.IsAny<decimal>(), It.IsAny<string>()))
            .Returns("Failed");

        var command = CreateCommand();
        await _handler.HandleAsync(command);

        var persisted = await _db.Payments.FirstAsync(p => p.ReferenceId == command.ReferenceId);
        Assert.That(persisted.Status, Is.EqualTo("Failed"));
        Assert.That(persisted.ProcessedAt, Is.Not.Null);
    }

    #endregion

    #region Event sourcing (PendingEvents on result)

    [Test]
    public async Task When_NewPaymentSubmitted_Then_TwoPendingEventsAreReturned()
    {
        var result = await _handler.HandleAsync(CreateCommand());

        Assert.That(result.PendingEvents, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task When_NewPaymentSubmitted_Then_FirstPendingEventIsPaymentCreated()
    {
        var result = await _handler.HandleAsync(CreateCommand());

        Assert.That(result.PendingEvents[0].EventType, Is.EqualTo("PaymentCreated"));
    }

    [Test]
    public async Task When_ProcessorReturnsCompleted_Then_SecondPendingEventIsPaymentCompleted()
    {
        _processorMock
            .Setup(p => p.Process(It.IsAny<decimal>(), It.IsAny<string>()))
            .Returns("Completed");

        var result = await _handler.HandleAsync(CreateCommand());

        Assert.That(result.PendingEvents[1].EventType, Is.EqualTo("PaymentCompleted"));
    }

    [Test]
    public async Task When_ProcessorReturnsFailed_Then_SecondPendingEventIsPaymentFailed()
    {
        _processorMock
            .Setup(p => p.Process(It.IsAny<decimal>(), It.IsAny<string>()))
            .Returns("Failed");

        var result = await _handler.HandleAsync(CreateCommand());

        Assert.That(result.PendingEvents[1].EventType, Is.EqualTo("PaymentFailed"));
    }

    [Test]
    public async Task When_NewPaymentSubmitted_Then_PendingEventPayloadContainsPaymentData()
    {
        var command = CreateCommand();

        var result = await _handler.HandleAsync(command);

        var payload = JsonDocument.Parse(result.PendingEvents[0].Payload);
        Assert.That(payload.RootElement.GetProperty("amount").GetDecimal(), Is.EqualTo(command.Amount));
        Assert.That(payload.RootElement.GetProperty("currency").GetString(), Is.EqualTo(command.Currency.ToUpperInvariant()));
        Assert.That(payload.RootElement.GetProperty("referenceId").GetString(), Is.EqualTo(command.ReferenceId));
    }

    [Test]
    public async Task When_DuplicateReferenceIdSubmitted_Then_NoPendingEventsReturned()
    {
        var command = CreateCommand();
        await _handler.HandleAsync(command);

        var retry = await _handler.HandleAsync(command);

        Assert.That(retry.PendingEvents, Is.Empty);
    }

    #endregion
}
