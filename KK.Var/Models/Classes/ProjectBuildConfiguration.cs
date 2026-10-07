using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KK.Var.Models;

public sealed class ProjectBuildConfiguration
{
    public string? DotNetProjectPath { get; set; }

    public bool? CgoEnabled { get; set; }

    public string? PythonDependencyFilePath { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

    public string? Configuration { get; set; }

    public string? Command { get; set; }

    public string? WorkingDirectory { get; set; }

    public string? ToolchainFile { get; set; }

    public string? CmakeGenerator { get; set; }

    public List<string> ConfigureArguments { get; set; } = [];

    public List<string> BuildArguments { get; set; } = [];

    public Dictionary<string, string?> Environment { get; set; } = [];
}
