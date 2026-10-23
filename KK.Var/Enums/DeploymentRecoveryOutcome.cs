namespace KK.Var.Enums
{
    public enum DeploymentRecoveryOutcome
    {
        NewVersionActive = 1,
        PreviousVersionRestored = 2,
        StagingCleaned = 3,
        NewVersionRemoved = 4,
        PreviousFilesRestored = 5,
    }
}
