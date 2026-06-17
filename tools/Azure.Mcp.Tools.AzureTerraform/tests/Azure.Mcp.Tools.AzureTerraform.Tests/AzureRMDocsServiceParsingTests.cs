// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.AzureTerraform.Services;
using Xunit;

namespace Azure.Mcp.Tools.AzureTerraform.Tests;

public class AzureRMDocsServiceParsingTests
{
    private const string SampleMarkdown = """
        ---
        subcategory: "Resource Group"
        layout: "azurerm"
        page_title: "Azure Resource Manager: azurerm_resource_group"
        description: Manages a Resource Group.
        ---

        # azurerm_resource_group

        Manages a Resource Group.

        ## Example Usage

        ```hcl
        resource "azurerm_resource_group" "example" {
          name     = "example"
          location = "West Europe"
        }
        ```

        ## Argument Reference

        The following arguments are supported:

        * `name` - (Required) The Name which should be used for this Resource Group. Changing this forces a new Resource Group to be created.
        * `location` - (Required) The Azure Region where the Resource Group should exist. Changing this forces a new Resource Group to be created.
        * `tags` - (Optional) A mapping of tags which should be assigned to the Resource Group.

        ## Attributes Reference

        In addition to the Arguments listed above - the following Attributes are exported:

        * `id` - The ID of the Resource Group.

        -> **NOTE:** Some note about the resource.
        """;

    [Fact]
    public void ExtractSummary_ReturnsSummaryFromMarkdown()
    {
        string summary = AzureRMDocsParser.ExtractSummary(SampleMarkdown, "azurerm_resource_group", false);

        Assert.Equal("Manages a Resource Group.", summary);
    }

    [Fact]
    public void ExtractArguments_ReturnsArgumentsFromMarkdown()
    {
        var args = AzureRMDocsParser.ExtractArguments(SampleMarkdown, false);

        Assert.Equal(3, args.Count);
        Assert.Equal("name", args[0].Name);
        Assert.True(args[0].Required);
        Assert.Equal("location", args[1].Name);
        Assert.True(args[1].Required);
        Assert.Equal("tags", args[2].Name);
        Assert.False(args[2].Required);
    }

    [Fact]
    public void ExtractAttributes_ReturnsAttributesFromMarkdown()
    {
        var attrs = AzureRMDocsParser.ExtractAttributes(SampleMarkdown);

        Assert.Contains(attrs, a => a.Name == "id");
    }

    [Fact]
    public void ExtractExamples_ReturnsExamplesFromMarkdown()
    {
        var examples = AzureRMDocsParser.ExtractExamples(SampleMarkdown, "resource_group", false);

        Assert.NotEmpty(examples);
        Assert.Contains("azurerm_resource_group", examples[0]);
    }

    [Fact]
    public void ExtractNotes_ReturnsNotesFromMarkdown()
    {
        var notes = AzureRMDocsParser.ExtractNotes(SampleMarkdown);

        Assert.NotEmpty(notes);
        Assert.Contains(notes, n => n.Contains("note about the resource", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractSummary_WithDataSource_ReturnsDefaultSummary()
    {
        const string markdown = """
            ---
            layout: "azurerm"
            ---
            Short line
            """;

        string summary = AzureRMDocsParser.ExtractSummary(markdown, "azurerm_resource_group", true);

        Assert.Contains("data source", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractArguments_WithNoArguments_ReturnsDefaults()
    {
        const string markdown = """
            ---
            layout: "azurerm"
            ---
            Some resource description that is long enough.
            """;

        var args = AzureRMDocsParser.ExtractArguments(markdown, false);

        Assert.NotEmpty(args);
        Assert.Contains(args, a => a.Name == "name");
        Assert.Contains(args, a => a.Name == "location");
    }

    [Fact]
    public void ExtractBlockDefinitions_ParsesBlockArguments()
    {
        const string markdown = """
            ## Argument Reference

            * `identity` - (Optional) An identity block as defined below.

            An `identity` block supports the following:

            * `type` - (Required) The type of identity.
            * `identity_ids` - (Optional) A list of identity IDs.

            ## Attributes Reference
            """;

        var blocks = AzureRMDocsParser.ExtractBlockDefinitions(markdown);

        Assert.True(blocks.ContainsKey("identity"));
        Assert.Equal(2, blocks["identity"].Count);
        Assert.Equal("type", blocks["identity"][0].Name);
        Assert.True(blocks["identity"][0].Required);
    }

    [Fact]
    public void HydrateBlockArguments_PopulatesNestedBlocks()
    {
        const string markdown = """
            ## Argument Reference

            The following arguments are supported:

            * `queue_properties` - (Optional) A `queue_properties` block as defined below.

            A `queue_properties` block supports the following:

            * `logging` - (Optional) A `logging` block as defined below.

            A `logging` block supports the following:

            * `delete` - (Required) Indicates whether all delete requests should be logged.
            * `read` - (Required) Indicates whether all read requests should be logged.
            * `version` - (Required) The version of storage analytics to configure.

            ## Attributes Reference
            """;

        var args = AzureRMDocsParser.ExtractArguments(markdown, false);
        var blocks = AzureRMDocsParser.ExtractBlockDefinitions(markdown);
        AzureRMDocsParser.HydrateBlockArguments(args, blocks);

        var queueProps = Assert.Single(args, a => a.Name == "queue_properties");
        Assert.Equal("Block", queueProps.Type);
        Assert.NotNull(queueProps.BlockArguments);

        var logging = Assert.Single(queueProps.BlockArguments!, a => a.Name == "logging");
        Assert.Equal("Block", logging.Type);
        Assert.NotNull(logging.BlockArguments);
        Assert.Equal(3, logging.BlockArguments!.Count);
        Assert.Contains(logging.BlockArguments!, a => a.Name == "delete" && a.Required);
        Assert.Contains(logging.BlockArguments!, a => a.Name == "read");
        Assert.Contains(logging.BlockArguments!, a => a.Name == "version");
    }

    [Fact]
    public void HydrateBlockArguments_BreaksSelfReferentialCycles()
    {
        const string markdown = """
            ## Argument Reference

            The following arguments are supported:

            * `rule` - (Optional) A `rule` block as defined below.

            A `rule` block supports the following:

            * `name` - (Required) The rule name.
            * `rule` - (Optional) A nested `rule` block as defined below.

            ## Attributes Reference
            """;

        var args = AzureRMDocsParser.ExtractArguments(markdown, false);
        var blocks = AzureRMDocsParser.ExtractBlockDefinitions(markdown);

        // Should terminate (no stack overflow) despite rule -> rule self-reference.
        AzureRMDocsParser.HydrateBlockArguments(args, blocks);

        var rule = Assert.Single(args, a => a.Name == "rule");
        Assert.NotNull(rule.BlockArguments);
        var nestedRule = Assert.Single(rule.BlockArguments!, a => a.Name == "rule");
        // The cycle is broken: the second-level rule is marked as a block but not expanded further.
        Assert.Equal("Block", nestedRule.Type);
        Assert.Empty(nestedRule.BlockArguments!);
    }

    [Fact]
    public void HydrateBlockArguments_IndependentInstancesPerAttachmentSite()
    {
        const string markdown = """
            ## Argument Reference

            The following arguments are supported:

            * `primary` - (Optional) A `primary` block as defined below.
            * `secondary` - (Optional) A `secondary` block as defined below.

            A `primary` block supports the following:

            * `shared` - (Optional) A `shared` block as defined below.

            A `secondary` block supports the following:

            * `shared` - (Optional) A `shared` block as defined below.

            A `shared` block supports the following:

            * `value` - (Required) Some value.

            ## Attributes Reference
            """;

        var args = AzureRMDocsParser.ExtractArguments(markdown, false);
        var blocks = AzureRMDocsParser.ExtractBlockDefinitions(markdown);
        AzureRMDocsParser.HydrateBlockArguments(args, blocks);

        var primaryShared = args.Single(a => a.Name == "primary").BlockArguments!.Single(a => a.Name == "shared");
        var secondaryShared = args.Single(a => a.Name == "secondary").BlockArguments!.Single(a => a.Name == "shared");

        // Both expanded, and they are not the same list instance.
        Assert.NotNull(primaryShared.BlockArguments);
        Assert.NotNull(secondaryShared.BlockArguments);
        Assert.NotSame(primaryShared.BlockArguments, secondaryShared.BlockArguments);
        Assert.Equal("value", primaryShared.BlockArguments!.Single().Name);
        Assert.Equal("value", secondaryShared.BlockArguments!.Single().Name);
    }
}
