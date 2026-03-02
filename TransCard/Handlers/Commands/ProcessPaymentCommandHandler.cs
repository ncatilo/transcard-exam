using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransCard.Data;
using TransCard.Models.Entities;
using TransCard.Models.Responses;
using TransCard.Services;

namespace TransCard.Handlers.Commands;

public class ProcessPaymentCommandHandler(
    PaymentsDbContext db,
    PaymentProcessor processor,
    ILogger<ProcessPaymentCommandHandler> logger)
    : ICommandHandler<ProcessPaymentCommand, ProcessPaymentResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<ProcessPaymentResult> HandleAsync(ProcessPaymentCommand command)
    {
        // Layer 1: Application-level idempotency check
        var existing = await db.Payments
            .FirstOrDefaultAsync(p => p.ReferenceId == command.ReferenceId);

        if (existing is not null)
        {
            logger.LogInformation(
                "Idempotent retry detected for ReferenceId {ReferenceId}", command.ReferenceId);
            return new ProcessPaymentResult(MapToResponse(existing), true, []);
        }

        // Create new payment entity
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Amount = command.Amount,
            Currency = command.Currency.ToUpperInvariant(),
            ReferenceId = command.ReferenceId,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        var events = new List<PaymentEvent>();

        try
        {
            db.Payments.Add(payment);
            events.Add(BuildEvent(payment, "PaymentCreated"));
            await db.SaveChangesAsync();

            // Simulate payment processing
            var result = processor.Process(payment.Amount, payment.Currency);
            payment.Status = result;
            payment.ProcessedAt = DateTime.UtcNow;

            events.Add(BuildEvent(payment, $"Payment{result}"));
            await db.SaveChangesAsync();

            return new ProcessPaymentResult(MapToResponse(payment), false, events);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Layer 2: Race condition — another request with same ReferenceId was processed concurrently
            var raceExisting = await db.Payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ReferenceId == command.ReferenceId);

            if (raceExisting is not null)
            {
                logger.LogInformation(
                    "Concurrent idempotent retry resolved for ReferenceId {ReferenceId}",
                    command.ReferenceId);
                return new ProcessPaymentResult(MapToResponse(raceExisting), true, []);
            }

            throw;
        }
    }

    private static PaymentEvent BuildEvent(Payment payment, string eventType) => new()
    {
        Id = Guid.NewGuid(),
        PaymentId = payment.Id,
        EventType = eventType,
        Payload = JsonSerializer.Serialize(new
        {
            payment.Amount,
            payment.Currency,
            payment.Status,
            payment.ReferenceId
        }, JsonOptions),
        OccurredAt = DateTime.UtcNow
    };

    private static PaymentResponse MapToResponse(Payment payment) => new()
    {
        Id = payment.Id,
        Amount = payment.Amount,
        Currency = payment.Currency,
        Status = payment.Status,
        ReferenceId = payment.ReferenceId,
        CreatedAt = payment.CreatedAt,
        ProcessedAt = payment.ProcessedAt
    };

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException?.Message.Contains("UNIQUE constraint failed") == true;
    }
}
