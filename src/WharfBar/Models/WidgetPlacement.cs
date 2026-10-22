namespace WharfBar.Models;

/// <summary>Soft desktop-widget position. <c>schemaVersion</c> stays 2.</summary>
internal sealed class WidgetPlacement
{
    public string Id { get; set; } = "";

    public double X { get; set; }

    public double Y { get; set; }
}
