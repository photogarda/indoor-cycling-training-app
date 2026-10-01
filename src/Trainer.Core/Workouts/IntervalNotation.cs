using System.Globalization;
using System.Text;
using Trainer.Core.Models;

namespace Trainer.Core.Workouts;

/// <summary>
/// Compact text form for workouts, used by the library editor and to define the built-in templates.
/// <code>
/// wu 12m 50-70; 3x(10m 95-100 "threshold", 5m 55); fill 20m 65; cd 8m 50
/// </code>
/// Items are separated by <c>;</c> or new lines (commas inside repeats). Each step is
/// <c>[kind] duration [power%] ["label"]</c> where kind is <c>wu</c>, <c>cd</c>, <c>rec</c>, <c>fill</c>
/// or <c>free</c> (ERG off, no power target); without a kind the step is work. Durations: <c>30s</c>,
/// <c>10m</c>, <c>1h30m</c>, <c>2m30s</c>. Power is % FTP, a single value or a <c>low-high</c> range.
/// Repeats are <c>Nx( ... )</c> and may nest.
/// </summary>
public static class IntervalNotation
{
    public static List<WorkoutStep> Parse(string text)
    {
        var p = new Parser(text);
        var steps = p.ParseList(topLevel: true);
        if (steps.Count == 0) throw new FormatException("The workout has no steps.");
        return steps;
    }

    public static bool TryParse(string text, out List<WorkoutStep> steps, out string? error)
    {
        try
        {
            steps = Parse(text);
            error = null;
            return true;
        }
        catch (FormatException ex)
        {
            steps = [];
            error = ex.Message;
            return false;
        }
    }

    public static string Format(IEnumerable<WorkoutStep> steps) => string.Join("; ", steps.Select(FormatStep));

    private static string FormatStep(WorkoutStep s)
    {
        if (s.Kind == StepKind.Repeat)
            return $"{s.Repeat}x({string.Join(", ", (s.Steps ?? []).Select(FormatStep))})";
        var sb = new StringBuilder();
        var prefix = s.Kind switch
        {
            StepKind.Warmup => "wu ",
            StepKind.Cooldown => "cd ",
            StepKind.Recovery => "rec ",
            StepKind.Filler => "fill ",
            StepKind.Free => "free ",
            _ => "",
        };
        sb.Append(prefix).Append(FormatDuration(s.DurationSec));
        if (s.Kind != StepKind.Free || s.PowerLow > 0)
        {
            sb.Append(' ').Append(Pct(s.PowerLow));
            if (Math.Abs(s.PowerHigh - s.PowerLow) > 0.001) sb.Append('-').Append(Pct(s.PowerHigh));
        }
        if (!string.IsNullOrEmpty(s.Label)) sb.Append(" \"").Append(s.Label).Append('"');
        return sb.ToString();
    }

    private static string Pct(double f) => Math.Round(f * 100).ToString(CultureInfo.InvariantCulture);

    public static string FormatDuration(int seconds)
    {
        var h = seconds / 3600;
        var m = seconds % 3600 / 60;
        var s = seconds % 60;
        var sb = new StringBuilder();
        if (h > 0) sb.Append(h).Append('h');
        if (m > 0) sb.Append(m).Append('m');
        if (s > 0 || sb.Length == 0) sb.Append(s).Append('s');
        return sb.ToString();
    }

    private sealed class Parser(string text)
    {
        private int _pos;

        public List<WorkoutStep> ParseList(bool topLevel)
        {
            var steps = new List<WorkoutStep>();
            while (true)
            {
                SkipSeparators(topLevel);
                if (AtEnd) break;
                if (Peek == ')')
                {
                    if (topLevel) throw Error("Unexpected ')'.");
                    break;
                }
                steps.Add(ParseItem());
            }
            return steps;
        }

        private WorkoutStep ParseItem()
        {
            // Repeat: digits 'x' '('
            var save = _pos;
            if (char.IsDigit(Peek))
            {
                var n = ReadInt();
                if (!AtEnd && (Peek == 'x' || Peek == 'X') && NextNonSpaceAfter(_pos + 1) == '(')
                {
                    _pos++;
                    SkipSpaces();
                    _pos++; // (
                    var children = ParseList(topLevel: false);
                    if (AtEnd || Peek != ')') throw Error("Missing ')' after repeat.");
                    _pos++;
                    if (n < 1) throw Error("Repeat count must be at least 1.");
                    if (children.Count == 0) throw Error("Empty repeat.");
                    return WorkoutStep.RepeatOf(n, [.. children]);
                }
                _pos = save;
            }
            return ParseStep();
        }

        private WorkoutStep ParseStep()
        {
            var kind = StepKind.Work;
            if (char.IsLetter(Peek))
            {
                var word = ReadWord().ToLowerInvariant();
                kind = word switch
                {
                    "wu" or "warmup" => StepKind.Warmup,
                    "cd" or "cooldown" => StepKind.Cooldown,
                    "rec" or "recovery" or "rest" => StepKind.Recovery,
                    "fill" or "filler" => StepKind.Filler,
                    "free" or "ergoff" => StepKind.Free,
                    "work" or "on" => StepKind.Work,
                    _ => throw Error($"Unknown step type '{word}'."),
                };
                SkipSpaces();
            }

            var seconds = ReadDuration();
            SkipSpaces();
            double low = 0, high = 0;
            if (!AtEnd && char.IsDigit(Peek))
            {
                low = ReadNumber() / 100;
                if (!AtEnd && Peek == '%') _pos++;
                high = low;
                if (!AtEnd && Peek == '-')
                {
                    _pos++;
                    high = ReadNumber() / 100;
                    if (!AtEnd && Peek == '%') _pos++;
                }
                if (high < low) (low, high) = (high, low);
            }
            else if (kind != StepKind.Free)
            {
                throw Error("Expected power in % FTP.");
            }
            SkipSpaces();
            string? label = null;
            if (!AtEnd && Peek == '"')
            {
                _pos++;
                var end = text.IndexOf('"', _pos);
                if (end < 0) throw Error("Unclosed label quote.");
                label = text[_pos..end];
                _pos = end + 1;
            }
            if (seconds <= 0 && kind != StepKind.Filler) throw Error("Step duration must be positive.");
            if (low > 3) throw Error("Power looks too high; use % FTP (e.g. 95 for 95 %).");
            return WorkoutStep.Make(kind, seconds, low, high, label);
        }

        private int ReadDuration()
        {
            var total = 0;
            var any = false;
            while (!AtEnd && char.IsDigit(Peek))
            {
                var start = _pos;
                var value = ReadNumber();
                if (AtEnd) { _pos = start; break; }
                var unit = char.ToLowerInvariant(Peek);
                if (unit == 'h') total += (int)(value * 3600);
                else if (unit == 'm') total += (int)(value * 60);
                else if (unit == 's') total += (int)value;
                else { _pos = start; break; }
                _pos++;
                any = true;
            }
            if (!any) throw Error("Expected a duration such as 10m, 30s or 1h30m.");
            return total;
        }

        private double ReadNumber()
        {
            var start = _pos;
            while (!AtEnd && (char.IsDigit(Peek) || Peek == '.')) _pos++;
            if (start == _pos) throw Error("Expected a number.");
            return double.Parse(text[start.._pos], CultureInfo.InvariantCulture);
        }

        private int ReadInt()
        {
            var start = _pos;
            while (!AtEnd && char.IsDigit(Peek)) _pos++;
            return int.Parse(text[start.._pos], CultureInfo.InvariantCulture);
        }

        private string ReadWord()
        {
            var start = _pos;
            while (!AtEnd && char.IsLetter(Peek)) _pos++;
            return text[start.._pos];
        }

        private char NextNonSpaceAfter(int i)
        {
            while (i < text.Length && text[i] == ' ') i++;
            return i < text.Length ? text[i] : '\0';
        }

        private void SkipSpaces()
        {
            while (!AtEnd && (Peek == ' ' || Peek == '\t')) _pos++;
        }

        private void SkipSeparators(bool topLevel)
        {
            while (!AtEnd && (char.IsWhiteSpace(Peek) || Peek == ';' || Peek == ',')) _pos++;
        }

        private bool AtEnd => _pos >= text.Length;
        private char Peek => text[_pos];
        private FormatException Error(string message) => new($"{message} (at position {_pos + 1})");
    }
}
