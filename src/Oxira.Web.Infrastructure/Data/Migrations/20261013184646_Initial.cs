using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oxira.Web.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentDemos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Business = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    Lang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Profile = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Welcome = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SuggestionsJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Example = table.Column<bool>(type: "bit", nullable: false),
                    Messages = table.Column<int>(type: "int", nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentDemos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DemoId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Plan = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Business = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Whatsapp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    City = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Lang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Url = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Host = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Lang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Lang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Page = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentPacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    PostsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Drafts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    SpecJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Used = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TypeName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Lang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckedInAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventTickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Buyer = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    Product = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Site = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Company = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Service = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Lang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Page = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MagicLinks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReturnUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MagicLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Product = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Site = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    N = table.Column<int>(type: "int", nullable: true),
                    Date = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    Href = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    TicketId = table.Column<int>(type: "int", nullable: true),
                    Read = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1001, 1"),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ClientType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Template = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SiteName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SiteLang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    DomainOption = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    DomainName = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    Addons = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Plan = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DomainPaid = table.Column<bool>(type: "bit", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    SpecJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MetaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(63)", maxLength: 63, nullable: false),
                    Verified = table.Column<bool>(type: "bit", nullable: false),
                    VerifyToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Ref = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Lang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Page = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Partners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Company = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    PaidOut = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Lang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    SubscriptionId = table.Column<int>(type: "int", nullable: true),
                    TicketId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ProviderId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActingOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SessionStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ReplacedById = table.Column<long>(type: "bigint", nullable: true),
                    CreatedByIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Lang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(90)", maxLength: 90, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeoProfiles",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    BriefJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GbpJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WpUrl = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WpUser = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    WpSecret = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WpOk = table.Column<bool>(type: "bit", nullable: false),
                    ReportJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HistoryJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RanksJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HealthJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastDraftAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeoProfiles", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "SiteBookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Service = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Date = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Time = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Lang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteBookings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Lang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SiteReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Hashtags = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Day = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Time = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Platforms = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Bg = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Image = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Idea = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialProfiles",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    BriefJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MetaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MetaSecret = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReportJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastPlanMonth = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialProfiles", x => x.OrderId);
                });

            migrationBuilder.CreateTable(
                name: "StatusChecks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Ok = table.Column<bool>(type: "bit", nullable: false),
                    Ms = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatusChecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Product = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Period = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    NextDue = table.Column<DateOnly>(type: "date", nullable: true),
                    PendingPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupportTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    UnreadByOwner = table.Column<bool>(type: "bit", nullable: false),
                    UnreadByStaff = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    MemberUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Role = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Testimonials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Company = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    Lang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Testimonials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Uploads",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Mime = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Size = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Uploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageCounters",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageCounters", x => new { x.Key, x.Day });
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Company = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AvatarId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmailVerified = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactor = table.Column<bool>(type: "bit", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    SecurityStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Suspended = table.Column<bool>(type: "bit", nullable: false),
                    FailedLogins = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Lang = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TicketMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TicketId = table.Column<int>(type: "int", nullable: false),
                    From = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    By = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImagesJson = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketMessages_SupportTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "SupportTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentDemos_CreatedAt",
                table: "AgentDemos",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditRuns_CreatedAt",
                table: "AuditRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_Channel_SessionId_Id",
                table: "ChatMessages",
                columns: new[] { "Channel", "SessionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPacks_OrderId_Month",
                table: "ContentPacks",
                columns: new[] { "OrderId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailCodes_Email_Purpose_CreatedAt",
                table: "EmailCodes",
                columns: new[] { "Email", "Purpose", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EventTickets_Code",
                table: "EventTickets",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventTickets_OrderId_Type_Status",
                table: "EventTickets",
                columns: new[] { "OrderId", "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PaymentId",
                table: "Invoices",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_UserId",
                table: "Invoices",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_Kind_CreatedAt",
                table: "Leads",
                columns: new[] { "Kind", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MagicLinks_CodeHash",
                table: "MagicLinks",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MagicLinks_Email_CreatedAt",
                table: "MagicLinks",
                columns: new[] { "Email", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_Read_Id",
                table: "Notifications",
                columns: new[] { "UserId", "Read", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Email",
                table: "Orders",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Ref",
                table: "Orders",
                column: "Ref");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Slug",
                table: "Orders",
                column: "Slug");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId",
                table: "Orders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Partners_Code",
                table: "Partners",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_Email",
                table: "Partners",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_UserId",
                table: "Partners",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                table: "Payments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ProviderId",
                table: "Payments",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SubscriptionId",
                table: "Payments",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_FamilyId",
                table: "RefreshTokens",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_RevokedAt",
                table: "RefreshTokens",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SeoItems_OrderId_Status",
                table: "SeoItems",
                columns: new[] { "OrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteBookings_OrderId_Date_Time",
                table: "SiteBookings",
                columns: new[] { "OrderId", "Date", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_SiteMessages_OrderId",
                table: "SiteMessages",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteReviews_OrderId",
                table: "SiteReviews",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialItems_OrderId_Month",
                table: "SocialItems",
                columns: new[] { "OrderId", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialItems_Status_Day",
                table: "SocialItems",
                columns: new[] { "Status", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_StatusChecks_At",
                table: "StatusChecks",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_StatusChecks_Key_At",
                table: "StatusChecks",
                columns: new[] { "Key", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_OrderId_Product",
                table: "Subscriptions",
                columns: new[] { "OrderId", "Product" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_Status_NextDue",
                table: "Subscriptions",
                columns: new[] { "Status", "NextDue" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_OwnerId_UpdatedAt",
                table: "SupportTickets",
                columns: new[] { "OwnerId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_MemberUserId",
                table: "TeamMembers",
                column: "MemberUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_OwnerId_Email",
                table: "TeamMembers",
                columns: new[] { "OwnerId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Testimonials_Status_CreatedAt",
                table: "Testimonials",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketMessages_TicketId",
                table: "TicketMessages",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_Uploads_CreatedAt",
                table: "Uploads",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentDemos");

            migrationBuilder.DropTable(
                name: "AgentSubscriptions");

            migrationBuilder.DropTable(
                name: "AuditRuns");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ContentPacks");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "Drafts");

            migrationBuilder.DropTable(
                name: "EmailCodes");

            migrationBuilder.DropTable(
                name: "EventTickets");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "Leads");

            migrationBuilder.DropTable(
                name: "MagicLinks");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Partners");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "SeoItems");

            migrationBuilder.DropTable(
                name: "SeoProfiles");

            migrationBuilder.DropTable(
                name: "SiteBookings");

            migrationBuilder.DropTable(
                name: "SiteMessages");

            migrationBuilder.DropTable(
                name: "SiteReviews");

            migrationBuilder.DropTable(
                name: "SocialItems");

            migrationBuilder.DropTable(
                name: "SocialProfiles");

            migrationBuilder.DropTable(
                name: "StatusChecks");

            migrationBuilder.DropTable(
                name: "Subscriptions");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "Testimonials");

            migrationBuilder.DropTable(
                name: "TicketMessages");

            migrationBuilder.DropTable(
                name: "Uploads");

            migrationBuilder.DropTable(
                name: "UsageCounters");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "SupportTickets");
        }
    }
}
