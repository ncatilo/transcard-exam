using System.ComponentModel.DataAnnotations;

namespace TransCard.Models.Requests;

public class TokenRequest
{
    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;
}
