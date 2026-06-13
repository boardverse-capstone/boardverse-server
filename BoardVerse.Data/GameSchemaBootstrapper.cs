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
                ALTER TABLE "GameTemplates" DROP COLUMN IF EXISTS "BggGameId";
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
