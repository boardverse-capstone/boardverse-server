namespace BoardVerse.Core.DTOs.Session
{
    public class CheckoutRequestDto
    {
        public List<ComponentCheckoutItemDto>? Components { get; set; }
        public bool ComponentsVerified { get; set; }
    }
}
