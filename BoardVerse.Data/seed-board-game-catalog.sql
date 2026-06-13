-- BoardVerse: Board game catalog seed (idempotent)
-- Run via: dotnet run --project tools/ExecSql -- BoardVerse.Data/seed-board-game-catalog.sql
--
-- Mapping nghiệp vụ:
--   GameTemplates        = BoardGames
--   GameComponentTemplates = GameComponents
--   Categories           = Thể loại

-- ── Schema extensions (safe if already applied via bootstrapper) ─────────────
ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "NameSearchKey" character varying(200) NOT NULL DEFAULT '';
ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "SearchAliases" character varying(500);
ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "SearchAliasesKey" character varying(500) NOT NULL DEFAULT '';
ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;

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

CREATE INDEX IF NOT EXISTS "IX_GameTemplates_NameSearchKey" ON "GameTemplates" ("NameSearchKey");
CREATE INDEX IF NOT EXISTS "IX_GameTemplates_SearchAliasesKey" ON "GameTemplates" ("SearchAliasesKey");

-- ── Categories ────────────────────────────────────────────────────────────────
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

ALTER TABLE "GameTemplates" DROP COLUMN IF EXISTS "BggGameId";

-- ── Board games (GameTemplates) ───────────────────────────────────────────────
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

UPDATE "GameTemplates" SET "SearchAliases" = 'Catán, Settlers of Catan, Catan', "SearchAliasesKey" = 'catan settlers of catan catan' WHERE lower(trim("Name")) = 'catan';
UPDATE "GameTemplates" SET "SearchAliases" = 'Cờ Tỷ Phú', "SearchAliasesKey" = 'co ty phu' WHERE lower(trim("Name")) = 'monopoly';
UPDATE "GameTemplates" SET "SearchAliases" = 'Uno', "SearchAliasesKey" = 'uno' WHERE lower(trim("Name")) = 'uno';
UPDATE "GameTemplates" SET "SearchAliases" = 'Splendor, Ngọc Trai', "SearchAliasesKey" = 'splendor ngoc trai' WHERE lower(trim("Name")) = 'splendor';
UPDATE "GameTemplates" SET "SearchAliases" = 'Ma Sói, Ma Soi, Werewolf, Ma Sói Ultimate', "SearchAliasesKey" = 'ma soi ma soi werewolf ma soi ultimate' WHERE lower(trim("Name")) = 'werewolf ultimate';
UPDATE "GameTemplates" SET "SearchAliases" = 'Avalon, Kháng Chiến, The Resistance', "SearchAliasesKey" = 'avalon khang chien the resistance' WHERE lower(trim("Name")) = 'the resistance: avalon';
UPDATE "GameTemplates" SET "SearchAliases" = 'Codenames, Mật Danh', "SearchAliasesKey" = 'codenames mat danh' WHERE lower(trim("Name")) = 'codenames';
UPDATE "GameTemplates" SET "SearchAliases" = 'Pandemic, Đại Dịch', "SearchAliasesKey" = 'pandemic dai dich' WHERE lower(trim("Name")) = 'pandemic';

-- ── Game ↔ Category links ─────────────────────────────────────────────────────
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

-- ── Avalon components (AC 1.3 ví dụ) ────────────────────────────────────────
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
