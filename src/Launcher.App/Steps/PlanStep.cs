using System.Collections.ObjectModel;
using ModlauncherIV.Core.Planning;

namespace ModlauncherIV.App;

/// <summary>One step of the path, as it appears in the list.</summary>
public sealed class PlanRow(int number, JourneyStep step)
{
    public string Number => $"{number}.";

    public string Name => step.Recipe.Name;

    public string Detail => step.Recipe.Description ?? step.Recipe.Id;

    public bool IsDone => step.State == JourneyStepState.AlreadyInstalled;

    public string State => step switch
    {
        { State: JourneyStepState.AlreadyInstalled } => "already there",
        { State: JourneyStepState.NeedsUpdate } => $"update to {step.Recipe.Version}",
        { Reason: JourneyReason.VersionTransition } => "version change",
        { Reason: JourneyReason.Dependency } => "required by another",
        _ => "picked by you",
    };
}

public sealed class PlanStep(Session session) : WizardStep(session)
{
    public override string Title => "The plan";

    public override string Lead =>
        "This is what happens, in this order. Up to the next step nothing in the "
        + "game has been changed.";

    public override string NextLabel => "Get the files";

    public ObservableCollection<PlanRow> Rows { get; } = [];

    public ObservableCollection<JourneyProblem> Problems { get; } = [];

    /// <summary>
    /// What this run would leave installed and not working. Only a version
    /// change produces any: everything in the ledger fitted the game when it
    /// was installed, and moving the game underneath it is what breaks that.
    /// </summary>
    public ObservableCollection<StrandedRecipe> LeftBehind { get; } = [];

    public bool HasLeftBehind => LeftBehind.Count > 0;

    /// <summary>The sentence above that list. Names the version being moved to.</summary>
    public string LeftBehindLead =>
        $"These are installed and were made for another version than {Session.TargetVersion}. "
        + "They stay on disk and stop working - take them back on the home page if that is not what you want.";

    public string Summary { get; private set; } = string.Empty;

    public bool HasProblems => Problems.Count > 0;

    public bool NothingToDo => Problems.Count == 0 && Rows.All(r => r.IsDone);

    public override bool CanGoNext =>
        Session.Journey is { IsPossible: true } journey && journey.Remaining.Count > 0;

    public override Task EnterAsync()
    {
        Rows.Clear();
        Problems.Clear();
        LeftBehind.Clear();

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

        foreach (var left in journey.LeftBehind)
        {
            LeftBehind.Add(left);
        }

        var open = journey.Remaining.Count;
        Summary = open switch
        {
            0 when journey.Steps.Count > 0 => "All of it is already installed. There is nothing to do.",
            0 => "There is nothing to do.",
            1 => "One step is open.",
            _ => $"{open} steps are open.",
        };

        Raise(nameof(Summary));
        Raise(nameof(HasProblems));
        Raise(nameof(HasLeftBehind));
        Raise(nameof(LeftBehindLead));
        Raise(nameof(NothingToDo));
        NotifyChanged();

        return Task.CompletedTask;
    }
}
