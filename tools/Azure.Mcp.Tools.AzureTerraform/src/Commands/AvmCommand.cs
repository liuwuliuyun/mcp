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
    Id = "1f9d4b2a-7e6c-4a51-9c8d-3b0e72f1a4d6",
    Name = "avm",
    Title = "Azure Verified Modules",
    Description = """
        Browses and reads Azure Verified Modules (AVM) for Terraform — both resource modules (avm-res-*)
        and pattern modules (avm-ptn-*). Use --method to choose the action:
        'list' returns the full module catalog; 'versions' returns release tags for one module
        (requires --module-name); 'get' returns the README for one module (requires --module-name;
        --module-version is optional and defaults to the latest stable release).
        For 'get', --response-format selects 'concise' (default — module summary plus required and
        optional input names with types only, descriptions stripped) or 'detailed' (full README).
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = true,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class AvmCommand(
    ILogger<AvmCommand> logger,
    IAvmDocsService avmDocsService) : BaseCommand<AvmCommandOptions>
{
    private readonly ILogger<AvmCommand> _logger = logger;
    private readonly IAvmDocsService _avmDocsService = avmDocsService;

    private readonly Option<string> _methodOption = AzureTerraformOptionDefinitions.CreateAvmMethodOption();
    private readonly Option<string> _moduleNameOption = new($"--{AzureTerraformOptionDefinitions.AvmModuleNameOption}")
    {
        Description = "The name of the Azure Verified Module (e.g., avm-res-storage-storageaccount). Required when --method is 'versions' or 'get'.",
        Required = false
    };
    private readonly Option<string> _moduleVersionOption = new($"--{AzureTerraformOptionDefinitions.AvmModuleVersionOption}")
    {
        Description = "The module version (e.g., 0.4.0). Only used when --method is 'get'. If omitted, the latest stable (non-prerelease) version is used.",
        Required = false
    };
    private readonly Option<string> _responseFormatOption = AzureTerraformOptionDefinitions.CreateResponseFormatOption();

    protected override void RegisterOptions(Command command)
    {
        base.RegisterOptions(command);
        command.Options.Add(_methodOption);
        command.Options.Add(_moduleNameOption);
        command.Options.Add(_moduleVersionOption);
        command.Options.Add(_responseFormatOption);

        command.Validators.Add(commandResult =>
        {
            commandResult.TryGetValue(_methodOption, out string? method);
            if (string.IsNullOrEmpty(method))
            {
                return;
            }

            bool needsModuleName = method.Equals(AzureTerraformOptionDefinitions.AvmMethodVersions, StringComparison.OrdinalIgnoreCase)
                || method.Equals(AzureTerraformOptionDefinitions.AvmMethodGet, StringComparison.OrdinalIgnoreCase);

            if (needsModuleName)
            {
                commandResult.TryGetValue(_moduleNameOption, out string? moduleName);
                if (string.IsNullOrWhiteSpace(moduleName))
                {
                    commandResult.AddError($"--module-name is required when --method is '{method}'.");
                }
            }
        });
    }

    protected override AvmCommandOptions BindOptions(ParseResult parseResult)
    {
        return new AvmCommandOptions
        {
            Method = parseResult.GetValueOrDefault<string>(_methodOption.Name),
            ModuleName = parseResult.GetValueOrDefault<string>(_moduleNameOption.Name),
            ModuleVersion = parseResult.GetValueOrDefault<string>(_moduleVersionOption.Name),
            ResponseFormat = parseResult.GetValueOrDefault<string>(_responseFormatOption.Name)
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

        AvmCommandOptions options;
        try
        {
            options = BindOptions(parseResult);
        }
        catch (Exception ex)
        {
            SetValidationError(context.Response, ex.Message, HttpStatusCode.BadRequest);
            return context.Response;
        }

        try
        {
            context.Activity?.AddTag(AzureTerraformTelemetryTags.ToolArea, "avm");

            switch (options.Method)
            {
                case AzureTerraformOptionDefinitions.AvmMethodList:
                    await ExecuteListAsync(context, cancellationToken).ConfigureAwait(false);
                    break;
                case AzureTerraformOptionDefinitions.AvmMethodVersions:
                    await ExecuteVersionsAsync(context, options.ModuleName!, cancellationToken).ConfigureAwait(false);
                    break;
                case AzureTerraformOptionDefinitions.AvmMethodGet:
                    await ExecuteGetAsync(context, options, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    SetValidationError(context.Response, $"Unknown method '{options.Method}'.", HttpStatusCode.BadRequest);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing AVM method {Method} for module {ModuleName}", options.Method, options.ModuleName);
            HandleException(context, ex);
        }

        return context.Response;
    }

    private async Task ExecuteListAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var modules = await _avmDocsService.ListModulesAsync(cancellationToken).ConfigureAwait(false);

        var result = new AvmModuleListResult { Modules = modules };
        context.Response.Status = HttpStatusCode.OK;
        context.Response.Results = ResponseResult.Create(result, AzureTerraformJsonContext.Default.AvmModuleListResult);
        context.Response.Message = string.Empty;
    }

    private async Task ExecuteVersionsAsync(CommandContext context, string moduleName, CancellationToken cancellationToken)
    {
        var versions = await _avmDocsService.GetVersionsAsync(moduleName, cancellationToken).ConfigureAwait(false);

        var result = new AvmVersionListResult
        {
            ModuleName = moduleName,
            Versions = versions
        };
        context.Response.Status = HttpStatusCode.OK;
        context.Response.Results = ResponseResult.Create(result, AzureTerraformJsonContext.Default.AvmVersionListResult);
        context.Response.Message = string.Empty;

        context.Activity?.AddTag(AzureTerraformTelemetryTags.ModuleName, moduleName);
    }

    private async Task ExecuteGetAsync(CommandContext context, AvmCommandOptions options, CancellationToken cancellationToken)
    {
        var documentation = await _avmDocsService.GetDocumentationAsync(
            options.ModuleName!,
            options.ModuleVersion,
            cancellationToken).ConfigureAwait(false);

        var responseFormat = NormalizeResponseFormat(options.ResponseFormat);
        var (summary, body) = RenderDocumentation(documentation.Documentation, responseFormat);

        var result = new AvmDocumentationResult
        {
            ModuleName = options.ModuleName!,
            ModuleVersion = documentation.ResolvedVersion,
            ResponseFormat = responseFormat,
            Summary = summary,
            Documentation = body
        };

        context.Response.Status = HttpStatusCode.OK;
        context.Response.Results = ResponseResult.Create(result, AzureTerraformJsonContext.Default.AvmDocumentationResult);
        context.Response.Message = string.Empty;

        context.Activity?.AddTag(AzureTerraformTelemetryTags.ModuleName, options.ModuleName);
    }

    private static string NormalizeResponseFormat(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return AzureTerraformOptionDefinitions.ResponseFormatConcise;
        }

        return requested.Equals(AzureTerraformOptionDefinitions.ResponseFormatDetailed, StringComparison.OrdinalIgnoreCase)
            ? AzureTerraformOptionDefinitions.ResponseFormatDetailed
            : AzureTerraformOptionDefinitions.ResponseFormatConcise;
    }

    internal static (string Summary, string Body) RenderDocumentation(string readmeMarkdown, string responseFormat)
    {
        var summary = ExtractAvmSummary(readmeMarkdown);

        if (string.Equals(responseFormat, AzureTerraformOptionDefinitions.ResponseFormatDetailed, StringComparison.Ordinal))
        {
            return (summary, readmeMarkdown);
        }

        var conciseBody = DocsConciseRenderer.RenderAvmConciseInputs(readmeMarkdown);
        return (summary, conciseBody);
    }

    private static string ExtractAvmSummary(string readmeMarkdown)
    {
        if (string.IsNullOrEmpty(readmeMarkdown))
        {
            return string.Empty;
        }

        var lines = readmeMarkdown.Split('\n');
        bool sawTitle = false;
        var collected = new System.Text.StringBuilder();
        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();

            if (!sawTitle)
            {
                if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                {
                    sawTitle = true;
                }
                continue;
            }

            if (trimmed.StartsWith("##", StringComparison.Ordinal))
            {
                break;
            }

            if (trimmed.Length == 0)
            {
                if (collected.Length > 0)
                {
                    break;
                }
                continue;
            }

            if (trimmed.StartsWith("[!", StringComparison.Ordinal) || trimmed.StartsWith("![", StringComparison.Ordinal))
            {
                continue;
            }

            if (collected.Length > 0)
            {
                collected.Append(' ');
            }
            collected.Append(trimmed);
        }

        var summary = collected.ToString().Trim();
        return summary.Length > 600 ? summary[..600].TrimEnd() + "…" : summary;
    }
}
