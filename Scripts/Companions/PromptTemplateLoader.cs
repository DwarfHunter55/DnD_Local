using System.Collections.Generic;
using Godot;

namespace ChroniclesUnbound.Companions;

/// <summary>
/// Loads YAML prompt templates from Data/Prompts/ using simple string parsing.
/// Templates contain system_prompt, user_prompt, temperature, and max_tokens fields.
/// No YAML library dependency — relies on the consistent key: | block format.
/// </summary>
public class PromptTemplateLoader
{
    /// <summary>Parsed representation of a YAML prompt template.</summary>
    public class PromptTemplate
    {
        public string SystemPrompt { get; set; } = string.Empty;
        public string UserPrompt { get; set; } = string.Empty;
        public float Temperature { get; set; } = 0.7f;
        public int MaxTokens { get; set; } = 256;
    }

    private const string TemplateDirectory = "res://Data/Prompts/";

    /// <summary>Cache of loaded templates keyed by template name (without extension).</summary>
    private readonly Dictionary<string, PromptTemplate> _cache = new();

    /// <summary>
    /// Loads and parses a YAML prompt template by name.
    /// Template name should match the file name without the .yaml extension
    /// (e.g. "companion_dialogue" loads companion_dialogue.yaml).
    /// Results are cached after first load.
    /// </summary>
    public PromptTemplate LoadTemplate(string templateName)
    {
        if (_cache.TryGetValue(templateName, out var cached))
            return cached;

        string path = $"{TemplateDirectory}{templateName}.yaml";

        if (!Godot.FileAccess.FileExists(path))
        {
            GD.PushError($"PromptTemplateLoader: Template not found: {path}");
            return null;
        }

        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError($"PromptTemplateLoader: Failed to open template: {path} — {Godot.FileAccess.GetOpenError()}");
            return null;
        }

        string raw = file.GetAsText();
        var template = ParseYaml(raw);

        if (template != null)
            _cache[templateName] = template;

        return template;
    }

    /// <summary>
    /// Returns all templates from the Data/Prompts/ directory.
    /// Scans for .yaml files and loads each one.
    /// </summary>
    public Dictionary<string, PromptTemplate> GetAllTemplates()
    {
        using var dir = DirAccess.Open(TemplateDirectory);
        if (dir == null)
        {
            GD.PushError($"PromptTemplateLoader: Cannot open directory: {TemplateDirectory}");
            return new Dictionary<string, PromptTemplate>();
        }

        dir.ListDirBegin();
        string fileName = dir.GetNext();

        while (!string.IsNullOrEmpty(fileName))
        {
            if (!dir.CurrentIsDir() && fileName.EndsWith(".yaml"))
            {
                string templateName = fileName.Replace(".yaml", "");
                if (!_cache.ContainsKey(templateName))
                    LoadTemplate(templateName);
            }
            fileName = dir.GetNext();
        }

        dir.ListDirEnd();
        return new Dictionary<string, PromptTemplate>(_cache);
    }

    /// <summary>
    /// Clears the template cache, forcing reload on next access.
    /// Useful if templates are modified at runtime during development.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    // ------------------------------------------------------------------
    // Simple YAML parser for our known template format
    // ------------------------------------------------------------------

    /// <summary>
    /// Parses a YAML template file with the known structure:
    ///   system_prompt: |
    ///     (indented block)
    ///   user_prompt: |
    ///     (indented block)
    ///   temperature: float
    ///   max_tokens: int
    ///   expected_output_format: (ignored — documentation only)
    /// </summary>
    private static PromptTemplate ParseYaml(string raw)
    {
        var template = new PromptTemplate();
        var lines = raw.Replace("\r\n", "\n").Split('\n');

        string currentKey = null;
        bool inBlock = false;
        var blockLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            // Skip comment-only lines at the top level (not inside blocks)
            if (!inBlock && line.TrimStart().StartsWith("#"))
                continue;

            // Check if this is a top-level key (no leading whitespace, ends with : or : |)
            if (line.Length > 0 && line[0] != ' ' && line[0] != '\t' && line.Contains(':'))
            {
                // Flush any previous block
                if (inBlock && currentKey != null)
                {
                    ApplyBlock(template, currentKey, blockLines);
                    blockLines.Clear();
                    inBlock = false;
                }

                int colonIdx = line.IndexOf(':');
                string key = line.Substring(0, colonIdx).Trim();
                string value = line.Substring(colonIdx + 1).Trim();

                if (value == "|")
                {
                    // Start of a multi-line block
                    currentKey = key;
                    inBlock = true;
                }
                else if (!string.IsNullOrEmpty(value))
                {
                    // Inline scalar value
                    ApplyScalar(template, key, value);
                    currentKey = null;
                }
                else
                {
                    // Key with nested mapping (like expected_output_format:) — skip
                    currentKey = key;
                    inBlock = false;
                }
            }
            else if (inBlock)
            {
                // Inside a block — collect indented lines, stripping 2-space indent
                if (line.Length >= 2 && (line[0] == ' ' || line[0] == '\t'))
                {
                    // Remove the first 2 spaces of indentation (YAML block indent)
                    string stripped = line.Length >= 2 && line.StartsWith("  ")
                        ? line.Substring(2)
                        : line.TrimStart();
                    blockLines.Add(stripped);
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    // Blank line inside block — preserve it
                    blockLines.Add("");
                }
                else
                {
                    // Non-indented, non-blank line ends the block
                    ApplyBlock(template, currentKey, blockLines);
                    blockLines.Clear();
                    inBlock = false;

                    // Re-process this line as a top-level key
                    i--;
                    continue;
                }
            }
        }

        // Flush final block
        if (inBlock && currentKey != null)
            ApplyBlock(template, currentKey, blockLines);

        return template;
    }

    private static void ApplyBlock(PromptTemplate template, string key, List<string> lines)
    {
        // Trim trailing empty lines from the block
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        string text = string.Join("\n", lines);

        switch (key)
        {
            case "system_prompt":
                template.SystemPrompt = text;
                break;
            case "user_prompt":
                template.UserPrompt = text;
                break;
            // expected_output_format blocks are documentation — intentionally ignored
        }
    }

    private static void ApplyScalar(PromptTemplate template, string key, string value)
    {
        // Strip surrounding quotes if present
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];

        switch (key)
        {
            case "temperature":
                if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float temp))
                    template.Temperature = temp;
                break;
            case "max_tokens":
                if (int.TryParse(value, out int tokens))
                    template.MaxTokens = tokens;
                break;
        }
    }
}
