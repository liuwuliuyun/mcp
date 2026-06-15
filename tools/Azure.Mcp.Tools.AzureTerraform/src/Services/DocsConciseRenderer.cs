// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Text.RegularExpressions;
using Azure.Mcp.Tools.AzureTerraform.Models;

namespace Azure.Mcp.Tools.AzureTerraform.Services;

internal static partial class DocsConciseRenderer
{
    internal static void ApplyConciseToAzureRM(AzureRMDocsResult result)
    {
        result.Arguments = StripArgumentDescriptions(result.Arguments);
        result.Attributes = result.Attributes
            .Select(a => new AttributeDetail { Name = a.Name })
            .ToList();
        result.Examples = [];
        result.Notes = [];
    }

    private static List<ArgumentDetail> StripArgumentDescriptions(List<ArgumentDetail> args)
    {
        var stripped = new List<ArgumentDetail>(args.Count);
        foreach (var arg in args)
        {
            stripped.Add(new ArgumentDetail
            {
                Name = arg.Name,
                Required = arg.Required,
                Type = arg.Type,
                BlockArguments = arg.BlockArguments is null ? null : StripArgumentDescriptions(arg.BlockArguments)
            });
        }
        return stripped;
    }

    internal static void ApplyConciseToAzApi(AzApiDocsResult result)
    {
        result.Schema = StripHclComments(result.Schema);
        result.Examples = null;
    }

    internal static string StripHclComments(string hcl)
    {
        if (string.IsNullOrEmpty(hcl))
        {
            return hcl;
        }

        var lines = hcl.Split('\n');
        var output = new List<string>(lines.Length);
        foreach (var rawLine in lines)
        {
            var stripped = StripInlineDoubleSlashComment(rawLine).TrimEnd();
            if (string.IsNullOrWhiteSpace(stripped) && rawLine.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }
            output.Add(stripped);
        }
        return string.Join('\n', output);
    }

    private static string StripInlineDoubleSlashComment(string line)
    {
        bool inString = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            char c = line[i];
            if (c == '\\' && inString)
            {
                i++;
                continue;
            }
            if (c == '"')
            {
                inString = !inString;
                continue;
            }
            if (!inString && c == '/' && line[i + 1] == '/')
            {
                return line[..i].TrimEnd();
            }
        }
        return line;
    }

    internal static string RenderAvmConciseInputs(string readmeMarkdown)
    {
        if (string.IsNullOrEmpty(readmeMarkdown))
        {
            return string.Empty;
        }

        var required = ExtractInputs(readmeMarkdown, "Required Inputs");
        var optional = ExtractInputs(readmeMarkdown, "Optional Inputs");

        if (required.Count == 0 && optional.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        if (required.Count > 0)
        {
            sb.AppendLine("## Required Inputs");
            sb.AppendLine();
            foreach (var input in required)
            {
                AppendInput(sb, input);
            }
        }

        if (optional.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }
            sb.AppendLine("## Optional Inputs");
            sb.AppendLine();
            foreach (var input in optional)
            {
                AppendInput(sb, input);
            }
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendInput(StringBuilder sb, AvmInput input)
    {
        if (string.IsNullOrEmpty(input.Type))
        {
            sb.AppendLine($"- {input.Name}");
        }
        else
        {
            sb.AppendLine($"- {input.Name}: {input.Type}");
        }
        foreach (var sub in input.Subproperties)
        {
            sb.AppendLine($"  - {sub}");
        }
    }

    private static List<AvmInput> ExtractInputs(string markdown, string sectionHeading)
    {
        var inputs = new List<AvmInput>();
        var lines = markdown.Split('\n');

        int i = FindSectionStart(lines, sectionHeading);
        if (i < 0)
        {
            return inputs;
        }

        AvmInput? current = null;
        bool inTypeBlock = false;
        var typeBuffer = new StringBuilder();

        for (i++; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                FinalizeInput(current, inputs, typeBuffer);
                break;
            }

            var headerMatch = InputHeader().Match(trimmed);
            if (headerMatch.Success)
            {
                FinalizeInput(current, inputs, typeBuffer);
                current = new AvmInput { Name = headerMatch.Groups[1].Value.Replace("\\_", "_", StringComparison.Ordinal) };
                inTypeBlock = false;
                typeBuffer.Clear();
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (inTypeBlock)
            {
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    inTypeBlock = false;
                    current.Type = typeBuffer.Length > 0 ? typeBuffer.ToString().Trim() : current.Type;
                    current.Subproperties.AddRange(ExtractSubproperties(typeBuffer.ToString()));
                    typeBuffer.Clear();
                }
                else
                {
                    typeBuffer.AppendLine(line);
                }
                continue;
            }

            var typeMatch = InlineType().Match(trimmed);
            if (typeMatch.Success)
            {
                current.Type = typeMatch.Groups[1].Value;
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inTypeBlock = true;
                typeBuffer.Clear();
            }
        }

        FinalizeInput(current, inputs, typeBuffer);

        return inputs;
    }

    private static void FinalizeInput(AvmInput? current, List<AvmInput> inputs, StringBuilder typeBuffer)
    {
        if (current is null)
        {
            return;
        }

        if (typeBuffer.Length > 0 && string.IsNullOrEmpty(current.Type))
        {
            current.Type = typeBuffer.ToString().Trim();
            current.Subproperties.AddRange(ExtractSubproperties(typeBuffer.ToString()));
        }

        typeBuffer.Clear();
        inputs.Add(current);
    }

    private static int FindSectionStart(string[] lines, string heading)
    {
        var target = "## " + heading;
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.Equals(lines[i].TrimEnd(), target, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private static List<string> ExtractSubproperties(string typeBlock)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(typeBlock))
        {
            return result;
        }

        foreach (var rawLine in typeBlock.Split('\n'))
        {
            var match = ObjectField().Match(rawLine);
            if (match.Success)
            {
                var name = match.Groups[1].Value;
                if (!result.Contains(name))
                {
                    result.Add(name);
                }
            }
        }
        return result;
    }

    private sealed class AvmInput
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public List<string> Subproperties { get; } = [];
    }

    [GeneratedRegex(@"^###\s*(?:<a[^>]*></a>\s*)?\[?([A-Za-z0-9_\\\.]+)\]?", RegexOptions.IgnoreCase)]
    private static partial Regex InputHeader();

    [GeneratedRegex(@"^Type:\s*`([^`]+)`", RegexOptions.IgnoreCase)]
    private static partial Regex InlineType();

    [GeneratedRegex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=", RegexOptions.None)]
    private static partial Regex ObjectField();
}
