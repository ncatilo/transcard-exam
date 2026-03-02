using TransCard.Models.Responses;

namespace TransCard.Services;

public interface ITokenService
{
    TokenResponse? GenerateToken(string clientId, string clientSecret);
}
