-- BoardVerse: Cafe Partner self-onboarding schema (idempotent)
-- Run via tools/ExecSql against your PostgreSQL database.

ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "Hotline" character varying(11);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "RepresentativeEmail" character varying(256);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "BusinessLicense" character varying(50);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "BusinessLicenseImageUrl" character varying(500);
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "NumberOfTables" integer NOT NULL DEFAULT 0;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "NumberOfPrivateRooms" integer NOT NULL DEFAULT 0;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "SpaceImageUrlsJson" text NOT NULL DEFAULT '[]';
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "NumberOfGamesOwned" integer NOT NULL DEFAULT 0;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "PopularGamesList" character varying(2000) NOT NULL DEFAULT '';
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "HasGameMaster" boolean NOT NULL DEFAULT false;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "BillingModel" character varying(20) NOT NULL DEFAULT 'ByHour';
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "RequiresCsSupport" boolean NOT NULL DEFAULT false;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "WeekdayOpen" interval;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "WeekdayClose" interval;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "WeekendOpen" interval;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "WeekendClose" interval;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "TableLayoutJson" text NOT NULL DEFAULT '[]';
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "ApprovedAt" timestamp with time zone;
ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "OperationalProfileUpdatedAt" timestamp with time zone;

UPDATE "CafePartnerApplications"
SET "RepresentativeEmail" = COALESCE(NULLIF(TRIM("RepresentativeEmail"), ''), "ContactEmail"),
    "Hotline" = COALESCE(NULLIF(TRIM("Hotline"), ''), "ContactPhone"),
    "BusinessLicense" = COALESCE(NULLIF(TRIM("BusinessLicense"), ''), "BusinessLicenseNumber", ''),
    "BusinessLicenseImageUrl" = COALESCE("BusinessLicenseImageUrl", "BusinessLicenseUrl")
WHERE "ContactEmail" IS NOT NULL
   OR "RepresentativeEmail" IS NULL
   OR TRIM(COALESCE("RepresentativeEmail", '')) = '';

UPDATE "CafePartnerApplications" SET "Status" = 'PendingApproval' WHERE "Status" IN ('Pending', 'PendingReview', 'PendingInfo', 'NeedsMoreInfo');
UPDATE "CafePartnerApplications" SET "Status" = 'Approved' WHERE "Status" IN ('Active', 'ContractSigned', 'PendingNegotiation');
UPDATE "CafePartnerApplications" SET "Status" = 'Rejected' WHERE "Status" IN ('Cancelled', 'Rejected');

ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "PartnerOperationalStatus" character varying(20);
ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "WeekdayOpen" interval;
ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "WeekdayClose" interval;
ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "WeekendOpen" interval;
ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "WeekendClose" interval;

UPDATE "Cafes" SET "PartnerOperationalStatus" = 'Active'
WHERE "PartnerOperationalStatus" IS NULL AND "IsActive" = true;
UPDATE "Cafes" SET "PartnerOperationalStatus" = 'DataBlank'
WHERE "PartnerOperationalStatus" IS NULL AND "IsActive" = false;

CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_RepresentativeEmail" ON "CafePartnerApplications" ("RepresentativeEmail");
CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_BusinessLicense" ON "CafePartnerApplications" ("BusinessLicense");
CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_Hotline" ON "CafePartnerApplications" ("Hotline");
CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_Status" ON "CafePartnerApplications" ("Status");

-- =============================================================================
-- Board game catalog schema + seed (idempotent)
-- GameTemplates = BoardGames | GameComponentTemplates = GameComponents
-- =============================================================================

CREATE TABLE IF NOT EXISTS "GameTemplates" (
    "Id" uuid PRIMARY KEY,
    "BggGameId" integer,
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

CREATE INDEX IF NOT EXISTS "IX_GameTemplates_NameSearchKey" ON "GameTemplates" ("NameSearchKey");
CREATE INDEX IF NOT EXISTS "IX_GameTemplates_SearchAliasesKey" ON "GameTemplates" ("SearchAliasesKey");

CREATE TABLE IF NOT EXISTS "GameComponentTemplates" (
    "Id" uuid PRIMARY KEY,
    "GameTemplateId" uuid NOT NULL,
    "ComponentName" character varying(200) NOT NULL,
    "DefaultQuantity" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "FK_GameComponentTemplates_GameTemplates_GameTemplateId"
        FOREIGN KEY ("GameTemplateId") REFERENCES "GameTemplates" ("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_GameComponentTemplates_GameTemplateId"
    ON "GameComponentTemplates" ("GameTemplateId");

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

INSERT INTO "GameTemplates" ("Id", "BggGameId", "Name", "NameSearchKey", "ThumbnailUrl", "Description", "MinPlayers", "MaxPlayers", "PlayTime", "IsActive", "CreatedAt", "UpdatedAt")
VALUES
    ('11111111-1111-1111-1111-111111111111', 13, 'Catan', 'catan', 'https://example.com/images/catan.jpg', 'Trò chơi chiến thuật xây dựng đường, làng và thành phố bằng cách giao dịch tài nguyên.', 3, 4, 60, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('22222222-2222-2222-2222-222222222222', 1406, 'Monopoly', 'monopoly', 'https://example.com/images/monopoly.jpg', 'Trò chơi kinh doanh bất động sản cổ điển: mua bán, đấu giá và phá sản đối thủ.', 2, 8, 120, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('33333333-3333-3333-3333-333333333333', 2225, 'Uno', 'uno', 'https://example.com/images/uno.jpg', 'Trò chơi bài nhanh: khớp màu/số và dùng thẻ đặc biệt để đổi hướng ván đấu.', 2, 10, 30, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('44444444-4444-4444-4444-444444444444', 148228, 'Splendor', 'splendor', 'https://example.com/images/splendor.jpg', 'Thu thập gem và phát triển thẻ để trở thành thương nhân giàu có nhất.', 2, 4, 30, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('55555555-5555-5555-5555-555555555555', 925, 'Werewolf Ultimate', 'werewolf ultimate', 'https://example.com/images/werewolf.jpg', 'Trò chơi suy luận vai trò: phe Dân làng phải tìm ra Ma Sói trước khi bị loại hết.', 5, 20, 45, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('66666666-6666-6666-6666-666666666666', 128882, 'The Resistance: Avalon', 'the resistance: avalon', 'https://example.com/images/avalon.jpg', 'Phe Hiệp sĩ phải hoàn thành 3 nhiệm vụ thành công, trong khi phe Phản bội âm thầm phá hoại. Mỗi vòng bỏ phiếu và suy luận vai trò quyết định thắng thua.', 5, 10, 30, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('77777777-7777-7777-7777-777777777777', 178900, 'Codenames', 'codenames', 'https://example.com/images/codenames.jpg', 'Hai đội tranh đấu để tìm mật danh của đồng đội qua gợi ý một từ duy nhất.', 4, 8, 15, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00'),
    ('88888888-8888-8888-8888-888888888888', 30549, 'Pandemic', 'pandemic', 'https://example.com/images/pandemic.jpg', 'Người chơi hợp tác để chữa bệnh và ngăn dịch bùng phát toàn cầu.', 2, 4, 45, true, '2024-01-01 00:00:00+00', '2024-01-01 00:00:00+00')
ON CONFLICT ("Id") DO UPDATE SET
    "BggGameId" = EXCLUDED."BggGameId",
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
SET "NameSearchKey" = lower("Name")
WHERE TRIM(COALESCE("NameSearchKey", '')) = '';

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

-- Avalon, Codenames, Pandemic components
INSERT INTO "GameComponentTemplates" ("Id", "GameTemplateId", "ComponentName", "DefaultQuantity", "CreatedAt")
VALUES
    ('a6666666-6666-6666-6666-666666666661', '66666666-6666-6666-6666-666666666666', 'Thẻ nhân vật', 10, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666662', '66666666-6666-6666-6666-666666666666', 'Token phiếu bầu (Approve/Reject)', 20, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666663', '66666666-6666-6666-6666-666666666666', 'Token thực hiện nhiệm vụ (Success/Fail)', 5, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666664', '66666666-6666-6666-6666-666666666666', 'Thẻ nhiệm vụ (Quest)', 5, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666665', '66666666-6666-6666-6666-666666666666', 'Thẻ chỉ dẫn Hiệp sĩ/Phản bội', 2, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666666', '66666666-6666-6666-6666-666666666666', 'Thẻ Lady of the Lake', 1, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666667', '66666666-6666-6666-6666-666666666666', 'Bảng điểm nhiệm vụ', 1, '2024-01-01 00:00:00+00'),
    ('a6666666-6666-6666-6666-666666666668', '66666666-6666-6666-6666-666666666666', 'Thẻ đánh dấu lãnh đạo (Leader)', 1, '2024-01-01 00:00:00+00'),
    ('a7777777-7777-7777-7777-777777777771', '77777777-7777-7777-7777-777777777777', 'Thẻ từ khóa (Key cards)', 200, '2024-01-01 00:00:00+00'),
    ('a7777777-7777-7777-7777-777777777772', '77777777-7777-7777-7777-777777777777', 'Thẻ mật danh (Agent cards)', 16, '2024-01-01 00:00:00+00'),
    ('a7777777-7777-7777-7777-777777777773', '77777777-7777-7777-7777-777777777777', 'Thẻ đội (Team cards)', 8, '2024-01-01 00:00:00+00'),
    ('a7777777-7777-7777-7777-777777777774', '77777777-7777-7777-7777-777777777777', 'Thẻ bẫy/bystander', 1, '2024-01-01 00:00:00+00'),
    ('a7777777-7777-7777-7777-777777777775', '77777777-7777-7777-7777-777777777777', 'Giá đỡ thẻ (Card stand)', 1, '2024-01-01 00:00:00+00'),
    ('a8888888-8888-8888-8888-888888888881', '88888888-8888-8888-8888-888888888888', 'Bản đồ thế giới', 1, '2024-01-01 00:00:00+00'),
    ('a8888888-8888-8888-8888-888888888882', '88888888-8888-8888-8888-888888888888', 'Thẻ thành phố', 48, '2024-01-01 00:00:00+00'),
    ('a8888888-8888-8888-8888-888888888883', '88888888-8888-8888-8888-888888888888', 'Thẻ dịch bệnh', 96, '2024-01-01 00:00:00+00'),
    ('a8888888-8888-8888-8888-888888888884', '88888888-8888-8888-8888-888888888888', 'Thẻ nhân vật', 5, '2024-01-01 00:00:00+00'),
    ('a8888888-8888-8888-8888-888888888885', '88888888-8888-8888-8888-888888888888', 'Mẫu nhân vật (Pawns)', 5, '2024-01-01 00:00:00+00'),
    ('a8888888-8888-8888-8888-888888888886', '88888888-8888-8888-8888-888888888888', 'Thẻ nghiên cứu', 48, '2024-01-01 00:00:00+00'),
    ('a8888888-8888-8888-8888-888888888887', '88888888-8888-8888-8888-888888888888', 'Xúc xắc dịch bệnh', 1, '2024-01-01 00:00:00+00')
ON CONFLICT ("Id") DO UPDATE SET
    "ComponentName" = EXCLUDED."ComponentName",
    "DefaultQuantity" = EXCLUDED."DefaultQuantity";

-- Search aliases (fuzzy search tiếng Việt)
UPDATE "GameTemplates" SET "SearchAliases" = 'Ma Sói, Ma Soi, Werewolf, Ma Sói Ultimate', "SearchAliasesKey" = 'ma soi, ma soi, werewolf, ma soi ultimate'
WHERE "BggGameId" = 925 OR "Id" = '55555555-5555-5555-5555-555555555555';

UPDATE "GameTemplates" SET "SearchAliases" = 'Avalon, Kháng Chiến, The Resistance', "SearchAliasesKey" = 'avalon, khang chien, the resistance'
WHERE "BggGameId" = 128882 OR "Id" = '66666666-6666-6666-6666-666666666666';

UPDATE "GameTemplates" SET "SearchAliases" = 'Codenames, Mật Danh', "SearchAliasesKey" = 'codenames, mat danh'
WHERE "BggGameId" = 178900 OR "Id" = '77777777-7777-7777-7777-777777777777';

UPDATE "GameTemplates" SET "SearchAliases" = 'Catán, Settlers of Catan', "SearchAliasesKey" = 'catan, settlers of catan'
WHERE "BggGameId" = 13 OR "Id" = '11111111-1111-1111-1111-111111111111';

UPDATE "GameTemplates" SET "SearchAliases" = 'Cờ Tỷ Phú', "SearchAliasesKey" = 'co ty phu'
WHERE "BggGameId" = 1406 OR "Id" = '22222222-2222-2222-2222-222222222222';

UPDATE "GameTemplates" SET "SearchAliases" = 'Ticket to Ride, Tàu Hỏa Miền Tây', "SearchAliasesKey" = 'ticket to ride, tau hoa mien tay'
WHERE "BggGameId" = 9209;

UPDATE "GameTemplates" SET "SearchAliases" = 'Pandemic, Đại Dịch', "SearchAliasesKey" = 'pandemic, dai dich'
WHERE "BggGameId" = 30549 OR "Id" = '88888888-8888-8888-8888-888888888888';

UPDATE "GameTemplates" SET "SearchAliases" = 'Wingspan, Chim', "SearchAliasesKey" = 'wingspan, chim'
WHERE "BggGameId" = 266192;

UPDATE "GameTemplates" SET "SearchAliases" = 'Terraforming Mars, Sao Hỏa', "SearchAliasesKey" = 'terraforming mars, sao hoa'
WHERE "BggGameId" = 167791;

UPDATE "GameTemplates" SET "SearchAliases" = 'Splendor, Ngọc Trai', "SearchAliasesKey" = 'splendor, ngoc trai'
WHERE "BggGameId" = 148228 OR "Id" = '44444444-4444-4444-4444-444444444444';

-- Gắn thể loại cho game BGG/catalog (theo BggGameId)
INSERT INTO "GameTemplateCategories" ("GameTemplateId", "CategoryId", "CreatedAt")
SELECT g."Id", c."Id", '2024-01-01 00:00:00+00'
FROM "GameTemplates" g
JOIN "Categories" c ON (
    (g."BggGameId" = 13 AND c."Slug" IN ('chien-thuat', 'doi-khang')) OR
    (g."BggGameId" = 1406 AND c."Slug" IN ('giai-tri', 'doi-khang')) OR
    (g."BggGameId" = 2225 AND c."Slug" = 'giai-tri') OR
    (g."BggGameId" = 148228 AND c."Slug" = 'chien-thuat') OR
    (g."BggGameId" = 925 AND c."Slug" IN ('an-vai', 'giai-tri')) OR
    (g."BggGameId" = 128882 AND c."Slug" IN ('an-vai', 'chien-thuat')) OR
    (g."BggGameId" = 178900 AND c."Slug" IN ('an-vai', 'giai-tri')) OR
    (g."BggGameId" = 30549 AND c."Slug" IN ('hop-tac', 'chien-thuat')) OR
    (g."BggGameId" = 9209 AND c."Slug" IN ('chien-thuat', 'giai-tri')) OR
    (g."BggGameId" = 822 AND c."Slug" = 'chien-thuat') OR
    (g."BggGameId" = 266192 AND c."Slug" = 'chien-thuat') OR
    (g."BggGameId" = 230802 AND c."Slug" = 'chien-thuat') OR
    (g."BggGameId" = 167791 AND c."Slug" = 'chien-thuat') OR
    (g."BggGameId" = 174430 AND c."Slug" IN ('phieu-luu', 'hop-tac', 'chien-thuat'))
)
ON CONFLICT ("GameTemplateId", "CategoryId") DO NOTHING;
