namespace BoardVerse.Core.Entities
{
    public class GameComponentTemplate
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid GameTemplateId { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        private int _defaultQuantity = 1;
        public int DefaultQuantity
        {
            get => _defaultQuantity;
            set
            {
                if (value < 1)
                    throw new ArgumentException("DefaultQuantity must be positive");
                _defaultQuantity = value;
            }
        }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property to GameTemplate
        public virtual GameTemplate GameTemplate { get; set; } = null!;
    }
}
