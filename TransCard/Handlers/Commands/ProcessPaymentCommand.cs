namespace TransCard.Handlers.Commands;

public record ProcessPaymentCommand(decimal Amount, string Currency, string ReferenceId);
