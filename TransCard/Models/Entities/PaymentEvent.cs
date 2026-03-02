using System.ComponentModel.DataAnnotations;

namespace TransCard.Models.Entities;

public class PaymentEvent
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid PaymentId { get; set; }

    [Required]
    [MaxLength(50)]
    public string EventType { get; set; } = string.Empty;

    [Required]
    public string Payload { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; }
}
