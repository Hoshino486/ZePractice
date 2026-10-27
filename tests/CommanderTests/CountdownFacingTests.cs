using System.Numerics;
using ZePractice;

internal static class CountdownFacingTests
{
    // Independent Source QAngle -> world basis. This tests the actual compiled mesh axes,
    // not just entity pitch/roll being zero (the old regression let the sideways mesh pass).
    private static Vector3 Rotate(Vector3 v, Vector3 degrees)
    {
        float p=degrees.X*MathF.PI/180, y=degrees.Y*MathF.PI/180, r=degrees.Z*MathF.PI/180;
        float cp=MathF.Cos(p),sp=MathF.Sin(p),cy=MathF.Cos(y),sy=MathF.Sin(y),cr=MathF.Cos(r),sr=MathF.Sin(r);
        return v.X*new Vector3(cp*cy,cp*sy,-sp) +
            v.Y*new Vector3(sr*sp*cy-cr*sy,sr*sp*sy+cr*cy,sr*cp) +
            v.Z*new Vector3(cr*sp*cy+sr*sy,cr*sp*sy-sr*cy,cr*cp);
    }
    public static void Run(Action<bool,string> check)
    {
        bool Near(Vector3 a,Vector3 b)=>Vector3.Distance(a,b)<.0002f;
        foreach(float yaw in new[]{-180f,-135,-90,-45,0,45,90,135,180,275})
        {
            float a=yaw*MathF.PI/180;
            var towardViewer=new Vector3(MathF.Cos(a),MathF.Sin(a),0);
            var screenRight=new Vector3(-MathF.Sin(a),MathF.Cos(a),0);
            var angles=CommanderCountdownLayout.ModelAngles(yaw);
            check(Near(Rotate(Vector3.UnitX,angles),Vector3.UnitZ),"compiled X height maps upright at yaw "+yaw);
            check(Near(Rotate(Vector3.UnitY,angles),towardViewer),"compiled Y front faces viewer at yaw "+yaw);
            check(Near(Rotate(Vector3.UnitZ,angles),screenRight),"compiled Z text-right is not mirrored at yaw "+yaw);
            var basePos=CommanderCountdownLayout.Position(Vector3.Zero,yaw,0,1);
            var min=new Vector3(float.PositiveInfinity);
            var max=new Vector3(float.NegativeInfinity);
            foreach(float x in new[]{0f,48})foreach(float y in new[]{-1.5f,1.5f})foreach(float z in new[]{-12f,12f})
            {
                var world=basePos+Rotate(new(x,y,z),angles);
                var projected=new Vector3(Vector3.Dot(world,screenRight),Vector3.Dot(world,towardViewer),world.Z);
                min=Vector3.Min(min,projected);max=Vector3.Max(max,projected);
            }
            check(Near(max-min,new(24,3,48)),"actual compiled bounds stay wide and tall, not edge-on at yaw "+yaw);
            check(Math.Abs(min.Z-CommanderCountdownLayout.BaselineAboveIcon)<.0002f,"actual mesh bottom is above icon at yaw "+yaw);
            foreach(int count in new[]{1,2,3,10})
            {
                var first=CommanderCountdownLayout.Position(Vector3.Zero,yaw,0,count);
                var last=CommanderCountdownLayout.Position(Vector3.Zero,yaw,count-1,count);
                check(Math.Abs(Vector3.Dot(last-first,screenRight)-(count-1)*CommanderCountdownLayout.Spacing)<.001f,
                    $"{count} digits left-to-right at yaw {yaw}");
            }
        }
        var oldUp=Rotate(Vector3.UnitX,new(0,0,0));
        check(Math.Abs(Vector3.Dot(oldUp,Vector3.UnitZ))<.01f,"regression rejects old yaw-only orientation");
        var s=new CommanderMarkerState();var mark=s.Place(Vector3.Zero,20);
        check(CommanderCountdownLayout.Digits(CommanderMarkerState.Remaining(mark,20))=="10","initial displayed value 10");
        check(CommanderCountdownLayout.Digits(CommanderMarkerState.Remaining(mark,21))=="9","10 to 9 loses leading digit");
        mark=s.Extend(mark.Id,21)!;
        check(CommanderCountdownLayout.Digits(CommanderMarkerState.Remaining(mark,21))=="19","extension from 9 becomes 19, not restarted");
        s.Expire(40);check(s.Markers.Count==0,"extended marker still expires on original game-time deadline");
    }
}
