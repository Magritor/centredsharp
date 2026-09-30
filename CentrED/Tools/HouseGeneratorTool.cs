using CentrED.Map;
using CentrED.UI;
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

    private int _width = 8;
    private int _depth = 8;
    private int _stories = 1;
    private int _storyHeight = 20;
    private int _windowSpacing = 3;
    private int _doorSide = (int)HouseSide.South;

    private int _floorTile;
    private int _northCornerTile;
    private int _southCornerTile;
    private int _horizontalWallTile;
    private int _verticalWallTile;
    private int _horizontalWindowTile;
    private int _verticalWindowTile;
    private int _doorTile;
    private int _roofTile;

    private bool _withWindows = true;
    private bool _withRoof;
    private bool _snapToTerrain = true;

    private TileObject? _previewParent;

    public override string Name => "House generator";
    public override Keys Shortcut => Keys.F9;

    internal override void Draw()
    {
        ImGui.Text("Dimensions");
        ImGuiEx.DragInt("Width", ref _width, 1, 3, 30);
        ImGuiEx.DragInt("Depth", ref _depth, 1, 3, 30);
        ImGuiEx.DragInt("Stories", ref _stories, 1, 1, 3);
        ImGuiEx.DragInt("Story height", ref _storyHeight, 1, 5, 30);

        ImGui.Separator();
        ImGui.Text("Structure tiles");
        DrawTileId("Floor", ref _floorTile);
        DrawTileId("North corner", ref _northCornerTile);
        DrawTileId("South corner", ref _southCornerTile);
        DrawTileId("Horizontal wall", ref _horizontalWallTile);
        DrawTileId("Vertical wall", ref _verticalWallTile);

        ImGui.Separator();
        ImGui.Checkbox("Windows", ref _withWindows);
        if (_withWindows)
        {
            DrawTileId("Horizontal window", ref _horizontalWindowTile);
            DrawTileId("Vertical window", ref _verticalWindowTile);
            ImGuiEx.DragInt("Window spacing", ref _windowSpacing, 1, 2, 8);
        }

        DrawTileId("Door", ref _doorTile);
        ImGui.Text("Door side");
        ImGui.RadioButton("North", ref _doorSide, (int)HouseSide.North);
        ImGui.SameLine();
        ImGui.RadioButton("South", ref _doorSide, (int)HouseSide.South);
        ImGui.RadioButton("West", ref _doorSide, (int)HouseSide.West);
        ImGui.SameLine();
        ImGui.RadioButton("East", ref _doorSide, (int)HouseSide.East);

        ImGui.Separator();
        ImGui.Checkbox("Flat roof", ref _withRoof);
        if (_withRoof)
            DrawTileId("Roof tile", ref _roofTile);

        ImGui.Checkbox("Snap base Z to terrain", ref _snapToTerrain);
        if (!_snapToTerrain)
            ImGuiEx.DragInt("Base Z", ref MapManager.VirtualLayerZ, 1, sbyte.MinValue, sbyte.MaxValue);

        ImGui.Separator();
        ImGui.TextWrapped("Move the mouse over the map to preview. Click once to place the complete house as one undo group.");
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
        if (o == null)
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

        if (o == null || !Client.Running)
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

        if (!Client.IsValidX(startX) || !Client.IsValidY(startY) ||
            !Client.IsValidX(endX) || !Client.IsValidY(endY))
            return result;

        for (int story = 0; story < stories; story++)
        {
            int rawZ = baseZ + story * _storyHeight;
            sbyte z = (sbyte)Math.Clamp(rawZ, sbyte.MinValue, sbyte.MaxValue);

            if (_floorTile > 0)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < depth; y++)
                    {
                        result.Add(NewTile(_floorTile, startX + x, startY + y, z));
                    }
                }
            }

            AddWalls(result, startX, startY, width, depth, z, story == 0);
        }

        if (_withRoof && _roofTile > 0)
        {
            int rawRoofZ = baseZ + stories * _storyHeight;
            sbyte roofZ = (sbyte)Math.Clamp(rawRoofZ, sbyte.MinValue, sbyte.MaxValue);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < depth; y++)
                    result.Add(NewTile(_roofTile, startX + x, startY + y, roofZ));
            }
        }

        return result;
    }

    private void AddWalls(List<StaticTile> result, ushort startX, ushort startY, int width, int depth, sbyte z, bool groundFloor)
    {
        int doorOffsetHorizontal = width / 2;
        int doorOffsetVertical = depth / 2;

        for (int x = 0; x < width; x++)
        {
            int northId = SelectWallTile(HouseSide.North, x, width, groundFloor, doorOffsetHorizontal);
            int southId = SelectWallTile(HouseSide.South, x, width, groundFloor, doorOffsetHorizontal);

            if (x == 0 && _northCornerTile > 0)
                northId = _northCornerTile;
            if (x == width - 1 && _southCornerTile > 0)
                southId = _southCornerTile;

            AddIfValid(result, northId, startX + x, startY, z);
            AddIfValid(result, southId, startX + x, startY + depth - 1, z);
        }

        for (int y = 1; y < depth - 1; y++)
        {
            int westId = SelectWallTile(HouseSide.West, y, depth, groundFloor, doorOffsetVertical);
            int eastId = SelectWallTile(HouseSide.East, y, depth, groundFloor, doorOffsetVertical);

            AddIfValid(result, westId, startX, startY + y, z);
            AddIfValid(result, eastId, startX + width - 1, startY + y, z);
        }
    }

    private int SelectWallTile(HouseSide side, int offset, int sideLength, bool groundFloor, int doorOffset)
    {
        bool horizontal = side is HouseSide.North or HouseSide.South;
        int wall = horizontal ? _horizontalWallTile : _verticalWallTile;
        int window = horizontal ? _horizontalWindowTile : _verticalWindowTile;

        if (groundFloor && _doorTile > 0 && _doorSide == (int)side && offset == doorOffset)
            return _doorTile;

        if (_withWindows && window > 0 && _windowSpacing > 0 &&
            offset > 0 && offset < sideLength - 1 && offset % _windowSpacing == 0)
            return window;

        return wall;
    }

    private static void AddIfValid(List<StaticTile> result, int tileId, int x, int y, sbyte z)
    {
        if (tileId <= 0 || tileId > ushort.MaxValue)
            return;

        result.Add(NewTile(tileId, x, y, z));
    }

    private static StaticTile NewTile(int tileId, int x, int y, sbyte z)
    {
        return new StaticTile((ushort)tileId, (ushort)x, (ushort)y, z, 0);
    }

    private static void DrawTileId(string label, ref int value)
    {
        ImGuiEx.DragInt(label, ref value, 1, 0, ushort.MaxValue);
        ImGui.SameLine();
        ImGui.TextDisabled($"0x{value:X4}");
    }
}
