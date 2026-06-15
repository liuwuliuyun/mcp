// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.Mcp.Tools.AzureTerraform.Models;
using Azure.Mcp.Tools.AzureTerraform.Options;
using Azure.Mcp.Tools.AzureTerraform.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Mcp.Core.Commands;
using Microsoft.Mcp.Core.Models.Command;

namespace Azure.Mcp.Tools.AzureTerraform.Commands;

[CommandMetadata(
    Id = "e5f6a7b8-c9d0-1234-ef01-456789012cde",
    Name = "get",
    Title = "Get AVM Module Documentation",
    Description = """
        Retrieves the documentation (README.md) for an Azure Verified Module (AVM), covering
        both resource modules (avm-res-*) and pattern modules (avm-ptn-*). Returns the full
        module documentation including usage examples, input variables, output values, and
        resource descriptions. Use --module-name to specify the module
        (e.g., avm-res-storage-storageaccount, avm-ptn-virtualnetwork). The --module-version
        is optional; when omitted, the latest stable (non-prerelease) release is used.
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = true,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class AvmDocumentationGetCommand(
    ILogger<AvmDocumentationGetCommand> logger,
    IAvmDocsService avmDocsService) : BaseCommand<AvmDocumentationOptions>
{
    private readonly ILogger<AvmDocumentationGetCommand> _logger = logger;
    private readonly IAvmDocsService _avmDocsService = avmDocsService;

    protected override void RegisterOptions(Command command)
    {
        base.RegisterOptions(command);
        command.Options.Add(AzureTerraformOptionDefinitions.AvmModuleName.AsRequired());
        command.Options.Add(AzureTerraformOptionDefinitions.AvmModuleVersion);
    }

    protected override AvmDocumentationOptions BindOptions(ParseResult parseResult)
    {
        return new AvmDocumentationOptions
        {
            ModuleName = parseResult.GetValueOrDefault<string>(AzureTerraformOptionDefinitions.AvmModuleName.Name),
            ModuleVersion = parseResult.GetValueOrDefault<string>(AzureTerraformOptionDefinitions.AvmModuleVersion.Name)
        };
    }

    public override async Task<CommandResponse> ExecuteAsync(
        CommandContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken)
    {
        if (!Validate(parseResult.CommandResult, context.Response).IsValid)
        {
            return context.Response;
        }

        var options = BindOptions(parseResult);

        try
        {
            var documentation = await _avmDocsService.GetDocumentationAsync(
                options.ModuleName!,
                options.ModuleVersion,
                cancellationToken).ConfigureAwait(false);

            var result = new Models.AvmDocumentationResult
            {
                ModuleName = options.ModuleName!,
                ModuleVersion = documentation.ResolvedVersion,
                Documentation = documentation.Documentation
            };

            context.Response.Status = HttpStatusCode.OK;
            context.Response.Results = ResponseResult.Create(result, AzureTerraformJsonContext.Default.AvmDocumentationResult);
            context.Response.Message = string.Empty;

            context.Activity
                ?.AddTag(AzureTerraformTelemetryTags.ToolArea, "avm")
                .AddTag(AzureTerraformTelemetryTags.ModuleName, options.ModuleName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving documentation for AVM module {ModuleName} version {Version}", options.ModuleName, options.ModuleVersion);
            HandleException(context, ex);
        }

        return context.Response;
    }
}
