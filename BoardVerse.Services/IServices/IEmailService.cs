namespace BoardVerse.Services.IServices
{
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string body, bool isHtml = false);
    }
}
