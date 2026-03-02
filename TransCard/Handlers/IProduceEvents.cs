using TransCard.Models.Entities;

namespace TransCard.Handlers;

public interface IProduceEvents
{
    IReadOnlyList<PaymentEvent> PendingEvents { get; }
}
