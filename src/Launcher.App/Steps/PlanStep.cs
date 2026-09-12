using System.Collections.ObjectModel;
using ModlauncherIV.Core.Planning;

namespace ModlauncherIV.App;

/// <summary>Ein Schritt des Wegs, wie er in der Liste steht.</summary>
public sealed class PlanRow(int number, JourneyStep step)
{
    public string Number => $"{number}.";

    public string Name => step.Recipe.Name;

    public string Detail => step.Recipe.Description ?? step.Recipe.Id;

    public bool IsDone => step.State == JourneyStepState.AlreadyInstalled;

    public string State => step.Reason switch
    {
        _ when IsDone => "bereits vorhanden",
        JourneyReason.VersionTransition => "Versionswechsel",
        JourneyReason.Dependency => "wird vorausgesetzt",
        _ => "von dir gewählt",
    };
}

public sealed class PlanStep(Session session) : WizardStep(session)
{
    public override string Title => "Der Plan";

    public override string Lead =>
        "Das passiert, in dieser Reihenfolge. Bis zum nächsten Schritt wurde am "
        + "Spiel nichts verändert.";

    public override string NextLabel => "Dateien beschaffen";

    public ObservableCollection<PlanRow> Rows { get; } = [];

    public ObservableCollection<JourneyProblem> Problems { get; } = [];

    public string Summary { get; private set; } = string.Empty;

    public bool HasProblems => Problems.Count > 0;

    public bool NothingToDo => Problems.Count == 0 && Rows.All(r => r.IsDone);

    public override bool CanGoNext =>
        Session.Journey is { IsPossible: true } journey && journey.Remaining.Count > 0;

    public override Task EnterAsync()
    {
        Rows.Clear();
        Problems.Clear();

        var journey = JourneyPlanner.Plan(
            new JourneyRequest(Session.TargetVersion, Session.Wanted),
            Session.Catalog?.Recipes ?? [],
            Session.Install?.Version.Raw ?? string.Empty,
            Session.Ledger);

        Session.Journey = journey;

        for (var i = 0; i < journey.Steps.Count; i++)
        {
            Rows.Add(new PlanRow(i + 1, journey.Steps[i]));
        }

        foreach (var problem in journey.Problems)
        {
            Problems.Add(problem);
        }

        var open = journey.Remaining.Count;
        Summary = open switch
        {
            0 when journey.Steps.Count > 0 => "Alles davon ist schon eingebaut. Es gibt nichts zu tun.",
            0 => "Es gibt nichts zu tun.",
            1 => "Ein Schritt ist offen.",
            _ => $"{open} Schritte sind offen.",
        };

        Raise(nameof(Summary));
        Raise(nameof(HasProblems));
        Raise(nameof(NothingToDo));
        NotifyChanged();

        return Task.CompletedTask;
    }
}
