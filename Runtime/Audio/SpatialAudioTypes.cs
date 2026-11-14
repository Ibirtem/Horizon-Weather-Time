namespace BlackHorizon.HorizonWeatherTime
{
    /// <summary>
    /// Defines how a spatial audio emitter is positioned relative to the listener.
    /// </summary>
    public enum SpatialAudioPlacementMode
    {
        /// <summary>Emitter is placed at a fixed offset from the listener.</summary>
        ListenerRelative = 0,

        /// <summary>Emitter orbits the listener on a horizontal plane.</summary>
        OrbitListener = 1,

        /// <summary>Emitter floats at a fixed height above the listener.</summary>
        AboveListener = 2,

        /// <summary>Emitter follows the strongest opening in the room (window, doorway, broken wall).</summary>
        OpeningDirection = 3,

        /// <summary>Emitter stays at its authored world position.</summary>
        FixedWorld = 4
    }

    /// <summary>
    /// Acoustic environment condition that gates an emitter's loudness.
    /// </summary>
    public enum SpatialAudioCondition
    {
        /// <summary>Plays everywhere.</summary>
        Always = 0,

        /// <summary>Plays when the listener is exposed to the sky or open space.</summary>
        Outdoors = 1,

        /// <summary>Plays when the listener is enclosed and sound has to pass through walls.</summary>
        Indoors = 2,

        /// <summary>Plays when the listener is directly under a single-story roof.</summary>
        UnderRoof = 3,

        /// <summary>Plays when the listener is indoors but close to an opening to the outside.</summary>
        Opening = 4,

        /// <summary>Plays whenever the listener is under any kind of cover.</summary>
        Sheltered = 5
    }

    /// <summary>
    /// Identifies which acoustic probe produced a debug sample.
    /// </summary>
    public enum SpatialAudioProbeKind
    {
        Zenith = 0,
        SkyCone = 1,
        SkyDown = 2,
        Wall = 3,
        OutsideWall = 4
    }
}
