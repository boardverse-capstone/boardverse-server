using BoardVerse.Core.Helpers;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Thông tin gốc của board game trong danh mục master.
    /// Bảng DB: GameTemplates (tương đương BoardGames trong thiết kế nghiệp vụ).
    /// </summary>
    public class GameTemplate
    {
        private int _minPlayers = 1;
        private int _maxPlayers = 4;
        private int _playTime = 30;
        private string _name = string.Empty;

        public Guid Id { get; set; }
        public string Name
        {
            get => _name;
            set
            {
                _name = value ?? string.Empty;
                NameSearchKey = VietnameseTextNormalizer.ToSearchKey(_name);
            }
        }

        /// <summary>
        /// Tên đã chuẩn hóa (bỏ dấu, chữ thường) — phục vụ fuzzy search AC 1.1.
        /// </summary>
        public string NameSearchKey { get; set; } = string.Empty;

        private string? _searchAliases;
        /// <summary>
        /// Tên gọi khác / tiếng Việt, phân tách bằng dấu phẩy (vd: Ma Sói, Werewolf).
        /// </summary>
        public string? SearchAliases
        {
            get => _searchAliases;
            set
            {
                _searchAliases = value;
                SearchAliasesKey = VietnameseTextNormalizer.ToSearchKey(value);
            }
        }

        public string SearchAliasesKey { get; set; } = string.Empty;

        public string? ThumbnailUrl { get; set; }
        public string? Description { get; set; }

        /// <summary>BoardGameGeek thing id (xmlapi2/thing?id=).</summary>
        public int? BggId { get; set; }
        public DateTime? BggSyncedAt { get; set; }

        public bool IsActive { get; set; } = true;

        // === Tournament Support ===
        /// <summary>
        /// Game có hỗ trợ tournament mode không. False = không thể tạo tournament với game này.
        /// Thay thế cho việc hardcode tên "Splendor".
        /// </summary>
        public bool IsTournamentSupported { get; set; } = false;

        /// <summary>
        /// Số điểm tối đa hợp lệ cho một player trong một bàn tournament.
        /// Splendor = 15 (max prestige). Splendor Duel = 20. Mặc định 15.
        /// </summary>
        public int TournamentMaxScorePerPlayer { get; set; } = 15;

        /// <summary>
        /// Số player tối thiểu để có thể tổ chức tournament 1 vòng.
        /// Splendor = 2 (chơi được 2 người). Mặc định 2.
        /// </summary>
        public int TournamentMinPlayersPerTable { get; set; } = 2;

        public int MinPlayers
        {
            get => _minPlayers;
            set
            {
                if (value < 1)
                    throw new ArgumentException(ApiErrorMessages.Entity.MinPlayersAtLeastOne);
                _minPlayers = value;
            }
        }

        public int MaxPlayers
        {
            get => _maxPlayers;
            set
            {
                if (value < 1)
                    throw new ArgumentException(ApiErrorMessages.Entity.MaxPlayersAtLeastOne);
                _maxPlayers = value;
            }
        }

        public int PlayTime
        {
            get => _playTime;
            set
            {
                if (value <= 0)
                    throw new ArgumentException(ApiErrorMessages.Entity.PlayTimeMustBePositive);
                _playTime = value;
            }
        }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<GameComponentTemplate> Components { get; set; } = [];
        public virtual ICollection<GameTemplateCategory> Categories { get; set; } = [];
    }
}
