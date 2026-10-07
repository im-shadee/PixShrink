using System;

namespace PixShrink.Models;

[Serializable]
public class WindowSettings
{
    public double Height { get; set; }
    public double Width { get; set; }
    public bool IsMaximized { get; set; }
}
