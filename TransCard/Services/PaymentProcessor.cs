namespace TransCard.Services;

/// <summary>
/// Simulates a payment gateway. In production, this would call Stripe/Adyen/etc.
/// </summary>
public class PaymentProcessor
{
    private readonly Random _random = new();

    /// <summary>
    /// Simulates processing a payment.
    /// Returns "Completed" (~70%), "Failed" (~20%), or "Pending" (~10%).
    /// </summary>
    public virtual string Process(decimal amount, string currency)
    {
        var roll = _random.Next(100);
        return roll switch
        {
            < 70 => "Completed",
            < 90 => "Failed",
            _ => "Pending"
        };
    }
}
