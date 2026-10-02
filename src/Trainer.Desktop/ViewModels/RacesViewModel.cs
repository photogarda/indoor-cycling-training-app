using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trainer.Desktop.Infrastructure;
using Trainer.Core.Models;

namespace Trainer.Desktop.ViewModels;

public record RaceRow(Race Race)
{
    public string Date => Race.Date.ToString("ddd d MMM yyyy");
    public string Name => Race.Name;
    public string Type => Race.Type.Display();
    public string Priority => Race.Priority.ToString();
    public string Duration => $"{Race.DurationHours:0.#} h";
    public int Intensity => Race.Intensity;
    public string Countdown { get; init; } = "";
}

public partial class RacesViewModel : ViewModelBase
{
    public override string Title => "Races";

    public ObservableCollection<RaceRow> Races { get; } = [];
    public IReadOnlyList<KeyValuePair<EventType, string>> EventTypes { get; } =
        Enum.GetValues<EventType>().Select(t => new KeyValuePair<EventType, string>(t, t.Display())).ToList();
    public IReadOnlyList<RacePriority> Priorities { get; } = Enum.GetValues<RacePriority>();

    [ObservableProperty] private RaceRow? _selected;
    [ObservableProperty] private int _editId;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private DateTime? _date;
    [ObservableProperty] private EventType _type = EventType.RoadRace;
    [ObservableProperty] private double _durationHours = 3;
    [ObservableProperty] private int _intensity = 7;
    [ObservableProperty] private RacePriority _priority = RacePriority.A;
    [ObservableProperty] private string? _validation;

    public string EditorTitle => EditId == 0 ? "Add a race" : "Edit race";

    public override void Load()
    {
        var today = Trainer.Today;
        Races.Clear();
        foreach (var r in Trainer.GetRaces())
        {
            var days = r.Date.DayNumber - today.DayNumber;
            Races.Add(new RaceRow(r)
            {
                Countdown = days < 0 ? "done" : days == 0 ? "today" : days < 14 ? $"in {days} days" : $"in {days / 7} weeks",
            });
        }
        if (EditId == 0 && Date is null) New();
    }

    partial void OnSelectedChanged(RaceRow? value)
    {
        if (value is null) return;
        var r = value.Race;
        EditId = r.Id;
        Name = r.Name;
        Date = r.Date.ToDateTime(TimeOnly.MinValue);
        Type = r.Type;
        DurationHours = r.DurationHours;
        Intensity = r.Intensity;
        Priority = r.Priority;
        OnPropertyChanged(nameof(EditorTitle));
    }

    partial void OnNameChanged(string value) => Validate();
    partial void OnDateChanged(DateTime? value) => Validate();
    partial void OnPriorityChanged(RacePriority value) => Validate();
    partial void OnDurationHoursChanged(double value) => Validate();
    partial void OnIntensityChanged(int value) => Validate();

    private Race? Build() => Date is null ? null : new Race
    {
        Id = EditId,
        Name = Name.Trim(),
        Date = DateOnly.FromDateTime(Date.Value),
        Type = Type,
        DurationHours = DurationHours,
        Intensity = Intensity,
        Priority = Priority,
    };

    /// <summary>Live check, so an A race closer than 3 months to another warns before saving.</summary>
    private void Validate()
    {
        var race = Build();
        Validation = race is null ? "Pick a date." : string.IsNullOrWhiteSpace(Name) ? null : Trainer.ValidateRace(race);
    }

    [RelayCommand]
    private void New()
    {
        Selected = null;
        EditId = 0;
        Name = "";
        Date = Trainer.Today.AddDays(90).ToDateTime(TimeOnly.MinValue);
        Type = EventType.RoadRace;
        DurationHours = 3;
        Intensity = 7;
        Priority = RacePriority.A;
        Validation = null;
        OnPropertyChanged(nameof(EditorTitle));
    }

    [RelayCommand]
    private Task Save() => RunAsync(async () =>
    {
        var race = Build() ?? throw new InvalidOperationException("Pick a date.");
        await Task.Run(() => Trainer.SaveRace(race));
        Shell?.Toast($"{race.Name} saved. The plan was regenerated around it.");
        New();
        Load();
    }, "Saving and re-planning…");

    [RelayCommand]
    private Task Delete() => RunAsync(async () =>
    {
        if (EditId == 0 || !await Dialogs.Confirm($"Delete {Name}? The plan will be regenerated.")) return;
        var id = EditId;
        await Task.Run(() => Trainer.DeleteRace(id));
        New();
        Load();
    }, "Re-planning…");
}
