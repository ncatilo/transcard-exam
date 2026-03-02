using AutoFixture;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TransCard.Data;
using TransCard.Handlers.Queries;
using TransCard.Models.Entities;

namespace TransCard.Tests.Handlers.Queries;

[TestFixture]
public class GetPaymentByIdQueryHandlerTests
{
    private IFixture _fixture = null!;
    private SqliteConnection _connection = null!;
    private PaymentsDbContext _db = null!;
    private GetPaymentByIdQueryHandler _handler = null!;

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

        _handler = new GetPaymentByIdQueryHandler(_db);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Payment SeedPayment()
    {
        var payment = new Payment
        {
            Id = _fixture.Create<Guid>(),
            Amount = _fixture.Create<decimal>(),
            Currency = "USD",
            Status = "Completed",
            ReferenceId = _fixture.Create<string>(),
            CreatedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        };
        _db.Payments.Add(payment);
        _db.SaveChanges();
        return payment;
    }

    [Test]
    public async Task When_PaymentExists_Then_ReturnsPaymentResponse()
    {
        var payment = SeedPayment();

        var result = await _handler.HandleAsync(new GetPaymentByIdQuery(payment.Id));

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task When_PaymentExists_Then_AllFieldsAreMappedCorrectly()
    {
        var payment = SeedPayment();

        var result = await _handler.HandleAsync(new GetPaymentByIdQuery(payment.Id));

        Assert.That(result!.Id, Is.EqualTo(payment.Id));
        Assert.That(result.Amount, Is.EqualTo(payment.Amount));
        Assert.That(result.Currency, Is.EqualTo(payment.Currency));
        Assert.That(result.Status, Is.EqualTo(payment.Status));
        Assert.That(result.ReferenceId, Is.EqualTo(payment.ReferenceId));
        Assert.That(result.CreatedAt, Is.EqualTo(payment.CreatedAt));
        Assert.That(result.ProcessedAt, Is.EqualTo(payment.ProcessedAt));
    }

    [Test]
    public async Task When_PaymentDoesNotExist_Then_ReturnsNull()
    {
        var result = await _handler.HandleAsync(new GetPaymentByIdQuery(_fixture.Create<Guid>()));

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task When_MultiplePaymentsExist_Then_ReturnsCorrectOne()
    {
        var payment1 = SeedPayment();
        var payment2 = SeedPayment();

        var result = await _handler.HandleAsync(new GetPaymentByIdQuery(payment2.Id));

        Assert.That(result!.Id, Is.EqualTo(payment2.Id));
        Assert.That(result.ReferenceId, Is.EqualTo(payment2.ReferenceId));
    }

    [Test]
    public async Task When_PaymentHasNullProcessedAt_Then_ResponseReflectsNull()
    {
        var payment = new Payment
        {
            Id = _fixture.Create<Guid>(),
            Amount = _fixture.Create<decimal>(),
            Currency = "EUR",
            Status = "Pending",
            ReferenceId = _fixture.Create<string>(),
            CreatedAt = DateTime.UtcNow,
            ProcessedAt = null
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        var result = await _handler.HandleAsync(new GetPaymentByIdQuery(payment.Id));

        Assert.That(result!.ProcessedAt, Is.Null);
    }
}
