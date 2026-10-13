using System.Linq;
using Content.Shared.Speech;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Scp.Backrooms.DogAccent;

// TODO: Перенести в Shared
public sealed partial class DogAccentSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;

    private readonly TimeSpan _cooldown = TimeSpan.FromSeconds(10);
    private TimeSpan _nextTrigger = TimeSpan.Zero;

    private readonly HashSet<string> _replacements =
    [
        "anomaly-accent-words-dog-1",
        "anomaly-accent-words-dog-2",
        "anomaly-accent-words-dog-3",
        "anomaly-accent-words-dog-4",
        "anomaly-accent-words-dog-5",
    ];

    public override void Initialize()
    {
        SubscribeLocalEvent<DogAccentComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(Entity<DogAccentComponent> ent, ref AccentGetEvent args)
    {
        if (_timing.CurTime < _nextTrigger)
            return;

        args.Message = ReplaceRandomWords(args.Message);
        _nextTrigger = _timing.CurTime + _cooldown;
    }

    private string ReplaceRandomWords(string message)
    {
        // Разбиваем строку на слова
        var words = message.Split([' ', ',', '.', '!', '?' ], StringSplitOptions.RemoveEmptyEntries);

        var count = _random.Next(1, words.Length + 1);

        // Получаем случайные индексы для замены
        var indicesToReplace = Enumerable.Range(0, words.Length)
            .OrderBy(x => _random.Next())
            .Take(count)
            .ToList();

        foreach (var index in indicesToReplace)
        {
            var randomReplacement = _replacements.ElementAt(_random.Next(_replacements.Count));
            words[index] = Loc.GetString(randomReplacement);
        }

        return string.Join(" ", words);
    }
}
