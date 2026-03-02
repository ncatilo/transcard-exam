using TransCard.Data;
using TransCard.Models.Entities;
using TransCard.Models.Responses;

namespace TransCard.Handlers.Queries;

public class GetPaymentByIdQueryHandler(PaymentsDbContext db)
    : IQueryHandler<GetPaymentByIdQuery, PaymentResponse?>
{
    public async Task<PaymentResponse?> HandleAsync(GetPaymentByIdQuery query)
    {
        var payment = await db.Payments.FindAsync(query.Id);
        return payment is null ? null : MapToResponse(payment);
    }

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
}
