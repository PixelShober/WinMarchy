namespace WinMarchy.Core.GlazeWm;

/// <summary>Which workspaces each screen gets, as the Tiling page suggests it.</summary>
public static class ScreenPlan
{
    /// <summary>What the main screen gets.</summary>
    public const int MainCount = 2;

    /// <summary>
    /// The main screen gets workspaces 1 and 2, the other screens share the rest evenly from left to
    /// right (the left ones one more where it does not come out even). A single screen gets them all.
    /// There are five workspaces with one screen and six with more (<see cref="GlazeConfig.WorkspaceLimit"/>).
    /// </summary>
    /// <returns>Per screen, in the screens' order, its workspace numbers.</returns>
    public static List<List<int>> Split(int screens, int main) => Split(screens, main, GlazeConfig.WorkspaceLimit(screens));

    /// <summary>As <see cref="Split(int, int)"/>, over <paramref name="count"/> workspaces.</summary>
    public static List<List<int>> Split(int screens, int main, int count)
    {
        var plan = Enumerable.Range(0, Math.Max(0, screens)).Select(_ => new List<int>()).ToList();
        if (plan.Count == 0) return plan;
        main = Math.Clamp(main, 0, plan.Count - 1);
        if (plan.Count == 1)
        {
            plan[0].AddRange(Enumerable.Range(1, count));
            return plan;
        }
        int own = Math.Min(MainCount, count);
        plan[main].AddRange(Enumerable.Range(1, own));
        var others = Enumerable.Range(0, plan.Count).Where(i => i != main).ToList();
        int rest = count - own, per = rest / others.Count, extra = rest % others.Count, next = own + 1;
        for (int k = 0; k < others.Count; k++)
        {
            int take = per + (k < extra ? 1 : 0);
            plan[others[k]].AddRange(Enumerable.Range(next, take));
            next += take;
        }
        return plan;
    }
}
