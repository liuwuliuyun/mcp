// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Mcp.Tools.AzureTerraform.Services;
using Xunit;

namespace Azure.Mcp.Tools.AzureTerraform.Tests;

public class DocsConciseRendererTests
{
    [Fact]
    public void StripHclComments_RemovesInlineDoubleSlashOutsideStrings()
    {
        var hcl = """
            resource "azapi_resource" "x" {
              type = "Microsoft.X/y@2024" // a type comment
              body = {
                properties = {
                  size = "(Required) String. The size" // inline
                  // standalone comment line
                  url  = "https://example.com" // not in a string
                }
              }
            }
            """;

        var stripped = DocsConciseRenderer.StripHclComments(hcl);

        Assert.DoesNotContain("a type comment", stripped);
        Assert.DoesNotContain("standalone comment", stripped);
        Assert.DoesNotContain("// inline", stripped);
        Assert.DoesNotContain("// not in a string", stripped);
        Assert.Contains("size = \"(Required) String. The size\"", stripped);
        Assert.Contains("type = \"Microsoft.X/y@2024\"", stripped);
        Assert.Contains("url  = \"https://example.com\"", stripped);
    }

    [Fact]
    public void StripHclComments_DoesNotStripDoubleSlashInsideStrings()
    {
        var hcl = """parent_id = "https://example.com/foo" """;

        var stripped = DocsConciseRenderer.StripHclComments(hcl);

        Assert.Contains("https://example.com/foo", stripped);
    }

    [Fact]
    public void StripHclComments_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", DocsConciseRenderer.StripHclComments(""));
    }

    [Fact]
    public void RenderAvmConciseInputs_ExtractsRequiredAndOptionalNames()
    {
        var readme = """
            # avm-res-foo

            Module description.

            ## Required Inputs

            The following input variables are required:

            ### <a name="input_location"></a> [location](#input\_location)

            Description: Where to deploy.

            Type: `string`

            ### <a name="input_name"></a> [name](#input\_name)

            Description: Name.

            Type: `string`

            ## Optional Inputs

            The following input variables are optional (have default values):

            ### <a name="input_tags"></a> [tags](#input\_tags)

            Description: Tags.

            Type: `map(string)`

            Default: `{}`
            """;

        var rendered = DocsConciseRenderer.RenderAvmConciseInputs(readme);

        Assert.Contains("## Required Inputs", rendered);
        Assert.Contains("- location: string", rendered);
        Assert.Contains("- name: string", rendered);
        Assert.Contains("## Optional Inputs", rendered);
        Assert.Contains("- tags: map(string)", rendered);
        Assert.DoesNotContain("Description:", rendered);
        Assert.DoesNotContain("Default:", rendered);
        Assert.DoesNotContain("Where to deploy", rendered);
    }

    [Fact]
    public void RenderAvmConciseInputs_NoInputsSection_ReturnsEmpty()
    {
        var readme = "# title\n\nSome description.\n\n## Outputs\n\nNo inputs here.\n";

        var rendered = DocsConciseRenderer.RenderAvmConciseInputs(readme);

        Assert.Equal("", rendered);
    }

    [Fact]
    public void RenderAvmConciseInputs_ComplexObjectType_ExtractsSubproperties()
    {
        var readme = """
            # avm-res-foo

            ## Required Inputs

            ### <a name="input_diagnostic_settings"></a> [diagnostic\_settings](#input\_diagnostic\_settings)

            Description: A map of diagnostic settings.

            Type:

            ```hcl
            map(object({
              name                                     = optional(string, null)
              log_categories                           = optional(set(string), [])
              workspace_resource_id                    = optional(string, null)
            }))
            ```
            """;

        var rendered = DocsConciseRenderer.RenderAvmConciseInputs(readme);

        Assert.Contains("- diagnostic_settings", rendered);
        Assert.Contains("- name", rendered);
        Assert.Contains("- log_categories", rendered);
        Assert.Contains("- workspace_resource_id", rendered);
        Assert.DoesNotContain("Description:", rendered);
    }
}
