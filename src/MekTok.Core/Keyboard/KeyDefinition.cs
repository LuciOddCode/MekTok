namespace MekTok.Core.Keyboard;

/// <summary>
/// Immutable physical definition and ANSI geometry of a single keyboard key.
/// </summary>
public sealed record KeyDefinition(
    int ScanCode,
    string KeyId,
    string Label,
    KeyGroup Group,
    double Row,
    double Col,
    double WidthU,
    double HeightU,
    bool IsNumpad,
    float PanPosition);

