namespace ClipShot;
public sealed class NoticeLifetime
{
    private TimeSpan? returned;
    public byte Opacity(TimeSpan elapsed,bool mouseMoved)
    {
        if(mouseMoved)returned??=elapsed;
        if(returned is null)return 255;
        double progress=(elapsed-returned.Value-TimeSpan.FromMilliseconds(1500)).TotalMilliseconds/900;
        return (byte)Math.Round(255*(1-Math.Clamp(progress,0,1)));
    }
}
