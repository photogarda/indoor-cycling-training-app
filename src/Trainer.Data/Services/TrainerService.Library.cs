using Microsoft.EntityFrameworkCore;
using Trainer.Core.Models;

namespace Trainer.Data.Services;

public partial class TrainerService
{
    public List<WorkoutTemplate> GetTemplates()
    {
        using var db = Db();
        return db.WorkoutTemplates.AsNoTracking().OrderBy(t => t.Kind).ThenBy(t => t.Id).ToList();
    }

    public void SaveTemplate(WorkoutTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Name)) throw new TrainerValidationException("Give the workout a name.");
        if (template.Steps.Count == 0) throw new TrainerValidationException("The workout has no steps.");
        using var db = Db();
        if (db.WorkoutTemplates.Any(t => t.Name == template.Name && t.Id != template.Id))
            throw new TrainerValidationException($"A workout called \"{template.Name}\" already exists.");
        if (template.Id == 0)
        {
            template.BuiltIn = false;
            db.WorkoutTemplates.Add(template);
        }
        else
        {
            var cur = db.WorkoutTemplates.First(t => t.Id == template.Id);
            if (cur.BuiltIn) throw new TrainerValidationException("Built-in workouts can't be edited. Duplicate it first.");
            db.Entry(cur).CurrentValues.SetValues(template);
            cur.Steps = template.Steps;
        }
        db.SaveChanges();
        OnChanged();
    }

    public void DeleteTemplate(int id)
    {
        using var db = Db();
        var t = db.WorkoutTemplates.First(x => x.Id == id);
        if (t.BuiltIn) throw new TrainerValidationException("Built-in workouts can't be deleted.");
        db.WorkoutTemplates.Remove(t);
        db.SaveChanges();
        OnChanged();
    }
}
