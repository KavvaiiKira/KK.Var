using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KK.Var.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RemoteMachines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    AuthenticationMethod = table.Column<int>(type: "INTEGER", nullable: false),
                    PrivateKeyPath = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    HostKeyFingerprint = table.Column<string>(type: "TEXT", nullable: true),
                    Architecture = table.Column<string>(type: "TEXT", nullable: true),
                    IsVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    ServerGeneration = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastVerifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteMachines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KKProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    RemoteMachineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    LocalDirectoryPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    GitHubRepositoryId = table.Column<long>(type: "INTEGER", nullable: true),
                    GitHubRepositoryFullName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    GitHubCloneUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    GitHubBranch = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    BuildProvider = table.Column<int>(type: "INTEGER", nullable: false),
                    BuildConfigurationJson = table.Column<string>(type: "TEXT", nullable: false),
                    RemoteServiceName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false, collation: "NOCASE"),
                    RemoteExecutableFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    RemoteDeploymentDirectory = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ProjectEnvironmentFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    EnvironmentFileFormat = table.Column<int>(type: "INTEGER", nullable: false),
                    HealthCheckType = table.Column<int>(type: "INTEGER", nullable: false),
                    HealthCheckTimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    HealthCheckIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    HealthCheckAttempts = table.Column<int>(type: "INTEGER", nullable: true),
                    HealthCheckStabilityDelaySeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    HealthCheckHttpUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    HealthCheckTcpPort = table.Column<int>(type: "INTEGER", nullable: true),
                    HealthCheckCommand = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    MaxStoredVersions = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 10),
                    MaxStoredVersionBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KKProjects", x => x.Id);
                    table.CheckConstraint("CK_KKProjects_MaxStoredVersionBytes", "MaxStoredVersionBytes IS NULL OR MaxStoredVersionBytes > 0");
                    table.CheckConstraint("CK_KKProjects_MaxStoredVersions", "MaxStoredVersions > 0");
                    table.CheckConstraint("CK_KKProjects_Source", "(SourceType = 1 AND LocalDirectoryPath IS NOT NULL AND GitHubRepositoryId IS NULL AND GitHubRepositoryFullName IS NULL AND GitHubCloneUrl IS NULL) OR (SourceType = 2 AND LocalDirectoryPath IS NULL AND GitHubRepositoryId IS NOT NULL AND GitHubRepositoryFullName IS NOT NULL AND GitHubCloneUrl IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_KKProjects_RemoteMachines_RemoteMachineId",
                        column: x => x.RemoteMachineId,
                        principalTable: "RemoteMachines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                    TargetArchitecture = table.Column<string>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false, collation: "NOCASE"),
                    ArtifactRelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ArtifactSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ArtifactSize = table.Column<long>(type: "INTEGER", nullable: false),
                    IsPinned = table.Column<bool>(type: "INTEGER", nullable: false),
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
                    OperationSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    KKProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RemoteMachineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RemoteMachineName = table.Column<string>(type: "TEXT", nullable: false),
                    RemoteHost = table.Column<string>(type: "TEXT", nullable: false),
                    RemotePort = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoteUserName = table.Column<string>(type: "TEXT", nullable: false),
                    RemoteArchitecture = table.Column<string>(type: "TEXT", nullable: false),
                    RemoteServerGeneration = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoteServiceNameSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    RemoteDirectorySnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    RemoteExecutableSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    KKProjectVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VersionTag = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    VersionDescription = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SourceCommitSha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    OperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoteOperationId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitChange = table.Column<int>(type: "INTEGER", nullable: false),
                    VariablesSnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LogPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    RecoveryRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    RecoveryAttemptsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KKProjectDeployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KKProjectDeployments_KKProjectVersions_KKProjectVersionId",
                        column: x => x.KKProjectVersionId,
                        principalTable: "KKProjectVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_KKProjectDeployments_KKProjects_KKProjectId",
                        column: x => x.KKProjectId,
                        principalTable: "KKProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KKProjectDeployments_RemoteMachines_RemoteMachineId",
                        column: x => x.RemoteMachineId,
                        principalTable: "RemoteMachines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "IX_KKProjectDeployments_OperationSequence",
                table: "KKProjectDeployments",
                column: "OperationSequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KKProjectDeployments_RemoteMachineId",
                table: "KKProjectDeployments",
                column: "RemoteMachineId");

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
                name: "IX_KKProjects_RemoteMachineId_RemoteServiceName",
                table: "KKProjects",
                columns: new[] { "RemoteMachineId", "RemoteServiceName" },
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

            migrationBuilder.DropTable(
                name: "RemoteMachines");
        }
    }
}
