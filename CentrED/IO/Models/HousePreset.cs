namespace CentrED.IO.Models;

public class HousePreset
{
    public int Width { get; set; } = 8;
    public int Depth { get; set; } = 8;
    public int Stories { get; set; } = 1;
    public int StoryHeight { get; set; } = 20;
    public int WindowSpacing { get; set; } = 3;
    public int DoorSide { get; set; } = 1;

    public ushort FloorTile { get; set; }
    public ushort NorthCornerTile { get; set; }
    public ushort SouthCornerTile { get; set; }
    public ushort HorizontalWallTile { get; set; }
    public ushort VerticalWallTile { get; set; }
    public ushort HorizontalWindowTile { get; set; }
    public ushort VerticalWindowTile { get; set; }
    public ushort DoorTile { get; set; }

    public bool WithWindows { get; set; } = true;
    public bool SnapToTerrain { get; set; } = true;

    public int RoofType { get; set; }
    public ushort FlatRoofTile { get; set; }
    public ushort RoofSlopeATile { get; set; }
    public ushort RoofSlopeBTile { get; set; }
    public ushort RoofRidgeTile { get; set; }
    public ushort RoofSlopeAEdgeStartTile { get; set; }
    public ushort RoofSlopeAEdgeEndTile { get; set; }
    public ushort RoofSlopeBEdgeStartTile { get; set; }
    public ushort RoofSlopeBEdgeEndTile { get; set; }
    public ushort RoofRidgeStartTile { get; set; }
    public ushort RoofRidgeEndTile { get; set; }
    public ushort GableStartTile { get; set; }
    public ushort GableEndTile { get; set; }
    public int RoofRiseStep { get; set; } = 3;
    public int RoofOverhang { get; set; }

    public bool WithStairs { get; set; }
    public ushort StairTile { get; set; }
    public int StairDirection { get; set; }
    public int StairRiseStep { get; set; } = 3;
    public int StairOffsetX { get; set; } = 1;
    public int StairOffsetY { get; set; } = 1;
}
