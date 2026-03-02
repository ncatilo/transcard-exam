using System.ComponentModel.DataAnnotations;

namespace TransCard.Models.Entities;

public class Payment
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public decimal Amount { get; set; }

    [Required]
    [MaxLength(3)]
    public string Currency { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string ReferenceId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }
}
