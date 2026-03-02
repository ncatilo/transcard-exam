using Microsoft.EntityFrameworkCore;
using TransCard.Data;
using TransCard.Models.Responses;

namespace TransCard.Handlers.Queries;

public class GetPaymentEventsQueryHandler(PaymentsDbContext db)
    : IQueryHandler<GetPaymentEventsQuery, IReadOnlyList<PaymentEventResponse>>
{
    public async Task<IReadOnlyList<PaymentEventResponse>> HandleAsync(GetPaymentEventsQuery query)
    {
        return await db.PaymentEvents
            .Where(e => e.PaymentId == query.PaymentId)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new PaymentEventResponse
            {
                Id = e.Id,
                PaymentId = e.PaymentId,
                EventType = e.EventType,
                Payload = e.Payload,
                OccurredAt = e.OccurredAt
            })
            .ToListAsync();
    }
}
