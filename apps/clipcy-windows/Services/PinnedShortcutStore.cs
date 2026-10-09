using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clipcy.Services;

/// <summary>Permanent, never-reused numeric codes keyed by clipboard item ID.</summary>
public sealed partial class PinnedShortcutStore
{
    private readonly string _path;
    private Dictionary<string, long>? _codes;

    public PinnedShortcutStore(string path) => _path = path;

    public IReadOnlyDictionary<string, long> EnsureCodes(IEnumerable<string> ids)
    {
        if (_codes == null)
        {
            _codes = File.Exists(_path)
                ? JsonSerializer.Deserialize(
                    File.ReadAllText(_path), ShortcutJsonContext.Default.DictionaryStringInt64)
                    ?? throw new InvalidDataException("Invalid pinned shortcut file.")
                : new Dictionary<string, long>();
            if (_codes.Values.Any(code => code <= 0) || _codes.Values.Distinct().Count() != _codes.Count)
            {
                _codes = null;
                throw new InvalidDataException("Pinned shortcut codes must be positive and unique.");
            }
        }

        var updated = new Dictionary<string, long>(_codes);
        long next = updated.Count == 0 ? 0 : updated.Values.Max();
        foreach (var id in ids)
        {
            if (!updated.ContainsKey(id))
                updated.Add(id, checked(++next));
        }
        if (updated.Count != _codes.Count)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary,
                JsonSerializer.Serialize(updated, ShortcutJsonContext.Default.DictionaryStringInt64));
            File.Move(temporary, _path, overwrite: true);
            _codes = updated;
        }
        return _codes;
    }

    [JsonSerializable(typeof(Dictionary<string, long>))]
    private partial class ShortcutJsonContext : JsonSerializerContext
    {
    }

    // Left-hand keys first. O/P are reserved prefixes so adding pins never delays a direct key.
    private const string DirectKeys = "ASDFQWERZXCV12345TGB6YHN7UJM8IK9L0";
    private const int LeavesPerPrefix = 35; // Enter plus the 34 direct keys.

    public static string Sequence(long code)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(code);
        if (code <= DirectKeys.Length)
            return DirectKeys[(int)code - 1].ToString();

        long index = code - DirectKeys.Length - 1;
        long levelSize = 2 * LeavesPerPrefix;
        int depth = 1;
        while (index >= levelSize)
        {
            index -= levelSize;
            levelSize = levelSize > long.MaxValue / 2 ? long.MaxValue : levelSize * 2;
            depth++;
        }

        long node = index / LeavesPerPrefix;
        int leaf = (int)(index % LeavesPerPrefix);
        var characters = new char[depth + 1];
        for (int i = 0; i < depth; i++)
            characters[i] = (node & (1L << (depth - i - 1))) == 0 ? 'O' : 'P';
        characters[depth] = leaf == 0 ? '\n' : DirectKeys[leaf - 1];
        return new string(characters);
    }

    public static string Label(long code) => "Ctrl+Alt+" + string.Join(
        ", ", Sequence(code).Select(character => character == '\n' ? "Enter" : character.ToString()));

    public static bool TryReadSequence(string input, out long code)
    {
        code = 0;
        if (input.Length == 0)
            return false;
        int direct = DirectKeys.IndexOf(input[0]);
        if (direct >= 0)
        {
            if (input.Length != 1)
                return false;
            code = direct + 1;
            return true;
        }
        try
        {
            int depth = input.Length - 1;
            if (depth < 1)
                return false;
            long node = 0;
            long firstCode = DirectKeys.Length + 1;
            long levelSize = 2 * LeavesPerPrefix;
            for (int i = 0; i < depth; i++)
            {
                if (input[i] is not ('O' or 'P'))
                    return false;
                node = checked(node * 2 + (input[i] == 'P' ? 1 : 0));
                if (i < depth - 1)
                {
                    firstCode = checked(firstCode + levelSize);
                    levelSize = levelSize > long.MaxValue / 2 ? long.MaxValue : levelSize * 2;
                }
            }
            int leaf = input[^1] == '\n' ? 0 : DirectKeys.IndexOf(input[^1]) + 1;
            if (leaf == 0 && input[^1] != '\n')
                return false;
            code = checked(firstCode + node * LeavesPerPrefix + leaf);
            return true;
        }
        catch (OverflowException)
        {
            code = 0;
            return false;
        }
    }
}
