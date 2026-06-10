-- BoardVerse Database Schema Update Script
-- This script creates/updates all entity tables for the BoardVerse application
-- Generated on: 2026-06-09
-- Keep in sync with: BoardVerseDbContext, GameSchemaBootstrapper, Configurations/

-- Enable UUID extension if not already enabled
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ============================================
-- USERS TABLE
-- ============================================
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

-- Create unique index on Email
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");

-- ============================================
-- USER PROFILES TABLE
-- ============================================
CREATE TABLE IF NOT EXISTS "UserProfiles" (
    "UserId" uuid PRIMARY KEY,
    "AvatarUrl" character varying(500),
    "AvatarBorderUrl" character varying(500),
    "Bio" character varying(1000),
    "KarmaPoints" integer NOT NULL DEFAULT 100,
    "GamerTier" character varying(50) NOT NULL DEFAULT 'Bronze',
    "GlobalElo" integer NOT NULL,
    "Level" integer NOT NULL,
    "CurrentExp" integer NOT NULL,
    "FirstName" character varying(100),
    "LastName" character varying(100),
    "DateOfBirth" date,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    CONSTRAINT "FK_UserProfiles_Users_UserId" FOREIGN KEY ("UserId") 
        REFERENCES "Users" ("Id") ON DELETE CASCADE
);

ALTER TABLE "UserProfiles" DROP COLUMN IF EXISTS "HomeAddress";

-- ============================================
-- REFRESH TOKENS TABLE
-- ============================================
CREATE TABLE IF NOT EXISTS "RefreshTokens" (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL,
    "Token" character varying(500) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "IsRevoked" boolean NOT NULL DEFAULT false,
    "CreatedAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    CONSTRAINT "FK_RefreshTokens_Users_UserId" FOREIGN KEY ("UserId") 
        REFERENCES "Users" ("Id") ON DELETE CASCADE
);

-- Create indexes
CREATE UNIQUE INDEX IF NOT EXISTS "IX_RefreshTokens_Token" ON "RefreshTokens" ("Token");
CREATE INDEX IF NOT EXISTS "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");

-- ============================================
-- TOKEN BLACKLIST TABLE
-- ============================================
CREATE TABLE IF NOT EXISTS "TokenBlacklists" (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL,
    "Token" character varying(500) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "Reason" character varying(500),
    CONSTRAINT "FK_TokenBlacklists_Users_UserId" FOREIGN KEY ("UserId") 
        REFERENCES "Users" ("Id") ON DELETE CASCADE
);

-- Create indexes
CREATE INDEX IF NOT EXISTS "IX_TokenBlacklists_Token" ON "TokenBlacklists" ("Token");
CREATE INDEX IF NOT EXISTS "IX_TokenBlacklists_UserId" ON "TokenBlacklists" ("UserId");

-- ============================================
-- PASSWORD RESET TOKENS TABLE
-- ============================================
CREATE TABLE IF NOT EXISTS "PasswordResetTokens" (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL,
    "Token" character varying(500) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "IsUsed" boolean NOT NULL DEFAULT false,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UsedAt" timestamp with time zone,
    CONSTRAINT "FK_PasswordResetTokens_Users_UserId" FOREIGN KEY ("UserId") 
        REFERENCES "Users" ("Id") ON DELETE CASCADE
);

-- Create indexes
CREATE INDEX IF NOT EXISTS "IX_PasswordResetTokens_Token" ON "PasswordResetTokens" ("Token");
CREATE INDEX IF NOT EXISTS "IX_PasswordResetTokens_UserId" ON "PasswordResetTokens" ("UserId");

-- ============================================
-- CAFES TABLE
-- ============================================
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
    CONSTRAINT "FK_Cafes_Users_ManagerId" FOREIGN KEY ("ManagerId") 
        REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

-- Create index on ManagerId
CREATE INDEX IF NOT EXISTS "IX_Cafes_ManagerId" ON "Cafes" ("ManagerId");

-- ============================================
-- CAFE STAFF TABLE (Junction Table)
-- ============================================
CREATE TABLE IF NOT EXISTS "CafeStaffs" (
    "CafeId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "JoinedAt" timestamp with time zone NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    PRIMARY KEY ("CafeId", "UserId"),
    CONSTRAINT "FK_CafeStaffs_Cafes_CafeId" FOREIGN KEY ("CafeId") 
        REFERENCES "Cafes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_CafeStaffs_Users_UserId" FOREIGN KEY ("UserId") 
        REFERENCES "Users" ("Id") ON DELETE CASCADE
);

-- ============================================
-- GAME TEMPLATES TABLE
-- ============================================
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

-- ============================================
-- GAME COMPONENT TEMPLATES TABLE
-- ============================================
CREATE TABLE IF NOT EXISTS "GameComponentTemplates" (
    "Id" uuid PRIMARY KEY,
    "GameTemplateId" uuid NOT NULL,
    "ComponentName" character varying(200) NOT NULL,
    "DefaultQuantity" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "FK_GameComponentTemplates_GameTemplates_GameTemplateId" FOREIGN KEY ("GameTemplateId") 
        REFERENCES "GameTemplates" ("Id") ON DELETE CASCADE
);

-- Create index on GameTemplateId
CREATE INDEX IF NOT EXISTS "IX_GameComponentTemplates_GameTemplateId" ON "GameComponentTemplates" ("GameTemplateId");

-- ============================================
-- CAFE GAME INVENTORIES TABLE
-- ============================================
CREATE TABLE IF NOT EXISTS "CafeGameInventories" (
    "Id" uuid PRIMARY KEY,
    "CafeId" uuid NOT NULL,
    "GameTemplateId" uuid NOT NULL,
    "BoxQuantity" integer NOT NULL,
    "Status" character varying(50) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    CONSTRAINT "FK_CafeGameInventories_Cafes_CafeId" FOREIGN KEY ("CafeId")
        REFERENCES "Cafes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_CafeGameInventories_GameTemplates_GameTemplateId" FOREIGN KEY ("GameTemplateId")
        REFERENCES "GameTemplates" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CafeGameInventories_CafeId_GameTemplateId"
    ON "CafeGameInventories" ("CafeId", "GameTemplateId")
    WHERE "IsActive" = true;

-- ============================================
-- CAFE GAME COMPONENT PENALTIES TABLE
-- ============================================
CREATE TABLE IF NOT EXISTS "CafeGameComponentPenalties" (
    "Id" uuid PRIMARY KEY,
    "CafeGameInventoryId" uuid NOT NULL,
    "GameComponentTemplateId" uuid NOT NULL,
    "PenaltyFee" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "FK_CafeGameComponentPenalties_CafeGameInventories_CafeGameInventoryId" FOREIGN KEY ("CafeGameInventoryId")
        REFERENCES "CafeGameInventories" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_CafeGameComponentPenalties_GameComponentTemplates_GameComponentTemplateId" FOREIGN KEY ("GameComponentTemplateId")
        REFERENCES "GameComponentTemplates" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CafeGameComponentPenalties_CafeGameInventoryId_GameComponentTemplateId"
    ON "CafeGameComponentPenalties" ("CafeGameInventoryId", "GameComponentTemplateId");

-- ============================================
-- SEED DATA FOR GAME TEMPLATES
-- ============================================
INSERT INTO "GameTemplates" ("Id", "BggGameId", "Name", "ThumbnailUrl", "Description", "MinPlayers", "MaxPlayers", "PlayTime", "CreatedAt", "UpdatedAt")
VALUES
    ('11111111-1111-1111-1111-111111111111', 13, 'Catan', 'https://example.com/images/catan.jpg', 'A strategy board game where players build settlements, roads, and cities by gathering and trading resources.', 3, 4, 60, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('22222222-2222-2222-2222-222222222222', 1406, 'Monopoly', 'https://example.com/images/monopoly.jpg', 'A classic real estate trading game where players buy, sell, and trade properties to bankrupt their opponents.', 2, 8, 120, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('33333333-3333-3333-3333-333333333333', 2225, 'Uno', 'https://example.com/images/uno.jpg', 'A fast-paced card game where players match colors and numbers, using action cards to change the game dynamics.', 2, 10, 30, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('44444444-4444-4444-4444-444444444444', 148228, 'Splendor', 'https://example.com/images/splendor.jpg', 'A strategy game of chip-collecting and card development where players act as Renaissance merchants.', 2, 4, 30, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('55555555-5555-5555-5555-555555555555', 925, 'Werewolf Ultimate', 'https://example.com/images/werewolf.jpg', 'A social deduction party game where players are assigned secret roles and must identify the werewolves among them.', 5, 20, 45, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00')
ON CONFLICT ("Id") DO NOTHING;

-- ============================================
-- SEED DATA FOR GAME COMPONENT TEMPLATES
-- ============================================
INSERT INTO "GameComponentTemplates" ("Id", "GameTemplateId", "ComponentName", "DefaultQuantity", "CreatedAt")
VALUES
    -- Catan
    ('a1111111-1111-1111-1111-111111111111', '11111111-1111-1111-1111-111111111111', 'Wood Hexagon Tiles', 4, '2024-01-01 00:00:00+00'),
    ('a1111111-1111-1111-1111-111111111112', '11111111-1111-1111-1111-111111111111', 'Brick Hexagon Tiles', 3, '2024-01-01 00:00:00+00'),
    ('a1111111-1111-1111-1111-111111111113', '11111111-1111-1111-1111-111111111111', 'Sheep Resource Cards', 19, '2024-01-01 00:00:00+00'),
    ('a1111111-1111-1111-1111-111111111114', '11111111-1111-1111-1111-111111111111', 'Wheat Resource Cards', 19, '2024-01-01 00:00:00+00'),
    ('a1111111-1111-1111-1111-111111111115', '11111111-1111-1111-1111-111111111111', 'Ore Resource Cards', 19, '2024-01-01 00:00:00+00'),
    ('a1111111-1111-1111-1111-111111111116', '11111111-1111-1111-1111-111111111111', 'Settlement Pieces', 20, '2024-01-01 00:00:00+00'),
    ('a1111111-1111-1111-1111-111111111117', '11111111-1111-1111-1111-111111111111', 'Road Pieces', 30, '2024-01-01 00:00:00+00'),
    ('a1111111-1111-1111-1111-111111111118', '11111111-1111-1111-1111-111111111111', 'City Pieces', 16, '2024-01-01 00:00:00+00'),
    ('a1111111-1111-1111-1111-111111111119', '11111111-1111-1111-1111-111111111111', 'Dice (2 pieces)', 2, '2024-01-01 00:00:00+00'),
    -- Monopoly
    ('a2222222-2222-2222-2222-222222222221', '22222222-2222-2222-2222-222222222222', 'Gameboard', 1, '2024-01-01 00:00:00+00'),
    ('a2222222-2222-2222-2222-222222222222', '22222222-2222-2222-2222-222222222222', 'Player Tokens (8 pieces)', 8, '2024-01-01 00:00:00+00'),
    ('a2222222-2222-2222-2222-222222222223', '22222222-2222-2222-2222-222222222222', 'Title Deed Cards (28 cards)', 28, '2024-01-01 00:00:00+00'),
    ('a2222222-2222-2222-2222-222222222224', '22222222-2222-2222-2222-222222222222', 'Chance Cards (16 cards)', 16, '2024-01-01 00:00:00+00'),
    ('a2222222-2222-2222-2222-222222222225', '22222222-2222-2222-2222-222222222222', 'Community Chest Cards (16 cards)', 16, '2024-01-01 00:00:00+00'),
    ('a2222222-2222-2222-2222-222222222226', '22222222-2222-2222-2222-222222222222', 'Houses (32 pieces)', 32, '2024-01-01 00:00:00+00'),
    ('a2222222-2222-2222-2222-222222222227', '22222222-2222-2222-2222-222222222222', 'Hotels (12 pieces)', 12, '2024-01-01 00:00:00+00'),
    ('a2222222-2222-2222-2222-222222222228', '22222222-2222-2222-2222-222222222222', 'Dice (2 pieces)', 2, '2024-01-01 00:00:00+00'),
    ('a2222222-2222-2222-2222-222222222229', '22222222-2222-2222-2222-222222222222', 'Monopoly Money', 1, '2024-01-01 00:00:00+00'),
    -- Uno
    ('a3333333-3333-3333-3333-333333333331', '33333333-3333-3333-3333-333333333333', 'Number Cards (Red)', 19, '2024-01-01 00:00:00+00'),
    ('a3333333-3333-3333-3333-333333333332', '33333333-3333-3333-3333-333333333333', 'Number Cards (Blue)', 19, '2024-01-01 00:00:00+00'),
    ('a3333333-3333-3333-3333-333333333333', '33333333-3333-3333-3333-333333333333', 'Number Cards (Green)', 19, '2024-01-01 00:00:00+00'),
    ('a3333333-3333-3333-3333-333333333334', '33333333-3333-3333-3333-333333333333', 'Number Cards (Yellow)', 19, '2024-01-01 00:00:00+00'),
    ('a3333333-3333-3333-3333-333333333335', '33333333-3333-3333-3333-333333333333', 'Skip Cards', 8, '2024-01-01 00:00:00+00'),
    ('a3333333-3333-3333-3333-333333333336', '33333333-3333-3333-3333-333333333333', 'Reverse Cards', 8, '2024-01-01 00:00:00+00'),
    ('a3333333-3333-3333-3333-333333333337', '33333333-3333-3333-3333-333333333333', 'Draw Two Cards', 8, '2024-01-01 00:00:00+00'),
    ('a3333333-3333-3333-3333-333333333338', '33333333-3333-3333-3333-333333333333', 'Wild Cards', 4, '2024-01-01 00:00:00+00'),
    ('a3333333-3333-3333-3333-333333333339', '33333333-3333-3333-3333-333333333333', 'Wild Draw Four Cards', 4, '2024-01-01 00:00:00+00'),
    -- Splendor
    ('a4444444-4444-4444-4444-444444444441', '44444444-4444-4444-4444-444444444444', 'Ruby Gem Tokens', 7, '2024-01-01 00:00:00+00'),
    ('a4444444-4444-4444-4444-444444444442', '44444444-4444-4444-4444-444444444444', 'Sapphire Gem Tokens', 7, '2024-01-01 00:00:00+00'),
    ('a4444444-4444-4444-4444-444444444443', '44444444-4444-4444-4444-444444444444', 'Emerald Gem Tokens', 7, '2024-01-01 00:00:00+00'),
    ('a4444444-4444-4444-4444-444444444444', '44444444-4444-4444-4444-444444444444', 'Onyx Gem Tokens', 7, '2024-01-01 00:00:00+00'),
    ('a4444444-4444-4444-4444-444444444445', '44444444-4444-4444-4444-444444444444', 'Diamond Gem Tokens', 7, '2024-01-01 00:00:00+00'),
    ('a4444444-4444-4444-4444-444444444446', '44444444-4444-4444-4444-444444444444', 'Gold Joker Tokens', 5, '2024-01-01 00:00:00+00'),
    ('a4444444-4444-4444-4444-444444444447', '44444444-4444-4444-4444-444444444444', 'Development Cards (Tier 1)', 40, '2024-01-01 00:00:00+00'),
    ('a4444444-4444-4444-4444-444444444448', '44444444-4444-4444-4444-444444444444', 'Development Cards (Tier 2)', 30, '2024-01-01 00:00:00+00'),
    ('a4444444-4444-4444-4444-444444444449', '44444444-4444-4444-4444-444444444444', 'Development Cards (Tier 3)', 20, '2024-01-01 00:00:00+00'),
    ('a4444444-4444-4444-4444-44444444444a', '44444444-4444-4444-4444-444444444444', 'Noble Tiles', 10, '2024-01-01 00:00:00+00'),
    -- Werewolf Ultimate
    ('a5555555-5555-5555-5555-555555555551', '55555555-5555-5555-5555-555555555555', 'Villager Role Cards', 10, '2024-01-01 00:00:00+00'),
    ('a5555555-5555-5555-5555-555555555552', '55555555-5555-5555-5555-555555555555', 'Werewolf Role Cards', 4, '2024-01-01 00:00:00+00'),
    ('a5555555-5555-5555-5555-555555555553', '55555555-5555-5555-5555-555555555555', 'Seer Role Card', 1, '2024-01-01 00:00:00+00'),
    ('a5555555-5555-5555-5555-555555555554', '55555555-5555-5555-5555-555555555555', 'Doctor Role Card', 1, '2024-01-01 00:00:00+00'),
    ('a5555555-5555-5555-5555-555555555555', '55555555-5555-5555-5555-555555555555', 'Witch Role Card', 1, '2024-01-01 00:00:00+00'),
    ('a5555555-5555-5555-5555-555555555556', '55555555-5555-5555-5555-555555555555', 'Hunter Role Card', 1, '2024-01-01 00:00:00+00'),
    ('a5555555-5555-5555-5555-555555555557', '55555555-5555-5555-5555-555555555555', 'Moderator Script', 1, '2024-01-01 00:00:00+00'),
    ('a5555555-5555-5555-5555-555555555558', '55555555-5555-5555-5555-555555555555', 'Night Phase Marker', 1, '2024-01-01 00:00:00+00'),
    ('a5555555-5555-5555-5555-555555555559', '55555555-5555-5555-5555-555555555555', 'Day Phase Marker', 1, '2024-01-01 00:00:00+00')
ON CONFLICT ("Id") DO NOTHING;

-- ============================================
-- MIGRATIONS HISTORY TABLE (for EF Core)
-- ============================================
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

-- Insert existing migrations if they don't exist
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES 
    ('20260526062046_InitialCreate', '8.0.0'),
    ('20260526_AuthEnhancements', '8.0.0')
ON CONFLICT ("MigrationId") DO NOTHING;

-- ============================================
-- END OF SCRIPT
-- ============================================
