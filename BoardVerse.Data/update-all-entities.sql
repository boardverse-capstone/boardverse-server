-- BoardVerse: Full schema sync + catalog seed (idempotent)
-- Run: dotnet run --project tools/ExecSql -- BoardVerse.Data/update-all-entities.sql
-- Or:  dotnet run --project tools/SeedDevData/SeedDevData.csproj  (bootstrap + EF seed)

-- ══════════════════════════════════════════════════════════════════════════════
-- 1. Users / Cafes / CafePartnerApplications
-- ══════════════════════════════════════════════════════════════════════════════

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

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");
UPDATE "Users" SET "Role" = 'Player' WHERE "Role" = 'User';
ALTER TABLE "Users" ALTER COLUMN "Role" SET DEFAULT 'Player';

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'UserProfiles') THEN
        ALTER TABLE "UserProfiles" DROP COLUMN IF EXISTS "HomeAddress";
    END IF;
END $$;

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

CREATE INDEX IF NOT EXISTS "IX_Cafes_ManagerId" ON "Cafes" ("ManagerId");

ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "PartnerOperationalStatus" character varying(20);
ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "WeekdayOpen" interval;
ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "WeekdayClose" interval;
ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "WeekendOpen" interval;
ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "WeekendClose" interval;

UPDATE "Cafes" SET "PartnerOperationalStatus" = 'Active'
WHERE "PartnerOperationalStatus" IS NULL AND "IsActive" = true;
UPDATE "Cafes" SET "PartnerOperationalStatus" = 'DataBlank'
WHERE "PartnerOperationalStatus" IS NULL AND "IsActive" = false;

-- CafePartnerApplications (create legacy shape, migrate to current columns)
CREATE TABLE IF NOT EXISTS "CafePartnerApplications" (
    "Id" uuid PRIMARY KEY,
    "ContactName" character varying(100),
    "ContactEmail" character varying(256),
    "ContactPhone" character varying(50),
    "CafeName" character varying(200) NOT NULL,
    "Address" character varying(500) NOT NULL,
    "CafePhoneNumber" character varying(50),
    "Description" character varying(1000),
    "BusinessLicenseNumber" character varying(100),
    "BusinessLicenseUrl" character varying(500),
    "CafeImageUrl" character varying(500),
    "Status" character varying(50) NOT NULL DEFAULT 'PendingApproval',
    "AdminNotes" character varying(1000),
    "RejectionReason" character varying(1000),
    "ResubmitCount" integer NOT NULL DEFAULT 0,
    "SubmittedByUserId" uuid,
    "ReviewedByAdminId" uuid,
    "ReviewedAt" timestamp with time zone,
    "CreatedManagerUserId" uuid,
    "CreatedCafeId" uuid,
    "SubmittedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
);

ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "BusinessLicenseNumber" character varying(100);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "BusinessLicenseUrl" character varying(500);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "CafeImageUrl" character varying(500);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "ResubmitCount" integer NOT NULL DEFAULT 0;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "SubmittedByUserId" uuid;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "Hotline" character varying(11);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "RepresentativeEmail" character varying(256);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "RepresentativeName" character varying(100);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "BusinessLicense" character varying(50);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "BusinessLicenseImageUrl" character varying(500);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "NumberOfTables" integer NOT NULL DEFAULT 0;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "NumberOfPrivateRooms" integer NOT NULL DEFAULT 0;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "MaximumCapacity" integer NOT NULL DEFAULT 1;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "SpaceImageUrlsJson" text NOT NULL DEFAULT '[]';
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "NumberOfGamesOwned" integer NOT NULL DEFAULT 0;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "PopularGamesList" character varying(2000) NOT NULL DEFAULT '';
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "HasGameMaster" boolean NOT NULL DEFAULT false;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "BillingModel" character varying(20) NOT NULL DEFAULT 'ByHour';
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "OpsAlertMessage" character varying(1000);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "CommissionRate" numeric(5,2);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "ContractSentAt" timestamp with time zone;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "ContractSignedAt" timestamp with time zone;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "RequiresCsSupport" boolean NOT NULL DEFAULT false;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "DisplayRankOverride" integer;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "WeekdayOpen" interval;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "WeekdayClose" interval;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "WeekendOpen" interval;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "WeekendClose" interval;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "TableLayoutJson" text NOT NULL DEFAULT '[]';
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "ApprovedAt" timestamp with time zone;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "OperationalProfileUpdatedAt" timestamp with time zone;

DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'CafePartnerApplications' AND column_name = 'ContactName') THEN
        ALTER TABLE "CafePartnerApplications" ALTER COLUMN "ContactName" DROP NOT NULL;
        ALTER TABLE "CafePartnerApplications" ALTER COLUMN "ContactEmail" DROP NOT NULL;
        ALTER TABLE "CafePartnerApplications" ALTER COLUMN "ContactPhone" DROP NOT NULL;
    END IF;
END $$;

UPDATE "CafePartnerApplications"
SET "RepresentativeEmail" = COALESCE(NULLIF(TRIM("RepresentativeEmail"), ''), "ContactEmail"),
    "Hotline" = COALESCE(NULLIF(TRIM("Hotline"), ''), "ContactPhone"),
    "RepresentativeName" = COALESCE("RepresentativeName", "ContactName"),
    "BusinessLicense" = COALESCE(NULLIF(TRIM("BusinessLicense"), ''), "BusinessLicenseNumber", ''),
    "BusinessLicenseImageUrl" = COALESCE("BusinessLicenseImageUrl", "BusinessLicenseUrl"),
    "SpaceImageUrlsJson" = CASE
        WHEN ("SpaceImageUrlsJson" IS NULL OR "SpaceImageUrlsJson" = '[]') AND "CafeImageUrl" IS NOT NULL
        THEN format('["%s"]', replace("CafeImageUrl", '"', '\"'))
        ELSE COALESCE("SpaceImageUrlsJson", '[]')
    END
WHERE EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'CafePartnerApplications' AND column_name = 'ContactEmail')
  AND ("ContactEmail" IS NOT NULL OR "RepresentativeEmail" IS NULL OR TRIM(COALESCE("RepresentativeEmail", '')) = '');

UPDATE "CafePartnerApplications" SET "Status" = 'PendingApproval' WHERE "Status" IN ('Pending', 'PendingReview', 'PendingInfo', 'NeedsMoreInfo');
UPDATE "CafePartnerApplications" SET "Status" = 'Approved' WHERE "Status" IN ('Active', 'ContractSigned', 'PendingNegotiation');
UPDATE "CafePartnerApplications" SET "Status" = 'Rejected' WHERE "Status" IN ('Cancelled', 'Rejected');

CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_Status" ON "CafePartnerApplications" ("Status");
CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_SubmittedByUserId" ON "CafePartnerApplications" ("SubmittedByUserId");
CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_RepresentativeEmail" ON "CafePartnerApplications" ("RepresentativeEmail");
CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_BusinessLicense" ON "CafePartnerApplications" ("BusinessLicense");
CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_Hotline" ON "CafePartnerApplications" ("Hotline");

DROP INDEX IF EXISTS "IX_CafePartnerApplications_ContactEmail";
ALTER TABLE "CafePartnerApplications" DROP COLUMN IF EXISTS "ContactName";
ALTER TABLE "CafePartnerApplications" DROP COLUMN IF EXISTS "ContactEmail";
ALTER TABLE "CafePartnerApplications" DROP COLUMN IF EXISTS "ContactPhone";
ALTER TABLE "CafePartnerApplications" DROP COLUMN IF EXISTS "CafePhoneNumber";
ALTER TABLE "CafePartnerApplications" DROP COLUMN IF EXISTS "Description";
ALTER TABLE "CafePartnerApplications" DROP COLUMN IF EXISTS "AdminNotes";
ALTER TABLE "CafePartnerApplications" DROP COLUMN IF EXISTS "BusinessLicenseNumber";
ALTER TABLE "CafePartnerApplications" DROP COLUMN IF EXISTS "BusinessLicenseUrl";
ALTER TABLE "CafePartnerApplications" DROP COLUMN IF EXISTS "CafeImageUrl";
ALTER TABLE "CafePartnerApplications" DROP COLUMN IF EXISTS "RepresentativeName";
ALTER TABLE "CafePartnerApplications" DROP COLUMN IF EXISTS "MaximumCapacity";

-- ══════════════════════════════════════════════════════════════════════════════
-- 2. Game catalog schema
-- ══════════════════════════════════════════════════════════════════════════════

CREATE TABLE IF NOT EXISTS "GameTemplates" (
    "Id" uuid PRIMARY KEY,
    "Name" character varying(200) NOT NULL,
    "NameSearchKey" character varying(200) NOT NULL DEFAULT '',
    "ThumbnailUrl" character varying(500),
    "Description" character varying(2000),
    "MinPlayers" integer NOT NULL,
    "MaxPlayers" integer NOT NULL,
    "PlayTime" integer NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
);

ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "NameSearchKey" character varying(200) NOT NULL DEFAULT '';
ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "SearchAliases" character varying(500);
ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "SearchAliasesKey" character varying(500) NOT NULL DEFAULT '';
ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;
ALTER TABLE "GameTemplates" DROP COLUMN IF EXISTS "BggGameId";

CREATE INDEX IF NOT EXISTS "IX_GameTemplates_NameSearchKey" ON "GameTemplates" ("NameSearchKey");
CREATE INDEX IF NOT EXISTS "IX_GameTemplates_SearchAliasesKey" ON "GameTemplates" ("SearchAliasesKey");

CREATE TABLE IF NOT EXISTS "Categories" (
    "Id" uuid PRIMARY KEY,
    "Name" character varying(100) NOT NULL,
    "Slug" character varying(100) NOT NULL,
    "Description" character varying(500),
    "SortOrder" integer NOT NULL,
    "IsActive" boolean NOT NULL DEFAULT true,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Categories_Slug" ON "Categories" ("Slug");

CREATE TABLE IF NOT EXISTS "GameTemplateCategories" (
    "GameTemplateId" uuid NOT NULL,
    "CategoryId" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    PRIMARY KEY ("GameTemplateId", "CategoryId"),
    CONSTRAINT "FK_GameTemplateCategories_GameTemplates_GameTemplateId"
        FOREIGN KEY ("GameTemplateId") REFERENCES "GameTemplates" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_GameTemplateCategories_Categories_CategoryId"
        FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS "GameComponentTemplates" (
    "Id" uuid PRIMARY KEY,
    "GameTemplateId" uuid NOT NULL,
    "ComponentName" character varying(200) NOT NULL,
    "DefaultQuantity" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "FK_GameComponentTemplates_GameTemplates_GameTemplateId"
        FOREIGN KEY ("GameTemplateId") REFERENCES "GameTemplates" ("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_GameComponentTemplates_GameTemplateId" ON "GameComponentTemplates" ("GameTemplateId");

-- ══════════════════════════════════════════════════════════════════════════════
-- 3. Cafe inventory schema
-- ══════════════════════════════════════════════════════════════════════════════

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

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CafeGameInventories_CafeId_GameTemplateId"
    ON "CafeGameInventories" ("CafeId", "GameTemplateId")
    WHERE "IsActive" = true;

-- Rename legacy table name if present
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'CafeGameComponents')
       AND NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'CafeGameComponentPenalties')
    THEN
        ALTER TABLE "CafeGameComponents" RENAME TO "CafeGameComponentPenalties";
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS "CafeGameComponentPenalties" (
    "Id" uuid PRIMARY KEY,
    "CafeGameInventoryId" uuid NOT NULL,
    "GameComponentTemplateId" uuid NOT NULL,
    "PenaltyFee" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "FK_CafeGameComponentPenalties_CafeGameInventories_CafeGameInventoryId"
        FOREIGN KEY ("CafeGameInventoryId") REFERENCES "CafeGameInventories" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_CafeGameComponentPenalties_GameComponentTemplates_GameComponentTemplateId"
        FOREIGN KEY ("GameComponentTemplateId") REFERENCES "GameComponentTemplates" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_CafeGameComponentPenalties_Inventory_Component"
    ON "CafeGameComponentPenalties" ("CafeGameInventoryId", "GameComponentTemplateId");

-- ══════════════════════════════════════════════════════════════════════════════
-- 4. Catalog seed (categories, games, links, components, aliases)
-- ══════════════════════════════════════════════════════════════════════════════

INSERT INTO "Categories" ("Id", "Name", "Slug", "Description", "SortOrder", "IsActive", "CreatedAt", "UpdatedAt")
VALUES
    ('c1111111-1111-1111-1111-111111111111', 'Ẩn vai', 'an-vai', 'Trò chơi suy luận vai trò bí mật', 1, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('c1111111-1111-1111-1111-111111111112', 'Chiến thuật', 'chien-thuat', 'Tư duy chiến lược, tối ưu nguồn lực và điểm số', 2, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('c1111111-1111-1111-1111-111111111113', 'Giải trí', 'giai-tri', 'Nhẹ nhàng, vui vẻ, phù hợp tụ tập đông người', 3, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('c1111111-1111-1111-1111-111111111114', 'Hợp tác', 'hop-tac', 'Người chơi cùng phối hợp để đạt mục tiêu chung', 4, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('c1111111-1111-1111-1111-111111111115', 'Đối kháng', 'doi-khang', 'Cạnh tranh trực tiếp giữa các người chơi', 5, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('c1111111-1111-1111-1111-111111111116', 'Phiêu lưu', 'phieu-luu', 'Khám phá cốt truyện và thế giới trong game', 6, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00')
ON CONFLICT ("Id") DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "Slug" = EXCLUDED."Slug",
    "Description" = EXCLUDED."Description",
    "SortOrder" = EXCLUDED."SortOrder",
    "IsActive" = EXCLUDED."IsActive",
    "UpdatedAt" = EXCLUDED."UpdatedAt";

INSERT INTO "GameTemplates" ("Id", "Name", "NameSearchKey", "ThumbnailUrl", "Description", "MinPlayers", "MaxPlayers", "PlayTime", "IsActive", "CreatedAt", "UpdatedAt")
VALUES
    ('11111111-1111-1111-1111-111111111111', 'Catan', 'catan', 'https://example.com/images/catan.jpg', 'Trò chơi chiến thuật xây dựng đường, làng và thành phố bằng cách giao dịch tài nguyên.', 3, 4, 60, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('22222222-2222-2222-2222-222222222222', 'Monopoly', 'monopoly', 'https://example.com/images/monopoly.jpg', 'Trò chơi kinh doanh bất động sản cổ điển: mua bán, đấu giá và phá sản đối thủ.', 2, 8, 120, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('33333333-3333-3333-3333-333333333333', 'Uno', 'uno', 'https://example.com/images/uno.jpg', 'Trò chơi bài nhanh: khớp màu/số và dùng thẻ đặc biệt để đổi hướng ván đấu.', 2, 10, 30, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('44444444-4444-4444-4444-444444444444', 'Splendor', 'splendor', 'https://example.com/images/splendor.jpg', 'Thu thập gem và phát triển thẻ để trở thành thương nhân giàu có nhất.', 2, 4, 30, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('55555555-5555-5555-5555-555555555555', 'Werewolf Ultimate', 'werewolf ultimate', 'https://example.com/images/werewolf.jpg', 'Trò chơi suy luận vai trò: phe Dân làng phải tìm ra Ma Sói trước khi bị loại hết.', 5, 20, 45, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('66666666-6666-6666-6666-666666666666', 'The Resistance: Avalon', 'the resistance: avalon', 'https://example.com/images/avalon.jpg', 'Phe Hiệp sĩ phải hoàn thành 3 nhiệm vụ thành công, trong khi phe Phản bội âm thầm phá hoại. Mỗi vòng bỏ phiếu và suy luận vai trò quyết định thắng thua.', 5, 10, 30, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('77777777-7777-7777-7777-777777777777', 'Codenames', 'codenames', 'https://example.com/images/codenames.jpg', 'Hai đội tranh đấu để tìm mật danh của đồng đội qua gợi ý một từ duy nhất.', 4, 8, 15, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('88888888-8888-8888-8888-888888888888', 'Pandemic', 'pandemic', 'https://example.com/images/pandemic.jpg', 'Người chơi hợp tác để chữa bệnh và ngăn dịch bùng phát toàn cầu.', 2, 4, 45, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00')
ON CONFLICT ("Id") DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "NameSearchKey" = EXCLUDED."NameSearchKey",
    "ThumbnailUrl" = EXCLUDED."ThumbnailUrl",
    "Description" = EXCLUDED."Description",
    "MinPlayers" = EXCLUDED."MinPlayers",
    "MaxPlayers" = EXCLUDED."MaxPlayers",
    "PlayTime" = EXCLUDED."PlayTime",
    "IsActive" = EXCLUDED."IsActive",
    "UpdatedAt" = EXCLUDED."UpdatedAt";

UPDATE "GameTemplates"
SET "NameSearchKey" = lower(trim("Name"))
WHERE "NameSearchKey" IS NULL OR TRIM("NameSearchKey") = '';

-- Search aliases (display + normalized key for fuzzy search)
UPDATE "GameTemplates" SET "SearchAliases" = 'Catán, Settlers of Catan, Catan', "SearchAliasesKey" = 'catan settlers of catan catan' WHERE lower(trim("Name")) = 'catan';
UPDATE "GameTemplates" SET "SearchAliases" = 'Cờ Tỷ Phú', "SearchAliasesKey" = 'co ty phu' WHERE lower(trim("Name")) = 'monopoly';
UPDATE "GameTemplates" SET "SearchAliases" = 'Uno', "SearchAliasesKey" = 'uno' WHERE lower(trim("Name")) = 'uno';
UPDATE "GameTemplates" SET "SearchAliases" = 'Splendor, Ngọc Trai', "SearchAliasesKey" = 'splendor ngoc trai' WHERE lower(trim("Name")) = 'splendor';
UPDATE "GameTemplates" SET "SearchAliases" = 'Ma Sói, Ma Soi, Werewolf, Ma Sói Ultimate', "SearchAliasesKey" = 'ma soi ma soi werewolf ma soi ultimate' WHERE lower(trim("Name")) = 'werewolf ultimate';
UPDATE "GameTemplates" SET "SearchAliases" = 'Avalon, Kháng Chiến, The Resistance', "SearchAliasesKey" = 'avalon khang chien the resistance' WHERE lower(trim("Name")) = 'the resistance: avalon';
UPDATE "GameTemplates" SET "SearchAliases" = 'Codenames, Mật Danh', "SearchAliasesKey" = 'codenames mat danh' WHERE lower(trim("Name")) = 'codenames';
UPDATE "GameTemplates" SET "SearchAliases" = 'Pandemic, Đại Dịch', "SearchAliasesKey" = 'pandemic dai dich' WHERE lower(trim("Name")) = 'pandemic';

INSERT INTO "GameTemplateCategories" ("GameTemplateId", "CategoryId", "CreatedAt")
VALUES
    ('11111111-1111-1111-1111-111111111111', 'c1111111-1111-1111-1111-111111111112', '2024-01-01 00:00:00+00'),
    ('11111111-1111-1111-1111-111111111111', 'c1111111-1111-1111-1111-111111111115', '2024-01-01 00:00:00+00'),
    ('22222222-2222-2222-2222-222222222222', 'c1111111-1111-1111-1111-111111111113', '2024-01-01 00:00:00+00'),
    ('22222222-2222-2222-2222-222222222222', 'c1111111-1111-1111-1111-111111111115', '2024-01-01 00:00:00+00'),
    ('33333333-3333-3333-3333-333333333333', 'c1111111-1111-1111-1111-111111111113', '2024-01-01 00:00:00+00'),
    ('44444444-4444-4444-4444-444444444444', 'c1111111-1111-1111-1111-111111111112', '2024-01-01 00:00:00+00'),
    ('55555555-5555-5555-5555-555555555555', 'c1111111-1111-1111-1111-111111111111', '2024-01-01 00:00:00+00'),
    ('55555555-5555-5555-5555-555555555555', 'c1111111-1111-1111-1111-111111111113', '2024-01-01 00:00:00+00'),
    ('66666666-6666-6666-6666-666666666666', 'c1111111-1111-1111-1111-111111111111', '2024-01-01 00:00:00+00'),
    ('66666666-6666-6666-6666-666666666666', 'c1111111-1111-1111-1111-111111111112', '2024-01-01 00:00:00+00'),
    ('77777777-7777-7777-7777-777777777777', 'c1111111-1111-1111-1111-111111111111', '2024-01-01 00:00:00+00'),
    ('77777777-7777-7777-7777-777777777777', 'c1111111-1111-1111-1111-111111111113', '2024-01-01 00:00:00+00'),
    ('88888888-8888-8888-8888-888888888888', 'c1111111-1111-1111-1111-111111111114', '2024-01-01 00:00:00+00'),
    ('88888888-8888-8888-8888-888888888888', 'c1111111-1111-1111-1111-111111111112', '2024-01-01 00:00:00+00')
ON CONFLICT ("GameTemplateId", "CategoryId") DO NOTHING;

INSERT INTO "GameComponentTemplates" ("Id", "GameTemplateId", "ComponentName", "DefaultQuantity", "CreatedAt")
VALUES
    ('a6666666-6666-6666-6666-666666666661', '66666666-6666-6666-6666-666666666666', 'Thẻ nhân vật', 10, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666662', '66666666-6666-6666-6666-666666666666', 'Token phiếu bầu (Approve/Reject)', 20, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666663', '66666666-6666-6666-6666-666666666666', 'Token thực hiện nhiệm vụ (Success/Fail)', 5, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666664', '66666666-6666-6666-6666-666666666666', 'Thẻ nhiệm vụ (Quest)', 5, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666665', '66666666-6666-6666-6666-666666666666', 'Thẻ chỉ dẫn Hiệp sĩ/Phản bội', 2, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666666', '66666666-6666-6666-6666-666666666666', 'Thẻ Lady of the Lake', 1, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666667', '66666666-6666-6666-6666-666666666666', 'Bảng điểm nhiệm vụ', 1, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666668', '66666666-6666-6666-6666-666666666666', 'Thẻ đánh dấu lãnh đạo (Leader)', 1, '2024-01-01 00:00:00+00')
ON CONFLICT ("Id") DO UPDATE SET
    "ComponentName" = EXCLUDED."ComponentName",
    "DefaultQuantity" = EXCLUDED."DefaultQuantity";
