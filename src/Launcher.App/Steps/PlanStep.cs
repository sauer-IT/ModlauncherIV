using System.Collections.ObjectModel;
using ModlauncherIV.Core.Planning;

namespace ModlauncherIV.App;

/// <summary>One step of the path, as it appears in the list.</summary>
public sealed class PlanRow(int number, JourneyStep step)
{
    public string Number => $"{number}.";

    public string Name => step.Recipe.Name;

    public string Detail => step switch
    {
        { Reason: JourneyReason.TakeBack, Recipe.IsVersionTransition: true } =>
            "Taken back first: the files it replaced come back from the snapshot taken when it was installed. "
            + "That is the original game the next version change starts from - nothing is downloaded for this.",

        { Reason: JourneyReason.TakeBack } =>
            $"Made for {string.Join(", ", step.Recipe.AppliesTo)}, not for the version this ends on - left in, it "
            + "would not load, and some keep the game from starting. Taken back from its snapshot; its files stay "
            + "in the cache, so installing it again later downloads nothing.",

        _ => step.Recipe.Description ?? step.Recipe.Id,
    };

    public bool IsDone => step.State == JourneyStepState.AlreadyInstalled;

    public string State => step switch
    {
        { Reason: JourneyReason.TakeBack } => "taken back",
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
            0 when journey.Steps.Count > 0 => "All of it is already installed. There is nothing to do.",
            0 => "There is nothing to do.",
            1 => "One step is open.",
            _ => $"{open} steps are open.",
        };

        Raise(nameof(Summary));
        Raise(nameof(HasProblems));
        Raise(nameof(NothingToDo));
        NotifyChanged();

        return Task.CompletedTask;
    }
}
