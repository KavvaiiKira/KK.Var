namespace KK.Var.Models
{
    public sealed class ProjectConfigurationDocument
    {
        public int FormatVersion { get; set; }

        public ProjectConfigurationData? Project { get; set; }
    }
}
