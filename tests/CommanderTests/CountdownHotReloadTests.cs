using System.Numerics;
using ZePractice;

internal static class CountdownHotReloadTests
{
    public static void Run(Action<bool, string> check)
    {
        Vector3[] min = [new(0,-1.5f,-12),new(4,-1.5f,7),new(0,-1.5f,-12),new(0,-1.5f,-8),new(4,-1.5f,-12),
            new(0,-1.5f,-12),new(0,-1.5f,-12),new(4,-1.5f,-8),new(0,-1.5f,-12),new(0,-1.5f,-12)];
        Vector3[] max = [new(48,1.5f,12),new(44,1.5f,12),new(48,1.5f,12),new(48,1.5f,12),new(44,1.5f,12),
            new(48,1.5f,12),new(48,1.5f,12),new(48,1.5f,12),new(48,1.5f,12),new(48,1.5f,12)];
        for (int i=0;i<10;i++)
        {
            check(CommanderCountdownLayout.MatchesCompiledBounds(i,min[i],max[i]),"mounted glyph bounds match actual engine "+i);
            check(!CommanderCountdownLayout.MatchesCompiledBounds(i,new(-16),new(16)),"error-model bounds rejected "+i);
            check(!CommanderCountdownLayout.MatchesCompiledBounds(i,new(min[i].Y,min[i].Z,min[i].X),new(max[i].Y,max[i].Z,max[i].X)),"different model basis rejected "+i);
        }
        check(!CommanderCountdownLayout.MatchesCompiledBounds(-1,Vector3.Zero,Vector3.Zero),"negative glyph rejected");
        check(!CommanderCountdownLayout.MatchesCompiledBounds(10,Vector3.Zero,Vector3.Zero),"unknown glyph rejected");
        check(!CommanderCountdownLayout.MatchesCompiledBounds(8,new(float.NaN,0,0),new(48,1.5f,12)),"nonfinite mounted bounds rejected");
    }
}
