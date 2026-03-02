using AutoFixture;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TransCard.Data;
using TransCard.Handlers.Queries;
using TransCard.Models.Entities;

namespace TransCard.Tests.Handlers.Queries;

[TestFixture]
public class GetPaymentEventsQueryHandlerTests
{
    private IFixture _fixture = null!;
    private SqliteConnection _connection = null!;
    private PaymentsDbContext _db = null!;
    private GetPaymentEventsQueryHandler _handler = null!;

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

        _handler = new GetPaymentEventsQueryHandler(_db);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Guid SeedPaymentWithEvents(int eventCount = 2)
    {
        var paymentId = _fixture.Create<Guid>();
        var payment = new Payment
        {
            Id = paymentId,
            Amount = _fixture.Create<decimal>(),
            Currency = "USD",
            Status = "Completed",
            ReferenceId = _fixture.Create<string>(),
            CreatedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        };
        _db.Payments.Add(payment);

        for (var i = 0; i < eventCount; i++)
        {
            _db.PaymentEvents.Add(new PaymentEvent
            {
                Id = _fixture.Create<Guid>(),
                PaymentId = paymentId,
                EventType = i == 0 ? "PaymentCreated" : "PaymentCompleted",
                Payload = $"{{\"index\":{i}}}",
                OccurredAt = DateTime.UtcNow.AddSeconds(i)
            });
        }

        _db.SaveChanges();
        return paymentId;
    }

    [Test]
    public async Task When_PaymentHasEvents_Then_ReturnsAllEvents()
    {
        var paymentId = SeedPaymentWithEvents(2);

        var result = await _handler.HandleAsync(new GetPaymentEventsQuery(paymentId));

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task When_PaymentHasEvents_Then_EventsAreOrderedByOccurredAt()
    {
        var paymentId = SeedPaymentWithEvents(2);

        var result = await _handler.HandleAsync(new GetPaymentEventsQuery(paymentId));

        Assert.That(result[0].EventType, Is.EqualTo("PaymentCreated"));
        Assert.That(result[1].EventType, Is.EqualTo("PaymentCompleted"));
        Assert.That(result[0].OccurredAt, Is.LessThanOrEqualTo(result[1].OccurredAt));
    }

    [Test]
    public async Task When_PaymentHasEvents_Then_AllFieldsAreMappedCorrectly()
    {
        var paymentId = SeedPaymentWithEvents(1);
        var seededEvent = await _db.PaymentEvents.FirstAsync(e => e.PaymentId == paymentId);

        var result = await _handler.HandleAsync(new GetPaymentEventsQuery(paymentId));

        Assert.That(result[0].Id, Is.EqualTo(seededEvent.Id));
        Assert.That(result[0].PaymentId, Is.EqualTo(paymentId));
        Assert.That(result[0].EventType, Is.EqualTo(seededEvent.EventType));
        Assert.That(result[0].Payload, Is.EqualTo(seededEvent.Payload));
        Assert.That(result[0].OccurredAt, Is.EqualTo(seededEvent.OccurredAt));
    }

    [Test]
    public async Task When_PaymentHasNoEvents_Then_ReturnsEmptyList()
    {
        var paymentId = _fixture.Create<Guid>();

        var result = await _handler.HandleAsync(new GetPaymentEventsQuery(paymentId));

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task When_MultiplePaymentsExist_Then_ReturnsOnlyEventsForRequestedPayment()
    {
        var paymentId1 = SeedPaymentWithEvents(2);
        var paymentId2 = SeedPaymentWithEvents(3);

        var result = await _handler.HandleAsync(new GetPaymentEventsQuery(paymentId1));

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result.All(e => e.PaymentId == paymentId1), Is.True);
    }
}
