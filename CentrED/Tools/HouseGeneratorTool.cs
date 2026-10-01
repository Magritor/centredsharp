using CentrED.Map;
using CentrED.UI;
using CentrED.UI.Windows;
using Hexa.NET.ImGui;
using Microsoft.Xna.Framework.Input;

namespace CentrED.Tools;

public class HouseGeneratorTool : Tool
{
    private enum HouseSide
    {
        North,
        South,
        West,
        East
    }

    private enum RoofType
    {
        None,
        Flat,
        GableNorthSouth,
        GableEastWest
    }

    private readonly TilesWindow _tilesWindow;

    private int _width = 8;
    private int _depth = 8;
    private int _stories = 1;
    private int _storyHeight = 20;
    private int _windowSpacing = 3;
    private int _doorSide = (int)HouseSide.South;

    private ushort _floorTile;
    private ushort _northCornerTile;
    private ushort _southCornerTile;
    private ushort _horizontalWallTile;
    private ushort _verticalWallTile;
    private ushort _horizontalWindowTile;
    private ushort _verticalWindowTile;
    private ushort _doorTile;

    private int _roofType = (int)RoofType.None;
    private ushort _flatRoofTile;
    private ushort _roofSlopeATile;
    private ushort _roofSlopeBTile;
    private ushort _roofRidgeTile;
    private int _roofRiseStep = 3;
    private int _roofOverhang;

    private bool _withWindows = true;
    private bool _snapToTerrain = true;

    private TileObject? _previewParent;

    public HouseGeneratorTool()
    {
        _tilesWindow = UIManager.GetWindow<TilesWindow>();
    }

    public override string Name => "House generator";
    public override Keys Shortcut => Keys.None;

    internal override void Draw()
    {
        ImGui.Text("Dimensions");
        ImGuiEx.DragInt("Width", ref _width, 1, 3, 30);
        ImGuiEx.DragInt("Depth", ref _depth, 1, 3, 30);
        ImGuiEx.DragInt("Stories", ref _stories, 1, 1, 3);
        ImGuiEx.DragInt("Story height", ref _storyHeight, 1, 5, 30);

        ImGui.Separator();
        ImGui.Text("Structure tiles");
        ImGui.TextDisabled("Drag static tiles here from the Tiles window.");
        DrawTileSlot("Floor", ref _floorTile);
        DrawTileSlot("North corner", ref _northCornerTile, true);
        DrawTileSlot("South corner", ref _southCornerTile, true);
        DrawTileSlot("Horizontal wall", ref _horizontalWallTile);
        DrawTileSlot("Vertical wall", ref _verticalWallTile);

        ImGui.Separator();
        ImGui.Checkbox("Windows", ref _withWindows);
        if (_withWindows)
        {
            DrawTileSlot("Horizontal window", ref _horizontalWindowTile, true);
            DrawTileSlot("Vertical window", ref _verticalWindowTile, true);
            ImGuiEx.DragInt("Window spacing", ref _windowSpacing, 1, 2, 8);
        }

        DrawTileSlot("Door", ref _doorTile, true);

        ImGui.Text("Door side");
        ImGui.RadioButton("North", ref _doorSide, (int)HouseSide.North);
        ImGui.SameLine();
        ImGui.RadioButton("South", ref _doorSide, (int)HouseSide.South);
        ImGui.RadioButton("West", ref _doorSide, (int)HouseSide.West);
        ImGui.SameLine();
        ImGui.RadioButton("East", ref _doorSide, (int)HouseSide.East);

        DrawRoofConfiguration();

        ImGui.Separator();
        ImGui.Checkbox("Snap base Z to terrain", ref _snapToTerrain);
        if (!_snapToTerrain)
            ImGuiEx.DragInt("Base Z", ref MapManager.VirtualLayerZ, 1, sbyte.MinValue, sbyte.MaxValue);

        ImGui.Separator();

        if (!HasAnyStructureTile())
        {
            ImGui.TextDisabled("Add at least one floor/wall tile to enable preview.");
        }
        else if (!IsRoofConfigurationValid())
        {
            ImGui.TextDisabled("Roof configuration is incomplete. Add the required roof tiles or select None.");
        }
        else
        {
            ImGui.TextWrapped("Move the mouse over the map to preview. Click once to place the complete house as one undo group.");
        }

        ImGui.TextDisabled("Right-click a tile slot to clear it.");
    }

    private void DrawRoofConfiguration()
    {
        ImGui.Separator();
        ImGui.Text("Roof");

        ImGui.RadioButton("None", ref _roofType, (int)RoofType.None);
        ImGui.SameLine();
        ImGui.RadioButton("Flat", ref _roofType, (int)RoofType.Flat);

        ImGui.RadioButton("Gable N-S", ref _roofType, (int)RoofType.GableNorthSouth);
        ImGui.SameLine();
        ImGui.RadioButton("Gable E-W", ref _roofType, (int)RoofType.GableEastWest);

        var roofType = (RoofType)_roofType;

        if (roofType == RoofType.Flat)
        {
            DrawTileSlot("Flat roof", ref _flatRoofTile);
            ImGuiEx.DragInt("Roof overhang", ref _roofOverhang, 1, 0, 2);
        }
        else if (roofType is RoofType.GableNorthSouth or RoofType.GableEastWest)
        {
            ImGui.TextDisabled(
                roofType == RoofType.GableNorthSouth
                    ? "Ridge runs North-South; slopes rise from West/East."
                    : "Ridge runs East-West; slopes rise from North/South.");

            DrawTileSlot("Slope A", ref _roofSlopeATile);
            DrawTileSlot("Slope B", ref _roofSlopeBTile);
            DrawTileSlot("Ridge", ref _roofRidgeTile, true);

            ImGuiEx.DragInt("Roof rise / row", ref _roofRiseStep, 1, 1, 10);
            ImGuiEx.DragInt("Roof overhang", ref _roofOverhang, 1, 0, 2);
        }
    }

    public override void OnActivated(TileObject? o)
    {
        UpdatePreview(o);
    }

    public override void OnDeactivated(TileObject? o)
    {
        ClearPreview();
    }

    public override void OnMouseEnter(TileObject? o)
    {
        UpdatePreview(o);
    }

    public override void OnMouseLeave(TileObject? o)
    {
        ClearPreview();
    }

    public override void OnMousePressed(TileObject? o)
    {
        if (o == null || !HasAnyStructureTile() || !IsRoofConfigurationValid())
            return;

        var tiles = BuildHouse(o.Tile.X, o.Tile.Y, GetBaseZ(o));
        if (tiles.Count == 0)
            return;

        Client.BeginUndoGroup();
        foreach (var tile in tiles)
            Client.Add(tile);
        Client.EndUndoGroup();

        UpdatePreview(o);
    }

    private void UpdatePreview(TileObject? o)
    {
        ClearPreview();

        if (o == null || !Client.Running || !HasAnyStructureTile() || !IsRoofConfigurationValid())
            return;

        var tiles = BuildHouse(o.Tile.X, o.Tile.Y, GetBaseZ(o));
        if (tiles.Count == 0)
            return;

        var parent = MapManager.LandTiles[o.Tile.X, o.Tile.Y];
        if (parent == null)
            return;

        var ghosts = tiles.Select(t => new StaticObject(t)).ToList();
        MapManager.StaticsManager.AddGhosts(parent, ghosts);
        _previewParent = parent;
    }

    private void ClearPreview()
    {
        if (_previewParent != null)
            MapManager.StaticsManager.ClearGhost(_previewParent);

        _previewParent = null;
    }

    private bool HasAnyStructureTile()
    {
        return _floorTile > 0 || _horizontalWallTile > 0 || _verticalWallTile > 0;
    }

    private bool IsRoofConfigurationValid()
    {
        return (RoofType)_roofType switch
        {
            RoofType.None => true,
            RoofType.Flat => _flatRoofTile > 0,
            RoofType.GableNorthSouth or RoofType.GableEastWest =>
                _roofSlopeATile > 0 && _roofSlopeBTile > 0,
            _ => false
        };
    }

    private sbyte GetBaseZ(TileObject o)
    {
        if (!_snapToTerrain)
            return (sbyte)Math.Clamp(MapManager.VirtualLayerZ, sbyte.MinValue, sbyte.MaxValue);

        var land = MapManager.LandTiles[o.Tile.X, o.Tile.Y];
        return land?.Tile.Z ?? o.Tile.Z;
    }

    private List<StaticTile> BuildHouse(ushort startX, ushort startY, sbyte baseZ)
    {
        var result = new List<StaticTile>();

        int width = Math.Clamp(_width, 3, 30);
        int depth = Math.Clamp(_depth, 3, 30);
        int stories = Math.Clamp(_stories, 1, 3);

        int endX = startX + width - 1;
        int endY = startY + depth - 1;

        if (!IsValidMapPosition(startX, startY) || !IsValidMapPosition(endX, endY))
            return result;

        for (int story = 0; story < stories; story++)
        {
            int rawZ = baseZ + story * _storyHeight;
            sbyte z = ClampZ(rawZ);

            if (_floorTile > 0)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < depth; y++)
                        result.Add(NewTile(_floorTile, startX + x, startY + y, z));
                }
            }

            AddWalls(result, startX, startY, width, depth, z, story == 0);
        }

        int roofBaseZ = baseZ + stories * _storyHeight;
        AddRoof(result, startX, startY, width, depth, roofBaseZ);

        return result;
    }

    private void AddWalls(
        List<StaticTile> result,
        ushort startX,
        ushort startY,
        int width,
        int depth,
        sbyte z,
        bool groundFloor)
    {
        int doorOffsetHorizontal = width / 2;
        int doorOffsetVertical = depth / 2;

        for (int x = 0; x < width; x++)
        {
            ushort northId = SelectWallTile(HouseSide.North, x, width, groundFloor, doorOffsetHorizontal);
            ushort southId = SelectWallTile(HouseSide.South, x, width, groundFloor, doorOffsetHorizontal);

            if (x == 0 && _northCornerTile > 0)
                northId = _northCornerTile;
            if (x == width - 1 && _southCornerTile > 0)
                southId = _southCornerTile;

            AddIfValid(result, northId, startX + x, startY, z);
            AddIfValid(result, southId, startX + x, startY + depth - 1, z);
        }

        for (int y = 1; y < depth - 1; y++)
        {
            ushort westId = SelectWallTile(HouseSide.West, y, depth, groundFloor, doorOffsetVertical);
            ushort eastId = SelectWallTile(HouseSide.East, y, depth, groundFloor, doorOffsetVertical);

            AddIfValid(result, westId, startX, startY + y, z);
            AddIfValid(result, eastId, startX + width - 1, startY + y, z);
        }
    }

    private ushort SelectWallTile(HouseSide side, int offset, int sideLength, bool groundFloor, int doorOffset)
    {
        bool horizontal = side is HouseSide.North or HouseSide.South;
        ushort wall = horizontal ? _horizontalWallTile : _verticalWallTile;
        ushort window = horizontal ? _horizontalWindowTile : _verticalWindowTile;

        if (groundFloor && _doorTile > 0 && _doorSide == (int)side && offset == doorOffset)
            return _doorTile;

        if (_withWindows && window > 0 && _windowSpacing > 0 &&
            offset > 0 && offset < sideLength - 1 && offset % _windowSpacing == 0)
            return window;

        return wall;
    }

    private void AddRoof(
        List<StaticTile> result,
        ushort startX,
        ushort startY,
        int width,
        int depth,
        int roofBaseZ)
    {
        var roofType = (RoofType)_roofType;
        if (roofType == RoofType.None)
            return;

        int overhang = Math.Clamp(_roofOverhang, 0, 2);
        int roofStartX = startX - overhang;
        int roofStartY = startY - overhang;
        int roofWidth = width + overhang * 2;
        int roofDepth = depth + overhang * 2;

        int roofEndX = roofStartX + roofWidth - 1;
        int roofEndY = roofStartY + roofDepth - 1;

        if (!IsValidMapPosition(roofStartX, roofStartY) || !IsValidMapPosition(roofEndX, roofEndY))
            return;

        if (roofType == RoofType.Flat)
        {
            AddFlatRoof(result, roofStartX, roofStartY, roofWidth, roofDepth, roofBaseZ);
            return;
        }

        bool northSouth = roofType == RoofType.GableNorthSouth;
        AddGableRoof(result, roofStartX, roofStartY, roofWidth, roofDepth, roofBaseZ, northSouth);
    }

    private void AddFlatRoof(
        List<StaticTile> result,
        int startX,
        int startY,
        int width,
        int depth,
        int roofBaseZ)
    {
        sbyte z = ClampZ(roofBaseZ);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < depth; y++)
                AddIfValid(result, _flatRoofTile, startX + x, startY + y, z);
        }
    }

    private void AddGableRoof(
        List<StaticTile> result,
        int startX,
        int startY,
        int width,
        int depth,
        int roofBaseZ,
        bool northSouth)
    {
        int riseStep = Math.Clamp(_roofRiseStep, 1, 10);
        int slopeSpan = northSouth ? width : depth;
        int ridgeIndex = slopeSpan / 2;
        bool hasSingleCenter = slopeSpan % 2 == 1;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < depth; y++)
            {
                int slopeIndex = northSouth ? x : y;
                int distanceFromNearEdge = slopeIndex;
                int distanceFromFarEdge = slopeSpan - 1 - slopeIndex;
                int riseRows = Math.Min(distanceFromNearEdge, distanceFromFarEdge);
                sbyte z = ClampZ(roofBaseZ + riseRows * riseStep);

                bool isRidge = hasSingleCenter && slopeIndex == ridgeIndex && _roofRidgeTile > 0;
                if (isRidge)
                {
                    AddIfValid(result, _roofRidgeTile, startX + x, startY + y, z);
                    continue;
                }

                ushort tileId = slopeIndex < ridgeIndex
                    ? _roofSlopeATile
                    : _roofSlopeBTile;

                if (hasSingleCenter && slopeIndex == ridgeIndex && _roofRidgeTile == 0)
                    tileId = _roofSlopeATile;

                AddIfValid(result, tileId, startX + x, startY + y, z);
            }
        }
    }

    private bool IsValidMapPosition(int x, int y)
    {
        return x >= 0 && y >= 0 &&
               x < Client.WidthInTiles &&
               y < Client.HeightInTiles &&
               Client.IsValidX(x) &&
               Client.IsValidY(y);
    }

    private static sbyte ClampZ(int z)
    {
        return (sbyte)Math.Clamp(z, sbyte.MinValue, sbyte.MaxValue);
    }

    private static void AddIfValid(List<StaticTile> result, ushort tileId, int x, int y, sbyte z)
    {
        if (tileId == 0)
            return;

        result.Add(NewTile(tileId, x, y, z));
    }

    private static StaticTile NewTile(ushort tileId, int x, int y, sbyte z)
    {
        return new StaticTile(tileId, (ushort)x, (ushort)y, z, 0);
    }

    private void DrawTileSlot(string label, ref ushort tileId, bool optional = false)
    {
        ImGui.PushID(label);
        ImGui.Text(label);

        var slotSize = TilesWindow.TilesDimensions;
        bool rendered = false;

        if (tileId > 0)
        {
            try
            {
                var tileInfo = _tilesWindow.GetObjectInfo(tileId);
                if (tileInfo.Texture != null)
                {
                    Application.CEDGame.UIManager.DrawImage(tileInfo.Texture, tileInfo.Bounds, slotSize, false);
                    rendered = true;
                }
            }
            catch
            {
                // Invalid/missing art: fall back to a normal button.
            }
        }

        if (!rendered)
        {
            var buttonLabel = tileId > 0 ? $"0x{tileId:X4}" : optional ? "(optional)" : "drop tile";
            ImGui.Button(buttonLabel, slotSize);
        }

        if (ImGuiEx.DragDropTarget(TilesWindow.OBJECT_DRAG_DROP_TYPE, out var ids) && ids.Length > 0)
            tileId = ids[0];

        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && tileId > 0)
            tileId = 0;

        ImGui.SameLine();
        ImGui.TextDisabled(tileId > 0 ? $"0x{tileId:X4}" : optional ? "optional" : "required");

        ImGui.PopID();
    }
}
