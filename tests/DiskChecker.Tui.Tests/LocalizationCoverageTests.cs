using System.Text.RegularExpressions;
using DiskChecker.Tui.Tests.Support;
using Xunit;

namespace DiskChecker.Tui.Tests;

/// <summary>
/// Guards the shared vernacular: every locale key statically referenced by the
/// terminal screens must exist in both shipped locale files.
/// </summary>
public sealed partial class LocalizationCoverageTests
{
    [GeneratedRegex("\\.(?:L|GetString|Get)\\(\\s*\"([A-Za-z][A-Za-z0-9_.]*)\"", RegexOptions.Compiled)]
    private static partial Regex LocaleKeyPattern();

    [Fact]
    public void EveryReferencedKeyExistsInBothLocales()
    {
        var keys = CollectKeys();
        Assert.True(keys.Count > 20, "Expected the terminal screens to reference locale keys, found " + keys.Count);

        var missing = new List<string>();
        foreach (string locale in new[] { "cs", "en" })
        {
            var catalog = ReadCatalog(locale);
            foreach (string key in keys)
            {
                if (!catalog.ContainsKey(key))
                {
                    missing.Add(locale + ":" + key);
                }
            }
        }

        Assert.True(missing.Count == 0, "Missing locale keys: " + string.Join(", ", missing.OrderBy(entry => entry)));
    }

    [Fact]
    public void CzechAndEnglishCatalogsHaveTheSameKeys()
    {
        var czech = ReadCatalog("cs");
        var english = ReadCatalog("en");

        var onlyCzech = czech.Keys.Except(english.Keys).ToList();
        var onlyEnglish = english.Keys.Except(czech.Keys).ToList();

        Assert.True(onlyCzech.Count == 0, "Keys only in cs.json: " + string.Join(", ", onlyCzech));
        Assert.True(onlyEnglish.Count == 0, "Keys only in en.json: " + string.Join(", ", onlyEnglish));
    }

    private static HashSet<string> CollectKeys()
    {
        string sourceDirectory = Path.Combine(TestPaths.RepositoryRoot, "DiskChecker.TUI");
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (string file in Directory.GetFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            {
                continue;
            }

            string content = File.ReadAllText(file);
            foreach (Match match in LocaleKeyPattern().Matches(content))
            {
                string key = match.Groups[1].Value;
                // A trailing dot means the screen builds the key dynamically ("Nav.Language." + code).
                if (key.EndsWith('.'))
                {
                    continue;
                }

                keys.Add(key);
            }
        }

        return keys;
    }

    private static Dictionary<string, string> ReadCatalog(string locale)
    {
        string path = Path.Combine(TestPaths.LocalesDirectory, locale + ".json");
        string json = File.ReadAllText(path);
        var catalog = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return catalog ?? new Dictionary<string, string>();
    }
}
