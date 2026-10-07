using Clipcy.Services;

var directory = Path.Combine(Path.GetTempPath(), "clipcy-shortcuts-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "codes.json");
    var store = new PinnedShortcutStore(path);
    var ids = Enumerable.Range(1, 1000).Select(i => $"item-{i}").ToArray();
    var original = store.EnsureCodes(ids).ToDictionary();
    Check(original.Count == 1000 && original.Values.Distinct().Count() == 1000, "1000 unique codes");

    var reordered = store.EnsureCodes(ids.Reverse());
    Check(ids.All(id => reordered[id] == original[id]), "stable after reordering");
    store.EnsureCodes(new[] { ids[50] });
    var restarted = new PinnedShortcutStore(path).EnsureCodes(new[] { ids[50], "new-item" });
    Check(restarted[ids[50]] == original[ids[50]], "stable across filtering and restart");
    Check(restarted["new-item"] == 1001, "removed codes are never reused");
    Check(new PinnedShortcutStore(path).EnsureCodes(ids)[ids[0]] == original[ids[0]], "repin keeps code");
    Check(PinnedShortcutStore.Label(1) == "Ctrl+Alt+A", "left-hand single shortcut first");
    Check(PinnedShortcutStore.Label(13) == "Ctrl+Alt+1", "left-hand digits before right-hand keys");
    Check(PinnedShortcutStore.Label(35) == "Ctrl+Alt+O, Enter", "prefix item uses Enter");
    Check(PinnedShortcutStore.Label(36) == "Ctrl+Alt+O, A", "second letter pastes directly");
    var sequences = Enumerable.Range(1, 5000).Select(i => PinnedShortcutStore.Sequence(i)).ToArray();
    Check(sequences.Distinct().Count() == sequences.Length, "5000 unique direct shortcuts");
    var sequenceSet = sequences.ToHashSet();
    foreach (var sequence in sequences)
    {
        Check(PinnedShortcutStore.TryReadSequence(sequence, out long code)
            && PinnedShortcutStore.Sequence(code) == sequence, "shortcut round trip");
        for (int length = 1; length < sequence.Length; length++)
            Check(!sequenceSet.Contains(sequence[..length]), "no shortcut prefixes another");
    }
    Check(sequences.Count(sequence => sequence.Length == 1) == 34, "prioritize all direct keys");
    Check(sequences.Take(104).All(sequence => sequence.Length <= 2), "100 pins need at most two keys");
    Check(PinnedShortcutStore.Sequence(34) == "0", "last direct shortcut");
    Check(PinnedShortcutStore.Sequence(104) == "P0", "last two-key shortcut");
    Check(PinnedShortcutStore.Sequence(105) == "OO\n", "extensions preserve earlier shortcuts");
    Check(PinnedShortcutStore.TryReadSequence(PinnedShortcutStore.Sequence(long.MaxValue), out long maximum)
        && maximum == long.MaxValue, "64-bit code round trip");
    Check(PinnedShortcutStore.TryReadSequence("A", out long first) && first == 1,
        "single key completes immediately");
    Check(!PinnedShortcutStore.TryReadSequence("O", out _) && !PinnedShortcutStore.TryReadSequence("P", out _),
        "only prefix keys wait for a second key");
    Check(PinnedShortcutStore.TryReadSequence("O\n", out long prefix) && prefix == 35,
        "Enter selects prefix item");
    Check(PinnedShortcutStore.TryReadSequence("OA", out long second) && second == 36,
        "second letter completes immediately");
    Check(!PinnedShortcutStore.TryReadSequence("A\n", out _), "direct key needs no Enter");
    Check(!PinnedShortcutStore.TryReadSequence("O?", out _), "invalid sequence rejected");

    File.WriteAllText(path, "{\"a\":1,\"b\":1}");
    ExpectFailure(() => new PinnedShortcutStore(path).EnsureCodes(ids), "reject duplicate codes");
    File.WriteAllText(path, "{\"a\":0}");
    ExpectFailure(() => new PinnedShortcutStore(path).EnsureCodes(ids), "reject invalid codes");
    File.WriteAllText(path, "broken json");
    ExpectFailure(() => new PinnedShortcutStore(path).EnsureCodes(ids), "reject damaged storage");
    Check(File.ReadAllText(path) == "broken json", "do not overwrite damaged assignments");

    var blockedPath = Path.Combine(directory, "blocked");
    File.WriteAllText(blockedPath, "not a directory");
    ExpectFailure(() => new PinnedShortcutStore(Path.Combine(blockedPath, "codes.json")).EnsureCodes(ids),
        "do not advertise assignments that cannot be saved");
    Console.WriteLine("All pinned shortcut checks passed.");
}
finally
{
    Directory.Delete(directory, recursive: true);
}

static void Check(bool condition, string behavior)
{
    if (!condition)
        throw new Exception("Failed: " + behavior);
}

static void ExpectFailure(Action action, string behavior)
{
    try { action(); }
    catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
    {
        return;
    }
    throw new Exception("Expected failure: " + behavior);
}
