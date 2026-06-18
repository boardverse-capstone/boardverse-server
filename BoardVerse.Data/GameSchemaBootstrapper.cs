using BoardVerse.Core.Data;
using BoardVerse.Core.Helpers;
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
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'UserProfiles') THEN
                        ALTER TABLE "UserProfiles" DROP COLUMN IF EXISTS "HomeAddress";

                        IF EXISTS (
                            SELECT 1 FROM information_schema.columns
                            WHERE table_schema = 'public'
                              AND table_name = 'UserProfiles'
                              AND column_name = 'DateOfBirth'
                              AND udt_name <> 'date'
                        ) THEN
                            ALTER TABLE "UserProfiles"
                            ALTER COLUMN "DateOfBirth" TYPE date
                            USING CASE
                                WHEN "DateOfBirth" IS NULL THEN NULL
                                ELSE ("DateOfBirth" AT TIME ZONE 'UTC')::date
                            END;
                        END IF;
                    END IF;
                END $$;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "LastKnownLatitude" double precision;
                ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "LastKnownLongitude" double precision;
                ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "LastLocationUpdatedAt" timestamp with time zone;
                ALTER TABLE "UserProfiles" ADD COLUMN IF NOT EXISTS "LastLocationSource" character varying(20);
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "PlayerLocationHistories" (
                    "Id" uuid PRIMARY KEY,
                    "UserId" uuid NOT NULL,
                    "Latitude" double precision NOT NULL,
                    "Longitude" double precision NOT NULL,
                    "Source" character varying(20) NOT NULL DEFAULT 'Gps',
                    "RecordedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "FK_PlayerLocationHistories_Users_UserId"
                        FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_PlayerLocationHistories_UserId_RecordedAt"
                    ON "PlayerLocationHistories" ("UserId", "RecordedAt" DESC);
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

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "CafePartnerApplications" (
                    "Id" uuid PRIMARY KEY,
                    "ContactName" character varying(100) NOT NULL,
                    "ContactEmail" character varying(256) NOT NULL,
                    "ContactPhone" character varying(50) NOT NULL,
                    "CafeName" character varying(200) NOT NULL,
                    "Address" character varying(500) NOT NULL,
                    "CafePhoneNumber" character varying(50),
                    "Description" character varying(1000),
                    "BusinessLicenseNumber" character varying(100),
                    "BusinessLicenseUrl" character varying(500),
                    "CafeImageUrl" character varying(500),
                    "Status" character varying(50) NOT NULL DEFAULT 'Pending',
                    "AdminNotes" character varying(1000),
                    "RejectionReason" character varying(1000),
                    "ResubmitCount" integer NOT NULL DEFAULT 0,
                    "SubmittedByUserId" uuid,
                    "ReviewedByAdminId" uuid,
                    "ReviewedAt" timestamp with time zone,
                    "CreatedManagerUserId" uuid,
                    "CreatedCafeId" uuid,
                    "SubmittedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "FK_CafePartnerApplications_SubmittedByUserId"
                        FOREIGN KEY ("SubmittedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_CafePartnerApplications_ReviewedByAdminId"
                        FOREIGN KEY ("ReviewedByAdminId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_CafePartnerApplications_CreatedManagerUserId"
                        FOREIGN KEY ("CreatedManagerUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_CafePartnerApplications_CreatedCafeId"
                        FOREIGN KEY ("CreatedCafeId") REFERENCES "Cafes" ("Id") ON DELETE SET NULL
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "BusinessLicenseNumber" character varying(100);
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "BusinessLicenseUrl" character varying(500);
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "CafeImageUrl" character varying(500);
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "ResubmitCount" integer NOT NULL DEFAULT 0;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "SubmittedByUserId" uuid;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                DO $$ BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public' AND table_name = 'CafePartnerApplications' AND column_name = 'ContactEmail'
                    ) THEN
                        CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_ContactEmail"
                            ON "CafePartnerApplications" ("ContactEmail");
                    END IF;
                END $$;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_Status"
                    ON "CafePartnerApplications" ("Status");
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_SubmittedByUserId"
                    ON "CafePartnerApplications" ("SubmittedByUserId");
                """);

            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "Hotline" character varying(11);
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "RepresentativeEmail" character varying(256);
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "RepresentativeName" character varying(100);
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "BusinessLicense" character varying(50);
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "BusinessLicenseImageUrl" character varying(500);
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "NumberOfTables" integer NOT NULL DEFAULT 1;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "NumberOfPrivateRooms" integer NOT NULL DEFAULT 0;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "MaximumCapacity" integer NOT NULL DEFAULT 1;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "SpaceImageUrlsJson" text NOT NULL DEFAULT '[]';
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "NumberOfGamesOwned" integer NOT NULL DEFAULT 1;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "PopularGamesList" character varying(2000) NOT NULL DEFAULT '';
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "HasGameMaster" boolean NOT NULL DEFAULT false;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "BillingModel" character varying(20) NOT NULL DEFAULT 'ByHour';
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "OpsAlertMessage" character varying(1000);
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "CommissionRate" numeric(5,2);
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "ContractSentAt" timestamp with time zone;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "ContractSignedAt" timestamp with time zone;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "RequiresCsSupport" boolean NOT NULL DEFAULT false;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "DisplayRankOverride" integer;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'CafePartnerApplications' AND column_name = 'ContactName') THEN
                        ALTER TABLE "CafePartnerApplications" ALTER COLUMN "ContactName" DROP NOT NULL;
                        ALTER TABLE "CafePartnerApplications" ALTER COLUMN "ContactEmail" DROP NOT NULL;
                        ALTER TABLE "CafePartnerApplications" ALTER COLUMN "ContactPhone" DROP NOT NULL;
                    END IF;
                END $$;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'CafePartnerApplications' AND column_name = 'ContactEmail') THEN
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
                        WHERE "ContactEmail" IS NOT NULL
                           OR "RepresentativeEmail" IS NULL
                           OR TRIM(COALESCE("RepresentativeEmail", '')) = '';
                    END IF;
                END $$;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                UPDATE "CafePartnerApplications" SET "Status" = 'PendingApproval' WHERE "Status" IN ('Pending', 'PendingReview', 'PendingInfo', 'NeedsMoreInfo');
                UPDATE "CafePartnerApplications" SET "Status" = 'Approved' WHERE "Status" IN ('Active', 'ContractSigned', 'PendingNegotiation');
                UPDATE "CafePartnerApplications" SET "Status" = 'Rejected' WHERE "Status" IN ('Cancelled', 'Rejected');
                """);

            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "WeekdayOpen" interval;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "WeekdayClose" interval;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "WeekendOpen" interval;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "WeekendClose" interval;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "TableLayoutJson" text NOT NULL DEFAULT '[]';
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "ApprovedAt" timestamp with time zone;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "OperationalProfileUpdatedAt" timestamp with time zone;
                ALTER TABLE "CafePartnerApplications" ALTER COLUMN "NumberOfTables" SET DEFAULT 0;
                ALTER TABLE "CafePartnerApplications" ALTER COLUMN "NumberOfGamesOwned" SET DEFAULT 0;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "PartnerOperationalStatus" character varying(20);
                ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "WeekdayOpen" interval;
                ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "WeekdayClose" interval;
                ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "WeekendOpen" interval;
                ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "WeekendClose" interval;
                UPDATE "Cafes" SET "PartnerOperationalStatus" = 'Active'
                WHERE "PartnerOperationalStatus" IS NULL AND "IsActive" = true;
                UPDATE "Cafes" SET "PartnerOperationalStatus" = 'DataBlank'
                WHERE "PartnerOperationalStatus" IS NULL AND "IsActive" = false;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE EXTENSION IF NOT EXISTS postgis;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "Latitude" double precision;
                ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "Longitude" double precision;
                ALTER TABLE "Cafes" ADD COLUMN IF NOT EXISTS "Location" geography(Point,4326);
                """);

            await context.Database.ExecuteSqlRawAsync("""
                UPDATE "Cafes"
                SET "Location" = ST_SetSRID(ST_MakePoint("Longitude", "Latitude"), 4326)::geography
                WHERE "Location" IS NULL
                  AND "Latitude" IS NOT NULL
                  AND "Longitude" IS NOT NULL;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_Cafes_Location" ON "Cafes" USING GIST ("Location");
                """);

            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "Latitude" double precision;
                ALTER TABLE "CafePartnerApplications" ADD COLUMN IF NOT EXISTS "Longitude" double precision;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_RepresentativeEmail"
                    ON "CafePartnerApplications" ("RepresentativeEmail");
                CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_BusinessLicense"
                    ON "CafePartnerApplications" ("BusinessLicense");
                CREATE INDEX IF NOT EXISTS "IX_CafePartnerApplications_Hotline"
                    ON "CafePartnerApplications" ("Hotline");
                """);

            // Backfill legacy Contact* from new columns (only when legacy columns still exist).
            await context.Database.ExecuteSqlRawAsync("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'CafePartnerApplications' AND column_name = 'ContactEmail') THEN
                        UPDATE "CafePartnerApplications"
                        SET "ContactEmail" = COALESCE(NULLIF(TRIM("ContactEmail"), ''), "RepresentativeEmail"),
                            "ContactPhone" = COALESCE(NULLIF(TRIM("ContactPhone"), ''), "Hotline"),
                            "ContactName" = COALESCE(NULLIF(TRIM("ContactName"), ''), "RepresentativeName", '')
                        WHERE TRIM(COALESCE("RepresentativeEmail", '')) <> ''
                           OR TRIM(COALESCE("Hotline", '')) <> '';
                    END IF;
                END $$;
                """);

            // Drop legacy / unused columns — entity only maps Hotline, RepresentativeEmail, etc.
            await context.Database.ExecuteSqlRawAsync("""
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
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "CafeTables" (
                    "Id" uuid PRIMARY KEY,
                    "CafeId" uuid NOT NULL,
                    "Name" character varying(100) NOT NULL,
                    "SortOrder" integer NOT NULL DEFAULT 0,
                    "Status" character varying(20) NOT NULL DEFAULT 'Available',
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone,
                    "IsActive" boolean NOT NULL DEFAULT true,
                    CONSTRAINT "FK_CafeTables_Cafes_CafeId"
                        FOREIGN KEY ("CafeId") REFERENCES "Cafes" ("Id") ON DELETE CASCADE
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CafeTables_CafeId_Name"
                    ON "CafeTables" ("CafeId", "Name")
                    WHERE "IsActive" = true;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_CafeTables_CafeId_Status"
                    ON "CafeTables" ("CafeId", "Status")
                    WHERE "IsActive" = true;
                """);
        }

        public static async Task EnsureGameTablesAsync(BoardVerseDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync("""
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
                """);

            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "NameSearchKey" character varying(200) NOT NULL DEFAULT '';
                ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "SearchAliases" character varying(500);
                ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "SearchAliasesKey" character varying(500) NOT NULL DEFAULT '';
                ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "IsActive" boolean NOT NULL DEFAULT true;
                ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "BggId" integer;
                ALTER TABLE "GameTemplates" ADD COLUMN IF NOT EXISTS "BggSyncedAt" timestamp with time zone;
                ALTER TABLE "GameTemplates" DROP COLUMN IF EXISTS "BggGameId";
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_GameTemplates_BggId"
                    ON "GameTemplates" ("BggId")
                    WHERE "BggId" IS NOT NULL;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_GameTemplates_NameSearchKey"
                    ON "GameTemplates" ("NameSearchKey");
                CREATE INDEX IF NOT EXISTS "IX_GameTemplates_SearchAliasesKey"
                    ON "GameTemplates" ("SearchAliasesKey");
                """);

            await context.Database.ExecuteSqlRawAsync("""
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
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Categories_Slug"
                    ON "Categories" ("Slug");
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "GameTemplateCategories" (
                    "GameTemplateId" uuid NOT NULL,
                    "CategoryId" uuid NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    PRIMARY KEY ("GameTemplateId", "CategoryId"),
                    CONSTRAINT "FK_GameTemplateCategories_GameTemplates_GameTemplateId"
                        FOREIGN KEY ("GameTemplateId")
                        REFERENCES "GameTemplates" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_GameTemplateCategories_Categories_CategoryId"
                        FOREIGN KEY ("CategoryId")
                        REFERENCES "Categories" ("Id") ON DELETE CASCADE
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

            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "GameComponentTemplates" ADD COLUMN IF NOT EXISTS "ComponentKind" character varying(50);
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

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "CafeInventoryBoxes" (
                    "Id" uuid PRIMARY KEY,
                    "CafeGameInventoryId" uuid NOT NULL,
                    "Barcode" character varying(50) NOT NULL,
                    "Status" character varying(50) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone,
                    "IsActive" boolean NOT NULL DEFAULT true,
                    CONSTRAINT "FK_CafeInventoryBoxes_CafeGameInventories_CafeGameInventoryId"
                        FOREIGN KEY ("CafeGameInventoryId")
                        REFERENCES "CafeGameInventories" ("Id") ON DELETE CASCADE
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_CafeInventoryBoxes_Barcode"
                    ON "CafeInventoryBoxes" ("Barcode")
                    WHERE "IsActive" = true;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_CafeInventoryBoxes_Inventory_Status"
                    ON "CafeInventoryBoxes" ("CafeGameInventoryId", "Status")
                    WHERE "IsActive" = true;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "ActiveSessions" (
                    "Id" uuid PRIMARY KEY,
                    "CafeId" uuid NOT NULL,
                    "CafeTableId" uuid NOT NULL,
                    "CafeInventoryBoxId" uuid NOT NULL,
                    "GameTemplateId" uuid NOT NULL,
                    "StartedAt" timestamp with time zone NOT NULL,
                    "EndedAt" timestamp with time zone,
                    "IsActive" boolean NOT NULL DEFAULT true,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "FK_ActiveSessions_Cafes_CafeId"
                        FOREIGN KEY ("CafeId") REFERENCES "Cafes" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ActiveSessions_CafeTables_CafeTableId"
                        FOREIGN KEY ("CafeTableId") REFERENCES "CafeTables" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_ActiveSessions_CafeInventoryBoxes_CafeInventoryBoxId"
                        FOREIGN KEY ("CafeInventoryBoxId") REFERENCES "CafeInventoryBoxes" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_ActiveSessions_GameTemplates_GameTemplateId"
                        FOREIGN KEY ("GameTemplateId") REFERENCES "GameTemplates" ("Id") ON DELETE RESTRICT
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ActiveSessions_CafeInventoryBoxId"
                    ON "ActiveSessions" ("CafeInventoryBoxId")
                    WHERE "IsActive" = true;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_ActiveSessions_Cafe_Game_Active"
                    ON "ActiveSessions" ("CafeId", "GameTemplateId", "IsActive");
                """);
        }

        /// <summary>
        /// Creates CafeInventoryBoxes for active inventories that have no box rows yet (legacy data).
        /// </summary>
        public static async Task EnsureInventoryBoxBackfillAsync(BoardVerseDbContext context)
        {
            var inventories = await context.CafeGameInventories
                .Where(i => i.IsActive)
                .ToListAsync();

            var changed = false;
            foreach (var inventory in inventories)
            {
                var hasActiveBoxes = await context.CafeInventoryBoxes
                    .AnyAsync(b => b.CafeGameInventoryId == inventory.Id && b.IsActive);

                if (hasActiveBoxes)
                {
                    continue;
                }

                var existingBoxes = await context.CafeInventoryBoxes
                    .Where(b => b.CafeGameInventoryId == inventory.Id)
                    .ToListAsync();

                CafeInventoryBoxSyncHelper.ApplySync(inventory, existingBoxes);
                changed = true;
            }

            if (changed)
            {
                await context.SaveChangesAsync();
            }
        }

        public static async Task EnsureLobbyAndKarmaRatingTablesAsync(BoardVerseDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "Lobbies" (
                    "Id" uuid PRIMARY KEY,
                    "GameTemplateId" uuid NOT NULL,
                    "ActiveSessionId" uuid,
                    "Status" character varying(30) NOT NULL DEFAULT 'Open',
                    "RatingOpenedAt" timestamp with time zone,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "FK_Lobbies_GameTemplates_GameTemplateId"
                        FOREIGN KEY ("GameTemplateId") REFERENCES "GameTemplates" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_Lobbies_ActiveSessions_ActiveSessionId"
                        FOREIGN KEY ("ActiveSessionId") REFERENCES "ActiveSessions" ("Id") ON DELETE SET NULL
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_Lobbies_Status" ON "Lobbies" ("Status");
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "LobbyMembers" (
                    "Id" uuid PRIMARY KEY,
                    "LobbyId" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "JoinedAt" timestamp with time zone NOT NULL,
                    "IsActive" boolean NOT NULL DEFAULT true,
                    CONSTRAINT "FK_LobbyMembers_Lobbies_LobbyId"
                        FOREIGN KEY ("LobbyId") REFERENCES "Lobbies" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_LobbyMembers_Users_UserId"
                        FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_LobbyMembers_LobbyId_UserId"
                    ON "LobbyMembers" ("LobbyId", "UserId")
                    WHERE "IsActive" = true;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "PlayerKarmaRatings" (
                    "Id" uuid PRIMARY KEY,
                    "LobbyId" uuid NOT NULL,
                    "RaterUserId" uuid NOT NULL,
                    "TargetUserId" uuid NOT NULL,
                    "TagsJson" character varying(500) NOT NULL,
                    "KarmaDeltaApplied" numeric(6,2) NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "FK_PlayerKarmaRatings_Lobbies_LobbyId"
                        FOREIGN KEY ("LobbyId") REFERENCES "Lobbies" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_PlayerKarmaRatings_Users_RaterUserId"
                        FOREIGN KEY ("RaterUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_PlayerKarmaRatings_Users_TargetUserId"
                        FOREIGN KEY ("TargetUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlayerKarmaRatings_Lobby_Rater_Target"
                    ON "PlayerKarmaRatings" ("LobbyId", "RaterUserId", "TargetUserId");
                """);
        }

        public static async Task EnsureMatchResultTablesAsync(BoardVerseDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "MatchResults" (
                    "Id" uuid PRIMARY KEY,
                    "LobbyId" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "Outcome" character varying(20) NOT NULL,
                    "SubmittedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "FK_MatchResults_Lobbies_LobbyId"
                        FOREIGN KEY ("LobbyId") REFERENCES "Lobbies" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_MatchResults_Users_UserId"
                        FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_MatchResults_LobbyId_UserId"
                    ON "MatchResults" ("LobbyId", "UserId");
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "MatchHistories" (
                    "Id" uuid PRIMARY KEY,
                    "LobbyId" uuid NOT NULL,
                    "GameTemplateId" uuid NOT NULL,
                    "Status" character varying(30) NOT NULL,
                    "WinnerUserId" uuid,
                    "IsDraw" boolean NOT NULL DEFAULT false,
                    "FinalizedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "FK_MatchHistories_Lobbies_LobbyId"
                        FOREIGN KEY ("LobbyId") REFERENCES "Lobbies" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_MatchHistories_GameTemplates_GameTemplateId"
                        FOREIGN KEY ("GameTemplateId") REFERENCES "GameTemplates" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_MatchHistories_Users_WinnerUserId"
                        FOREIGN KEY ("WinnerUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_MatchHistories_LobbyId"
                    ON "MatchHistories" ("LobbyId");
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "MatchHistoryParticipants" (
                    "Id" uuid PRIMARY KEY,
                    "MatchHistoryId" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "ReportedOutcome" character varying(20) NOT NULL,
                    "EloBefore" integer NOT NULL,
                    "EloAfter" integer NOT NULL,
                    "EloDelta" integer NOT NULL,
                    CONSTRAINT "FK_MatchHistoryParticipants_MatchHistories_MatchHistoryId"
                        FOREIGN KEY ("MatchHistoryId") REFERENCES "MatchHistories" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_MatchHistoryParticipants_Users_UserId"
                        FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_MatchHistoryParticipants_MatchHistoryId_UserId"
                    ON "MatchHistoryParticipants" ("MatchHistoryId", "UserId");
                """);
        }

        public static async Task EnsureUserModerationColumnsAsync(BoardVerseDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "AccountStatus" character varying(20) NOT NULL DEFAULT 'Active';
                """);

            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "LockoutEndDate" timestamp with time zone;
                """);

            await context.Database.ExecuteSqlRawAsync("""
                UPDATE "Users"
                SET "AccountStatus" = CASE
                    WHEN "IsBlocked" = true AND COALESCE("BlockReason", '') ILIKE '%ban%' THEN 'Banned'
                    WHEN "IsBlocked" = true THEN 'Suspended'
                    ELSE 'Active'
                END
                WHERE "AccountStatus" IS NULL OR "AccountStatus" = 'Active' AND "IsBlocked" = true;
                """);
        }

        public static async Task EnsureKarmaLogAndSystemConfigTablesAsync(BoardVerseDbContext context)
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "KarmaLogs" (
                    "Id" uuid PRIMARY KEY,
                    "UserId" uuid NOT NULL,
                    "ViolationCategory" character varying(50) NOT NULL,
                    "Source" character varying(50) NOT NULL,
                    "DeltaAmount" numeric(8,2) NOT NULL,
                    "KarmaBefore" integer NOT NULL,
                    "KarmaAfter" integer NOT NULL,
                    "Reason" character varying(1000) NOT NULL,
                    "RelatedLobbyId" uuid,
                    "ActorUserId" uuid,
                    "IsAdminAdjustment" boolean NOT NULL DEFAULT false,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "FK_KarmaLogs_Users_UserId"
                        FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_KarmaLogs_Users_ActorUserId"
                        FOREIGN KEY ("ActorUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
                );
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_KarmaLogs_UserId" ON "KarmaLogs" ("UserId");
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_KarmaLogs_CreatedAt" ON "KarmaLogs" ("CreatedAt");
                """);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "SystemConfigurations" (
                    "ConfigKey" character varying(100) PRIMARY KEY,
                    "ConfigValue" character varying(500) NOT NULL,
                    "Description" character varying(1000) NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL
                );
                """);

            foreach (var (key, (value, description)) in SystemConfigKeys.SeedDefaults)
            {
                await context.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO "SystemConfigurations" ("ConfigKey", "ConfigValue", "Description", "UpdatedAt")
                    VALUES ({0}, {1}, {2}, NOW() AT TIME ZONE 'UTC')
                    ON CONFLICT ("ConfigKey") DO NOTHING;
                    """,
                    key,
                    value,
                    description);
            }
        }
    }
}
