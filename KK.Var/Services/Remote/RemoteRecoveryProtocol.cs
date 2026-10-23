using System;
using System.Linq;
using System.Text.RegularExpressions;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;
using static KK.Var.Services.RemoteShell;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services
{
    internal static class RemoteRecoveryProtocol
    {
        public static string PrepareOperation(string target, string operationId)
        {
            ValidateDirectory(target);
            return ShellCommand(Header(target, operationId, allowPreviousMarker: true) + "\n" + """
                test ! -e "$receipt" && test ! -e "$backup" || fail
                mkdir -p -- "$(dirname -- "$target")"
                mkdir -- "$receipt"
                printf '%s' "$op" > "$receipt/owner.tmp"
                mv -- "$receipt/owner.tmp" "$receipt/owner"
                printf '%s' preparing > "$receipt/phase.tmp"
                mv -- "$receipt/phase.tmp" "$receipt/phase"
                """);
        }

        public static string PrepareSwitch(string target, string operationId, bool cleanDeployment)
        {
            ValidateDirectory(target);
            var script = Header(target, operationId, allowPreviousMarker: true) + "\n" + $$"""
                staging="$target.staging-$op"
                test ! -L "$staging" && test ! -L "$staging{{DeploymentConstants.MarkerRelativePath}}" || fail
                test "$(cat "$staging{{DeploymentConstants.MarkerRelativePath}}")" = "$op" || fail
                test ! -e "$backup" || fail
                """ + "\n" + Prepare + "\n" + $$"""
                test "$(cat "$receipt/phase")" = preparing || fail
                previous=0
                if {{(cleanDeployment ? "false" : "true")}} && test -e "$target"; then
                    test -d "$target" || fail
                    previous=1
                    printf '%s' "$op" > "$restored"
                    rm -f -- "$marker"
                fi
                printf '%s' "$previous" > "$receipt/previous.tmp"
                mv -- "$receipt/previous.tmp" "$receipt/previous"
                printf '%s' switching > "$receipt/phase.tmp"
                mv -- "$receipt/phase.tmp" "$receipt/phase"
                """;
            return ShellCommand(script);
        }

        public static string PrepareRollback(string target, string operationId) =>
            ShellCommand(Header(target, operationId) + "\n" + Prepare + "\n" + MarkBackup);

        public static string Recover(KKProject project, string operationId, DeploymentUnitChange unitChange)
        {
            var target = project.RemoteDeploymentDirectory.TrimEnd('/');
            ValidateDirectory(target);
            if (!Regex.IsMatch(project.RemoteServiceName, DeploymentConstants.ServiceNameValidationPattern, RegexOptions.CultureInvariant))
            {
                throw new InvalidOperationException(Texts.RecoveryTargetChanged);
            }
            var unit = DeploymentConstants.SystemdDirectory + project.RemoteServiceName;
            var unitBackup = DeploymentConstants.RemoteTemporaryPrefix + operationId + DeploymentConstants.ServiceBackupExtension;
            var restoreUnit = unitChange switch
            {
                DeploymentUnitChange.Created => $"rm -f -- {Q(unit)}; systemctl daemon-reload;",
                DeploymentUnitChange.Changed => $"test -f {Q(unitBackup)} || fail; install -o root -g root -m 0644 {Q(unitBackup)} {Q(unit)}; systemctl daemon-reload;",
                _ => "true;",
            };
            var restart = unitChange == DeploymentUnitChange.Created ?
                "outcome=5;" :
                $"systemctl restart -- {Q(project.RemoteServiceName)}; systemctl is-active --quiet -- {Q(project.RemoteServiceName)}; outcome=2;";
            var script = Header(target, operationId) + "\n" + Prepare + "\n" + MarkBackup + "\n" +
                $"test ! -L {Q(unit)} && test ! -L {Q(unitBackup)} || fail\n" + $$"""
                test ! -f "$receipt/committed" || fail
                if test "$(cat "$receipt/phase")" = preparing; then
                    {{CleanupTemporary(target, operationId)}}
                    printf '3\n'
                    exit 0
                fi
                test "$(cat "$receipt/phase")" = switching || fail
                if test -f "$receipt/complete"; then
                    outcome=$(cat "$receipt/complete")
                    case "$outcome" in 2|4|5) ;; *) fail ;; esac
                    if test "$outcome" = 4; then test ! -e "$target" || fail; else recovered_matches || fail; fi
                    if test "$outcome" = 2; then
                        systemctl is-active --quiet -- {{Q(project.RemoteServiceName)}} || {
                            systemctl restart -- {{Q(project.RemoteServiceName)}}
                            systemctl is-active --quiet -- {{Q(project.RemoteServiceName)}}
                        }
                    else
                        if systemctl is-active --quiet -- {{Q(project.RemoteServiceName)}}; then fail; fi
                    fi
                    printf '%s\n' "$outcome"
                    exit 0
                fi
                previous=$(cat "$receipt/previous")
                case "$previous" in 0|1) ;; *) fail ;; esac
                if test -e "$target"; then marker_matches || recovered_matches || fail; fi
                if test "$previous" = 1; then
                    test -d "$backup" || recovered_matches || fail
                else
                    marker_matches || test ! -e "$target" || fail
                fi
                """ + "\n" +
                (unitChange == DeploymentUnitChange.Changed ?
                    $"test -f \"$receipt/unit-restored\" || test -f {Q(unitBackup)} || fail\n" : string.Empty) +
                $"if test \"$(systemctl show --property=LoadState --value -- {Q(project.RemoteServiceName)})\" != not-found; then systemctl stop -- {Q(project.RemoteServiceName)}; fi\n" + $$"""
                if marker_matches; then rm -rf -- "$target"; fi
                if test -e "$backup"; then
                    test ! -e "$target" || fail
                    mv -- "$backup" "$target"
                fi
                if test "$previous" = 1; then recovered_matches || fail; else test ! -e "$target" || fail; fi
                if test ! -f "$receipt/unit-restored"; then
                    {{restoreUnit}}
                    touch "$receipt/unit-restored"
                fi
                if test "$previous" = 1; then
                    {{restart}}
                else
                    outcome=4
                fi
                printf '%s' "$outcome" > "$receipt/complete.tmp"
                mv -- "$receipt/complete.tmp" "$receipt/complete"
                {{CleanupTemporary(target, operationId)}}
                printf '%s\n' "$outcome"
                """;
            return ShellCommand(script);
        }

        public static string RecoverAtCheckpoint(KKProject project, string operationId, DeploymentStage stage, DeploymentUnitChange unitChange)
        {
            var target = project.RemoteDeploymentDirectory.TrimEnd('/');
            ValidateDirectory(target);
            if (stage == DeploymentStage.Committed)
            {
                return ShellCommand(Header(target, operationId) + "\n" + Prepare + "\n" + MarkBackup + "\n" + $$"""
                    test "$(cat "$receipt/phase")" = switching || fail
                    if ! marker_matches; then
                        test -f "$receipt/committed" && test "$(cat "$receipt/committed")" = "$op" || fail
                        test "$(cat "$target/.kk-var-committed")" = "$op" || fail
                    fi
                    systemctl is-active --quiet -- {{Q(project.RemoteServiceName)}}
                    {{CommitProof}}
                    rm -rf -- "$backup"
                    {{CleanupTemporary(target, operationId)}}
                    rm -f -- "$marker"
                    printf '1\n'
                    """);
            }
            if (stage < DeploymentStage.SwitchingVersion)
            {
                return ShellCommand(Header(target, operationId) + "\n" + Prepare + "\n" + $$"""
                    test "$(cat "$receipt/phase")" = preparing && test ! -e "$backup" || fail
                    {{CleanupTemporary(target, operationId)}}
                    printf '3\n'
                    """);
            }
            return Recover(project, operationId, unitChange);
        }

        public static string CommitOwned(string target, string operationId) =>
            ShellCommand(Header(target, operationId) + "\n" + Prepare + "\n" + MarkBackup + "\nmarker_matches || fail\n" + CommitProof +
                "\nrm -rf -- \"$backup\"\n" + CleanupTemporary(target, operationId) + "\nrm -f -- \"$marker\"");

        public static string CleanupOwned(string target, string operationId) =>
            ShellCommand(Header(target, operationId) + "\n" + Prepare + "\n" + MarkBackup + "\n" + CleanupTemporary(target, operationId));

        private static string CleanupTemporary(string target, string operationId) => $$"""
            staging={{Q(target + ".staging-" + operationId)}}
            test ! -L "$staging" || fail
            if test -e "$staging"; then
                test ! -L "$staging{{DeploymentConstants.MarkerRelativePath}}" || fail
                test "$(cat "$staging{{DeploymentConstants.MarkerRelativePath}}")" = "$op" || fail
            fi
            """ + "\n" + $"rm -rf -- \"$staging\" {Q(DeploymentConstants.RemoteTemporaryPrefix + operationId + ArtifactConstants.ArchiveExtension)} " +
            $"{Q(DeploymentConstants.RemoteTemporaryPrefix + operationId + DeploymentConstants.ServiceExtension)} {Q(DeploymentConstants.RemoteTemporaryPrefix + operationId + DeploymentConstants.ServiceBackupExtension)}";

        private const string CommitProof = """
            printf '%s' "$op" > "$target/.kk-var-committed"
            printf '%s' "$op" > "$receipt/committed.tmp"
            mv -- "$receipt/committed.tmp" "$receipt/committed"
            """;

        private static string ShellCommand(string script) => "sudo sh -c " + Q(script.ReplaceLineEndings("\n"));

        private static void ValidateDirectory(string target)
        {
            if (!target.StartsWith('/') || target.Split('/', StringSplitOptions.RemoveEmptyEntries).Length < 2 ||
                target.Split('/').Any(part => part is "." or "..") || RemotePaths.IsProtectedDirectory(target))
            {
                throw new InvalidOperationException(Texts.RecoveryTargetChanged);
            }
        }

        private static string Header(string target, string operationId, bool allowPreviousMarker = false) => $$"""
            set -eu
            umask 077
            target={{Q(target)}}
            op={{Q(operationId)}}
            backup="$target.previous-$op"
            receipt="$target.recovery-$op"
            marker="$target{{DeploymentConstants.MarkerRelativePath}}"
            restored="$target/.kk-var-recovered"
            fail() { printf '%s\n' {{Q(Texts.RecoveryOwnershipUnknown)}} >&2; exit 73; }
            marker_matches() { test -f "$marker" && test "$(cat "$marker")" = "$op"; }
            recovered_matches() { test -f "$restored" && test "$(cat "$restored")" = "$op"; }
            for path in "$target" "$backup" "$receipt" "$marker" "$restored" "$target/.kk-var-committed"; do
                while test "$path" != /; do test ! -L "$path" || fail; path=$(dirname -- "$path"); done
            done
            for name in owner owner.tmp previous previous.tmp lock unit-restored complete complete.tmp phase phase.tmp committed committed.tmp; do
                test ! -L "$receipt/$name" || fail
            done
            """ + (allowPreviousMarker ? string.Empty : "\nif test -f \"$marker\"; then marker_matches || fail; fi");

        private const string Prepare = """
            test -d "$receipt" && test -f "$receipt/owner" && test -f "$receipt/phase" || fail
            test "$(cat "$receipt/owner")" = "$op" || fail
            test ! -L "$receipt/lock" || fail
            exec 9>"$receipt/lock"
            flock -n 9 || fail
            test "$(cat "$receipt/owner")" = "$op" || fail
            """;

        private const string MarkBackup = """
            if test -e "$backup"; then
                test -d "$backup" || fail
                test ! -L "$backup/.kk-var-recovered" || fail
                test "$(cat "$backup/.kk-var-recovered")" = "$op" || fail
            fi
            """;
    }
}
