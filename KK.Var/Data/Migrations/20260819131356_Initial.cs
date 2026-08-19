using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KK.Var.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KKProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    LocalDirectoryPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    GitHubRepositoryId = table.Column<long>(type: "INTEGER", nullable: true),
                    GitHubRepositoryFullName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    GitHubCloneUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    BuildProvider = table.Column<int>(type: "INTEGER", nullable: false),
                    BuildConfigurationJson = table.Column<string>(type: "TEXT", nullable: false),
                    EnvironmentFileFormat = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoteServiceName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false, collation: "NOCASE"),
                    RemoteExecutableFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    RemoteDeploymentDirectory = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ProjectEnvironmentFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KKProjects", x => x.Id);
                    table.CheckConstraint("CK_KKProjects_Source", "(SourceType = 1 AND LocalDirectoryPath IS NOT NULL AND GitHubRepositoryId IS NULL AND GitHubRepositoryFullName IS NULL AND GitHubCloneUrl IS NULL) OR (SourceType = 2 AND LocalDirectoryPath IS NULL AND GitHubRepositoryId IS NOT NULL AND GitHubRepositoryFullName IS NOT NULL AND GitHubCloneUrl IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "KKProjectEnvironmentVariables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KKProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KKProjectEnvironmentVariables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KKProjectEnvironmentVariables_KKProjects_KKProjectId",
                        column: x => x.KKProjectId,
                        principalTable: "KKProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KKProjectVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KKProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    ArtifactRelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ArtifactSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ArtifactSize = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceCommitSha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KKProjectVersions", x => x.Id);
                    table.CheckConstraint("CK_KKProjectVersions_ArtifactSize", "ArtifactSize >= 0");
                    table.ForeignKey(
                        name: "FK_KKProjectVersions_KKProjects_KKProjectId",
                        column: x => x.KKProjectId,
                        principalTable: "KKProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KKProjectDeployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KKProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KKProjectVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    VariablesSnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LogPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KKProjectDeployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KKProjectDeployments_KKProjectVersions_KKProjectVersionId",
                        column: x => x.KKProjectVersionId,
                        principalTable: "KKProjectVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KKProjectDeployments_KKProjects_KKProjectId",
                        column: x => x.KKProjectId,
                        principalTable: "KKProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KKProjectDeployments_KKProjectId_StartedAtUtc",
                table: "KKProjectDeployments",
                columns: new[] { "KKProjectId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_KKProjectDeployments_KKProjectVersionId",
                table: "KKProjectDeployments",
                column: "KKProjectVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_KKProjectDeployments_StartedAtUtc",
                table: "KKProjectDeployments",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_KKProjectEnvironmentVariables_KKProjectId_Name",
                table: "KKProjectEnvironmentVariables",
                columns: new[] { "KKProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KKProjectEnvironmentVariables_KKProjectId_SortOrder",
                table: "KKProjectEnvironmentVariables",
                columns: new[] { "KKProjectId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KKProjects_Name",
                table: "KKProjects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KKProjects_RemoteServiceName",
                table: "KKProjects",
                column: "RemoteServiceName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KKProjectVersions_KKProjectId_Tag",
                table: "KKProjectVersions",
                columns: new[] { "KKProjectId", "Tag" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KKProjectDeployments");

            migrationBuilder.DropTable(
                name: "KKProjectEnvironmentVariables");

            migrationBuilder.DropTable(
                name: "KKProjectVersions");

            migrationBuilder.DropTable(
                name: "KKProjects");
        }
    }
}
