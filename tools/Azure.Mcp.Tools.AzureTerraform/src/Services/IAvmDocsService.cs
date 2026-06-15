// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.AzureTerraform.Models;

namespace Azure.Mcp.Tools.AzureTerraform.Services;

public interface IAvmDocsService
{
    Task<List<AvmModule>> ListModulesAsync(CancellationToken cancellationToken = default);

    Task<List<AvmVersion>> GetVersionsAsync(string moduleName, CancellationToken cancellationToken = default);

    Task<AvmDocumentation> GetDocumentationAsync(string moduleName, string? moduleVersion = null, CancellationToken cancellationToken = default);
}

public readonly record struct AvmDocumentation(string ResolvedVersion, string Documentation);
