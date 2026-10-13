IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [AgentDemos] (
        [Id] nvarchar(16) NOT NULL,
        [Business] nvarchar(100) NOT NULL,
        [Url] nvarchar(300) NOT NULL,
        [Email] nvarchar(160) NOT NULL,
        [Phone] nvarchar(40) NOT NULL,
        [Notes] nvarchar(1500) NOT NULL,
        [Lang] nvarchar(5) NOT NULL,
        [Profile] nvarchar(max) NOT NULL,
        [Welcome] nvarchar(1000) NOT NULL,
        [SuggestionsJson] nvarchar(1000) NOT NULL,
        [Example] bit NOT NULL,
        [Messages] int NOT NULL,
        [Ip] nvarchar(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AgentDemos] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [AgentSubscriptions] (
        [Id] int NOT NULL IDENTITY,
        [DemoId] nvarchar(16) NOT NULL,
        [Plan] nvarchar(10) NOT NULL,
        [Name] nvarchar(80) NOT NULL,
        [Business] nvarchar(100) NOT NULL,
        [Email] nvarchar(160) NOT NULL,
        [Phone] nvarchar(40) NOT NULL,
        [Whatsapp] nvarchar(40) NOT NULL,
        [City] nvarchar(60) NOT NULL,
        [Notes] nvarchar(1000) NOT NULL,
        [Lang] nvarchar(5) NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AgentSubscriptions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [AuditRuns] (
        [Id] int NOT NULL IDENTITY,
        [Url] nvarchar(300) NOT NULL,
        [Host] nvarchar(200) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Email] nvarchar(160) NOT NULL,
        [Phone] nvarchar(40) NOT NULL,
        [Score] int NOT NULL,
        [ResultJson] nvarchar(max) NOT NULL,
        [Lang] nvarchar(5) NOT NULL,
        [Ip] nvarchar(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AuditRuns] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [ChatMessages] (
        [Id] bigint NOT NULL IDENTITY,
        [SessionId] nvarchar(64) NOT NULL,
        [Channel] nvarchar(20) NOT NULL,
        [Role] nvarchar(12) NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [Lang] nvarchar(5) NOT NULL,
        [Page] nvarchar(300) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_ChatMessages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [ContentPacks] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [Month] nvarchar(7) NOT NULL,
        [PostsJson] nvarchar(max) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_ContentPacks] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [DataProtectionKeys] (
        [Id] int NOT NULL IDENTITY,
        [FriendlyName] nvarchar(max) NULL,
        [Xml] nvarchar(max) NULL,
        CONSTRAINT [PK_DataProtectionKeys] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [Drafts] (
        [Id] nvarchar(48) NOT NULL,
        [SpecJson] nvarchar(max) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Drafts] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [EmailCodes] (
        [Id] uniqueidentifier NOT NULL,
        [Email] nvarchar(160) NOT NULL,
        [Purpose] nvarchar(12) NOT NULL,
        [CodeHash] nvarchar(64) NOT NULL,
        [Attempts] int NOT NULL,
        [Used] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_EmailCodes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [EventTickets] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [Code] nvarchar(16) NOT NULL,
        [Type] nvarchar(40) NOT NULL,
        [TypeName] nvarchar(80) NOT NULL,
        [Qty] int NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Phone] nvarchar(40) NOT NULL,
        [Email] nvarchar(160) NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [Lang] nvarchar(5) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CheckedInAt] datetimeoffset NULL,
        CONSTRAINT [PK_EventTickets] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [Invoices] (
        [Id] int NOT NULL IDENTITY,
        [Number] nvarchar(20) NOT NULL,
        [PaymentId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NULL,
        [Email] nvarchar(160) NOT NULL,
        [Buyer] nvarchar(120) NOT NULL,
        [OrderId] int NULL,
        [Product] nvarchar(20) NOT NULL,
        [Kind] nvarchar(14) NOT NULL,
        [Description] nvarchar(300) NOT NULL,
        [Site] nvarchar(253) NOT NULL,
        [Subtotal] decimal(12,2) NOT NULL,
        [Vat] decimal(12,2) NOT NULL,
        [Total] decimal(12,2) NOT NULL,
        [PaidAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Invoices] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [Leads] (
        [Id] int NOT NULL IDENTITY,
        [Kind] nvarchar(12) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Email] nvarchar(160) NOT NULL,
        [Phone] nvarchar(40) NOT NULL,
        [Company] nvarchar(160) NOT NULL,
        [Service] nvarchar(160) NOT NULL,
        [Details] nvarchar(max) NOT NULL,
        [Lang] nvarchar(5) NOT NULL,
        [Page] nvarchar(300) NOT NULL,
        [Ip] nvarchar(64) NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Leads] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [MagicLinks] (
        [Id] bigint NOT NULL IDENTITY,
        [Email] nvarchar(160) NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CodeHash] nvarchar(64) NOT NULL,
        [ReturnUrl] nvarchar(1000) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [UsedAt] datetimeoffset NULL,
        CONSTRAINT [PK_MagicLinks] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] uniqueidentifier NOT NULL,
        [Type] nvarchar(16) NOT NULL,
        [Product] nvarchar(20) NOT NULL,
        [Site] nvarchar(253) NOT NULL,
        [N] int NULL,
        [Date] nvarchar(20) NOT NULL,
        [Subject] nvarchar(140) NOT NULL,
        [Href] nvarchar(20) NOT NULL,
        [OrderId] int NULL,
        [TicketId] int NULL,
        [Read] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [Orders] (
        [Id] int NOT NULL IDENTITY(1001, 1),
        [Kind] nvarchar(12) NOT NULL,
        [UserId] uniqueidentifier NULL,
        [Email] nvarchar(160) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Phone] nvarchar(40) NOT NULL,
        [Notes] nvarchar(2000) NOT NULL,
        [ClientType] nvarchar(40) NOT NULL,
        [Template] nvarchar(60) NOT NULL,
        [SiteName] nvarchar(120) NOT NULL,
        [SiteLang] nvarchar(5) NOT NULL,
        [DomainOption] nvarchar(8) NOT NULL,
        [DomainName] nvarchar(253) NOT NULL,
        [Addons] nvarchar(200) NOT NULL,
        [Plan] nvarchar(20) NOT NULL,
        [DomainPaid] bit NOT NULL,
        [Subtotal] decimal(12,2) NOT NULL,
        [Vat] decimal(12,2) NOT NULL,
        [Total] decimal(12,2) NOT NULL,
        [SpecJson] nvarchar(max) NOT NULL,
        [SettingsJson] nvarchar(max) NOT NULL,
        [MetaJson] nvarchar(max) NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [Url] nvarchar(300) NOT NULL,
        [Slug] nvarchar(63) NOT NULL,
        [Verified] bit NOT NULL,
        [VerifyToken] nvarchar(64) NOT NULL,
        [Ref] nvarchar(20) NOT NULL,
        [Lang] nvarchar(5) NOT NULL,
        [Page] nvarchar(300) NOT NULL,
        [Ip] nvarchar(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        [PaidAt] datetimeoffset NULL,
        [PublishedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [Partners] (
        [Id] int NOT NULL IDENTITY,
        [UserId] uniqueidentifier NULL,
        [Name] nvarchar(120) NOT NULL,
        [Company] nvarchar(120) NOT NULL,
        [Phone] nvarchar(40) NOT NULL,
        [Email] nvarchar(160) NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [Rate] decimal(5,4) NOT NULL,
        [PaidOut] decimal(12,2) NOT NULL,
        [Lang] nvarchar(5) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Partners] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [Payments] (
        [Id] uniqueidentifier NOT NULL,
        [Kind] nvarchar(14) NOT NULL,
        [OrderId] int NULL,
        [SubscriptionId] int NULL,
        [TicketId] int NULL,
        [UserId] uniqueidentifier NULL,
        [Email] nvarchar(160) NOT NULL,
        [Subtotal] decimal(12,2) NOT NULL,
        [Vat] decimal(12,2) NOT NULL,
        [Amount] decimal(12,2) NOT NULL,
        [Description] nvarchar(300) NOT NULL,
        [Provider] nvarchar(12) NOT NULL,
        [ProviderId] nvarchar(80) NOT NULL,
        [Url] nvarchar(500) NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [PaidAt] datetimeoffset NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] uniqueidentifier NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [FamilyId] uniqueidentifier NOT NULL,
        [ActingOwnerId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [SessionStartedAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [RevokedAt] datetimeoffset NULL,
        [RevokedReason] nvarchar(20) NULL,
        [ReplacedById] bigint NULL,
        [CreatedByIp] nvarchar(64) NULL,
        [UserAgent] nvarchar(300) NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [SeoItems] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [Kind] nvarchar(10) NOT NULL,
        [Lang] nvarchar(5) NOT NULL,
        [Title] nvarchar(120) NOT NULL,
        [Description] nvarchar(200) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [Slug] nvarchar(90) NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [Url] nvarchar(400) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [DecidedAt] datetimeoffset NULL,
        CONSTRAINT [PK_SeoItems] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [SeoProfiles] (
        [OrderId] int NOT NULL,
        [BriefJson] nvarchar(max) NOT NULL,
        [GbpJson] nvarchar(max) NOT NULL,
        [WpUrl] nvarchar(200) NOT NULL,
        [WpUser] nvarchar(80) NOT NULL,
        [WpSecret] nvarchar(max) NOT NULL,
        [WpOk] bit NOT NULL,
        [ReportJson] nvarchar(max) NULL,
        [HistoryJson] nvarchar(max) NOT NULL,
        [RanksJson] nvarchar(max) NOT NULL,
        [HealthJson] nvarchar(max) NULL,
        [LastDraftAt] datetimeoffset NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_SeoProfiles] PRIMARY KEY ([OrderId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [SiteBookings] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Phone] nvarchar(40) NOT NULL,
        [Email] nvarchar(160) NOT NULL,
        [Service] nvarchar(160) NOT NULL,
        [Date] nvarchar(10) NOT NULL,
        [Time] nvarchar(5) NOT NULL,
        [Note] nvarchar(600) NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [Lang] nvarchar(5) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_SiteBookings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [SiteMessages] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Phone] nvarchar(40) NOT NULL,
        [Message] nvarchar(2000) NOT NULL,
        [Lang] nvarchar(5) NOT NULL,
        [Ip] nvarchar(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_SiteMessages] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [SiteReviews] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [Rating] int NOT NULL,
        [Comment] nvarchar(1500) NOT NULL,
        [Name] nvarchar(80) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_SiteReviews] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [SocialItems] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [Month] nvarchar(7) NOT NULL,
        [Kind] nvarchar(8) NOT NULL,
        [Status] nvarchar(10) NOT NULL,
        [Title] nvarchar(120) NOT NULL,
        [Caption] nvarchar(max) NOT NULL,
        [Hashtags] nvarchar(400) NOT NULL,
        [Day] nvarchar(10) NOT NULL,
        [Time] nvarchar(5) NOT NULL,
        [Platforms] nvarchar(40) NOT NULL,
        [Bg] nvarchar(48) NOT NULL,
        [Image] nvarchar(48) NOT NULL,
        [Idea] nvarchar(max) NOT NULL,
        [AdJson] nvarchar(max) NOT NULL,
        [ResultJson] nvarchar(max) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_SocialItems] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [SocialProfiles] (
        [OrderId] int NOT NULL,
        [BriefJson] nvarchar(max) NOT NULL,
        [MetaJson] nvarchar(max) NOT NULL,
        [MetaSecret] nvarchar(max) NOT NULL,
        [ReportJson] nvarchar(max) NULL,
        [LastPlanMonth] nvarchar(7) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_SocialProfiles] PRIMARY KEY ([OrderId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [StatusChecks] (
        [Id] bigint NOT NULL IDENTITY,
        [Key] nvarchar(20) NOT NULL,
        [Ok] bit NOT NULL,
        [Ms] int NOT NULL,
        [At] datetimeoffset NOT NULL,
        CONSTRAINT [PK_StatusChecks] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [Subscriptions] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [UserId] uniqueidentifier NULL,
        [Product] nvarchar(20) NOT NULL,
        [Price] decimal(12,2) NOT NULL,
        [Period] nvarchar(6) NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [NextDue] date NULL,
        [PendingPaymentId] uniqueidentifier NULL,
        [InvoiceUrl] nvarchar(500) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CancelledAt] datetimeoffset NULL,
        CONSTRAINT [PK_Subscriptions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [SupportTickets] (
        [Id] int NOT NULL IDENTITY,
        [Number] nvarchar(12) NOT NULL,
        [OwnerId] uniqueidentifier NOT NULL,
        [CreatedById] uniqueidentifier NOT NULL,
        [Email] nvarchar(160) NOT NULL,
        [Subject] nvarchar(140) NOT NULL,
        [Category] nvarchar(12) NOT NULL,
        [OrderId] int NULL,
        [Status] nvarchar(10) NOT NULL,
        [UnreadByOwner] bit NOT NULL,
        [UnreadByStaff] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_SupportTickets] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [TeamMembers] (
        [Id] int NOT NULL IDENTITY,
        [OwnerId] uniqueidentifier NOT NULL,
        [Email] nvarchar(160) NOT NULL,
        [MemberUserId] uniqueidentifier NULL,
        [Role] nvarchar(10) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [JoinedAt] datetimeoffset NULL,
        CONSTRAINT [PK_TeamMembers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [Testimonials] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(80) NOT NULL,
        [Email] nvarchar(160) NOT NULL,
        [Role] nvarchar(80) NOT NULL,
        [Company] nvarchar(100) NOT NULL,
        [Rating] int NOT NULL,
        [Text] nvarchar(800) NOT NULL,
        [Lang] nvarchar(5) NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Testimonials] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [Uploads] (
        [Id] nvarchar(48) NOT NULL,
        [Mime] nvarchar(30) NOT NULL,
        [Content] varbinary(max) NOT NULL,
        [Size] int NOT NULL,
        [SessionId] nvarchar(64) NOT NULL,
        [UserId] uniqueidentifier NULL,
        [Ip] nvarchar(64) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Uploads] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [UsageCounters] (
        [Key] nvarchar(120) NOT NULL,
        [Day] date NOT NULL,
        [Count] int NOT NULL,
        CONSTRAINT [PK_UsageCounters] PRIMARY KEY ([Key], [Day])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [Email] nvarchar(160) NOT NULL,
        [Name] nvarchar(80) NOT NULL,
        [Phone] nvarchar(20) NOT NULL,
        [Company] nvarchar(120) NOT NULL,
        [AvatarId] nvarchar(48) NULL,
        [PasswordHash] nvarchar(200) NULL,
        [EmailVerified] bit NOT NULL,
        [TwoFactor] bit NOT NULL,
        [Role] nvarchar(12) NOT NULL,
        [SecurityStamp] nvarchar(40) NOT NULL,
        [Suspended] bit NOT NULL,
        [FailedLogins] int NOT NULL,
        [LockedUntil] datetimeoffset NULL,
        [Lang] nvarchar(5) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [LastLoginAt] datetimeoffset NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE TABLE [TicketMessages] (
        [Id] bigint NOT NULL IDENTITY,
        [TicketId] int NOT NULL,
        [From] nvarchar(8) NOT NULL,
        [By] nvarchar(160) NOT NULL,
        [Text] nvarchar(max) NOT NULL,
        [ImagesJson] nvarchar(400) NOT NULL,
        [At] datetimeoffset NOT NULL,
        CONSTRAINT [PK_TicketMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TicketMessages_SupportTickets_TicketId] FOREIGN KEY ([TicketId]) REFERENCES [SupportTickets] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_AgentDemos_CreatedAt] ON [AgentDemos] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_AuditRuns_CreatedAt] ON [AuditRuns] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_ChatMessages_Channel_SessionId_Id] ON [ChatMessages] ([Channel], [SessionId], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ContentPacks_OrderId_Month] ON [ContentPacks] ([OrderId], [Month]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_EmailCodes_Email_Purpose_CreatedAt] ON [EmailCodes] ([Email], [Purpose], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EventTickets_Code] ON [EventTickets] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_EventTickets_OrderId_Type_Status] ON [EventTickets] ([OrderId], [Type], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Invoices_PaymentId] ON [Invoices] ([PaymentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Invoices_UserId] ON [Invoices] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Leads_Kind_CreatedAt] ON [Leads] ([Kind], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MagicLinks_CodeHash] ON [MagicLinks] ([CodeHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_MagicLinks_Email_CreatedAt] ON [MagicLinks] ([Email], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_Read_Id] ON [Notifications] ([UserId], [Read], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Orders_Email] ON [Orders] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Orders_Ref] ON [Orders] ([Ref]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Orders_Slug] ON [Orders] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Orders_UserId] ON [Orders] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Partners_Code] ON [Partners] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Partners_Email] ON [Partners] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Partners_UserId] ON [Partners] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Payments_OrderId] ON [Payments] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Payments_ProviderId] ON [Payments] ([ProviderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Payments_SubscriptionId] ON [Payments] ([SubscriptionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_FamilyId] ON [RefreshTokens] ([FamilyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId_RevokedAt] ON [RefreshTokens] ([UserId], [RevokedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_SeoItems_OrderId_Status] ON [SeoItems] ([OrderId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_SiteBookings_OrderId_Date_Time] ON [SiteBookings] ([OrderId], [Date], [Time]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_SiteMessages_OrderId] ON [SiteMessages] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_SiteReviews_OrderId] ON [SiteReviews] ([OrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_SocialItems_OrderId_Month] ON [SocialItems] ([OrderId], [Month]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_SocialItems_Status_Day] ON [SocialItems] ([Status], [Day]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_StatusChecks_At] ON [StatusChecks] ([At]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_StatusChecks_Key_At] ON [StatusChecks] ([Key], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Subscriptions_OrderId_Product] ON [Subscriptions] ([OrderId], [Product]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Subscriptions_Status_NextDue] ON [Subscriptions] ([Status], [NextDue]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_SupportTickets_OwnerId_UpdatedAt] ON [SupportTickets] ([OwnerId], [UpdatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_TeamMembers_MemberUserId] ON [TeamMembers] ([MemberUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TeamMembers_OwnerId_Email] ON [TeamMembers] ([OwnerId], [Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Testimonials_Status_CreatedAt] ON [Testimonials] ([Status], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_TicketMessages_TicketId] ON [TicketMessages] ([TicketId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE INDEX [IX_Uploads_CreatedAt] ON [Uploads] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013184646_Initial'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013184646_Initial', N'10.0.12');
END;

COMMIT;
GO

