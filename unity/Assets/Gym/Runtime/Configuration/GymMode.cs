namespace Gym.Runtime
{
    /// <summary>Train: random 720-step episodes on the training segment. Eval: one full pass over validation or test.</summary>
    public enum GymMode
    {
        Train,
        Eval,
    }
}
