namespace MekTok.Core.Platform;

public interface IStartupManager
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

