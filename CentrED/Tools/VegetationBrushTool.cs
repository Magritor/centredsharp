using CentrED.Map;
using CentrED.UI;
using CentrED.UI.Windows;
using Hexa.NET.ImGui;
using Microsoft.Xna.Framework.Input;

namespace CentrED.Tools;

public class VegetationBrushTool : BaseTool
{
    private readonly TilesWindow _tilesWindow;
    private int _minSpacing = 2;
    private int _randomZ = 0;
    private bool _avoidWet = true;
    private bool _avoidImpassableLand = true;

    private readonly HashSet<(ushort x, ushort y)> _planned = new();

    public VegetationBrushTool()
    {
        _tilesWindow = UIManager.GetWindow<TilesWindow>();
    }

    public override string Name => "Vegetation brush";
    public override Keys Shortcut => Keys.F8;

    internal override void Draw()
    {
        base.Draw();

        ImGui.Text("Source: active Tile Set");
        if (_tilesWindow.ActiveTileSetValues.Count == 0)
        {
            ImGui.TextDisabled("Create/select a Tile Set containing trees, bushes, rocks, flowers...");
        }
        else
        {
            ImGui.Text($"Tiles: {_tilesWindow.ActiveTileSetValues.Count}");
        }

        ImGui.Separator();
        ImGuiEx.DragInt("Minimum spacing", ref _minSpacing, 1, 0, 12);
        ImGuiEx.DragInt("Random Z", ref _randomZ, 1, 0, 8);
        ImGui.Checkbox("Avoid water/wet land", ref _avoidWet);
        ImGui.Checkbox("Avoid impassable land", ref _avoidImpassableLand);

        ImGui.Separator();
        ImGui.TextWrapped("Ctrl + drag = scatter over an area. Chance controls density. Shift includes lower tiles.");
    }

    public override void OnDeactivated(TileObject? o)
    {
        base.OnDeactivated(o);
        _planned.Clear();
    }

    protected override void GhostApply(TileObject? o)
    {
        if (o == null || _tilesWindow.ActiveTileSetValues.Count == 0)
            return;

        var x = o.Tile.X;
        var y = o.Tile.Y;

        if (!Client.IsValidX(x) || !Client.IsValidY(y))
            return;

        var land = MapManager.LandTiles[x, y];
        if (land == null)
            return;

        ref var landData = ref MapManager.UoFileManager.TileData.LandData[land.Tile.Id];

        if (_avoidWet && landData.IsWet)
            return;

        if (_avoidImpassableLand && landData.IsImpassable)
            return;

        if (_planned.Contains((x, y)) || HasNearbyVegetation(x, y))
            return;

        var ids = _tilesWindow.ActiveTileSetValues;
        var id = ids[Random.Shared.Next(ids.Count)];

        int z = land.Tile.Z;
        if (_randomZ > 0)
            z += Random.Shared.Next(0, _randomZ + 1);

        z = Math.Clamp(z, sbyte.MinValue, sbyte.MaxValue);

        var tile = new StaticTile(id, x, y, (sbyte)z, 0);
        MapManager.StaticsManager.AddGhost(land, new StaticObject(tile));
        _planned.Add((x, y));
    }

    protected override void GhostClear(TileObject? o)
    {
        if (o == null)
            return;

        var land = MapManager.LandTiles[o.Tile.X, o.Tile.Y];
        if (land != null)
            MapManager.StaticsManager.ClearGhost(land);

        _planned.Remove((o.Tile.X, o.Tile.Y));
    }

    protected override void InternalApply(TileObject? o)
    {
        if (o == null)
            return;

        var land = MapManager.LandTiles[o.Tile.X, o.Tile.Y];
        if (land == null)
            return;

        if (MapManager.StaticsManager.TryGetGhost(land, out var ghost))
            Client.Add(ghost.StaticTile);
    }

    private bool HasNearbyVegetation(ushort x, ushort y)
    {
        if (_minSpacing <= 0)
            return false;

        var vegetationIds = _tilesWindow.ActiveTileSetValues;
        if (vegetationIds.Count == 0)
            return false;

        var idSet = vegetationIds as HashSet<ushort> ?? vegetationIds.ToHashSet();

        int minX = Math.Max(0, x - _minSpacing);
        int maxX = Math.Min(Client.WidthInTiles - 1, x + _minSpacing);
        int minY = Math.Max(0, y - _minSpacing);
        int maxY = Math.Min(Client.HeightInTiles - 1, y + _minSpacing);

        for (int px = minX; px <= maxX; px++)
        {
            for (int py = minY; py <= maxY; py++)
            {
                if (_planned.Contains(((ushort)px, (ushort)py)))
                    return true;

                var statics = MapManager.StaticsManager.Get((ushort)px, (ushort)py);
                if (statics == null)
                    continue;

                foreach (var so in statics)
                {
                    if (idSet.Contains(so.StaticTile.Id))
                        return true;
                }
            }
        }

        return false;
    }
}
