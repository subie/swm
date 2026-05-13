namespace ScrollingWM.Core;

public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public static Rect FromSize(int left, int top, int width, int height) =>
        new(left, top, left + width, top + height);
}
