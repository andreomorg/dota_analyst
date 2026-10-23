using System.Text;

namespace DotaAnalyst.Infrastructure.GameData;

// One entry of a Valve KeyValues (KV1) text: a key with either a text value or child entries. Keys are compared ignoring
// case, as the game does; repeated keys are kept, in file order.
public sealed class KeyValue(string key, string? value, IReadOnlyList<KeyValue> children)
{
    public string Key { get; } = key;

    // The text value; null for a block.
    public string? Value { get; } = value;

    public IReadOnlyList<KeyValue> Children { get; } = children;

    public bool IsBlock => Value is null;

    // The first child with this key, or null.
    public KeyValue? this[string childKey] => Children.FirstOrDefault(child => child.Key.Equals(childKey, StringComparison.OrdinalIgnoreCase));

    // The text of the first child with this key, or null (also null when that child is a block).
    public string? Text(string childKey) => this[childKey]?.Value;
}

// Reads KeyValues text, the format of the game's scripts (scripts/npc/*.txt) and translations (resource/localization):
//   "key" "value"   or   "key" { ...children... }
// with // comments, \" \\ \n \t escapes inside quotes, unquoted tokens, and #base "file" directives (kept as entries
// with the key "#base": the caller decides what to include). Conditionals ([$WIN32]) after a value are skipped. A
// format error throws InvalidDataException with the line, so a broken file never turns into silently missing data.
public static class KeyValues
{
    public static IReadOnlyList<KeyValue> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reader = new Reader(text);
        var entries = reader.ReadEntries(topLevel: true);
        return entries;
    }

    private sealed class Reader(string text)
    {
        private int position;
        private int line = 1;

        public List<KeyValue> ReadEntries(bool topLevel)
        {
            var entries = new List<KeyValue>();
            while (true)
            {
                SkipSpaceAndComments();
                if (position >= text.Length)
                {
                    return topLevel ? entries : throw Error("A block is not closed");
                }

                if (text[position] == '}')
                {
                    if (topLevel)
                    {
                        throw Error("A block is closed without being opened");
                    }

                    position++;
                    return entries;
                }

                var key = ReadToken() ?? throw Error("Expected a key");
                SkipSpaceAndComments();
                if (position < text.Length && text[position] == '{')
                {
                    position++;
                    entries.Add(new KeyValue(key, null, ReadEntries(topLevel: false)));
                    continue;
                }

                var value = ReadToken() ?? throw Error($"Expected a value or a block after \"{key}\"");
                SkipConditional();
                entries.Add(new KeyValue(key, value, []));
            }
        }

        // A quoted string (with escapes) or a bare word; null at a brace or the end.
        private string? ReadToken()
        {
            if (position >= text.Length || text[position] is '{' or '}')
            {
                return null;
            }

            if (text[position] != '"')
            {
                var start = position;
                while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}' or '"'))
                {
                    position++;
                }

                return text[start..position];
            }

            position++;
            var builder = new StringBuilder();
            while (true)
            {
                if (position >= text.Length)
                {
                    throw Error("A quoted text is not closed");
                }

                var current = text[position++];
                if (current == '"')
                {
                    return builder.ToString();
                }

                if (current == '\n')
                {
                    line++;
                }

                if (current == '\\' && position < text.Length)
                {
                    // An unknown escape stays as written (\x is kept as the two characters).
                    var escaped = text[position++];
                    builder.Append(escaped switch
                    {
                        'n' => "\n",
                        't' => "\t",
                        '"' => "\"",
                        '\\' => "\\",
                        _ => $"\\{escaped}",
                    });
                    continue;
                }

                builder.Append(current);
            }
        }

        private void SkipConditional()
        {
            var saved = (position, line);
            SkipSpaceAndComments(stopAtNewLine: true);
            if (position < text.Length && text[position] == '[')
            {
                var end = text.IndexOf(']', position);
                position = end < 0 ? throw Error("A conditional is not closed") : end + 1;
                return;
            }

            (position, line) = saved;
        }

        private void SkipSpaceAndComments(bool stopAtNewLine = false)
        {
            while (position < text.Length)
            {
                var current = text[position];
                if (current == '\n')
                {
                    if (stopAtNewLine)
                    {
                        return;
                    }

                    line++;
                    position++;
                }
                else if (char.IsWhiteSpace(current) || current == '﻿')
                {
                    position++;
                }
                else if (current == '/' && position + 1 < text.Length && text[position + 1] == '/')
                {
                    while (position < text.Length && text[position] != '\n')
                    {
                        position++;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        private InvalidDataException Error(string message) => new($"{message} (KeyValues line {line}).");
    }
}
