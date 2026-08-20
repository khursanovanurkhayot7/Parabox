namespace Luxodd.Game.Scripts.Input
{
    /// <summary>
    /// The physical Luxodd cabinet button colours.
    ///
    /// This type belongs to the Runtime assembly because both the runtime input API and the
    /// optional Input System backend use it. Keeping it under Scripts/Input makes it part of
    /// Assembly-CSharp, which a named assembly such as Luxodd.Game.Runtime cannot reference.
    /// </summary>
    public enum ArcadeButtonColor
    {
        Black,
        Red,
        Green,
        Yellow,
        Blue,
        Purple,
        Orange,
        White
    }
}
