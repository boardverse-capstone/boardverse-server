namespace BoardVerse.Core.DTOs.Auth.Responses
{
    public class RefreshTokenResponseDto
    {
        public required string Token { get; set; }
        public required string RefreshToken { get; set; }
        public bool HasProfile { get; set; }
    }
}
