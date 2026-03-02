using AutoFixture;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using TransCard.Data;
using TransCard.Handlers;
using TransCard.Handlers.Commands;
using TransCard.Models.Entities;
using TransCard.Models.Responses;

namespace TransCard.Tests.Handlers;

[TestFixture]
public class EventSourcingDecoratorTests
{
    private IFixture _fixture = null!;
    private SqliteConnection _connection = null!;
    private PaymentsDbContext _db = null!;
    private Mock<ICommandHandler<ProcessPaymentCommand, ProcessPaymentResult>> _innerHandlerMock = null!;
    private EventSourcingDecorator<ProcessPaymentCommand, ProcessPaymentResult> _decorator = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture();

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new PaymentsDbContext(options);
        _db.Database.EnsureCreated();

        _innerHandlerMock = new Mock<ICommandHandler<ProcessPaymentCommand, ProcessPaymentResult>>();
        _decorator = new EventSourcingDecorator<ProcessPaymentCommand, ProcessPaymentResult>(
            _innerHandlerMock.Object, _db);
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

    [Test]
    public async Task When_InnerHandlerReturnsPendingEvents_Then_EventsArePersistedToDatabase()
    {
        var paymentId = _fixture.Create<Guid>();
        var events = new List<PaymentEvent>
        {
            new() { Id = Guid.NewGuid(), PaymentId = paymentId, EventType = "PaymentCreated", Payload = "{}", OccurredAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), PaymentId = paymentId, EventType = "PaymentCompleted", Payload = "{}", OccurredAt = DateTime.UtcNow }
        };
        var result = new ProcessPaymentResult(new PaymentResponse { Id = paymentId }, false, events);

        _innerHandlerMock
            .Setup(h => h.HandleAsync(It.IsAny<ProcessPaymentCommand>()))
            .ReturnsAsync(result);

        await _decorator.HandleAsync(CreateCommand());

        var persisted = await _db.PaymentEvents.Where(e => e.PaymentId == paymentId).ToListAsync();
        Assert.That(persisted, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task When_InnerHandlerReturnsNoPendingEvents_Then_NoEventsArePersistedToDatabase()
    {
        var result = new ProcessPaymentResult(new PaymentResponse(), true, []);

        _innerHandlerMock
            .Setup(h => h.HandleAsync(It.IsAny<ProcessPaymentCommand>()))
            .ReturnsAsync(result);

        await _decorator.HandleAsync(CreateCommand());

        var count = await _db.PaymentEvents.CountAsync();
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task When_InnerHandlerThrows_Then_NoEventsArePersistedToDatabase()
    {
        _innerHandlerMock
            .Setup(h => h.HandleAsync(It.IsAny<ProcessPaymentCommand>()))
            .ThrowsAsync(new InvalidOperationException("Simulated failure"));

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _decorator.HandleAsync(CreateCommand()));

        var count = await _db.PaymentEvents.CountAsync();
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task When_DecoratorCompletes_Then_InnerHandlerResultIsReturned()
    {
        var expectedResponse = new PaymentResponse { Id = _fixture.Create<Guid>() };
        var result = new ProcessPaymentResult(expectedResponse, false, []);

        _innerHandlerMock
            .Setup(h => h.HandleAsync(It.IsAny<ProcessPaymentCommand>()))
            .ReturnsAsync(result);

        var actual = await _decorator.HandleAsync(CreateCommand());

        Assert.That(actual.Response.Id, Is.EqualTo(expectedResponse.Id));
        Assert.That(actual.IsExistingPayment, Is.False);
    }

    [Test]
    public async Task When_DecoratorCalled_Then_InnerHandlerReceivesCommand()
    {
        var command = CreateCommand();
        var result = new ProcessPaymentResult(new PaymentResponse(), false, []);

        _innerHandlerMock
            .Setup(h => h.HandleAsync(It.IsAny<ProcessPaymentCommand>()))
            .ReturnsAsync(result);

        await _decorator.HandleAsync(command);

        _innerHandlerMock.Verify(
            h => h.HandleAsync(It.Is<ProcessPaymentCommand>(c =>
                c.Amount == command.Amount &&
                c.Currency == command.Currency &&
                c.ReferenceId == command.ReferenceId)), Times.Once);
    }
}
