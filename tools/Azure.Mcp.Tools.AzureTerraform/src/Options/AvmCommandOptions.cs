// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.Mcp.Tools.AzureTerraform.Options;

public sealed class AvmCommandOptions
{
    public string? Method { get; set; }
    public string? ModuleName { get; set; }
    public string? ModuleVersion { get; set; }
    public string? ResponseFormat { get; set; }
}
