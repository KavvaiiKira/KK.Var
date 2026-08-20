namespace KK.Var.Enums;

public enum DeploymentStage
{
    Preparing = 1,
    ReadyToSwitch = 2,
    SwitchingVersion = 3,
    VersionSwitched = 4,
    UpdatingUnit = 5,
    UnitUpdated = 6,
    StartingService = 7,
    ServiceStarted = 8,
    Committed = 9,
}
