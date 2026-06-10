namespace BoardVerse.Core.Enum
{
    public static class UserRoleParser
    {
        public static bool TryParse(string? value, out UserRole role)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                role = default;
                return false;
            }

            if (string.Equals(value, "User", StringComparison.OrdinalIgnoreCase))
            {
                role = UserRole.Player;
                return true;
            }

            return global::System.Enum.TryParse(value, ignoreCase: true, out role);
        }
    }
}
