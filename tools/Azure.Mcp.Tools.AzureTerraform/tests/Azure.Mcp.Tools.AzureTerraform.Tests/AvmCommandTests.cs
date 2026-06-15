// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.Mcp.Tools.AzureTerraform.Commands;
using Azure.Mcp.Tools.AzureTerraform.Models;
using Azure.Mcp.Tools.AzureTerraform.Services;
using Microsoft.Mcp.Tests.Client;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Azure.Mcp.Tools.AzureTerraform.Tests;

public class AvmCommandTests : CommandUnitTestsBase<AvmCommand, IAvmDocsService>
{
    [Fact]
    public void Constructor_InitializesCommandCorrectly()
    {
        Assert.Equal("avm", Command.Name);
        Assert.NotEmpty(Command.Description);
        Assert.NotEmpty(Command.Id);
        Assert.NotEmpty(Command.Title);
        Assert.False(Command.Metadata.Destructive);
        Assert.True(Command.Metadata.ReadOnly);
        Assert.True(Command.Metadata.Idempotent);
    }

    [Fact]
    public async Task ExecuteAsync_MissingMethod_ReturnsValidationError()
    {
        var response = await ExecuteCommandAsync([]);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidMethod_ReturnsError()
    {
        var response = await ExecuteCommandAsync("--method", "bogus");

        Assert.NotEqual(HttpStatusCode.OK, response.Status);
    }

    [Fact]
    public async Task ExecuteAsync_MethodVersions_RequiresModuleName()
    {
        var response = await ExecuteCommandAsync("--method", "versions");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
    }

    [Fact]
    public async Task ExecuteAsync_MethodGet_RequiresModuleName()
    {
        var response = await ExecuteCommandAsync("--method", "get");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
    }

    [Fact]
    public async Task ExecuteAsync_MethodList_ReturnsModules()
    {
        var modules = new List<AvmModule>
        {
            new() { ModuleName = "avm-res-storage-storageaccount", ModuleType = "Resource", Source = "Azure/avm-res-storage-storageaccount/azurerm", RepoUrl = "https://github.com/Azure/terraform-azurerm-avm-res-storage-storageaccount" },
            new() { ModuleName = "avm-ptn-virtualnetwork", ModuleType = "Pattern", Source = "Azure/avm-ptn-virtualnetwork/azurerm", RepoUrl = "https://github.com/Azure/terraform-azurerm-avm-ptn-virtualnetwork" }
        };
        Service.ListModulesAsync(Arg.Any<CancellationToken>()).Returns(modules);

        var response = await ExecuteCommandAsync("--method", "list");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = ValidateAndDeserializeResponse(response, AzureTerraformJsonContext.Default.AvmModuleListResult);
        Assert.Equal(2, result.Modules.Count);
        Assert.Equal("Resource", result.Modules[0].ModuleType);
        Assert.Equal("Pattern", result.Modules[1].ModuleType);
    }

    [Fact]
    public async Task ExecuteAsync_MethodVersions_ReturnsVersions()
    {
        var versions = new List<AvmVersion>
        {
            new() { TagName = "0.5.0", CreatedAt = "2025-01-01T00:00:00Z", TarballUrl = "https://example/0.5.0.tar.gz", Prerelease = false },
            new() { TagName = "0.4.0", CreatedAt = "2024-10-01T00:00:00Z", TarballUrl = "https://example/0.4.0.tar.gz", Prerelease = false }
        };
        Service.GetVersionsAsync("avm-res-storage-storageaccount", Arg.Any<CancellationToken>()).Returns(versions);

        var response = await ExecuteCommandAsync("--method", "versions", "--module-name", "avm-res-storage-storageaccount");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = ValidateAndDeserializeResponse(response, AzureTerraformJsonContext.Default.AvmVersionListResult);
        Assert.Equal("avm-res-storage-storageaccount", result.ModuleName);
        Assert.Equal(2, result.Versions.Count);
        Assert.Equal("0.5.0", result.Versions[0].TagName);
    }

    [Fact]
    public async Task ExecuteAsync_MethodGet_DefaultsToConcise_StripsBodyToInputs()
    {
        const string readme = """
            # avm-res-storage-storageaccount

            This module creates Azure Storage Accounts.

            ## Required Inputs

            The following input variables are required:

            ### <a name="input_location"></a> [location](#input\_location)

            Description: Azure region where the resource should be deployed.

            Type: `string`

            ### <a name="input_name"></a> [name](#input\_name)

            Description: The storage account name.

            Type: `string`

            ## Optional Inputs

            The following input variables are optional (have default values):

            ### <a name="input_tags"></a> [tags](#input\_tags)

            Description: Tags to assign.

            Type: `map(string)`

            Default: `{}`

            ## Outputs

            The following outputs are exported.
            """;

        Service.GetDocumentationAsync("avm-res-storage-storageaccount", null, Arg.Any<CancellationToken>())
            .Returns(new AvmDocumentation("0.5.0", readme));

        var response = await ExecuteCommandAsync("--method", "get", "--module-name", "avm-res-storage-storageaccount");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = ValidateAndDeserializeResponse(response, AzureTerraformJsonContext.Default.AvmDocumentationResult);

        Assert.Equal("concise", result.ResponseFormat);
        Assert.Equal("0.5.0", result.ModuleVersion);
        Assert.Contains("Azure Storage Accounts", result.Summary);
        Assert.Contains("Required Inputs", result.Documentation);
        Assert.Contains("location", result.Documentation);
        Assert.Contains("name", result.Documentation);
        Assert.Contains("Optional Inputs", result.Documentation);
        Assert.Contains("tags", result.Documentation);
        Assert.DoesNotContain("Description:", result.Documentation);
        Assert.DoesNotContain("Default:", result.Documentation);
        Assert.DoesNotContain("Outputs", result.Documentation);
    }

    [Fact]
    public async Task ExecuteAsync_MethodGet_Detailed_ReturnsFullReadme()
    {
        const string readme = "# avm-res-foo\n\nFull README text with **everything**.\n\n## Required Inputs\n\n### name\n\nType: `string`\n";

        Service.GetDocumentationAsync("avm-res-foo", "1.2.3", Arg.Any<CancellationToken>())
            .Returns(new AvmDocumentation("1.2.3", readme));

        var response = await ExecuteCommandAsync(
            "--method", "get",
            "--module-name", "avm-res-foo",
            "--module-version", "1.2.3",
            "--response-format", "detailed");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        var result = ValidateAndDeserializeResponse(response, AzureTerraformJsonContext.Default.AvmDocumentationResult);

        Assert.Equal("detailed", result.ResponseFormat);
        Assert.Equal(readme, result.Documentation);
    }

    [Fact]
    public async Task ExecuteAsync_MethodGet_InvalidResponseFormat_ReturnsError()
    {
        var response = await ExecuteCommandAsync(
            "--method", "get",
            "--module-name", "avm-res-foo",
            "--response-format", "verbose");

        Assert.NotEqual(HttpStatusCode.OK, response.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceThrows_HandlesException()
    {
        Service.ListModulesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var response = await ExecuteCommandAsync("--method", "list");

        Assert.NotEqual(HttpStatusCode.OK, response.Status);
    }

    [Fact]
    public void BindOptions_BindsOptionsCorrectly()
    {
        var args = CommandDefinition.Parse([
            "--method", "get",
            "--module-name", "avm-res-storage-storageaccount",
            "--module-version", "0.4.0",
            "--response-format", "concise"
        ]);

        Assert.NotNull(args);
        Assert.Empty(args.Errors);

        var options = CommandDefinition.Options;
        Assert.Contains(options, o => o.Name == "--method");
        Assert.Contains(options, o => o.Name == "--module-name");
        Assert.Contains(options, o => o.Name == "--module-version");
        Assert.Contains(options, o => o.Name == "--response-format");
    }
}
