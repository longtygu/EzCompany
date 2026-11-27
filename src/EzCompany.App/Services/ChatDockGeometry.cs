namespace EzCompany.App.Services;

public readonly record struct ScreenRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Math.Max(0, Right - Left);
    public double Height => Math.Max(0, Bottom - Top);
    public double Area => Width * Height;
    public double CenterX => Left + Width / 2.0;
    public double CenterY => Top + Height / 2.0;

    public bool ContainsPoint(double x, double y) =>
        x >= Left && x <= Right && y >= Top && y <= Bottom;
}

/// <summary>
/// Pure geometry helper for detecting when a dragged detached chat window overlaps
/// the main workspace window enough to trigger automatic redocking.
/// </summary>
public static class ChatDockGeometry
{
    public static bool ShouldRedockToMainWindow(ScreenRect detachedRect, ScreenRect hostRect)
    {
        if (detachedRect.Area <= 0 || hostRect.Area <= 0)
        {
            return false;
        }

        if (hostRect.ContainsPoint(detachedRect.CenterX, detachedRect.CenterY))
        {
            return true;
        }

        double interLeft = Math.Max(detachedRect.Left, hostRect.Left);
        double interTop = Math.Max(detachedRect.Top, hostRect.Top);
        double interRight = Math.Min(detachedRect.Right, hostRect.Right);
        double interBottom = Math.Min(detachedRect.Bottom, hostRect.Bottom);

        double interWidth = Math.Max(0.0, interRight - interLeft);
        double interHeight = Math.Max(0.0, interBottom - interTop);
        double intersectionArea = interWidth * interHeight;

        return intersectionArea / detachedRect.Area >= 0.25;
    }
}
