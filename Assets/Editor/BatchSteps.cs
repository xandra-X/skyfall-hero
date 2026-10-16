/// Batch-mode entry points (used by the command-line tests).
public static class BatchSteps
{
    public static void SupermanAndDestruction()
    {
        SupermanSetup.Run();
        DestructionSetup.Run();
    }

    public static void SupermanAndLife()
    {
        SupermanSetup.Run();
        LifeSetup.Run();
    }
}
