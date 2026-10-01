using Microsoft.EntityFrameworkCore;
using Trainer.Core.Models;
using Trainer.Core.Planning;

namespace Trainer.Data.Services;

public partial class TrainerService
{
    public List<Race> GetRaces()
    {
        using var db = Db();
        return db.Races.AsNoTracking().OrderBy(r => r.Date).ToList();
    }

    /// <summary>Returns the problem with a race, or null. A races closer than 3 months are blocked.</summary>
    public string? ValidateRace(Race race)
    {
        using var db = Db();
        return RaceRules.Validate(race, db.Races.AsNoTracking().ToList());
    }

    public void SaveRace(Race race)
    {
        using (var db = Db())
        {
            var error = RaceRules.Validate(race, db.Races.AsNoTracking().ToList());
            if (error is not null) throw new TrainerValidationException(error);
            if (race.Id == 0) db.Races.Add(race);
            else db.Races.Update(race);
            db.SaveChanges();
        }
        Refresh();
    }

    public void DeleteRace(int id)
    {
        using (var db = Db())
        {
            db.Races.Where(r => r.Id == id).ExecuteDelete();
        }
        Refresh();
    }
}
