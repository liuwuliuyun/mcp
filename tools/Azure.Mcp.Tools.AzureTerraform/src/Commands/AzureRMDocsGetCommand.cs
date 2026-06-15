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
    Id = "d4e5f6a7-b8c9-0123-abcd-567890123def",
    Name = "get",
    Title = "Get AzureRM Provider Documentation",
    Description = """
        Retrieves AzureRM Terraform provider documentation for a specified resource type.
        Returns the resource summary, arguments with required/optional flags and types,
        attributes, and usage examples. Use --resource-type to specify the resource
        (e.g., azurerm_resource_group). Optionally filter by --doc-type (resource or data-source),
        --argument, or --attribute.
        --response-format selects 'concise' (default — names/types/required only; per-field
        descriptions, examples, and notes stripped) or 'detailed' (everything).
        """,
    Destructive = false,
    Idempotent = true,
    OpenWorld = true,
    ReadOnly = true,
    Secret = false,
    LocalRequired = false)]
public sealed class AzureRMDocsGetCommand(
    ILogger<AzureRMDocsGetCommand> logger,
    IAzureRMDocsService docsService) : BaseCommand<AzureRMDocsOptions>
{
    private readonly ILogger<AzureRMDocsGetCommand> _logger = logger;
    private readonly IAzureRMDocsService _docsService = docsService;
    private readonly Option<string> _responseFormatOption = AzureTerraformOptionDefinitions.CreateResponseFormatOption();

    protected override void RegisterOptions(Command command)
    {
        base.RegisterOptions(command);
        command.Options.Add(AzureTerraformOptionDefinitions.ResourceType.AsRequired());
        command.Options.Add(AzureTerraformOptionDefinitions.DocType.AsOptional());
        command.Options.Add(AzureTerraformOptionDefinitions.ArgumentName.AsOptional());
        command.Options.Add(AzureTerraformOptionDefinitions.AttributeName.AsOptional());
        command.Options.Add(_responseFormatOption);
    }

    protected override AzureRMDocsOptions BindOptions(ParseResult parseResult)
    {
        return new AzureRMDocsOptions
        {
            ResourceType = parseResult.GetValueOrDefault<string>(AzureTerraformOptionDefinitions.ResourceType.Name),
            DocType = parseResult.GetValueOrDefault<string>(AzureTerraformOptionDefinitions.DocType.Name),
            ArgumentName = parseResult.GetValueOrDefault<string>(AzureTerraformOptionDefinitions.ArgumentName.Name),
            AttributeName = parseResult.GetValueOrDefault<string>(AzureTerraformOptionDefinitions.AttributeName.Name),
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

        AzureRMDocsOptions options;
        try
        {
            options = BindOptions(parseResult);
        }
        catch (Exception ex)
        {
            SetValidationError(context.Response, ex.Message, HttpStatusCode.BadRequest);
            return context.Response;
        }

        var responseFormat = NormalizeResponseFormat(options.ResponseFormat);

        try
        {
            var result = await _docsService.GetDocumentationAsync(
                options.ResourceType!,
                options.DocType ?? "resource",
                options.ArgumentName,
                options.AttributeName,
                cancellationToken).ConfigureAwait(false);

            result.ResponseFormat = responseFormat;
            if (string.Equals(responseFormat, AzureTerraformOptionDefinitions.ResponseFormatConcise, StringComparison.Ordinal))
            {
                DocsConciseRenderer.ApplyConciseToAzureRM(result);
            }

            context.Response.Status = HttpStatusCode.OK;
            context.Response.Results = ResponseResult.Create(result, AzureTerraformJsonContext.Default.AzureRMDocsResult);
            context.Response.Message = string.Empty;

            context.Activity
                ?.AddTag(AzureTerraformTelemetryTags.ToolArea, "azurerm")
                .AddTag(AzureTerraformTelemetryTags.ResourceType, options.ResourceType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving AzureRM documentation for {ResourceType}", options.ResourceType);
            HandleException(context, ex);
        }

        return context.Response;
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
}
