using TransCard.Models.Entities;
using TransCard.Models.Responses;

namespace TransCard.Handlers.Commands;

public record ProcessPaymentResult(
    PaymentResponse Response,
    bool IsExistingPayment,
    List<PaymentEvent> PendingEvents) : IProduceEvents
{
    IReadOnlyList<PaymentEvent> IProduceEvents.PendingEvents => PendingEvents;
}
