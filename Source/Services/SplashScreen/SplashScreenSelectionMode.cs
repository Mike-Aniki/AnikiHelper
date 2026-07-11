namespace AnikiHelper.Services.SplashScreen
{
    public enum SplashScreenSelectionMode
    {
        // Kept as the serialized value for the default mode, but displayed as "Game priority" in the UI.
        Automatic,

        // Custom user-defined priority order.
        CustomPriority,

        // Kept for backward compatibility with existing configs. Displayed as priority modes in the UI.
        AlwaysSource,
        AlwaysPlatform,
        AlwaysGlobal
    }

    public enum SplashScreenPriorityTarget
    {
        None,
        GameCustom,
        GameBackground,
        Platform,
        Source,
        Global
    }
}
