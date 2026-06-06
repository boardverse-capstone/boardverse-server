namespace BoardVerse.Core.DTOs.Auth.Responses
{
    public class LoginResponseDto
    {
        public required string Token { get; set; }
        public required string RefreshToken { get; set; }
    }
}
