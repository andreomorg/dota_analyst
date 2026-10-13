namespace DotaAnalyst.Domain.Analysis;

public static class LaneMatchup
{
    public static int? OpposingRole(int role) => role switch
    {
        1 => 3,
        2 => 2,
        3 => 1,
        4 => 5,
        5 => 4,
        _ => null,
    };
}
