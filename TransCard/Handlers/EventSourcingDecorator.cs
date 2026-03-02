using TransCard.Data;

namespace TransCard.Handlers;

public class EventSourcingDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    PaymentsDbContext db)
    : ICommandHandler<TCommand, TResult>
    where TResult : IProduceEvents
{
    public async Task<TResult> HandleAsync(TCommand command)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var result = await inner.HandleAsync(command);

            if (result.PendingEvents.Count > 0)
            {
                db.PaymentEvents.AddRange(result.PendingEvents);
                await db.SaveChangesAsync();
            }

            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
