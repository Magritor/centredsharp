using CentrED.IO;
using CentrED.IO.Models;
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

    private enum StairDirection
    {
        East,
        South,
        West,
        North
    }

    private readonly TilesWindow _tilesWindow;

    private int _width = 8;
    private int _depth = 8;
    private int _stories = 1;
    private int _storyHeight = 20;
    private int _windowSpacing = 3;
    private int _doorSide = (int)HouseSide.South;

    private ushort _floorTile;
    // Legacy mapping kept for preset compatibility:
    // _northCornerTile = NW, _southCornerTile = SE.
    private ushort _northCornerTile;
    private ushort _northEastCornerTile;
    private ushort _southWestCornerTile;
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
    private ushort _roofSlopeAEdgeStartTile;
    private ushort _roofSlopeAEdgeEndTile;
    private ushort _roofSlopeBEdgeStartTile;
    private ushort _roofSlopeBEdgeEndTile;
    private ushort _roofRidgeStartTile;
    private ushort _roofRidgeEndTile;
    private ushort _gableStartTile;
    private ushort _gableEndTile;
    private int _roofRiseStep = 3;
    private int _roofOverhang;
    private int _roofZOffset;

    private bool _withStairs;
    private ushort _stairTile;
    private int _stairDirection = (int)StairDirection.East;
    private int _stairRiseStep = 3;
    private int _stairOffsetX = 1;
    private int _stairOffsetY = 1;

    private bool _withWindows = true;
    private bool _snapToTerrain = true;

    private TileObject? _previewParent;

    private string[] _housePresetNames = [""];
    private int _housePresetIndex;
    private string _housePresetNewName = "";

    private Dictionary<string, HousePreset> HousePresets => ProfileManager.ActiveProfile.HousePresets;

    public HouseGeneratorTool()
    {
        _tilesWindow = UIManager.GetWindow<TilesWindow>();
    }

    public override string Name => "House generator";
    public override Keys Shortcut => Keys.None;

    internal override void Draw()
    {
        DrawHousePresets();

        ImGui.Separator();
        ImGui.Text("Dimensions");
        ImGuiEx.DragInt("Width", ref _width, 1, 3, 30);
        ImGuiEx.DragInt("Depth", ref _depth, 1, 3, 30);
        ImGuiEx.DragInt("Stories", ref _stories, 1, 1, 3);
        ImGuiEx.DragInt("Story height", ref _storyHeight, 1, 5, 30);

        ImGui.Separator();
        ImGui.Text("Structure tiles");
        ImGui.TextDisabled("Drag static tiles here from the Tiles window.");
        DrawTileSlot("Floor", ref _floorTile);
        DrawTileSlot("NW corner", ref _northCornerTile, true);
        DrawTileSlot("NE corner", ref _northEastCornerTile, true);
        DrawTileSlot("SW corner", ref _southWestCornerTile, true);
        DrawTileSlot("SE corner", ref _southCornerTile, true);
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

        DrawStairConfiguration();
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
            ImGui.TextDisabled("Roof configuration is incomplete.");
        }
        else if (!IsStairConfigurationValid())
        {
            ImGui.TextDisabled("Stair configuration does not fit inside the house.");
        }
        else
        {
            ImGui.TextWrapped("Move the mouse over the map to preview. Click once to place the complete house as one undo group.");
        }

        ImGui.TextDisabled("Right-click a tile slot to clear it.");
    }

    private void DrawHousePresets()
    {
        RefreshHousePresetNames();

        ImGui.Text("House preset");

        if (ImGui.Button("New"))
            ImGui.OpenPopup("NewHousePreset");

        ImGui.SameLine();
        ImGui.BeginDisabled(_housePresetIndex == 0);
        if (ImGui.Button("Save"))
            SaveCurrentPreset();
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(_housePresetIndex == 0);
        if (ImGui.Button("Delete"))
            ImGui.OpenPopup("DeleteHousePreset");
        ImGui.EndDisabled();

        if (ImGui.Combo("##HousePresetCombo", ref _housePresetIndex, _housePresetNames, _housePresetNames.Length))
            LoadSelectedPreset();

        if (ImGui.BeginPopupModal("NewHousePreset", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
        {
            ImGuiEx.InputText("Name", "##HousePresetName", ref _housePresetNewName, 48);

            bool invalid = string.IsNullOrWhiteSpace(_housePresetNewName) || HousePresets.ContainsKey(_housePresetNewName);
            ImGui.BeginDisabled(invalid);
            if (ImGui.Button("Create"))
            {
                HousePresets[_housePresetNewName] = CapturePreset();
                ProfileManager.Save();
                RefreshHousePresetNames();
                _housePresetIndex = Array.IndexOf(_housePresetNames, _housePresetNewName);
                _housePresetNewName = "";
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _housePresetNewName = "";
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        if (ImGui.BeginPopupModal("DeleteHousePreset", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
        {
            ImGui.Text($"Delete preset '{_housePresetNames[_housePresetIndex]}'?");

            if (ImGui.Button("Yes"))
            {
                HousePresets.Remove(_housePresetNames[_housePresetIndex]);
                ProfileManager.Save();
                _housePresetIndex = 0;
                RefreshHousePresetNames();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("No"))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }
    }

    private void RefreshHousePresetNames()
    {
        var names = HousePresets.Keys.Prepend("").ToArray();
        if (_housePresetNames.SequenceEqual(names))
            return;

        string selected = _housePresetIndex >= 0 && _housePresetIndex < _housePresetNames.Length
            ? _housePresetNames[_housePresetIndex]
            : "";

        _housePresetNames = names;
        _housePresetIndex = Array.IndexOf(_housePresetNames, selected);
        if (_housePresetIndex < 0)
            _housePresetIndex = 0;
    }

    private void SaveCurrentPreset()
    {
        if (_housePresetIndex <= 0 || _housePresetIndex >= _housePresetNames.Length)
            return;

        HousePresets[_housePresetNames[_housePresetIndex]] = CapturePreset();
        ProfileManager.Save();
    }

    private void LoadSelectedPreset()
    {
        if (_housePresetIndex <= 0 || _housePresetIndex >= _housePresetNames.Length)
            return;

        if (HousePresets.TryGetValue(_housePresetNames[_housePresetIndex], out var preset))
            ApplyPreset(preset);
    }

    private HousePreset CapturePreset()
    {
        return new HousePreset
        {
            Width = _width,
            Depth = _depth,
            Stories = _stories,
            StoryHeight = _storyHeight,
            WindowSpacing = _windowSpacing,
            DoorSide = _doorSide,

            FloorTile = _floorTile,
            NorthCornerTile = _northCornerTile,
            NorthEastCornerTile = _northEastCornerTile,
            SouthWestCornerTile = _southWestCornerTile,
            SouthCornerTile = _southCornerTile,
            HorizontalWallTile = _horizontalWallTile,
            VerticalWallTile = _verticalWallTile,
            HorizontalWindowTile = _horizontalWindowTile,
            VerticalWindowTile = _verticalWindowTile,
            DoorTile = _doorTile,

            WithWindows = _withWindows,
            SnapToTerrain = _snapToTerrain,

            RoofType = _roofType,
            FlatRoofTile = _flatRoofTile,
            RoofSlopeATile = _roofSlopeATile,
            RoofSlopeBTile = _roofSlopeBTile,
            RoofRidgeTile = _roofRidgeTile,
            RoofSlopeAEdgeStartTile = _roofSlopeAEdgeStartTile,
            RoofSlopeAEdgeEndTile = _roofSlopeAEdgeEndTile,
            RoofSlopeBEdgeStartTile = _roofSlopeBEdgeStartTile,
            RoofSlopeBEdgeEndTile = _roofSlopeBEdgeEndTile,
            RoofRidgeStartTile = _roofRidgeStartTile,
            RoofRidgeEndTile = _roofRidgeEndTile,
            GableStartTile = _gableStartTile,
            GableEndTile = _gableEndTile,
            RoofRiseStep = _roofRiseStep,
            RoofOverhang = _roofOverhang,
            RoofZOffset = _roofZOffset,

            WithStairs = _withStairs,
            StairTile = _stairTile,
            StairDirection = _stairDirection,
            StairRiseStep = _stairRiseStep,
            StairOffsetX = _stairOffsetX,
            StairOffsetY = _stairOffsetY
        };
    }

    private void ApplyPreset(HousePreset preset)
    {
        _width = preset.Width;
        _depth = preset.Depth;
        _stories = preset.Stories;
        _storyHeight = preset.StoryHeight;
        _windowSpacing = preset.WindowSpacing;
        _doorSide = preset.DoorSide;

        _floorTile = preset.FloorTile;
        _northCornerTile = preset.NorthCornerTile;
        _northEastCornerTile = preset.NorthEastCornerTile;
        _southWestCornerTile = preset.SouthWestCornerTile;
        _southCornerTile = preset.SouthCornerTile;
        _horizontalWallTile = preset.HorizontalWallTile;
        _verticalWallTile = preset.VerticalWallTile;
        _horizontalWindowTile = preset.HorizontalWindowTile;
        _verticalWindowTile = preset.VerticalWindowTile;
        _doorTile = preset.DoorTile;

        _withWindows = preset.WithWindows;
        _snapToTerrain = preset.SnapToTerrain;

        _roofType = preset.RoofType;
        _flatRoofTile = preset.FlatRoofTile;
        _roofSlopeATile = preset.RoofSlopeATile;
        _roofSlopeBTile = preset.RoofSlopeBTile;
        _roofRidgeTile = preset.RoofRidgeTile;
        _roofSlopeAEdgeStartTile = preset.RoofSlopeAEdgeStartTile;
        _roofSlopeAEdgeEndTile = preset.RoofSlopeAEdgeEndTile;
        _roofSlopeBEdgeStartTile = preset.RoofSlopeBEdgeStartTile;
        _roofSlopeBEdgeEndTile = preset.RoofSlopeBEdgeEndTile;
        _roofRidgeStartTile = preset.RoofRidgeStartTile;
        _roofRidgeEndTile = preset.RoofRidgeEndTile;
        _gableStartTile = preset.GableStartTile;
        _gableEndTile = preset.GableEndTile;
        _roofRiseStep = preset.RoofRiseStep;
        _roofOverhang = preset.RoofOverhang;
        _roofZOffset = preset.RoofZOffset;

        _withStairs = preset.WithStairs;
        _stairTile = preset.StairTile;
        _stairDirection = preset.StairDirection;
        _stairRiseStep = preset.StairRiseStep;
        _stairOffsetX = preset.StairOffsetX;
        _stairOffsetY = preset.StairOffsetY;
    }

    private void DrawStairConfiguration()
    {
        ImGui.Separator();
        ImGui.Text("Stairs");
        ImGui.Checkbox("Automatic stairs", ref _withStairs);

        if (!_withStairs)
            return;

        DrawTileSlot("Stair step", ref _stairTile);

        ImGui.Text("Direction");
        ImGui.RadioButton("East", ref _stairDirection, (int)StairDirection.East);
        ImGui.SameLine();
        ImGui.RadioButton("South", ref _stairDirection, (int)StairDirection.South);
        ImGui.RadioButton("West", ref _stairDirection, (int)StairDirection.West);
        ImGui.SameLine();
        ImGui.RadioButton("North", ref _stairDirection, (int)StairDirection.North);

        ImGuiEx.DragInt("Stair rise / step", ref _stairRiseStep, 1, 1, 10);
        ImGuiEx.DragInt("Stair offset X", ref _stairOffsetX, 1, 1, Math.Max(1, _width - 2));
        ImGuiEx.DragInt("Stair offset Y", ref _stairOffsetY, 1, 1, Math.Max(1, _depth - 2));

        if (_stories <= 1)
            ImGui.TextDisabled("Stairs are generated only when Stories > 1.");
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
            ImGuiEx.DragInt("Roof Z offset", ref _roofZOffset, 1, -20, 20);
            return;
        }

        if (roofType is not (RoofType.GableNorthSouth or RoofType.GableEastWest))
            return;

        ImGui.TextDisabled(
            roofType == RoofType.GableNorthSouth
                ? "Ridge North-South; slopes rise from West/East."
                : "Ridge East-West; slopes rise from North/South.");

        DrawTileSlot("Slope A", ref _roofSlopeATile);
        DrawTileSlot("Slope B", ref _roofSlopeBTile);
        DrawTileSlot("Ridge", ref _roofRidgeTile, true);

        ImGuiEx.DragInt("Roof rise / row", ref _roofRiseStep, 1, 1, 10);
        ImGuiEx.DragInt("Roof overhang", ref _roofOverhang, 1, 0, 2);
        ImGuiEx.DragInt("Roof Z offset", ref _roofZOffset, 1, -20, 20);

        ImGui.Separator();
        ImGui.Text("Roof edge pieces (optional)");
        ImGui.TextDisabled("Used only at the two gable ends of each slope.");

        DrawTileSlot("Slope A edge start", ref _roofSlopeAEdgeStartTile, true);
        DrawTileSlot("Slope A edge end", ref _roofSlopeAEdgeEndTile, true);
        DrawTileSlot("Slope B edge start", ref _roofSlopeBEdgeStartTile, true);
        DrawTileSlot("Slope B edge end", ref _roofSlopeBEdgeEndTile, true);
        DrawTileSlot("Ridge start", ref _roofRidgeStartTile, true);
        DrawTileSlot("Ridge end", ref _roofRidgeEndTile, true);

        ImGui.Separator();
        ImGui.Text("Gable pieces (optional)");
        ImGui.TextDisabled("Use triangular gable/cap statics if your art set provides them.");
        DrawTileSlot("Gable start", ref _gableStartTile, true);
        DrawTileSlot("Gable end", ref _gableEndTile, true);
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
        if (o == null || !CanGenerate())
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

        if (o == null || !Client.Running || !CanGenerate())
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

    private bool CanGenerate()
    {
        return HasAnyStructureTile() && IsRoofConfigurationValid() && IsStairConfigurationValid();
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

    private bool IsStairConfigurationValid()
    {
        if (!_withStairs || _stories <= 1)
            return true;

        if (_stairTile == 0)
            return false;

        int width = Math.Clamp(_width, 3, 30);
        int depth = Math.Clamp(_depth, 3, 30);
        int rise = Math.Clamp(_stairRiseStep, 1, 10);
        int stepCount = Math.Max(1, (_storyHeight + rise - 1) / rise);

        var (dx, dy) = GetStairDelta();
        int startX = _stairOffsetX;
        int startY = _stairOffsetY;
        int endX = startX + dx * (stepCount - 1);
        int endY = startY + dy * (stepCount - 1);

        return startX > 0 && startX < width - 1 &&
               startY > 0 && startY < depth - 1 &&
               endX > 0 && endX < width - 1 &&
               endY > 0 && endY < depth - 1;
    }

    private (int dx, int dy) GetStairDelta()
    {
        return (StairDirection)_stairDirection switch
        {
            StairDirection.East => (1, 0),
            StairDirection.South => (0, 1),
            StairDirection.West => (-1, 0),
            StairDirection.North => (0, -1),
            _ => (1, 0)
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
                    {
                        if (story > 0 && IsStairOpening(x, y))
                            continue;

                        result.Add(NewTile(_floorTile, startX + x, startY + y, z));
                    }
                }
            }

            AddWalls(result, startX, startY, width, depth, z, story == 0);
        }

        AddStairs(result, startX, startY, baseZ, stories);

        int roofBaseZ = baseZ + stories * _storyHeight + _roofZOffset;
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
                northId = _northCornerTile; // NW
            else if (x == width - 1 && _northEastCornerTile > 0)
                northId = _northEastCornerTile; // NE

            if (x == 0 && _southWestCornerTile > 0)
                southId = _southWestCornerTile; // SW
            else if (x == width - 1 && _southCornerTile > 0)
                southId = _southCornerTile; // SE

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

    private bool IsStairOpening(int localX, int localY)
    {
        if (!_withStairs || _stories <= 1 || _stairTile == 0)
            return false;

        int rise = Math.Clamp(_stairRiseStep, 1, 10);
        int stepCount = Math.Max(1, (_storyHeight + rise - 1) / rise);
        var (dx, dy) = GetStairDelta();

        for (int step = 0; step < stepCount; step++)
        {
            int x = _stairOffsetX + dx * step;
            int y = _stairOffsetY + dy * step;

            if (localX == x && localY == y)
                return true;
        }

        return false;
    }

    private void AddStairs(
        List<StaticTile> result,
        ushort startX,
        ushort startY,
        sbyte baseZ,
        int stories)
    {
        if (!_withStairs || _stairTile == 0 || stories <= 1)
            return;

        int rise = Math.Clamp(_stairRiseStep, 1, 10);
        int stepCount = Math.Max(1, (_storyHeight + rise - 1) / rise);
        var (dx, dy) = GetStairDelta();

        for (int story = 0; story < stories - 1; story++)
        {
            int storyBaseZ = baseZ + story * _storyHeight;

            for (int step = 0; step < stepCount; step++)
            {
                int x = startX + _stairOffsetX + dx * step;
                int y = startY + _stairOffsetY + dy * step;
                sbyte z = ClampZ(storyBaseZ + step * rise);

                AddIfValid(result, _stairTile, x, y, z);
            }
        }
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
        int axisLength = northSouth ? depth : width;
        int ridgeIndex = slopeSpan / 2;
        bool hasSingleCenter = slopeSpan % 2 == 1;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < depth; y++)
            {
                int slopeIndex = northSouth ? x : y;
                int axisIndex = northSouth ? y : x;
                int distanceFromNearEdge = slopeIndex;
                int distanceFromFarEdge = slopeSpan - 1 - slopeIndex;
                int riseRows = Math.Min(distanceFromNearEdge, distanceFromFarEdge);
                sbyte z = ClampZ(roofBaseZ + riseRows * riseStep);

                bool isRidge = hasSingleCenter && slopeIndex == ridgeIndex && _roofRidgeTile > 0;
                if (isRidge)
                {
                    ushort ridgeTile = SelectRidgeTile(axisIndex, axisLength);
                    AddIfValid(result, ridgeTile, startX + x, startY + y, z);
                    continue;
                }

                bool sideA = slopeIndex < ridgeIndex;
                if (hasSingleCenter && slopeIndex == ridgeIndex && _roofRidgeTile == 0)
                    sideA = true;

                ushort tileId = SelectSlopeTile(sideA, axisIndex, axisLength);
                AddIfValid(result, tileId, startX + x, startY + y, z);
            }
        }

        AddGableCaps(result, startX, startY, width, depth, roofBaseZ, northSouth);
    }

    private ushort SelectSlopeTile(bool sideA, int axisIndex, int axisLength)
    {
        if (sideA)
        {
            if (axisIndex == 0 && _roofSlopeAEdgeStartTile > 0)
                return _roofSlopeAEdgeStartTile;
            if (axisIndex == axisLength - 1 && _roofSlopeAEdgeEndTile > 0)
                return _roofSlopeAEdgeEndTile;
            return _roofSlopeATile;
        }

        if (axisIndex == 0 && _roofSlopeBEdgeStartTile > 0)
            return _roofSlopeBEdgeStartTile;
        if (axisIndex == axisLength - 1 && _roofSlopeBEdgeEndTile > 0)
            return _roofSlopeBEdgeEndTile;
        return _roofSlopeBTile;
    }

    private ushort SelectRidgeTile(int axisIndex, int axisLength)
    {
        if (axisIndex == 0 && _roofRidgeStartTile > 0)
            return _roofRidgeStartTile;
        if (axisIndex == axisLength - 1 && _roofRidgeEndTile > 0)
            return _roofRidgeEndTile;
        return _roofRidgeTile;
    }

    private void AddGableCaps(
        List<StaticTile> result,
        int startX,
        int startY,
        int width,
        int depth,
        int roofBaseZ,
        bool northSouth)
    {
        if (_gableStartTile == 0 && _gableEndTile == 0)
            return;

        if (northSouth)
        {
            int centerX = startX + (width - 1) / 2;
            AddIfValid(result, _gableStartTile, centerX, startY, ClampZ(roofBaseZ));
            AddIfValid(result, _gableEndTile, centerX, startY + depth - 1, ClampZ(roofBaseZ));
        }
        else
        {
            int centerY = startY + (depth - 1) / 2;
            AddIfValid(result, _gableStartTile, startX, centerY, ClampZ(roofBaseZ));
            AddIfValid(result, _gableEndTile, startX + width - 1, centerY, ClampZ(roofBaseZ));
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
