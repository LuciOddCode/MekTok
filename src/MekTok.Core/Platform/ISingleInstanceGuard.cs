namespace MekTok.Core.Platform;

public interface ISingleInstanceGuard : IDisposable
{
    bool IsPrimary { get; }

    event EventHandler? SecondInstanceSignaled;

    void NotifyPrimaryInstance();
}

