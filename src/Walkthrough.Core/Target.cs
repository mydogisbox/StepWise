namespace Walkthrough.Core;

/// <summary>
/// Base class for execution targets. Manages step registration and routing by request type.
/// TSelf is the concrete target type (CRTP) — used only to preserve the return type of Register.
/// </summary>
public abstract class Target<TSelf, TStep>
    where TSelf : Target<TSelf, TStep>
    where TStep : IStep
{
    protected readonly Dictionary<Type, TStep> _steps = [];

    public bool CanHandle(Type requestType) => _steps.ContainsKey(requestType);

    public TSelf Register(TStep step)
    {
        _steps[step.RequestType] = step;
        return (TSelf)this;
    }
}
