using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data
{
    public static class GameSchemaBootstrapper
    {
        public static async Task EnsureUserAndCafeTablesAsync(BoardVerseDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "Users" (
                    "Id" uuid PRIMARY KEY,
                    "Username" character varying(100) NOT NULL,
                    "Email" character varying(256) NOT NULL,
                    "PhoneNumber" character varying(50),
                    "PasswordHash" character varying(500),
                    "Role" character varying(50) NOT NULL DEFAULT 'Player',
                    "Provider" character varying(50) NOT NULL DEFAULT 'Local',
                    "ProviderId" character varying(200),
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    "IsEmailVerified" boolean NOT NULL DEFAULT false,
                    "EmailVerificationToken" character varying(500),
                    "EmailVerificationTokenExpiresAt" timestamp with time zone,
                    "PasswordResetToken" character varying(500),
                    "PasswordResetTokenExpiresAt" timestamp with time zone,
                    "IsActive" boolean NOT NULL DEFAULT true,
                    "IsBlocked" boolean NOT NULL DEFAULT false,
                    "BlockReason" character varying(500),
                    "BlockedAt" timestamp with time zone,
                    "LastLoginAt" timestamp with time zone
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");
                """);

            await context.Database.ExecuteSqlRawAsync("""
                UPDATE "Users" SET "Role" = 'Player' WHERE "Role" = 'User';
                """);

            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "Users" ALTER COLUMN "Role" SET DEFAULT 'Player';
                """);

            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "UserProfiles" DROP COLUMN IF EXISTS "HomeAddress";
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "Cafes" (
                    "Id" uuid PRIMARY KEY,
                    "Name" character varying(200) NOT NULL,
                    "Address" character varying(500) NOT NULL,
                    "PhoneNumber" character varying(50),
                    "Description" character varying(1000),
                    "ManagerId" uuid NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone,
                    "IsActive" boolean NOT NULL DEFAULT true,
                    CONSTRAINT "FK_Cafes_Users_ManagerId"
                        FOREIGN KEY ("ManagerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_Cafes_ManagerId" ON "Cafes" ("ManagerId");
                """);
        }

        public static async Task EnsureGameTablesAsync(BoardVerseDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "GameTemplates" (
                    "Id" uuid PRIMARY KEY,
                    "BggGameId" integer,
                    "Name" character varying(200) NOT NULL,
                    "ThumbnailUrl" character varying(500),
                    "Description" character varying(2000),
                    "MinPlayers" integer NOT NULL,
                    "MaxPlayers" integer NOT NULL,
                    "PlayTime" integer NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "GameComponentTemplates" (
                    "Id" uuid PRIMARY KEY,
                    "GameTemplateId" uuid NOT NULL,
                    "ComponentName" character varying(200) NOT NULL,
                    "DefaultQuantity" integer NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "FK_GameComponentTemplates_GameTemplates_GameTemplateId"
                        FOREIGN KEY ("GameTemplateId")
                        REFERENCES "GameTemplates" ("Id") ON DELETE CASCADE
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_GameComponentTemplates_GameTemplateId"
                    ON "GameComponentTemplates" ("GameTemplateId");
                """);

        }

        public static async Task EnsureInventoryTablesAsync(BoardVerseDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "CafeGameInventories" (
                    "Id" uuid PRIMARY KEY,
                    "CafeId" uuid NOT NULL,
                    "GameTemplateId" uuid NOT NULL,
                    "BoxQuantity" integer NOT NULL,
                    "Status" character varying(50) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    "IsActive" boolean NOT NULL DEFAULT true,
                    CONSTRAINT "FK_CafeGameInventories_Cafes_CafeId"
                        FOREIGN KEY ("CafeId") REFERENCES "Cafes" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_CafeGameInventories_GameTemplates_GameTemplateId"
                        FOREIGN KEY ("GameTemplateId") REFERENCES "GameTemplates" ("Id") ON DELETE RESTRICT
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CafeGameInventories_CafeId_GameTemplateId"
                    ON "CafeGameInventories" ("CafeId", "GameTemplateId")
                    WHERE "IsActive" = true;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "CafeGameComponentPenalties" (
                    "Id" uuid PRIMARY KEY,
                    "CafeGameInventoryId" uuid NOT NULL,
                    "GameComponentTemplateId" uuid NOT NULL,
                    "PenaltyFee" numeric(18,2) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "FK_CafeGameComponentPenalties_CafeGameInventories_CafeGameInventoryId"
                        FOREIGN KEY ("CafeGameInventoryId")
                        REFERENCES "CafeGameInventories" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_CafeGameComponentPenalties_GameComponentTemplates_GameComponentTemplateId"
                        FOREIGN KEY ("GameComponentTemplateId")
                        REFERENCES "GameComponentTemplates" ("Id") ON DELETE RESTRICT
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CafeGameComponentPenalties_Inventory_Component"
                    ON "CafeGameComponentPenalties" ("CafeGameInventoryId", "GameComponentTemplateId");
                """);
        }
    }
}
