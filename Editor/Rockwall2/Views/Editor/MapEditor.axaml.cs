using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Toolbar;
using Rockwall2.Editor.Mapper;
using Rockwall2.Editor.Mapper.Toolbar;
using Rockwall2.Tools;
using Rockwall2.ViewModels.Editor;
using Rockwall2.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rockwall2;
public partial class MapEditor : UserControl
{
    public Control GameView => gameControl;

    private ToolButtonDef?[] hotbarSlots = Array.Empty<ToolButtonDef?>();
    private readonly List<Border> hotbarSlotBorders = new();
    private DispatcherTimer hotbarHideTimer = null!;

    public MapEditor()
    {
        if (!Design.IsDesignMode)
            DataContext = new MapEditorViewModel();
        InitializeComponent();
        gameControl.Host = App.Host;
        GameView.PointerEntered += (a, b) => GameView.Focus();

        var atlas = new ToolIconAtlas(
            new Bitmap(AssetLoader.Open(new Uri("avares://Rockwall2/Assets/Icons/rw2icons.png"))),
            cellSize: 32);

        var leftCategories = ToolbarDefinitions.BuildLeftToolbar(atlas);
        var topCategories = ToolbarDefinitions.BuildTopToolbar(atlas);

        LeftToolbar.ItemsSource = leftCategories;
        TopToolbar.ItemsSource = topCategories;

        Hotbar.RegisterAvailable(leftCategories.Concat(topCategories));
        ToolbarDefinitions.ApplyDefaultHotbarAssignments();

        BuildHotbarVisuals();
        Hotbar.SlotAssigned += slot => Dispatcher.UIThread.Post(() => RefreshHotbarSlot(slot));
        Hotbar.SlotActivated += slot => Dispatcher.UIThread.Post(() => ShowHotbarPopout(slot));

        hotbarHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        hotbarHideTimer.Tick += (_, _) => { hotbarHideTimer.Stop(); HideHotbar(); };

        RefreshGridSize();
    }

    private void BuildHotbarVisuals()
    {
        HotbarItems.Children.Clear();
        hotbarSlotBorders.Clear();
        for (int slot = 1; slot <= Hotbar.SlotCount; slot++)
        {
            var border = new Border { Classes = { "hotbar-slot" } };
            hotbarSlotBorders.Add(border);
            HotbarItems.Children.Add(border);
            RefreshHotbarSlot(slot);
        }
    }
    private void RefreshHotbarSlot(int slot)
    {
        var border = hotbarSlotBorders[slot - 1];
        var def = Hotbar.GetSlot(slot);

        border.Classes.Set("empty", def == null);
        ToolTip.SetTip(border, def != null ? $"{slot}: {def.ToolTip}" : $"{slot}: (empty)");
        border.Child = def != null
            ? new Image { Source = def.IconSource, Stretch = Stretch.Uniform }
            : null;
    }
    private void ShowHotbarPopout(int slot)
    {
        for (int i = 0; i < hotbarSlotBorders.Count; i++)
            hotbarSlotBorders[i].Classes.Set("active", i + 1 == slot);

        HotbarOverlay.Opacity = 1;
        HotbarOverlay.Margin = new Thickness(0, 56, 0, 0);
        hotbarHideTimer.Stop();
        hotbarHideTimer.Start();
    }
    private void HideHotbar()
    {
        HotbarOverlay.Opacity = 0;
    }

    private async void BrowseTextures_Click(object? sender, RoutedEventArgs e)
    {
        int mat = await MaterialPicker.PickAsync(MainWindow.Instance);
        if (mat == -1) return;

        TexturePreview.Source = GlobalEditorData.TexturesAsImages[mat].Image;
        Toolbelt.ActiveTexture = GlobalMapData.loadedMaterials[mat].name;
    }
    public void RefreshViews()
    {
        TexturePreview.Source = GlobalEditorData.TexturesAsImages[GlobalMapData.materialNameToIndex[Toolbelt.ActiveTexture]].Image;
    }
    public void RefreshGridSize()
    {
        gridSizeLabel.Content = Transformable.GridSize;
    }

    public void ChangePrimitiveRes(object? sender, NumericUpDownValueChangedEventArgs args)
    {
        (Toolbelt.BrushTool as BrushTool).SetPrimitiveParams((int)(args.NewValue??0));
    }
}