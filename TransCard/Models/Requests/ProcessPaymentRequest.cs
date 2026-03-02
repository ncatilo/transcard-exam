using System.ComponentModel.DataAnnotations;

namespace TransCard.Models.Requests;

public class ProcessPaymentRequest
{
    [Required]
    [Range(0.01, (double)decimal.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency must be a 3-letter ISO 4217 code.")]
    public string Currency { get; set; } = string.Empty;

    [Required(ErrorMessage = "ReferenceId is required for idempotency.")]
    public string ReferenceId { get; set; } = string.Empty;
}
